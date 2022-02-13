using Mirror;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles client/server time synchronisation for predictive play. This is hard to explain so bullet points:
/// 
/// * Server controls the GameTime and calls the overridable Tick() function.
/// * Clients listen to cues from the server, figuring out what GameTime they should run, and calling its Tick() function accordingly as well.
/// * Clients' GameTime runs slightly ahead of the server depending on latency.
/// * -> A client will run enough time ahead that its inputs can reliably reach the server before the server runs the same GameTime.
/// * -> To do this, the server tells the client its (CGT - SGT) offset, where CGT is client GameTime and SGH is server GameTime, which should always be above zero.
/// * -> A client can give itself more time if it detects that offset fluctuates too much.
/// * GameTime is stored in doubles because we use it to calculate deltas, which are tiny. In terms of deltas, GameTimes will get comparatively imprecise within an hour.
/// 
/// </summary>
public class TickSynchroniser : NetworkBehaviour
{
    [Header("Automatic client time calculation")]
    public bool useAutomaticClientExtrapolation = true;
    [Tooltip("Defines how far back our client time offset history goes, with the offsets deciding the best game time to use for the server to reliably receive everything")]
    public float clientTimeOffsetHistorySamplePeriod = 3f;

    [Header("Manual client time calculation")]
    [Tooltip("If useAutomaticClientExtrapolation=false, this is a fixed offset the client adds to the server gameTime when it receives time updates")]
    public float clientExtrapolation = 0.5f;

    [Header("Client/server update frequency")]
    [Tooltip("How often time messages are sent to clients or the server per second. Does not necessarily need to match the tick rate.")]
    public int syncsPerSecond = 30;

    public float autoCalculatedClientExtrapolation => (float)(Time.timeAsDouble + autoCalculatedTimeExtrapolation - timeOnServer - (Time.timeAsDouble - timeOfLastServerUpdate));
    public float autoCalculatedTimeExtrapolation { get; private set; }

    protected double timeOnServer;
    protected double timeOfLastServerUpdate;
    public double lastTickTime { get; protected set; } // last local tick time

    protected TimelineList<float> clientTimeOffsetHistory = new TimelineList<float>();

    protected Dictionary<NetworkConnectionToClient, double> lastClientGameTime = new Dictionary<NetworkConnectionToClient, double>();

    /// <summary>
    /// Updates the current game time and delta time.
    /// 
    /// WARNING! Unfortunately, networked time is not reliable--as the server and client attempts to resync, time could occasionally go backwards! deltaTime may be negative in such cases.
    /// </summary>
    public virtual void Tick(double gameTime, float deltaTime)
    {
    }

    /// <summary>
    /// Called whenever the server or client sends a game time update
    /// </summary>
    public virtual void OnSync()
    {
    }

    protected virtual void Update()
    {
        double lastFrameTickTime = lastTickTime;

        // Refresh networked timers, run ticks if necessary
        if (NetworkServer.active)
        {
            RunTick(Time.timeAsDouble);
        }
        else
        {
            if (!useAutomaticClientExtrapolation)
            {
                RunTick(timeOnServer + Time.timeAsDouble - timeOfLastServerUpdate + clientExtrapolation);
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
                if (TimeTool.IsTick(Time.timeAsDouble, Time.deltaTime, 2))
                {
                    float worstKnownTimeOffset = float.MaxValue;

                    for (int i = 0; i < clientTimeOffsetHistory.Count; i++)
                        worstKnownTimeOffset = Mathf.Min(clientTimeOffsetHistory[i], worstKnownTimeOffset);

                    autoCalculatedTimeExtrapolation -= worstKnownTimeOffset - 0.005f; // magic number, todo?
                }

                RunTick(Time.timeAsDouble + autoCalculatedTimeExtrapolation);
            }
        }

        if (TimeTool.IsTick(lastTickTime, lastFrameTickTime, syncsPerSecond))
        {
            if (NetworkServer.active)
            {
                // send current time and per-client time offset to clients
                foreach (var conn in NetworkServer.connections)
                {
                    if (conn.Value != null && conn.Value.isReady && conn.Value != NetworkServer.localConnection)
                    {
                        double clientTime = 0f;
                        lastClientGameTime.TryGetValue(conn.Value, out clientTime);

                        TargetTimeAndOffset(conn.Value, lastTickTime, (float)(clientTime - lastTickTime));
                    }
                }
            }
            else
            {
                // tell server our current time
                CmdTime(lastTickTime);
            }

            OnSync();
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

    private void RunTick(double gameTime)
    {
        Tick(gameTime, (float)(gameTime - lastTickTime));
        lastTickTime = gameTime;
    }
}
