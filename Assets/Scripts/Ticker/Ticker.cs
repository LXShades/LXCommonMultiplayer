using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public delegate void TickerEvent(TickInfo tickInfo);

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
    DontConfirm = 2,

    /// <summary>
    /// Treat this as an internal replay.
    /// 
    /// Use when you e.g. want to modify and replay a piece of the timeline, but don't want sounds, visual effects etc to play. Because it's not playing in realtime, it's just internally replaying a new version of things that already happened.
    /// </summary>
    TreatAsReplay = 4,

    /// <summary>
    /// Prints debug messages detailing basically every process that occurs in the Seek
    /// </summary>
    DebugMessages = 8,
};

public enum TickerMaxInputRateConstraint
{
    /// <summary>
    /// A new inputs is ignored if it shares the same quantised chunk of time (for example a fixed 0.05 interval from 0) as the last
    /// </summary>
    QuantizedTime,

    /// <summary>
    /// A new input is ignored if it is inserted in under 1/maxInputRate seconds since the last input was inserted.
    /// Works for many cases but may not work well with TimeTool.Quantize setups after long periods of time, as the floating points become fuzzy
    /// </summary>
    Flexible
}

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
    [Tooltip("Defines how the input rate will be constrained")]
    public TickerMaxInputRateConstraint maxInputRateConstraint;

    [Header("Reconciling")]
    [Tooltip("Whether to reconcile even if the server's confirmed state matched the local state at the time")]
    public bool alwaysReconcile;

    [Header("History")]
    [Tooltip("How long to keep input, state, etc history in seconds. Should be able to fit in a bit more ")]
    public float historyLength;

    [Header("Debug")]
    public bool debugLogReconciles;
    public bool debugLogSeekWarnings;

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
        maxDeltaTime = 0.03334f,
        maxSeekIterations = 15,
        maxInputRate = 60,
        maxInputRateConstraint = TickerMaxInputRateConstraint.QuantizedTime,
        alwaysReconcile = false,
        historyLength = 1f,
        debugLogReconciles = false,
        debugSelfReconcileDelay = 0.3f,
        debugDrawReconciles = false,
        debugSelfReconcile = false,
        debugLogSeekWarnings = false
    };
}

public struct TickInfo
{
    /// <summary>
    /// The time of this tick. This is evaluated after deltaTime (so eg first frame with deltaTime=0.5, time is 0.5). Therefore this is not guaranteed to start at 0.
    /// </summary>
    public double time;

    /// <summary>
    /// Whether this tick is part of a confirmation. A "confirmation" happens when:
    /// * There is a known input both before and after the tick period (inclusive)
    /// * OR the target time from the known input exceeds maxDeltaTime (a confirmation happens in this case)
    /// * AND seekFlags does not contain TickerSeekFlags.DontConfirm.
    /// 
    /// Confirmations exist so that inputs can have variable delta times, and they make sure both client/server get the same deltas and results.
    /// 
    /// Confirmations may occur multiple times between a single pair of inputs if the gap between them exceeds maxDeltaTime.
    /// This is usually nothing to be worried about, it just means the deltas are split into smaller pieces.
    /// </summary>
    public bool isConfirming;

    /// <summary>
    /// Whether this tick is approaching a target time greater than the last Seek.
    /// This is confusing so here's an example of why it's needed:
    /// * A player is ticking at time=5
    /// * Player receives a state confirmation in the past at time 4.5. playbackTime has been set to 4.5.
    /// * Next frame, the player is ticking at time=5.5. However, playbackTime is still 4.5, and we don't want to replay sound and visual effects between 4.5 and 5.0.
    /// 
    /// In the above scenario, isReplaying will be true between 4.5 and 5.0, and false from 5.0 beyond
    /// tl;dr isReplaying = currentTime > destinationTimeAtPreviousSeek
    /// </summary>
    public bool isReplaying;

    /// <summary>
    /// A confirmation may hop back slightly in time if extrapolation is used, but the destination is still forward in time.
    /// This creates an awkward scenario where it's not technically a replay, but it is partially replaying, but treating it as a full replay might miss crucial effects.
    /// In those cases, try isConfirmingNew
    /// </summary>
    public bool isConfirmingForward => !isReplaying && isConfirming;

    public TickerSeekFlags seekFlags;

    public static TickInfo Default = new TickInfo()
    {
        isReplaying = false
    };
}


