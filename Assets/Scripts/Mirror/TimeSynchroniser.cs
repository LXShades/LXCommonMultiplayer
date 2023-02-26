using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles client/server time synchronisation for predictive play. This is hard to explain so bullet points:
/// 
/// * Server controls the GameTime and calls the overridable Tick() function.
/// * Clients listen to cues from the server, figuring out what GameTime they should run, and calling its Tick() function accordingly as well.
/// * Clients' GameTime runs slightly ahead of the server depending on latency.
/// * -> A client will run enough time ahead that its inputs can reliably reach the server before the server runs the same GameTime.
/// * -> To do this, the server tells the client its (CGT - SGT) offset, where CGT is client GameTime and SGT is server GameTime, which should always be above zero.
/// * -> A client can give itself more time if it detects that offset fluctuates too much.
/// * GameTime is stored in doubles because we use it to calculate deltas, which are tiny. In terms of deltas, GameTimes will get comparatively imprecise within an hour.
/// 
/// </summary>
public class TimeSynchroniser : NetworkBehaviour
{
    [Header("Automatic client time calculation")]
    [Tooltip("When enabled, time is automatically continually adjusted based on a sampling of the client-server time offset history")]
    public bool useAutomaticClientTimePrediction = true;

    [Header("Automatic client time calculation (Advanced)")]
    // todo: misleading, actual measuring period is currently secondsPerTimeOffsetRecalculation + recalculationCooldownTime
    [Tooltip("Defines how far back our client time offset history goes, with the offsets deciding the best game time to use for the server to reliably receive everything")]
    public float clientTimeOffsetHistorySamplePeriod = 3f;
    [Tooltip("While time adjustments are occurring, the dataset during this period following it is ignored to avoid the adjustments interfering with the jitter estimation")]
    public float recalculationCooldownTime = 0.6f;
    [Tooltip("Time offsets are recalculated at this frequency, triggering time adjustments.")]
    public float secondsPerTimeOffsetRecalculation = 1f;
    [Tooltip("An extra bit of prediction buffering, fixed, added to the calculated amount")]
    public float additionalPredictionAmount = 0.01f;

    public bool useCurvedAdjustments = true;
    [Tooltip("If using curved adjustments, when a smooth time adjustment occurs, this is the damping")]
    public float curvedAdjustmentDamping = 0.3f;
    [Tooltip("If not using curved adjustments, when a smooth time adjustment occurs, this is the speed of the adjustment, in seconds per second")]
    public float linearAdjustmentSpeed = 0.07f;

    [Header("Manual client time calculation")]
    [Tooltip("If useAutomaticClientExtrapolation=false, this is a fixed offset the client adds to the server gameTime when it receives time updates")]
    public float manualClientPredictionAmount = 0.5f;

    [Header("Client/server update frequency")]
    [Tooltip("How often time messages are sent to clients or the server per second. Does not necessarily need to match the tick rate.")]
    public int syncsPerSecond = 30;

    /// <summary>
    /// How far ahead the client is prediction compared to the server, when automatic client time prediction is on
    /// </summary>
    public float autoCalculatedClientPrediction => (float)(Time.timeAsDouble + autoCalculatedTimeOffset - timeOnServer - (Time.timeAsDouble - timeOfLastServerUpdate));

    /// <summary>
    /// Tiem offset that you can add to Time.timeAsDouble to get the current client gameTime
    /// </summary>
    public double autoCalculatedTimeOffset { get; private set; }

    /// <summary>
    /// Time on the server, according to the last update we got from the server with no prediction/extrapolation
    /// </summary>
    public double timeOnServer { get; protected set; }

    /// <summary>
    /// Our time after the last tick we ran, with prediction depending on settings
    /// </summary>
    public double timeOnLastUpdate { get; protected set; }

    /// <summary>
    /// Time.timeAsDouble that we last received the server time (todo: use realtime?)
    /// </summary>
    public double timeOfLastServerUpdate { get; protected set; }

    /// <summary>
    /// [client] The last client offset we received from the server (an offset of how far ahead or behind our inputs were when the server received them)
    /// </summary>
    public double lastAckedClientOffset => clientTimeOffsetHistory.Latest;

