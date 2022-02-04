using Mirror;
using System;
using UnityEngine;


namespace UnityMultiplayerEssentials.Examples.Mirror
{
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

        private Movement movement;

        private double timeOnServer;

        private double timeOfLastReceivedServerUpdate;
        private double timeOfLastReceivedClientInput;

        Ticker<PlayerInput, PlayerState> ticker;

        private void Awake()
        {
            ticker = new Ticker<PlayerInput, PlayerState>(this);
            ticker.settings = tickerSettings;

            movement = GetComponent<Movement>();
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                // build inputs, send to the ticker
                PlayerInput nextInput = new PlayerInput().GenerateLocal();

                ticker.InsertInput(nextInput, Time.time);

                // seek to the current Time.time. this may amount to a mixture of reverting/confirming states and ticking forward with delta time
                ticker.Seek(Time.time);

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
                    ticker.Seek(ticker.inputTimelineBase.LatestTime);
                }
                else
                {
                    // move the player to the latest time it had on the server
                    // add Time.time - timeOfLastReceivedServerUpdates to smoothly extrapolate other players.
                    // note that this isn't always an "extrapolation" - we have their latest input, but that input wasn't processed by the server yet
                    // by the time we receive the next state from the server, the server have processed that input, as we did too, so it's almost more like an interpolation.

                    // another note: the server also added its Time.time - timeOfLastReceivedClientInput
                    // both are needed to extrapolate it as expected
                    double extrapolatedTimeOnServer = timeOnServer + Time.timeAsDouble - timeOfLastReceivedServerUpdate;

                    ticker.Seek(extrapolatedTimeOnServer, TickerSeekFlags.IgnoreDeltas);
                }
            }

            if (NetworkServer.active && IsNetUpdate())
                RpcPlayerState(ticker.lastConfirmedState, ticker.latestInput, ticker.confirmedStateTime, (float)Math.Min(isLocalPlayer ? Time.timeAsDouble - ticker.confirmedStateTime : Time.timeAsDouble - timeOfLastReceivedClientInput, 0.5f));
        }

        #region ITickable
        /// <summary>
        /// Ticks the object. In a networked game, you may put most important gameplay things in this function, as though it were an Update function.
        /// 
        /// * Do NOT use Time.deltaTime, or anything from Time (except debugging info)! Use provided deltaTime instead.
        /// * Do not read live inputs from here (except, again, debugging). Put everything you need into TInput and ensure it is passed into the ticker timeline.
        /// * Always remember Tick() may be called multiple times in a single frame. As such, avoid playing sounds or spawning objects unless isRealtime is true.
        /// * As such, always remember Tick() may be called on past/outdated states.
        /// * You can still use Update for things that don't affect gameplay, such as visual effects.
        /// </summary>
        public void Tick(float deltaTime, PlayerInput input, TickInfo tickInfo)
        {
            Vector3 movementDirection = new Vector3(movementSpeed * input.horizontal, 0f, movementSpeed * input.vertical);

            // "movement" is kind of like a character controller, it lets us move with collision
            // it's very useful for client prediction because we can do it whenever we want with no physics timing restrictions
            movement.Move(movementDirection * deltaTime, out _, tickInfo); // don't use Time.deltaTime!

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
        /// Copy the current state to a PlayerState struct. Called by the ticker. You should store all important Tick-affected values here.
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
        /// Restore a previous state. Called by the ticker. You should store all important Tick-affected values here.
        /// Remember that for physics simulations, you may need to call Physics.SyncTransforms() or just turn Physics.autoSyncTransforms on.
        /// </summary>
        public void ApplyState(PlayerState state)
        {
            transform.position = state.position;
            transform.rotation = state.rotation;
        }
        #endregion

        #region Networking
        /// <summary>
        /// Sends player inputs to the server. The input pack may contain old inputs in case packets are missed.
        /// </summary>
        [Command(channel = Channels.Unreliable)]
        private void CmdPlayerInput(PlayerInput[] inputs, double[] times)
        {
            double lastLatest = ticker.inputTimeline.LatestTime;
            ticker.InsertInputPack(new TickerInputPack<PlayerInput>(inputs, times));

            if (ticker.inputTimeline.LatestTime > lastLatest)
                timeOfLastReceivedClientInput = Time.timeAsDouble;
        }

        /// <summary>
        /// Receives player state from the server. When received we immediately reconcile to our local time, replaying Ticks between the server's time and our own.
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcPlayerState(PlayerState state, PlayerInput input, double time, float serverExtrapolation)
        {
            if (!NetworkServer.active) // don't affect host player
            {
                ticker.InsertInput(input, time);
                ticker.Reconcile(state, time, TickerSeekFlags.DontConfirm);
                timeOnServer = time + serverExtrapolation;
                timeOfLastReceivedServerUpdate = Time.timeAsDouble;
            }
        }

        private bool IsNetUpdate() => TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, netUpdateRate);
        #endregion
    }
}