/// <summary>
/// The TickerBase allows you to use a Ticker even if you don't know TState or TInput.
/// </summary>
public abstract class TickerBase
{
    /// <summary>
    /// All tickers that currently exist. This list may contain null gaps. The list is cleaned when new tickers are created.
    /// </summary>
    public static List<WeakReference<TickerBase>> allTickers = new List<WeakReference<TickerBase>>();

    /// <summary>
    /// The name of the target component or struct
    /// </summary>
    public abstract string targetName { get; }

    /// <summary>
    /// The current playback time. This can be in any unit that matches the unit you use in the inputTimeline and stateTimelines, for example Time.time or Time.realtimeSinceStartup
    /// </summary>
    public double playbackTime { get; protected set; }

    /// <summary>
    /// The last time that was Seeked to. Similar to playbackTime, BUT it does not change during ConfirmStateAt. This is needed for isForward Usually you shouldn't care about this
    /// </summary>
    public double lastSeekTargetTime { get; protected set; }

    /// <summary>
    /// The playback time of the latest input
    /// </summary>
    public double latestInputTime => inputTimelineBase.Count > 0 ? inputTimelineBase.LatestTime : -1f;

    /// <summary>
    /// The playback time of the latest confirmed state
    /// </summary>
    public double latestConfirmedStateTime => stateTimelineBase.Count > 0 ? stateTimelineBase.LatestTime : -1f;

    /// <summary>
    /// Whether the ticker is currently ticking in a Seek() or MultiSeek() processs
    /// </summary>
    public bool isInTick { get; protected set; }

    /// <summary>
    /// While in a tick, this contains information about the tick
    /// </summary>
    public TickInfo currentTickInfo { get; protected set; }

    /// <summary>
    /// Whether the ticker is temporarily paused. When paused, the Seek() function may run, but will always tick to the time it was originally
    /// </summary>
    public bool isDebugPaused { get; protected set; }

    /// <summary>
    /// Input history in this Ticker
    /// </summary>
    public abstract TimelineListBase inputTimelineBase { get; }

    /// <summary>
    /// State history in this Ticker
    /// </summary>
    public abstract TimelineListBase stateTimelineBase { get; }

    /// <summary>
    /// Directly acceses the event timeline (inserted events paired with the time they apply at)
    /// </summary>
    protected readonly TimelineList<TickerEvent> eventTimeline = new TimelineList<TickerEvent>();

    /// <summary>
    /// Seeks to the given time. Ticks going forward beyond the latest confirmed state will call Tick on the target, with the closest available inputs.
    /// New states up to that point may be confirmed into the timeline, unless DontConfirm is used.
    ///  
    /// If something goes wrong and the result is inaccurate - such as the seek limit being reached - Seek is still guaranteed to set playbackTime to the targetTime to avoid locking the object.
    /// </summary>
    public abstract void Seek(double targetTime, TickerSeekFlags flags = TickerSeekFlags.None);

    /// <summary>
    /// Seeks forward by the given deltaTime, if possible
    /// </summary>
    public void SeekBy(float deltaTime, TickerSeekFlags flags = TickerSeekFlags.None) => Seek(playbackTime + deltaTime, flags);

    /// <summary>
    /// Applies the state at the given index without requiring type specialisation
    /// </summary>
    public abstract void GenericApplyState(int stateIndex);

    /// <summary>
    /// Ticks the target with the input at the given index without requiring type specialisation
    /// If previousInputIndex is different to currentInputIndex and not -1, delta inputs are applied between them
    /// </summary>
    public abstract void GenericTickTarget(float deltaTime, int currentInputIndex, int previousInputIndex, TickInfo tickInfo);

    /// <summary>
    /// Confirms the current state without requiring type specialisation
    /// </summary>
    protected abstract void GenericConfirmCurrentState(bool doClearFutureStates);

    /// <summary>
    /// Prunes old history that shouldn't be needed anymore
    /// </summary>
    protected abstract void CleanupHistory();

    /// <summary>
    /// Gets a debug string describing the latest input available at [time]
    /// </summary>
    public abstract string GetInputInfoAtTime(double time);

    /// <summary>
    /// Gets a debug string describing the latest confirmed state available at [time]
    /// </summary>
    public abstract string GetStateInfoAtTime(double time);

    /// <summary>
    /// Toggles debug pause. During debug pause Seek will always seek to the same time as it was when the pause began
    /// </summary>
    public abstract void SetDebugPaused(bool isDebugPaused);

