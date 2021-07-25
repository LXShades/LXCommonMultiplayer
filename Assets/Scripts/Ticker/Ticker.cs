using System;
using System.Text;
using UnityEngine;

public delegate void TickerEvent(bool isRealtime);

[Flags]
public enum TickerSeekFlags
{
    None = 0,

    /// <summary>
    /// Specifies that inputs should not use deltas. Useful when the full input history is not known, meaning a delta may not necessarily be correct
    /// 
    /// For example, a client receives an input for T=0 and T=1. At T=1 they extrapoalte the state noting that Jump has been pressed since T=0.
    /// However, they are unaware that jump was actually first pressed at T=0.5, and the state they received for T=1 has already jumped
    /// On the server, T=1 had no jump delta as T=0.5 already did that. On the client, T=1 is thought to have the jump delta despite being untrue, leading to inconsistency.
    /// </summary>
    IgnoreDeltas = 1,

    /// <summary>
    /// Specifies that states should not be confirmed during the seek--the state is allowed to diverge from the input feed's deltas
    /// 
    /// This is slightly more efficient as the character doesn't need to be rewound or fast-forwarded or to have its states confirmed and stored
    /// </summary>
    DontConfirm = 2
};

[System.Serializable]
public struct TickerSettings
{
    [Header("Tick")]
    [Tooltip("The maximum delta time to pass to tickable components")]
    public float maxDeltaTime;
    [Tooltip("The maximum amount of iterations we can run while extrapolating")]
    public int maxSeekIterations;

    [Header("Input")]
    [Tooltip("The maximum input rate in hz. If <=0, the input rate is unlimited. This should be restricted sensibly so that clients do not send too many inputs and save CPU.")]
    public int maxInputRate;

    [Header("Reconciling")]
    [Tooltip("Whether to reconcile even if the server's confirmed state matched the local state at the time")]
    public bool alwaysReconcile;

    [Header("History")]
    [Tooltip("How long to keep input, state, etc history in seconds. Should be able to fit in a bit more ")]
    public float historyLength;

    [Header("Debug")]
    public bool debugLogReconciles;

#if UNITY_EDITOR
    public float debugSelfReconcileDelay;
    public bool debugSelfReconcile;
    public bool debugDrawReconciles;
#else
    // just don't do these debug things in builds
    [NonSerialized]
    public float debugSelfReconcileDelay;
    [NonSerialized]
    public bool debugSelfReconcile;
    [NonSerialized]
    public bool debugDrawReconciles;
#endif

    public static TickerSettings Default = new TickerSettings()
    {
        maxDeltaTime = 0.03f,
        maxSeekIterations = 15,
        maxInputRate = 60,
        alwaysReconcile = false,
        historyLength = 1f,
        debugLogReconciles = false,
        debugSelfReconcileDelay = 0.3f,
        debugDrawReconciles = false,
        debugSelfReconcile = false
    };
}

