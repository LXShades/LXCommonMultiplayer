using Mirror;
using UnityEngine;


namespace UnityMultiplayerEssentials.Examples.Mirror
{
    public class TickController : NetworkBehaviour
    {
        private PhysicsTickable physTickable;

        private Timeline physTimeline;
        private Timeline.Entity<PhysicsTickable.State, PhysicsTickable.Input> physTimelineEntity;

        private double timeOnServer;
        private double timeOfLastServerUpdate;

        public TimelineSettings tickerSettings = TimelineSettings.Default;

        public bool useAutomaticClientExtrapolation = false;
        public float clientExtrapolation = 0.5f;
        public float autoCalculatedClientExtrapolation => (float)(Time.timeAsDouble + autoCalculatedTimeExtrapolation - timeOnServer - (Time.timeAsDouble - timeOfLastServerUpdate));
        public float autoCalculatedTimeExtrapolation { get; private set; }

        public int updatesPerSecond = 30;

        public double playbackTime => physTimeline != null ? physTimeline.playbackTime : 0f;

        private TimelineTrack<double> serverTimeHistory = new TimelineTrack<double>();

        private void Start()
        {
            physTickable = FindObjectOfType<PhysicsTickable>();

            physTimeline = Timeline.CreateSingle("PhysicsTimeline", physTickable, out physTimelineEntity);
            physTimeline.settings = tickerSettings;
        }

        // Update is called once per frame
        void Update()
        {
            // seek physics
            if (NetworkServer.active)
                physTimeline.Seek(Time.timeAsDouble);
            else
            {
                if (!useAutomaticClientExtrapolation)
                {
                    physTimeline.Seek(timeOnServer + Time.timeAsDouble - timeOfLastServerUpdate + clientExtrapolation, TimelineSeekFlags.IgnoreDeltas);
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

                    physTimeline.Seek(Time.time + autoCalculatedTimeExtrapolation, TimelineSeekFlags.IgnoreDeltas);
                }
            }

            // send target ticker's state to clients
            if (NetworkServer.active && TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, updatesPerSecond))
                RpcState(physTimelineEntity.stateTrack.Latest, physTimelineEntity.stateTrack.LatestTime, (float)(Time.timeAsDouble - physTimelineEntity.stateTrack.LatestTime));
        }

        [ClientRpc(channel = Channels.Unreliable)]
        private void RpcState(PhysicsTickable.State state, double time, float serverExtrapolation)
        {
            // client-only
            if (!NetworkServer.active)
            {
                serverTimeHistory.Insert(Time.timeAsDouble, time + serverExtrapolation);
                serverTimeHistory.Trim(Time.timeAsDouble - 3d, Time.timeAsDouble + 3d);

                physTimelineEntity.ConfirmStateAt(state, time);
                timeOnServer = time + serverExtrapolation;
                timeOfLastServerUpdate = Time.time;
            }
        }
    }
}