    /// <summary>
    /// When a time adjustment is ongoing, this is an offset that's being progressively added to autoCalculatedTimeOffset (after being set, this value gradually gravitates towards 0)
    /// </summary>
    public double remainingTimeAdjustment { get; protected set; }

    private float remainingTimeAdjustmentVelocity = 0f;

    // realtimeSinceStartupAsDouble of the last time adjustment
    private double timeOfLastTimeAdjustment = 0;

    protected TimelineList<float> clientTimeOffsetHistory = new TimelineList<float>();

    protected Dictionary<NetworkConnectionToClient, double> lastClientGameTime = new Dictionary<NetworkConnectionToClient, double>();

    /// <summary>
    /// Sent on Update after the current game time and delta time are set.
    /// 
    /// TIME IS A LIE! gameTime may speed up and slow down on clients to stay in sync. It MAY EVEN RUN BACKWARDS during desyncs! deltaTime may be negative, or huge, in such cases.
    /// </summary>
    public virtual void OnUpdate(double gameTime, float deltaTime)
    {
    }

    /// <summary>
    /// Called whenever the server or client sends a game time update
    /// </summary>
    public virtual void OnSentTimeSync()
    {
    }

    protected virtual void Update()
    {
        double lastFrameTime = timeOnLastUpdate;

        // Refresh networked timers, run ticks if necessary
        if (NetworkServer.active)
        {
            // We kind of fake this info
            timeOnServer = Time.timeAsDouble;
            timeOfLastServerUpdate = Time.timeAsDouble;

            RunUpdate(Time.timeAsDouble);
        }
        else
        {
            if (!useAutomaticClientTimePrediction)
            {
                RunUpdate(timeOnServer + Time.timeAsDouble - timeOfLastServerUpdate + manualClientPredictionAmount);
            }
            else
            {
                UpdateTimeAdjustment();

                RunUpdate(Time.timeAsDouble + autoCalculatedTimeOffset);
            }
        }

        if (TimeTool.IsTick(timeOnLastUpdate, lastFrameTime, syncsPerSecond))
        {
            if (NetworkServer.active)
            {
                // send current time and per-client time offset to clients
                foreach (var conn in NetworkServer.connections)
                {
                    if (conn.Value != null && conn.Value.isReady && conn.Value != NetworkServer.localConnection)
                    {
                        double clientTime;
                        lastClientGameTime.TryGetValue(conn.Value, out clientTime);

                        TargetTimeAndOffset(conn.Value, timeOnLastUpdate, (float)(clientTime - timeOnLastUpdate));
                    }
                }
            }
            else
            {
                // tell server our current time
                CmdTime(timeOnLastUpdate);
            }

            OnSentTimeSync();
        }
    }

    private List<float> tempFloatList = new List<float>();