public class Ticker<TInput, TState> : ITickerBase, ITickerStateFunctions<TState>, ITickerInputFunctions<TInput>
    where TInput : ITickerInput<TInput> where TState : ITickerState<TState>
{
    /// <summary>
    /// The name of the target
    /// </summary>
    public string targetName => (target is Component targetAsComponent) ? targetAsComponent.gameObject.name : "N/A";

    /// <summary>
    /// The target tickable component, class or struct
    /// </summary>
    public ITickable<TInput, TState> target;

    public TickerSettings settings = TickerSettings.Default;

    /// <summary>
    /// Whether this ticker has an owning player's input
    /// </summary>
    public bool hasInput => inputTimeline.Count > 0;

    /// <summary>
    /// The latest known input in this ticker
    /// </summary>
    public TInput latestInput => inputTimeline.Latest;

    /// <summary>
    /// The current playback time, based on no specific point of reference, but is expected to use the same time format as input and event history
    /// </summary>
    public float playbackTime { get; private set; }

    /// <summary>
    /// The realtime playback during the last Seek. This is mainly for debugging and doesn't affect current state
    /// </summary>
    public float realtimePlaybackTime { get; private set; }

    /// <summary>
    /// The current confirmed state time - the non-extrapolated playback time of the last input-confirmed state
    /// </summary>
    public float confirmedStateTime { get; private set; }

    /// <summary>
    /// Whether the ticker is temporarily paused. When paused, the Seek() function may run, but will always tick to the time it was originally
    /// </summary>
    public bool isDebugPaused { get; private set; }

    public TState lastConfirmedState { get; private set; }


    public readonly TimelineList<TInput> inputTimeline = new TimelineList<TInput>();

    public readonly TimelineList<TState> stateTimeline = new TimelineList<TState>();

    private readonly TimelineList<TickerEvent> eventTimeline = new TimelineList<TickerEvent>();

    // for our clueless friends in ITickerBase-land who don't know what types we're using. ;)
    public TimelineListBase inputTimelineBase => inputTimeline;
    public TimelineListBase stateTimelineBase => stateTimeline;

    public Ticker(ITickable<TInput, TState> target)
    {
        this.target = target;
        ConfirmCurrentState();
    }

    /// <summary>
    /// Inserts a single input into the input history. Old inputs at hte same time will be replaced.
    /// </summary>
    public void InsertInput(TInput input, float time)
    {
        float timeSinceLastInput = time - inputTimeline.LatestTime;

        if (settings.maxInputRate <= 0 || timeSinceLastInput >= 1f / settings.maxInputRate - 0.0001f)
        {
            // Add current player input to input history
            inputTimeline.Set(time, input);
        }
    }

    /// <summary>
    /// Stores an input pack into input history. Old inputs at the same time will be replaced.
    /// </summary>
    public void InsertInputPack(TickerInputPack<TInput> inputPack)
    {
        for (int i = inputPack.inputs.Length - 1; i >= 0; i--)
        {
            // TODO: max input rate enforcement
            inputTimeline.Set(inputPack.times[i], inputPack.inputs[i], 0.001f);
        }
    }

    /// <summary>
    /// Makes an input pack from input history
    /// </summary>
    public TickerInputPack<TInput> MakeInputPack(float maxLength)
    {
        return TickerInputPack<TInput>.MakeFromHistory(inputTimeline, maxLength);
    }

    /// <summary>
    /// Loads a state and sets the playback time to that state
    /// </summary>
    public void Rewind(TState state, float time)
    {
        int index = stateTimeline.IndexAt(time, 0.0001f);

        // do we need to reconcile (recalculate from this point next time we seek?)
        if (settings.alwaysReconcile || index == -1 || !stateTimeline[index].Equals(state))
        {
            if (settings.debugLogReconciles)
            {
                Debug.Log(
                    $"Reconcile {targetName}:\n" +
                    $"Time: {time.ToString("F2")}\n" +
                    $"Index: {index}\n" +
                    $"Diffs: {(index != -1 ? PrintStructDifferences("recv", "old", state, stateTimeline[index]) : "N/A")}");
            }

            target.ApplyState(state);
            lastConfirmedState = state;
            confirmedStateTime = time;
            playbackTime = time;
        }
    }

    /// <summary>
    /// Confirms the current character state. Needed to teleport or otherwise influence movement (except where events are used)
    /// </summary>
    public void ConfirmCurrentState()
    {
        lastConfirmedState = target.MakeState();
        confirmedStateTime = playbackTime;

        stateTimeline.Set(playbackTime, lastConfirmedState);
    }

    /// <summary>
    /// Seeks forward by the given deltaTime, if possible
    /// </summary>
    public void SeekBy(float deltaTime, float realtimePlaybackTime)
    {
        Seek(playbackTime + deltaTime, realtimePlaybackTime);
    }

    /// <summary>
    /// Ticks the player FORWARD only until the targetTime is reached, if possible
    /// This uses replayed inputs if those are available, otherwise extrapolates in steps up to maxDeltaTime length
    /// RealtimePlaybackTime describes the time after which e.g. sound effects and particles could be performed. This exists because if you have rewinded the state and are reconciling, you don't always want to replay those.
    ///  -> When hit during realtime, Tick() is called with isRealtime = true
    ///  -> When hit during realtime, TickerEvents are also called with isRealtime = true
    /// </summary>
    public void Seek(float targetTime, float realtimePlaybackTime, TickerSeekFlags flags = TickerSeekFlags.None)
    {
        Debug.Assert(settings.maxDeltaTime > 0f);

        if (isDebugPaused)
            targetTime = playbackTime;

        float initialPlaybackTime = playbackTime;

        if ((flags & TickerSeekFlags.DontConfirm) == 0 && confirmedStateTime < inputTimeline.LatestTime)
        {
            // Restore our actual non-extrapolated position
            target.ApplyState(lastConfirmedState);
            playbackTime = confirmedStateTime;
        }

        // Playback our latest movements
        try
        {
            int numIterations = 0;

            // Execute ticks, grabbing and consuming inputs if they are available, or using the latest inputs
            while (playbackTime < targetTime)
            {
                int index = inputTimeline.ClosestIndexBefore(playbackTime, 0.001f);
                TInput input;
                float deltaTime = Mathf.Min(targetTime - playbackTime, settings.maxDeltaTime);
                bool canConfirmState = false;
                bool isRealtime = false;
                float confirmableTime = 0f;

                if (index == -1)
                {
                    // Sometimes we fall behind of the inputs and only a crazy amount of extrapolation would get us there
                    // That is bad, but we have a half-baked solution which is to just skip to the oldest input we have and start from there. We need to get there somehow.
                    int nextAfter = inputTimeline.ClosestIndexAfter(playbackTime, 0.001f);

                    if (nextAfter != -1 && inputTimeline.TimeAt(nextAfter) < targetTime)
                    {
                        index = nextAfter;
                        playbackTime = inputTimeline.TimeAt(nextAfter);
                    }
                }

                if (index != -1)
                {
                    float inputTime = inputTimeline.TimeAt(index);

                    if (index > 0)
                    {
                        // tick using the deltas between inputs, or the delta up to targetTime, whichever's smaller
                        // if we can do a full previous->next input tick, we can "confirm" this state
                        float inputDeltaTime = inputTimeline.TimeAt(index - 1) - inputTimeline.TimeAt(index);

                        deltaTime = Mathf.Min(inputDeltaTime, targetTime - playbackTime);

                        if (deltaTime == inputDeltaTime && (flags & TickerSeekFlags.DontConfirm) == 0)
                        {
                            canConfirmState = true;
                            confirmableTime = inputTimeline.TimeAt(index - 1);
                        }
                    }

                    // use a delta if it's the crossing the beginning part of the input, otherwise extrapolate without delta
                    if (index + 1 < inputTimeline.Count && playbackTime <= inputTime && playbackTime + deltaTime > inputTime && (flags & TickerSeekFlags.IgnoreDeltas) == 0)
                        input = inputTimeline[index].WithDeltas(inputTimeline[index + 1]);
                    else
                        input = inputTimeline[index].WithoutDeltas();

                    // on the server, the true non-reconciled state is the one that uses full inputs
                    // on the client, the same is true except when replaying things we've already played - i.e. Reconciles - and we pass forceReconciliation for that.
                    if (canConfirmState && playbackTime >= realtimePlaybackTime)
                        isRealtime = true;
                }
                else
                {
                    input = inputTimeline.Latest.WithoutDeltas();
                }

                if (deltaTime > 0f)
                {
                    // invoke events
                    for (int i = 0; i < eventTimeline.Count; i++)
                    {
                        if (eventTimeline.TimeAt(i) >= playbackTime && eventTimeline.TimeAt(i) < playbackTime + deltaTime)
                            eventTimeline[i]?.Invoke(isRealtime);
                    }

                    // run a tick
                    target.Tick(deltaTime, input, isRealtime);

                    playbackTime += deltaTime;

                    if (canConfirmState)
                    {
                        // direct assignment of target time can reduce tiny floating point differences (these differences can accumulate _fast_) and reduce reconciles
                        playbackTime = confirmableTime;
                        confirmedStateTime = confirmableTime;

                        // since this tick is a complete one, save the result as our next confirmed state
                        ConfirmCurrentState();

                        // since states can be compressed, reapply the confirmed state to self to ensure we get the same result as a decompressed confirmedState
                        target.ApplyState(lastConfirmedState);
                    }
                }

                numIterations++;

                if (numIterations == settings.maxSeekIterations)
                {
                    Debug.LogWarning($"Ticker.Seek(): Hit max {numIterations} iterations on {targetName}. T (Confirmed): {playbackTime.ToString("F2")} ({confirmedStateTime}) Target: {initialPlaybackTime.ToString("F2")}-{targetTime.ToString("F2")}");

                    if ((flags & TickerSeekFlags.DontConfirm) == 0)
                    {
                        // we can't process everything, so stop here, accept the time we're given and call it confirmed
                        playbackTime = targetTime;
                        ConfirmCurrentState();
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        // even if something went wrong, we prefer to say we're at the target time
        playbackTime = targetTime;
        this.realtimePlaybackTime = realtimePlaybackTime;

        // Perform history cleanup
        CleanupHistory();
    }

    /// <summary>
    /// Rewinds to pastState and fast forwards to the present time, using recorded inputs if available
    /// </summary>
    public void Reconcile(TState pastState, float pastStateTime, TickerSeekFlags seekFlags)
    {
        float originalPlaybackTime = playbackTime;

        Rewind(pastState, pastStateTime);
        Seek(originalPlaybackTime, originalPlaybackTime);
    }

    /// <summary>
    /// Enables or disables debug pause. When paused, the Seek() function may run, but will always tick to the time it was originally.
    /// </summary>
    public void SetDebugPaused(bool isDebugPaused)
    {
        this.isDebugPaused = isDebugPaused;
    }

    /// <summary>
    /// Draws the current character state
    /// </summary>
    public void DebugDrawCurrentState(Color colour)
    {
        target.MakeState().DebugDraw(colour);
    }

    private string PrintStructDifferences<T>(string aName, string bName, T structureA, T structureB)
    {
        StringBuilder stringBuilder = new StringBuilder(512);

        foreach (var member in typeof(T).GetFields())
        {
            object aValue = member.GetValue(structureA);
            object bValue = member.GetValue(structureB);

            if (!aValue.Equals(bValue))
                stringBuilder.AppendLine($"[!=] {member.Name}: ({aName}) {aValue.ToString()} != ({bName}) {bValue.ToString()}");
            else
                stringBuilder.AppendLine($"[=] {member.Name}: {aValue.ToString()}");
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Calls an event for the current playback time
    /// </summary>
    public void CallEvent(TickerEvent eventToCall)
    {
        int currentIndex = eventTimeline.IndexAt(playbackTime, 0f);

        if (currentIndex != -1)
        {
            eventTimeline[currentIndex] += eventToCall;
        }
        else
        {
            eventTimeline.Set(playbackTime, eventToCall, 0f);
        }
    }

    /// <summary>
    /// Prunes old history that shouldn't be needed anymore
    /// </summary>
    private void CleanupHistory()
    {
        float trimTo = playbackTime - settings.historyLength;

        inputTimeline.TrimBefore(trimTo);
        eventTimeline.TrimBefore(trimTo);
        stateTimeline.TrimBefore(trimTo);
    }

    /// <summary>
    /// Clears all timeline history
    /// </summary>
    private void ClearHistory()
    {
        inputTimeline.Clear();
        eventTimeline.Clear();
        stateTimeline.Clear();
    }
}
