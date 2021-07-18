using Mirror;
using UnityEngine;

/// <summary>
/// Basic player that moves towards the given direction in a predicted-reconciled way.
/// 
/// Handles all movement and prediction-reconciliation by itself.
/// </summary>
public class Player : NetworkBehaviour, ITickable<Player.PlayerInput, Player.PlayerState>
{
    public struct PlayerState : ITickerState<PlayerState>
    {
        public Vector3 position;
        public Quaternion rotation;

        public void DebugDraw(Color colour) { } // doesn't necessarily need implementing

        public bool Equals(PlayerState other)
        {
            return other.position == position && other.rotation == rotation;
        }
    }

    public struct PlayerInput : ITickerInput<PlayerInput>
    {
        public float horizontal;
        public float vertical;

        public PlayerInput GenerateLocal()
        {
            return new PlayerInput()
            {
                horizontal = Input.GetAxisRaw("Horizontal"),
                vertical = Input.GetAxisRaw("Vertical")
            };
        }

        public PlayerInput WithDeltas(PlayerInput previousInput) => this;

        public PlayerInput WithoutDeltas() => this;
    }

    public float movementSpeed = 1f;

    public int netUpdateRate = 30;

    public TickerSettings tickerSettings = TickerSettings.Default;

    private float timeOnServer;

    private float timeOfLastReceivedServerUpdate;
    private float timeOfLastReceivedClientInput;

    Ticker<PlayerInput, PlayerState> ticker;

    private void Awake()
    {
        ticker = new Ticker<PlayerInput, PlayerState>(this);
        ticker.settings = tickerSettings;
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            // build inputs, send to the ticker
            PlayerInput nextInput = new PlayerInput().GenerateLocal();

            ticker.PushInput(nextInput, Time.time);

            // seek to the current Time.time. this may amount to a mixture of reverting/confirming states and ticking forward with delta time
            ticker.Seek(Time.time, Time.time);

            // send our inputs and times to the server
            if (IsNetUpdate())
            {
                TickerInputPack<PlayerInput> inputPacks = ticker.MakeInputPack(0.5f);

                if (!NetworkServer.active) // host player shouldn't send inputs
                    CmdPlayerInput(inputPacks.inputs, inputPacks.times);
            }
        }
        else
        {
            if (NetworkServer.active)
            {
                // server receiving client's state - just process the inputs, moving to the latest client time (no speedhack tests)
                ticker.Seek(ticker.inputTimelineBase.LatestTime, ticker.inputTimelineBase.LatestTime);
            }
            else
            {
                // move the player to the latest time it had on the server
                // add Time.time - timeOfLastReceivedServerUpdates to smoothly extrapolate other players.
                // note that this isn't always an "extrapolation" - we have their latest input, but that input wasn't processed by the server yet
                // by the time we receive the next state from the server, the server have processed that input, as we did too, so it's almost more like an interpolation.

                // another note: the server also added its Time.time - timeOfLastReceivedClientInput
                // both are needed to extrapolate it as expected
                float extrapolatedTimeOnServer = timeOnServer + Time.time - timeOfLastReceivedServerUpdate;

                ticker.Seek(extrapolatedTimeOnServer, extrapolatedTimeOnServer, TickerSeekFlags.IgnoreDeltas);
            }
        }

        if (NetworkServer.active && IsNetUpdate())
            RpcPlayerState(ticker.lastConfirmedState, ticker.latestInput, ticker.confirmedStateTime, Mathf.Min(isLocalPlayer ? Time.time - ticker.confirmedStateTime : Time.time - timeOfLastReceivedClientInput, 0.5f));
    }

    #region ITickable
    /// <summary>
    /// Ticks the object. In a networked game, you may put most important gameplay things in this function, as though it were an Update function.
    /// 
    /// * Do NOT use Time.deltaTime, or anything from Time (except debugging info)!
    /// * Do not read live inputs from here (except, again, debugging). Put everything you need into TInput and ensure it is passed into the ticker timeline.
    /// * Always remember Tick() may be called during states in the past, where Time.deltaTime may completely inaccurate.
    /// * Always remember Tick() may be called multiple times in a single frame. As such, avoid playing sounds or spawning objects unless isRealtime is true.
    /// * You can still use Update for things that don't affect gameplay, such as visual effects.
    /// </summary>
    public void Tick(float deltaTime, PlayerInput input, bool isRealtime)
    {
        Vector3 movementDirection = new Vector3(movementSpeed * input.horizontal, 0f, movementSpeed * input.vertical);

        transform.position += movementDirection * deltaTime; // don't use Time.deltaTime!

        if (movementDirection.sqrMagnitude > 0f)
            transform.forward = movementDirection;
    }

    /// <summary>
    /// Returns the ticker owned by this player
    /// </summary>
    public ITickerBase GetTicker()
    {
        return ticker;
    }

    /// <summary>
    /// Used to restore to a previous state by the ticker. Store all important ticker-affected information here.
    /// </summary>
    public PlayerState MakeState()
    {
        return new PlayerState()
        {
            position = transform.position,
            rotation = transform.rotation
        };
    }

    /// <summary>
    /// Used to restore a previous state by the ticker. Apply all important ticker-affected information here.
    /// Remember that for physics simulations, you may need to call Physics.SyncTransforms() or just turn Physics.autoSyncTransforms on.
    /// </summary>
    public void ApplyState(PlayerState state)
    {
        transform.position = state.position;
        transform.rotation = state.rotation;
    }
#endregion

    [Command(channel = Channels.Unreliable)]
    private void CmdPlayerInput(PlayerInput[] inputs, float[] times)
    {
        float lastLatest = ticker.inputTimeline.LatestTime;
        ticker.PushInputPack(new TickerInputPack<PlayerInput>(inputs, times));

        if (ticker.inputTimeline.LatestTime > lastLatest)
            timeOfLastReceivedClientInput = Time.time;
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcPlayerState(PlayerState state, PlayerInput input, float time, float serverExtrapolation)
    {
        if (!NetworkServer.active) // don't affect host player
        {
            ticker.PushInput(input, time);
            ticker.Reconcile(state, time, TickerSeekFlags.DontConfirm);
            timeOnServer = time + serverExtrapolation;
            timeOfLastReceivedServerUpdate = Time.time;
        }
    }

    private bool IsNetUpdate() => (int)((Time.time - Time.deltaTime) * netUpdateRate) != (int)(Time.time * netUpdateRate);
}
