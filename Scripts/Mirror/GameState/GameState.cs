using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// * A persistent networked state of the game - essentially a networked game manager. Might include time in a match, game/map restart flow, etc.
/// 
/// * Each game mode (e.g. Match, CTF, ...) could exist as a different GameState prefab. It can contain multiple optional GameStateComponents.
/// * * GameStateComponents are designed to be reused. Many game modes can share similar features, but don't need to share a monster base class.
/// * * GameStateComponents could include a timer, a point counts, team tracking, winning conditions, etc.
/// * * Different parts of the game (such as the timer HUD) can check for the existence of a GameStateComponent (e.g. GameStateTimerComponent) to decide its behaviour
/// 
/// * There is a "primary" GameState (default) along with additional secondary GameStates.
/// * * The primary GameState gets destroyed and replaced whenever you call ServerChangeGameState
/// * * Secondary GameStates allow for more or less persistent GameStates, such as networked server settings that won't change regardless of game mode
/// * * Secondary GameStates can also get replaced via ServerChangeGameState, if the original one is passed to the function.
/// 
/// * Can be inherited. Spawned upon server start, spawned on clients when available.
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
    /// The primary GameState. This is the "default" game state that gets changed and replaced by default.
    /// </summary>
    public static GameState primary
    {
        get => _primary;
        private set
        {
            if (_primary != null)
                _primary.isPrimary = false;
            if (value != null)
                value.isPrimary = true;
            _primary = value;
        }
    }
    private static GameState _primary;

    /// <summary>
    /// All GameStates including primary and secondary. When created, these can also be changed and replaced if specified in the ServerChangeGameState overload.
    /// </summary>
    private static List<GameState> all { get; set; } = new List<GameState>();

    /// <summary>
    /// How long the win screen lasts in seconds
    /// </summary>
    public int winScreenDuration = 15;

    /// <summary>
    /// Returns time until game restart during intermission
    /// </summary>
    public float timeTilRestart { get; private set; }

    /// <summary>
    /// Whether this is the primary GameState. Used so the client knows the primary.
    /// </summary>
    [SyncVar(hook = nameof(OnPrimaryChanged))]
    public bool isPrimary;

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

        DontDestroyOnLoad(gameObject);

        if (isPrimary)
            primary = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        DontDestroyOnLoad(gameObject);
    }

    private void Awake()
    {
        foreach (GameStateComponent gsComponent in GetComponents<GameStateComponent>())
        {
            components.Add(gsComponent);
            gsComponent.OnAwake();
        }

        timeTilRestart = winScreenDuration;
    }

    private void Start()
    {
        foreach (GameStateComponent component in components)
            component.OnStart();
    }

    private void OnDestroy()
    {
        all.Remove(this);
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
    /// Changes the specified game state to another game state prefab.
    /// 
    /// If originalInstance is null, a new GameState is created without destruction of any others.
    /// If originalInstance is any other GameState, that GameState will be replaced by the new one.
    /// </summary>
    public static GameState ServerChangeGameState(GameState previousGameStateInstance, GameState newGameStatePrefab, bool isPrimary)
    {
        if (previousGameStateInstance != null && !all.Contains(previousGameStateInstance))
        {
            Debug.LogError($"[{nameof(GameState)}.{nameof(ServerChangeGameState)}] supplied instance {previousGameStateInstance} does not exist");
            return null;
        }

        if (!NetworkServer.active)
        {
            Debug.LogError($"[{nameof(GameState)}.{nameof(ServerChangeGameState)}] cannot be called on client");
            return null;
        }

        if (!newGameStatePrefab)
        {
            Debug.LogError($"[{nameof(GameState)}.{nameof(ServerChangeGameState)}] GameStatePrefab is null");
            return null;
        }

        bool wasPrimaryGamestate = previousGameStateInstance != null && primary == previousGameStateInstance;

        if (previousGameStateInstance)
            Destroy(previousGameStateInstance.gameObject);
        all.RemoveAll(x => x == null);

        GameState newGameState = Instantiate(newGameStatePrefab);
        NetworkServer.Spawn(newGameState.gameObject);
        all.Add(newGameState);

        if (wasPrimaryGamestate || isPrimary)
            primary = newGameState;

        return newGameState;
    }

    /// <summary>
    /// Changes the primary game state to another game state prefab
    /// </summary>
    public static GameState ServerChangeGameState(GameState newGameStatePrefab)
    {
        return ServerChangeGameState(primary, newGameStatePrefab, true);
    }

    /// <summary>
    /// Destroys all existing game states
    /// </summary>
    public static void ServerDestroyAllGameStates()
    {
        foreach (var gameState in all)
        {
            if (gameState)
            {
                NetworkServer.UnSpawn(gameState.gameObject);
                Destroy(gameState.gameObject);
            }
        }

        all.Clear();
    }

    public static void SetPrimary(GameState primaryGameState)
    {
        primary = primaryGameState;
    }

    /// <summary>
    /// Returns a GameState component, if it exists, from one of the current game states (primary is searched first)
    /// </summary>
    public static TComponent Get<TComponent>() where TComponent : GameStateComponent
    {
        if (primary && primary.TryGetComponent<TComponent>(out TComponent primaryComponent))
        {
            // found the component on the primary gamestate
            return primaryComponent;
        }
        else
        {
            // search the rest
            foreach (var secondary in all)
            {
                if (secondary != primary)
                {
                    if (secondary.TryGetComponent<TComponent>(out TComponent secondaryComponent))
                        return secondaryComponent;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Returns a GameState component, if it exists in the current game state
    /// </summary>
    public static bool Get<TComponent>(out TComponent netGameStateComponent) where TComponent : GameStateComponent
    {
        netGameStateComponent = Get<TComponent>();
        return netGameStateComponent != null;
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

    /// <summary>
    /// Ends the win screen if it's ongoing
    /// </summary>
    [Server]
    public void ServerEndWinScreen()
    {
        if (IsWinScreen)
            timeTilRestart = 0.001f;
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcTimeTilRestart(float timeRemaining)
    {
        if (!NetworkServer.active)
            timeTilRestart = timeRemaining;
    }

    private void OnPrimaryChanged(bool oldVal, bool newVal)
    {
        if (!NetworkServer.active && newVal == true)
            primary = this;
    }
}