    /// <summary>
    /// [WIP] Seeks multiple timelines together in a fixed time interval. Each ticker is expected to run on the same time scale (e.g. seconds from start), but may seek to different target times if desired. There's a lot going on under the hood:
    /// 
    /// * Find the earliest point needed to begin the seek. (if a past state of one of the objects is overwritten, we need to begin the entire process from there again for them to update together)
    /// * Iteratively tick each object at the intervals that suit each object.
    /// 
    /// This uses a different method than Seek(). Seek() typically confirms states at an interval of a) settings.maxDeltaTime or b) up to the next input or c) to the target time, whichever is the smallest delta.
    /// SeekMultiple() instead confirms states at a) the specified maxInterval or b) to the target time. This is because running multiple timelines at variable intervals to each other while keeping them in sync 
    /// would either produce deltas that would make sense for some and wouldn't make sense for others, or could cause some states to be slightly older than others during the seek, depending on the strategy used.
    /// A quantized fixed-time strategy is used instead to keep everything together as much as possible.
    /// 
    /// WIP: Events not supported, some flags unsupported, just making what's needed for my project atm
    /// </summary>
    public static void SeekMultiple(MultiSeek seekOp)
    {
        StringBuilder debugMessages = (seekOp.flags & TickerSeekFlags.DebugMessages) != 0 ? new StringBuilder(512) : null;
        double startTime = float.MaxValue;

        // Find the starting time of the seek
        foreach (MultiSeek.Operation op in seekOp.operations)
            startTime = op.target.latestConfirmedStateTime < startTime ? op.target.latestConfirmedStateTime : startTime;

        startTime = TimeTool.Quantize(startTime, seekOp.tickRate);

        // Send all tickers back to the closest confirmed time available to them before startTime
        foreach (MultiSeek.Operation op in seekOp.operations)
        {
            TickerBase ticker = op.target;
            int closestStateToStart = ticker.stateTimelineBase.ClosestIndexBeforeInclusive(startTime);

            if (closestStateToStart != -1)
            {
                ticker.GenericApplyState(closestStateToStart);
                ticker.playbackTime = ticker.stateTimelineBase.TimeAt(closestStateToStart);
                ticker.stateTimelineBase.TrimAfter(ticker.playbackTime);
            }
            else
            {
                ticker.playbackTime = startTime;
                ticker.GenericConfirmCurrentState(true);
                Debug.LogWarning($"TickerBase.SeekMultiple(): Ticker {ticker.targetName} did not have a valid state in the history to start at. Ignoring this, generating a new one from the current state, and proceeding.");
            }
        }

        debugMessages?.Append($"TickerBase.SeekMultiple: {startTime.ToString("F2")}->{seekOp.targetTime.ToString("F2")}\n");

        // Begin the ticking process
        double currentTime = startTime;
        float tickrateDelta = 1f / seekOp.tickRate;
        int numIterations = 0;
        float kMaxDelta = tickrateDelta * 2f;

        // Run proper tick
        while (currentTime < seekOp.targetTime && numIterations <= seekOp.maxIterations)
        {
            double nextTime = Math.Min(TimeTool.Quantize(currentTime + tickrateDelta, seekOp.tickRate), seekOp.targetTime);
            bool canConfirmNextState = nextTime != seekOp.targetTime || seekOp.targetTime == TimeTool.Quantize(seekOp.targetTime, seekOp.tickRate);

            if (numIterations == seekOp.maxIterations)
            {
                Debug.LogWarning($"TickerBase.SeekMultiple({seekOp.targetTime.ToString("F1")}) hit max iterations {seekOp.maxIterations}. Abandoning process and leaving the latest result which may not be accurate to the target time.");
                nextTime = seekOp.targetTime;
                canConfirmNextState = true; // todo - otherwise possible freeze when max iterations is reached and that's no good
            }

            TickInfo tickInfo = new TickInfo()
            {
                isConfirming = canConfirmNextState,
                isReplaying = false, // todo
                seekFlags = seekOp.flags,
                time = startTime
            };

            // Prepare all the tickers for the tick
            foreach (MultiSeek.Operation op in seekOp.operations)
            {
                op.target.isInTick = true;
                op.target.currentTickInfo = tickInfo;
            }

            // Run all the tickers for this interval
            foreach (MultiSeek.Operation op in seekOp.operations)
            {
                TickerBase ticker = op.target;

                // Previous input is quantized to our tickrate, meaning if there are multiple inputs between a single interval in our low tickrate (ie tickrate < input rate), we accept the quantized one only
                // This is unique to the multi seek. In regular Seek, inputs will define the deltas, this one uses a fixed delta so needs to ensure it uses the inputs closest to those deltas
                int currentInput = ticker.inputTimelineBase.ClosestIndexBeforeOrEarliestInclusive(TimeTool.Quantize(ticker.playbackTime, seekOp.tickRate));
                float currentDelta = (float)(nextTime - ticker.playbackTime);
                int prevInput = ticker.inputTimelineBase.ClosestIndexBeforeOrEarliestInclusive(TimeTool.Quantize(ticker.playbackTime - tickrateDelta, seekOp.tickRate));

                if (currentDelta > kMaxDelta)
                {
                    Debug.LogWarning($"TickerBase.SeekMultiple({seekOp.targetTime.ToString("F1")}) delta on {ticker.targetName} was too big ({currentDelta.ToString("F2")}), clamping and continuing.");
                    currentDelta = kMaxDelta;
                }

                debugMessages?.Append($"{ticker} Tick({ticker.playbackTime.ToString("F2")}->{nextTime.ToString("F2")} dt={currentDelta.ToString("F2")}, curInput={currentInput} prevInput={prevInput}). Confirm {(canConfirmNextState ? "yes" : "no")}\n");

                // Invoke events
                TimelineList<TickerEvent> eventTimeline = ticker.eventTimeline;
                for (int i = 0; i < ticker.eventTimeline.Count; i++)
                {
                    if (eventTimeline.TimeAt(i) >= ticker.playbackTime && eventTimeline.TimeAt(i) < nextTime)
                        eventTimeline[i]?.Invoke(tickInfo);
                }

                // Do the tick
                try
                {
                    ticker.GenericTickTarget(currentDelta, currentInput, prevInput, tickInfo);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                // Finalise the result
                ticker.playbackTime = nextTime;
            }

            // Finalise tick
            foreach (MultiSeek.Operation op in seekOp.operations)
            {
                // We confirm states at the end, so that different tickers can influence each other without these influences being erased
                // e.g. character is simulated, then rocks are simulated, rocks impart a force on characters.
                // if we confirmed the state of the character before we simulated the rock, their state would be saved before the force is imparted and the force wouldn't exist in the timeline
                if (canConfirmNextState)
                    op.target.GenericConfirmCurrentState(false);
                op.target.isInTick = false;
            }

            currentTime = nextTime;
            numIterations++;
        }

        // Run history cleanups
        foreach  (MultiSeek.Operation op in seekOp.operations)
            op.target.CleanupHistory();

        // Done!
        if (debugMessages != null && debugMessages.Length > 0)
            Debug.Log(debugMessages.ToString());
    }
}

/// <summary>
/// A Ticker allows you to run "ticks" based on inputs, storing the resulting "states" along the way.
/// 
/// * You can scrub through an object's history using the Seek function. 
/// * You can overwrite a state in the object's history using ConfirmStateAt, and those changes will be propagated to later states using the recorded inputs.
/// * Recorded inputs and states are in the Timeline members (inputTimeline, stateTimeline) with time-data pairs.
/// * Time can be in any format you like, recommended to be based on seconds. Times are internally stored as a double to suit a practically infinite spectrum. Deltas, however, use floats for efficiency as they will typically be much smaller.
/// * See "settings" for a bunch of overrideables, such as limits on delta time and more.
/// </summary>
public class Ticker<TInput, TState> : TickerBase, ITickerStateFunctions<TState>, ITickerInputFunctions<TInput>
    where TInput : ITickerInput<TInput> where TState : ITickerState<TState>
{
    /// <summary>
    /// The target tickable component, class or struct
    /// </summary>
    public ITickable<TInput, TState> target;

    /// <summary>
    /// Target name, is usually gameObject name if possible
    /// </summary>
    public override string targetName => (target is Component targetAsComponent) ? targetAsComponent.gameObject.name : "N/ng thiA";

    /// <summary>
    /// Advanced settings for this ticker. Exposed as TickerSettings so that it is fully serializable for the inspector
    /// </summary>
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
    /// The most recent confirmed state
    /// </summary>
    public TState latestConfirmedState => stateTimeline.Count > 0 ? stateTimeline.Latest : default;

    /// <summary>
    /// Directly accesses the input timeline (inserted inputs paired with the time they apply at)
    /// </summary>
    public readonly TimelineList<TInput> inputTimeline = new TimelineList<TInput>();

    /// <summary>
    /// Directly accesses the state timeline (inserted or generated states paired with the time they apply at)
    /// </summary>
    public readonly TimelineList<TState> stateTimeline = new TimelineList<TState>();

    // for our clueless friends in ITickerBase-land who don't know what types we're using. ;)
    public override TimelineListBase inputTimelineBase => inputTimeline;
    public override TimelineListBase stateTimelineBase => stateTimeline;

    // caches whether the state implements ITickerStateDebug
    private bool doesStateImplementDebug;

    public Ticker(ITickable<TInput, TState> target)
    {
        this.target = target;
        doesStateImplementDebug = typeof(ITickerStateDebug).IsAssignableFrom(typeof(TState));

        // Ideally we'd clean up all tickers whenever one is destroyed, but we don't track that yet
        CleanupAllTickers();
        allTickers.Add(new WeakReference<TickerBase>(this));

        ConfirmCurrentState(false);
    }

    /// <summary>
    /// Inserts a single input into the input history. Old inputs at the same time will be replaced.
    /// </summary>
    public void InsertInput(TInput input, double time)
    {
        // Inclusive because otherwise items at the same time won't be detected and will get replaced
        int closestPriorInputIndex = inputTimeline.ClosestIndexBeforeInclusive(time);

        if (settings.maxInputRate <= 0 || closestPriorInputIndex == -1
            || (settings.maxInputRateConstraint == TickerMaxInputRateConstraint.Flexible && time * settings.maxInputRate - inputTimeline.TimeAt(closestPriorInputIndex) * settings.maxInputRate >= 0.999f)
            || (settings.maxInputRateConstraint == TickerMaxInputRateConstraint.QuantizedTime && TimeTool.Quantize(time, settings.maxInputRate) != TimeTool.Quantize(inputTimeline.TimeAt(closestPriorInputIndex), settings.maxInputRate)))
        {
            // Add current player input to input history
            inputTimeline.Set(time, input);
        }
    }

    /// <summary>
    /// Inserts a single input into the input history quantized to the MaxInputRate
    /// </summary>
    public void InsertQuantizedInput(TInput input, double time)
    {
        InsertInput(input, TimeTool.Quantize(time, settings.maxInputRate));
    }

    /// <summary>
    /// Stores an input pack into input history. Old inputs at the same time will be replaced.
    /// </summary>
    public void InsertInputPack(TickerInputPack<TInput> inputPack)
    {
        for (int i = inputPack.inputs.Length - 1; i >= 0; i--)
            InsertInput(inputPack.inputs[i], inputPack.times[i]);
    }

    /// <summary>
    /// Makes an input pack from the input history (useful for sending multiple inputs to server)
    /// </summary>
    public TickerInputPack<TInput> MakeInputPack(float maxLength)
    {
        return TickerInputPack<TInput>.MakeFromHistory(inputTimeline, maxLength);
    }

    /// <summary>
    /// Seeks to the given time. Ticks going forward beyond the latest confirmed state will call Tick on the target, with the closest available inputs.
    /// New states up to that point may be confirmed into the timeline, unless DontConfirm is used.
    ///  
    /// If something goes wrong and the result is inaccurate - such as the seek limit being reached - Seek is still guaranteed to set playbackTime to the targetTime to avoid locking the object.
    /// </summary>
    public override void Seek(double targetTime, TickerSeekFlags flags = TickerSeekFlags.None)
    {
        StringBuilder debugMessages = (flags & TickerSeekFlags.DebugMessages) != 0 ? new StringBuilder() : null;
        double initialPlaybackTime = playbackTime;
        Debug.Assert(settings.maxDeltaTime > 0f);

        // in debug pause mode we never seek to other times
        if (isDebugPaused)
            targetTime = playbackTime;

        // See if we can rewind to relevant confirmed keyframe (should we call states keyframes? which one is the easiest to comprehend?)
        bool isRewinding = targetTime < playbackTime;
        bool canAttemptConfirmNextState = (flags & TickerSeekFlags.DontConfirm) == 0 && targetTime > playbackTime;

        if (isRewinding || canAttemptConfirmNextState)
        {
            // Try rewind. If canAttemptConfirmNextState, the closest earlier keyframe will happen to be the latest confirmed state
            int closestStateBeforeTargetTime = stateTimeline.ClosestIndexBeforeInclusive(targetTime, 0f);

            if (closestStateBeforeTargetTime != -1)
            {
                debugMessages?.Append($"Applying earlier confirmed state: {playbackTime.ToString("F2")}->{stateTimeline.TimeAt(closestStateBeforeTargetTime).ToString("F2")} ({closestStateBeforeTargetTime})\n");

                target.ApplyState(stateTimeline[closestStateBeforeTargetTime]);
                playbackTime = stateTimeline.TimeAt(closestStateBeforeTargetTime);
            }
            else if (isRewinding && settings.debugLogSeekWarnings)
                Debug.LogWarning($"Ticker.Seek({initialPlaybackTime.ToString("F2")}->{targetTime.ToString("F2")}): reverse seek could not find earlier state to seek to. Using current state.");
        }

        // Begin moving forward through the timeline
        try
        {
            int numIterations = 0;

            // Execute ticks, grabbing and consuming inputs if they are available, or using the latest inputs
            while (playbackTime < targetTime)
            {
                TInput input = default;
                int inputIndex = inputTimeline.ClosestIndexBeforeOrEarliest(playbackTime, 0.001f);
                bool canConfirmState = false;
                double confirmStateTime = 0f;

                // Decide the delta time to use
                double deltaTime = targetTime - playbackTime;
                if (deltaTime >= settings.maxDeltaTime)
                {
                    deltaTime = settings.maxDeltaTime;

                    canConfirmState = canAttemptConfirmNextState;
                    confirmStateTime = playbackTime + deltaTime;
                }

                if (inputIndex != -1)
                {
                    double inputTime = inputTimeline.TimeAt(inputIndex);

                    if (inputIndex > 0)
                    {
                        // if we can do a full previous->next input tick, we can "confirm" this state
                        // note that we may subdivide the confirmed ticks by settings.maxDeltaTime, so we check based on the current playbackTime rather than the previous input's time
                        double timeToNextInput = inputTimeline.TimeAt(inputIndex - 1) - playbackTime;

                        if (deltaTime >= timeToNextInput && canAttemptConfirmNextState)
                        {
                            // we can confirm the resulting state
                            deltaTime = timeToNextInput;

                            canConfirmState = true;
                            confirmStateTime = inputTimeline.TimeAt(inputIndex - 1);
                        }
                    }

                    // use a delta if it's the crossing the beginning part of the input, otherwise extrapolate without delta
                    if (inputIndex + 1 < inputTimeline.Count && playbackTime <= inputTime && playbackTime + deltaTime > inputTime && (flags & TickerSeekFlags.IgnoreDeltas) == 0)
                    {
                        input = inputTimeline[inputIndex].WithDeltas(inputTimeline[inputIndex + 1]);

                        debugMessages?.Append($"Using input with Deltas: {inputIndex} ({inputTimeline.TimeAt(inputIndex).ToString("F2")})->{inputIndex + 1} ({inputTimeline.TimeAt(inputIndex + 1).ToString("F2")}) {GetStructDebugInfo(input)}\n");
                    }
                    else
                    {
                        input = inputTimeline[inputIndex].WithDeltas(inputTimeline[inputIndex]);

                        debugMessages?.Append($"Using input with NoDeltas: {inputIndex} ({inputTimeline.TimeAt(inputIndex).ToString("F2")})\n");
                    }
                }

                if (deltaTime > 0f)
                {
                    TickInfo tickInfo = new TickInfo()
                    {
                        time = playbackTime + deltaTime,
                        isReplaying = playbackTime + deltaTime <= lastSeekTargetTime || (flags & TickerSeekFlags.TreatAsReplay) != 0,
                        isConfirming = canConfirmState,
                        seekFlags = flags
                    };

                    // invoke events
                    for (int i = 0; i < eventTimeline.Count; i++)
                    {
                        if (eventTimeline.TimeAt(i) >= playbackTime && eventTimeline.TimeAt(i) < playbackTime + deltaTime)
                            eventTimeline[i]?.Invoke(tickInfo);
                    }

                    // run a tick
                    isInTick = true;
                    currentTickInfo = tickInfo;
                    target.Tick((float)deltaTime, input, tickInfo);
                    isInTick = false;

                    debugMessages?.Append($"Ticking {playbackTime.ToString("F2")}->{(playbackTime + deltaTime).ToString("F2")}\n");

                    playbackTime += deltaTime;

                    if (canConfirmState)
                    {
                        // direct assignment of target time reduces tiny floating point differences (these differences can accumulate _fast_) and reduce reconciles
                        playbackTime = confirmStateTime;

                        // save confirmed state into the timeline.
                        // reapply the confirmed state to self to ensure we get the same result (states can be compressed and decompressed with slightly different results, we need max consistency)
                        target.ApplyState(ConfirmCurrentState(false));
                    }
                }

                // Clamp iterations at the maximum to avoid freezes
                numIterations++;
                if (numIterations == settings.maxSeekIterations)
                {
                    if (settings.debugLogSeekWarnings)
                        Debug.LogWarning($"Ticker.Seek({initialPlaybackTime.ToString("F2")}->{targetTime.ToString("F2")}): Hit max {numIterations} iterations on {targetName}. T (Confirmed): {playbackTime.ToString("F2")} ({latestConfirmedStateTime})");

                    if (canAttemptConfirmNextState)
                    {
                        // we can't process everything, risking a lockup, so accept the time we're given and call it confirmed
                        playbackTime = targetTime;
                        ConfirmCurrentState(false);
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
        lastSeekTargetTime = targetTime;

        // Perform history cleanup
        CleanupHistory();

        // Print debugs
        if (debugMessages != null && debugMessages.Length > 0)
            Debug.Log(debugMessages?.ToString());
    }

    /// <summary>
    /// Rewinds to pastState and fast forwards to the present time, using recorded inputs if available
    /// </summary>
    public void Reconcile(TState pastState, double pastStateTime, TickerSeekFlags seekFlags)
    {
        double originalPlaybackTime = playbackTime;

        ConfirmStateAt(pastState, pastStateTime);
        Seek(originalPlaybackTime, seekFlags);
    }

    /// <summary>
    /// Inserts a state into the history.
    /// 
    /// If the new state differs from the original confirmed state, states following that time will be cleared, playbackTime will be reverted and the next Seek will need to start from this earlier time.
    /// 
    /// CAUTION: If the state differs from the original state at that time:
    /// * The new state is confirmed
    /// * All future confirmed states are removed (as they will likely be different when seeked again)
    /// * The current playback time is set to the time provided.
    /// </summary>
    public void ConfirmStateAt(TState state, double time)
    {
        int index = stateTimeline.IndexAt(time, 0.0001f);

        // If we have a state at this time and it's not correct 
        if (index == -1 || !stateTimeline[index].Equals(state) || settings.alwaysReconcile)
        {
            if (settings.debugLogReconciles)
            {
                Debug.Log(
                    $"Reconcile {targetName}:\n" +
                    $"Time: {time.ToString("F2")}\n" +
                    $"Index: {index}\n" +
                    $"Diffs: {(index != -1 ? PrintStructDifferences("recv", "old", state, stateTimeline[index]) : "N/A")}");
            }

            playbackTime = time;
            target.ApplyState(state);

            stateTimeline.Set(time, state);
            stateTimeline.TrimAfter(time);
        }
    }

    /// <summary>
    /// Confirms the current character state. Needed to teleport or otherwise influence movement (except where events are used).
    /// [doValidateFutureStates==true]: Clears future states
    /// </summary>
    public TState ConfirmCurrentState(bool doClearFutureStates = true)
    {
        TState state = target.MakeState();
        stateTimeline.Set(playbackTime, state);

        if (doClearFutureStates)
            stateTimeline.TrimAfter(playbackTime);

        return state;
    }

    /// <summary>
    /// Confirms the current character state but does not return the state (perhaps we don't know the generic type)
    /// </summary>
    protected override void GenericConfirmCurrentState(bool doClearFutureStates)
    {
        ConfirmCurrentState(doClearFutureStates);
    }

    /// <summary>
    /// Applies the state at the index
    /// </summary>
    public override void GenericApplyState(int index)
    {
        target.ApplyState(stateTimeline[index]);
    }

    public override void GenericTickTarget(float deltaTime, int currentInputIndex, int previousInputIndex, TickInfo tickInfo)
    {
        TInput inputToUse = default;

        if (currentInputIndex >= 0 && currentInputIndex < inputTimeline.Count)
        {
            if (previousInputIndex >= 0 && previousInputIndex < inputTimeline.Count)
                inputToUse = inputTimeline[currentInputIndex].WithDeltas(inputTimeline[previousInputIndex]);
            else
                inputToUse = inputTimeline[currentInputIndex].WithDeltas(inputTimeline[currentInputIndex]);
        }

        target.Tick(deltaTime, inputToUse, tickInfo);
    }

    /// <summary>
    /// Enables or disables debug pause. When paused, the Seek() function may run, but will always tick to the time it was originally.
    /// </summary>
    public override void SetDebugPaused(bool isDebugPaused)
    {
        this.isDebugPaused = isDebugPaused;
    }

    /// <summary>
    /// Draws the current character state
    /// </summary>
    public void DebugDrawCurrentState(Color colour)
    {
        if (doesStateImplementDebug)
            (target.MakeState() as ITickerStateDebug).DebugDraw(colour);
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
    /// Gets a debug string describing the latest input available at [time]
    /// </summary>
    public override string GetInputInfoAtTime(double time)
    {
        int index = inputTimeline.ClosestIndexBefore(time);
        if (index != -1)
            return GetStructDebugInfo(inputTimeline[index]);
        else
            return "[N/A]";
    }

    /// <summary>
    /// Gets a debug string describing the latest confirmed state available at [time]
    /// </summary>
    public override string GetStateInfoAtTime(double time)
    {
        int index = stateTimeline.ClosestIndexBefore(time);
        if (index != -1)
            return GetStructDebugInfo(stateTimeline[index]);
        else
            return "[N/A]";
    }

    private string GetStructDebugInfo<TStruct>(TStruct value)
    {
        StringBuilder output = new StringBuilder(512);
        System.Reflection.FieldInfo[] fields = typeof(TStruct).GetFields();
        System.Reflection.PropertyInfo[] properties = typeof(TStruct).GetProperties();

        for (int type = 0; type < 2; type++)
        {
            int count = type == 0 ? fields.Length : properties.Length;

            for (int i = 0; i < count; i++)
            {
                string fieldName = type == 0 ? fields[i].Name : properties[i].Name;
                object fieldValue = type == 0 ? fields[i].GetValue(value) : properties[i].GetValue(value);

                if (fieldValue is System.Collections.IEnumerable enumerableValue)
                {
                    int previousLength = output.Length;
                    output.Append($"{fieldName} [enumerable {fieldValue.GetType()}...]:\n");

                    foreach (var enumerated in enumerableValue)
                    {
                        output.Append(enumerated);
                        output.Append("\n");
                    }

                    // indent everything under this value
                    output.Replace("\n", "\n> ", previousLength, output.Length - previousLength);
                }
                else
                {
                    output.Append($"{fieldName}: {fieldValue}\n");
                }
            }
        }

        return output.ToString();
    }

    /// <summary>
    /// Inserts a new event at the current playback time.
    /// * An event is a function called at a certain point at a timeline.
    /// * The key advantage is that the function is called again at this point in time if a reconcile occurs.
    /// * It's great for letting the outside world interact with the tightly controlled reconciled world, without getting forgotten in replays.
    /// 
    /// * For example: a jump pad implements OnTriggerEnter. When a player overlaps, it calls an event on the player that sets the player's y velocity to 10.
    /// * Due to a server correction, the player reconciles to about 0.5 seconds before the jump and replays back to the current time.
    /// * Without an event, the player won't jump at the right point in the timeline, if at all, because OnTriggerEnter only gets called during a new physics frame. Our reconcile could run straight through it.
    /// * However, with the event, the jump function is called again at the original time it was supposed to be called.
    /// 
    /// (todo: allow multiple events at the same moment in time--there is no reason that shouldn't be possible, just an unfinished implementation)
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
    protected override void CleanupHistory()
    {
        double trimTo = playbackTime - settings.historyLength;

        // we always want to have a confirmed state ready for us among other things, so we tend to preserve at least one item in each list as the "last known"
        inputTimeline.TrimBeforeExceptLatest(trimTo);
        eventTimeline.TrimBeforeExceptLatest(trimTo);
        stateTimeline.TrimBeforeExceptLatest(trimTo);
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

    /// <summary>
    /// Clears invalid entries from allTickers
    /// </summary>
    private static void CleanupAllTickers()
    {
        allTickers.RemoveAll(a => a == null);
    }
}
