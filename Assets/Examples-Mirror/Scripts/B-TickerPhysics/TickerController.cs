using Mirror;
using UnityEngine;


namespace MultiplayerToolset.Examples.Mirror
{
    public class TickerController : NetworkBehaviour
    {
        PhysicsTickable physTickable;

        Ticker<PhysicsTickable.Input, PhysicsTickable.State> physTicker;

        private float timeOnServer;
        private float timeOfLastServerUpdate;

        public bool useAutomaticClientExtrapolation = false;
        public float clientExtrapolation = 0.5f;
        public float autoCalculatedClientExtrapolation => Time.time + autoCalculatedTimeExtrapolation - timeOnServer - (Time.time - timeOfLastServerUpdate);
        public float autoCalculatedTimeExtrapolation { get; private set; }

        public int updatesPerSecond = 30;

        public float playbackTime => physTicker != null ? physTicker.playbackTime : 0f;

        private TimelineList<float> serverTimeHistory = new TimelineList<float>();

        private void Start()
        {
            physTickable = FindObjectOfType<PhysicsTickable>();
            physTicker = physTickable.GetTicker() as Ticker<PhysicsTickable.Input, PhysicsTickable.State>;
        }

        // Update is called once per frame
        void Update()
        {
            // seek physics
            if (NetworkServer.active)
                physTicker.Seek(Time.time, Time.time);
            else
            {
                if (!useAutomaticClientExtrapolation)
                {
                    physTicker.Seek(timeOnServer + Time.time - timeOfLastServerUpdate + clientExtrapolation, timeOnServer, TickerSeekFlags.IgnoreDeltas);
                }
                else
                {
                    // ping extrapolation is based on how far ahead/behind the server received our inputs
                    // the server reports the offsets and we collect them in inputTimeOffsetHistory
                    // it is in the format clientInputTime - serverTime, where clientInputTime is the game time of our intended input
                    // and serverTime is the game time of the server at the moment the input was acked
                    // if the offset is > 0, our inputs were coming in early and we can subtract a bit from our extrapolation
                    // if the offset is < 0, our inputs were coming in late and we need to add a bit to our extrapolation
                    // we continually adjust our game time based on this feedback
                    if (NetworkClient.localPlayer && NetworkClient.localPlayer.TryGetComponent(out PhysicsPlayer localPhysPlayer))
                    {
                        if (TimeTool.IsTick(Time.time, Time.deltaTime, 2))
                        {
                            float bestTimeOffset = float.MaxValue;

                            for (int i = 0; i < localPhysPlayer.inputTimeOffsetHistoryOnClient.Count; i++)
                                bestTimeOffset = Mathf.Min(localPhysPlayer.inputTimeOffsetHistoryOnClient[i], bestTimeOffset);

                            autoCalculatedTimeExtrapolation -= bestTimeOffset - 0.005f;
                        }
                    }

                    physTicker.Seek(Time.time + autoCalculatedTimeExtrapolation, timeOnServer, TickerSeekFlags.IgnoreDeltas);
                }
            }

            // send target ticker's state to clients
            if (NetworkServer.active && TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, updatesPerSecond))
                RpcState(physTicker.lastConfirmedState, physTicker.confirmedStateTime, Time.time - physTicker.confirmedStateTime);
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcState(PhysicsTickable.State state, float time, float serverExtrapolation)
        {
            // client-only
            if (!NetworkServer.active)
            {
                serverTimeHistory.Insert(Time.time, time + serverExtrapolation);
                serverTimeHistory.Trim(Time.time - 3f, Time.time + 3f);

                physTicker.Reconcile(state, time, 0/*TickerSeekFlags.DontConfirm*/);
                timeOnServer = time + serverExtrapolation;
                timeOfLastServerUpdate = Time.time;
            }
        }
    }
}