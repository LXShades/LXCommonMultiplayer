using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// * The main networked state of the game - essentially a networked game manager. Might include time in match, game/map restart flow, etc.
/// 
/// * Each game mode can be a different GameState prefab. It can contain multiple optional GameStateComponents.
/// * * You could expect each game mode to have a different composition of reusable GameStateComponents such as timers, point counts, tournament podiums, etc.
/// 
/// * Can be inherited. Spawned upon host, spawned on clients when available.
/// </summary>
public class GameState : NetworkBehaviour
{
    internal struct GameStateComponentAwaiter
    {
        public UnityEngine.Object awaiter;
        public Action<GameStateComponent> onAvailableCallback;
    }

    public delegate void OnGameEndedDelegate(GameState gameState);

    /// <summary>
    /// Single instance of the ServerState. You may want to make an inherited version
    /// </summary>
    public static GameState singleton { get; private set; }

    /// <summary>
    /// How long the win screen lasts in seconds
    /// </summary>
    public int winScreenDuration = 15;

    /// <summary>
    /// Returns time until game restart during intermission
    /// </summary>
    public float timeTilRestart { get; private set; }

    /// <summary>
    /// Returns whether we're currently on the win screen
    /// </summary>
    public bool IsWinScreen => _isWinScreen;

    /// <summary>
    /// Called when the game ends
    /// </summary>
    public OnGameEndedDelegate onGameEnded;

    /// <summary>
    /// Called when the win screen finished
    /// </summary>
    public OnGameEndedDelegate onWinScreenEnded;

    /// <summary>
    /// GameStateComponents attached to this gamestate
    /// </summary>
    List<GameStateComponent> components = new List<GameStateComponent>();

    [SyncVar]
    private bool _isWinScreen = false;

    internal static Dictionary<Type, List<GameStateComponentAwaiter>> gameStateComponentAwaiters = new Dictionary<Type, List<GameStateComponentAwaiter>>();

    public override void OnStartClient()
    {
        base.OnStartClient();

        singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Awake()
    {
        if (singleton != null)
        {
            Destroy(gameObject);
            Debug.LogWarning($"[{gameObject.name}.Awake()] There is already a NetGameState running");
            return;
        }

        foreach (GameStateComponent matchComponent in GetComponents<GameStateComponent>())
        {
            components.Add(matchComponent);
            matchComponent.OnAwake();
        }

        timeTilRestart = winScreenDuration;

        singleton = this;
    }

    private void Start()
    {
        foreach (GameStateComponent component in components)
            component.OnStart();
    }

    private void Update()
    {
        // update components
        foreach (GameStateComponent component in components)
            component.OnUpdate();

        // update win screen / intermission countdown
        if (IsWinScreen && isServer)
        {
            if (timeTilRestart > 0f)
            {
                // Tick down the timeTilRestart and inform clients
                float nextTimeTilRestart = Mathf.Max(timeTilRestart - Time.deltaTime, 0f);
                if ((int)nextTimeTilRestart != (int)timeTilRestart)
                    RpcTimeTilRestart(nextTimeTilRestart);

                timeTilRestart = nextTimeTilRestart;
            }

            // end the win screen, do this here just in case timetilrestart started with 0
            if (timeTilRestart <= 0f)
            {
                _isWinScreen = false;
                onWinScreenEnded?.Invoke(this);
            }
        }
    }

    /// <summary>
    /// Changes the game state to another game state prefab
    /// </summary>
    public static void ServerChangeGameState(GameObject newGameStatePrefab)
    {
        if (!NetworkServer.active)
        {
            Debug.LogError($"[{nameof(GameState)}.{nameof(ServerChangeGameState)}] cannot be called on client");
            return;
        }

        if (singleton != null)
        {
            Destroy(singleton.gameObject);
            singleton = null;
        }

        GameObject newState = Instantiate(newGameStatePrefab);
        NetworkServer.Spawn(newState);
    }

    /// <summary>
    /// Returns a GameState component, if it exists in the current game state
    /// </summary>
    public static bool Get<TComponent>(out TComponent netGameStateComponent) where TComponent : GameStateComponent
    {
        netGameStateComponent = null;
        return singleton != null ? singleton.TryGetComponent<TComponent>(out netGameStateComponent) : false;
    }

    /// <summary>
    /// Calls onAvailable when the requested GameState component becomes available, or immediately if it is already
    /// </summary>
    public static void GetWhenAvailable<TComponent>(UnityEngine.Object awaiter, Action<TComponent> onAvailable) where TComponent : GameStateComponent
    {
        TComponent component;
        if (Get(out component))
            onAvailable?.Invoke(component);
        else
        {
            List<GameStateComponentAwaiter> awaiters;
            if (!gameStateComponentAwaiters.TryGetValue(typeof(TComponent), out awaiters))
            {
                awaiters = new List<GameStateComponentAwaiter>(8);
                gameStateComponentAwaiters[typeof(TComponent)] = awaiters;
            }

            if (awaiters.Count > 50) // HACK: clean up occasionally, this needs work.
                awaiters.RemoveAll(x => x.awaiter == null);

            awaiters.Add(new GameStateComponentAwaiter()
            {
                awaiter = awaiter,
                onAvailableCallback = component => onAvailable?.Invoke((TComponent)component)
            });
        }
    }

    public string GetWinners()
    {
        string winners = "";
        foreach (GameStateComponent component in components)
        {
            string winner = component.GetWinners();
            if (!string.IsNullOrEmpty(winner))
            {
                if (winners.Length == 0)
                    winners += winner;
                else
                    winners += $" and {winner}";
            }
        }

        return winners;
    }

    /// <summary>
    /// Ends the game if it's still running.
    /// </summary>
    [Server]
    public void ServerEndGame()
    {
        if (!_isWinScreen)
        {
            _isWinScreen = true;
            timeTilRestart = winScreenDuration;
            onGameEnded?.Invoke(this);
        }
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcTimeTilRestart(float timeRemaining)
    {
        if (!NetworkServer.active)
            timeTilRestart = timeRemaining;
    }
}