    private void UpdateTimeAdjustment()
    {
        // ping extrapolation is based on how far ahead/behind the server received our inputs
        // the server reports the offsets and we collect them in inputTimeOffsetHistory
        // it is in the format clientInputTime - serverTime, where clientInputTime is the game time of our intended input
        // and serverTime is the game time of the server at the moment the input was acked
        // if the offset is > 0, our inputs were coming in early and we can subtract a bit from our extrapolation
        // if the offset is < 0, our inputs were coming in late and we need to add a bit to our extrapolation
        // we continually adjust our game time based on this feedback
        float timeAdjustmentSpeed = 0.07f; // in seconds per second... per second (if 0.1, it takes a second to accelerate or slow down time by 0.1)
        float maxTimeAdjustmentDuration = 0.5f; // never spend more than this long adjusting time

        if (Time.realtimeSinceStartupAsDouble > timeOfLastTimeAdjustment + secondsPerTimeOffsetRecalculation + recalculationCooldownTime)
        {
            // Update smoothing
            // Client time smoothing and adjustment
            tempFloatList.Clear();
            for (int i = 0; i < clientTimeOffsetHistory.Count && clientTimeOffsetHistory.TimeAt(i) >= Time.timeAsDouble - secondsPerTimeOffsetRecalculation; i++)
                tempFloatList.Add(clientTimeOffsetHistory[i]);
            tempFloatList.Sort();

            if (tempFloatList.Count > 0)
                remainingTimeAdjustment = tempFloatList[(int)(tempFloatList.Count * 0.02f)] - additionalPredictionAmount;

            // Server ping averaging
            /*tempFloatList.Clear();
            for (int i = 0; i < clientServerTimeOffsetHistory.Count && clientServerTimeOffsetHistory.TimeAt(i) >= Time.realtimeSinceStartupAsDouble - measurementPeriod; i++)
                tempFloatList.Add(clientServerTimeOffsetHistory[i]);
            tempFloatList.Sort();

            if (tempSortedList.Count > 0)
            {
                double targetServerPredictedTime = Time.timeAsDouble + currentClientServerTimeOffset - nextTimeAdjustment;
                double targetServerTimeMedian = Time.timeAsDouble + tempSortedList[tempSortedList.Count / 2];

                smoothLocalPlayerPing = (float)(targetServerPredictedTime - targetServerTimeMedian);
            }*/
        }

        if (remainingTimeAdjustment != 0d)
        {
            if (!useCurvedAdjustments)
            {
                // if we can adjust the time smoothly within maxTimeAdjustmentDuration
                if (Math.Abs(remainingTimeAdjustment) / timeAdjustmentSpeed < maxTimeAdjustmentDuration)
                {
                    float smoothed = Mathf.Clamp((float)remainingTimeAdjustment, -timeAdjustmentSpeed * Time.unscaledDeltaTime, timeAdjustmentSpeed * Time.unscaledDeltaTime);
                    autoCalculatedTimeOffset -= smoothed;
                    remainingTimeAdjustment -= smoothed;
                }
                else
                {
                    autoCalculatedTimeOffset -= remainingTimeAdjustment;
                    remainingTimeAdjustment = 0d;
                }
            }
            else
            {
                if (Math.Abs(remainingTimeAdjustment) < 1f)
                {
                    double oldRemainingTimeAdjustment = remainingTimeAdjustment;
                    remainingTimeAdjustment = Mathf.SmoothDamp((float)remainingTimeAdjustment, 0f, ref remainingTimeAdjustmentVelocity, curvedAdjustmentDamping);

                    if (Math.Abs(remainingTimeAdjustment) < 0.0001f)
                        remainingTimeAdjustment = 0f; // avoid going forever tiny tiny

                    autoCalculatedTimeOffset += remainingTimeAdjustment - oldRemainingTimeAdjustment;
                }
                else
                {
                    autoCalculatedTimeOffset -= remainingTimeAdjustment;
                    remainingTimeAdjustment = 0d;
                    remainingTimeAdjustmentVelocity = 0f;
                }
            }

            timeOfLastTimeAdjustment = Time.realtimeSinceStartupAsDouble;
        }
    }

    /// <summary>
    /// Inform the clients of the current gameTime on the server, and the offset of their own gameTime compared to the server (CGT - SGT)
    /// </summary>
    [TargetRpc(channel = Channels.Unreliable)]
    private void TargetTimeAndOffset(NetworkConnection target, double gameTime, float clientOffset)
    {
        // record the time offset - we'll use this to continually rebalance the game speed
        clientTimeOffsetHistory.Insert(Time.timeAsDouble, clientOffset);
        clientTimeOffsetHistory.TrimBefore(Time.timeAsDouble - clientTimeOffsetHistorySamplePeriod);

        timeOnServer = gameTime;
        timeOfLastServerUpdate = Time.timeAsDouble;
    }

    /// <summary>
    /// Inform the server of the current client's time
    /// </summary>
    [Command(channel = Channels.Unreliable, requiresAuthority = false)]
    private void CmdTime(double gameTime, NetworkConnectionToClient connection = null)
    {
        lastClientGameTime[connection] = gameTime;
    }

    private void RunUpdate(double gameTime)
    {
        OnUpdate(gameTime, (float)(gameTime - timeOnLastUpdate));
        timeOnLastUpdate = gameTime;
    }
}
