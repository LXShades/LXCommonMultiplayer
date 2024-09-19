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
    public class Player : NetworkBehaviour, ITickable<Player.State, Player.Input>
    {
        public struct State : ITickerState<State>
        {
            public Vector3 position;
            public Quaternion rotation;

            public void DebugDraw(Color colour) { } // doesn't necessarily need implementing

            public bool Equals(State other)
            {
                return other.position == position && other.rotation == rotation;
            }
        }

        public struct Input : ITickerInput<Input>
        {
            public float horizontal;
            public float vertical;

            public Input GenerateLocal()
            {
                return new Input()
                {
                    horizontal = UnityEngine.Input.GetAxisRaw("Horizontal"),
                    vertical = UnityEngine.Input.GetAxisRaw("Vertical")
                };
            }

            public Input WithDeltas(Input previousInput) => this;

            public Input WithoutDeltas() => this;
        }

        public float movementSpeed = 1f;

        public int netUpdateRate = 30;

        public TimelineSettings tickerSettings = TimelineSettings.Default;

        private Movement movement;

        private double timeOnServer;

        private double timeOfLastReceivedServerUpdate;
        private double timeOfLastReceivedClientInput;

        Timeline timeline;
        Timeline.Entity<State, Input> timelineEntity;

        private void Awake()
        {
            timeline = Timeline.CreateSingle(name, this, out timelineEntity);
            timeline.settings = tickerSettings;

            movement = GetComponent<Movement>();
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                // build inputs, send to the ticker
                Input nextInput = new Input().GenerateLocal();

                timelineEntity.seekFlags = EntitySeekFlags.None;
                timelineEntity.InsertInput(nextInput, Time.time);

                // seek to the current Time.time. this may amount to a mixture of reverting/confirming states and ticking forward with delta time
                timeline.Seek(Time.time);

                // send our inputs and times to the server
                if (IsNetUpdate())
                {
                    TickerInputPack<Input> inputPacks = timelineEntity.MakeInputPack(0.5f);

                    if (!NetworkServer.active) // host player shouldn't send inputs
                        CmdPlayerInput(inputPacks.inputs, inputPacks.times);
                }
            }
            else
            {
                timelineEntity.seekFlags = EntitySeekFlags.NoInputDeltas; // We don't know all the inputs of non-local characters, so we don't assume we know their input deltas

                if (NetworkServer.active)
                {
                    // server receiving client's state - just process the inputs, moving to the latest client time (no speedhack tests)
                    timeline.Seek(timelineEntity.inputTrack.LatestTime);
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

                    timeline.Seek(extrapolatedTimeOnServer, TimelineSeekFlags.None);
                }
            }

            if (NetworkServer.active && IsNetUpdate())
                RpcPlayerState(timelineEntity.stateTrack.Latest, timelineEntity.inputTrack.Latest, timelineEntity.stateTrack.LatestTime, (float)Math.Min(isLocalPlayer ? Time.timeAsDouble - timelineEntity.stateTrack.LatestTime: Time.timeAsDouble - timeOfLastReceivedClientInput, 0.5f));
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
        public void Tick(float deltaTime, Input input, TickInfo tickInfo)
        {
            Vector3 movementDirection = new Vector3(movementSpeed * input.horizontal, 0f, movementSpeed * input.vertical);

            // "movement" is kind of like a character controller, it lets us move with collision
            // it's very useful for client prediction because we can do it whenever we want with no physics timing restrictions
            movement.Move(movementDirection * deltaTime, out _, tickInfo); // don't use Time.deltaTime!

            if (movementDirection.sqrMagnitude > 0f)
                transform.forward = movementDirection;
        }

        /// <summary>
        /// Copy the current state to a PlayerState struct. Called by the ticker. You should store all important Tick-affected values here.
        /// </summary>
        public State MakeState()
        {
            return new State()
            {
                position = transform.position,
                rotation = transform.rotation
            };
        }

        /// <summary>
        /// Restore a previous state. Called by the ticker. You should store all important Tick-affected values here.
        /// Remember that for physics simulations, you may need to call Physics.SyncTransforms() or just turn Physics.autoSyncTransforms on.
        /// </summary>
        public void ApplyState(State state)
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
        private void CmdPlayerInput(Input[] inputs, double[] times)
        {
            double lastLatest = timelineEntity.inputTrack.LatestTime;
            timelineEntity.InsertInputPack(new TickerInputPack<Input>(inputs, times));

            if (timelineEntity.inputTrack.LatestTime > lastLatest)
                timeOfLastReceivedClientInput = Time.timeAsDouble;
        }

        /// <summary>
        /// Receives player state from the server. When received we immediately reconcile to our local time, replaying Ticks between the server's time and our own.
        /// </summary>
        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcPlayerState(State state, Input input, double time, float serverExtrapolation)
        {
            if (!NetworkServer.active) // don't affect host player
            {
                timelineEntity.InsertInput(input, time);
                timelineEntity.StoreStateAt(state, time);
                timeOnServer = time + serverExtrapolation;
                timeOfLastReceivedServerUpdate = Time.timeAsDouble;
            }
        }

        private bool IsNetUpdate() => TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, netUpdateRate);
        #endregion
    }
}