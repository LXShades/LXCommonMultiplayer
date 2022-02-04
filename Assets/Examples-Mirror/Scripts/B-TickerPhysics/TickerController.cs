using Mirror;
using UnityEngine;


namespace UnityMultiplayerEssentials.Examples.Mirror
{
    public class TickerController : NetworkBehaviour
    {
        PhysicsTickable physTickable;

        Ticker<PhysicsTickable.Input, PhysicsTickable.State> physTicker;

        private double timeOnServer;
        private double timeOfLastServerUpdate;

        public bool useAutomaticClientExtrapolation = false;
        public float clientExtrapolation = 0.5f;
        public float autoCalculatedClientExtrapolation => (float)(Time.timeAsDouble + autoCalculatedTimeExtrapolation - timeOnServer - (Time.timeAsDouble - timeOfLastServerUpdate));
        public float autoCalculatedTimeExtrapolation { get; private set; }

        public int updatesPerSecond = 30;

        public double playbackTime => physTicker != null ? physTicker.playbackTime : 0f;

        private TimelineList<double> serverTimeHistory = new TimelineList<double>();

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
                physTicker.Seek(Time.timeAsDouble);
            else
            {
                if (!useAutomaticClientExtrapolation)
                {
                    physTicker.Seek(timeOnServer + Time.timeAsDouble - timeOfLastServerUpdate + clientExtrapolation, TickerSeekFlags.IgnoreDeltas);
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
                        if (TimeTool.IsTick(Time.timeAsDouble, Time.deltaTime, 2))
                        {
                            float bestTimeOffset = float.MaxValue;

                            for (int i = 0; i < localPhysPlayer.inputTimeOffsetHistoryOnClient.Count; i++)
                                bestTimeOffset = Mathf.Min(localPhysPlayer.inputTimeOffsetHistoryOnClient[i], bestTimeOffset);

                            autoCalculatedTimeExtrapolation -= bestTimeOffset - 0.005f;
                        }
                    }

                    physTicker.Seek(Time.time + autoCalculatedTimeExtrapolation, TickerSeekFlags.IgnoreDeltas);
                }
            }

            // send target ticker's state to clients
            if (NetworkServer.active && TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, updatesPerSecond))
                RpcState(physTicker.lastConfirmedState, physTicker.confirmedStateTime, (float)(Time.timeAsDouble - physTicker.confirmedStateTime));
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcState(PhysicsTickable.State state, double time, float serverExtrapolation)
        {
            // client-only
            if (!NetworkServer.active)
            {
                serverTimeHistory.Insert(Time.timeAsDouble, time + serverExtrapolation);
                serverTimeHistory.Trim(Time.timeAsDouble - 3d, Time.timeAsDouble + 3d);

                physTicker.Reconcile(state, time, 0/*TickerSeekFlags.DontConfirm*/);
                timeOnServer = time + serverExtrapolation;
                timeOfLastServerUpdate = Time.time;
            }
        }
    }
}