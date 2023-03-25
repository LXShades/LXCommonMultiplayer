using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public delegate void TimelineEvent(TickInfo tickInfo);

[Flags]
public enum TimelineSeekFlags
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
    /// Disables debug sequence recording, lastSeekDebugSequence will not be updated
    /// </summary>
    NoDebugSequence = 8,
};

public enum TimelineTickRateConstraint
{
    /// <summary>
    /// A new input is ignored if it shares the same quantised chunk of time (for example a fixed 0.05 interval from 0) as the last.
    /// Inputs are all inserted at a fixed rate, between these quantized time chunks, and ticks occur at this rate
    /// </summary>
    QuantizedTime,

    /// <summary>
    /// A new input is ignored if it is inserted in under 1/maxInputRate seconds since the last input was inserted. Otherwise, it can be inserted at any time.
    /// Works for many cases, but can fail when using TimeTool.Quantize intervals after long periods of time even if that interval is the tick rate, as the floating points become fuzzy and begin to overlap
    /// </summary>
    Variable
}

/// <summary>
/// An operation that occurs during a Seek. Multiple things happen during a seek. Useful for debugging.
/// </summary>
public struct SeekOp
{
    public enum Type
    {
        DetermineStartTime, // source: playback time target: start state time
        ApplyState, // target: time at state applied
        Tick, // source: tick start time target: tick end time
        DeltaTooBig,
        NoValidStartState,
        ReachedMaxIterations
    }

    public Timeline.EntityBase entity;
    public Type type;
    public double sourceTime;
    public double targetTime;
    public int lastInput;
    public int nextInput;
}

public class SeekOpSequence : List<SeekOp>
{
    private StringBuilder sb = new StringBuilder();

    public void AddOp(Timeline.EntityBase entity, SeekOp.Type type, double sourceTime = 0, double targetTime = 0, int lastInput = -1, int nextInput = -1)
    {
        Add(new SeekOp() { entity = entity, sourceTime = sourceTime, targetTime = targetTime, lastInput = lastInput, nextInput = nextInput });
    }

    public string GenerateLogMessage(bool includeInfo = true, bool includeWarnings = true)
    {
        sb.Clear();

        foreach (SeekOp op in this)
        {
            if (includeInfo)
            {
                switch (op.type)
                {
                    case SeekOp.Type.ApplyState: sb.AppendLine($"ApplyState({op.entity.name}, {op.targetTime})"); break;
                    case SeekOp.Type.Tick: sb.AppendLine($"Tick({op.entity.name}, {op.sourceTime} -> {op.targetTime}) dt={(op.targetTime - op.sourceTime):F2}, curInput={op.nextInput} prevInput={op.lastInput}"); break;
                    case SeekOp.Type.DetermineStartTime: sb.AppendLine($"DetermineStartTime: rewind {op.sourceTime:F2}->{op.targetTime:F2}"); break;
                }
            }

            if (includeWarnings)
            {
                switch (op.type)
                {
                    case SeekOp.Type.NoValidStartState: sb.AppendLine($"NoValidStartState: Entity {op.entity.name} does not have a valid state to start from. Generating a new one from the current state, and proceeding anyway."); break;
                    case SeekOp.Type.ReachedMaxIterations: sb.AppendLine($"ReachedMaxIterations: Max iterations reached at {op.targetTime:F2}. Using the latest result we have and confirming anyway."); break;
                }
            }
        }
        return sb.ToString();
    }

    public override string ToString() => GenerateLogMessage();

}

[System.Serializable]
public struct TimelineSettings
{
    [Header("Tick")]
    [Tooltip("The maximum delta time to pass to tickable components")]
    public float maxDeltaTime;
    [Tooltip("The maximum amount of iterations we can run while extrapolating")]
    public int maxSeekIterations;

    [Header("Input")]
    [Tooltip("The maximum input rate in hz. If <=0, the input rate is unlimited. This should be restricted sensibly so that clients do not send too many inputs and save CPU.")]
    public int maxTickRate;
    [Tooltip("Defines how the input and confirmed state rate will be constrained")]
    public TimelineTickRateConstraint maxTickRateConstraint;
    public int fixedTickRate; // TODO

    [Header("Reconciling")]
    [Tooltip("Whether to reconcile even if the server's confirmed state matched the local state at the time")]
    public bool alwaysReconcile;

    [Header("History")]
    [Tooltip("How long to keep input, state, and event history in seconds both past and future, relative to last seeked time. Recommended at least a second, or more if you'd like to scrub back and forth through the timeline for debugging.")]
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

    public static TimelineSettings Default = new TimelineSettings()
    {
        maxDeltaTime = 1f / 30f,
        maxSeekIterations = 15,
        maxTickRate = 60,
        fixedTickRate = 60,
        maxTickRateConstraint = TimelineTickRateConstraint.QuantizedTime,
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
    /// If deltaTime is clamped (due to too many iterations or missing data), time is still the intended target time which may be further ahead than previousTime+deltaTime
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
    public bool isWholeTick;

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
    public bool isConfirmingForward => !isReplaying && isWholeTick;

    public TimelineSeekFlags seekFlags;

    public static TickInfo Default = new TickInfo()
    {
        isReplaying = false
    };
}

/// <summary>
/// A Timeline allows you to run "ticks" based on inputs, storing the resulting "states" along the way, and rewind to any point in the game using this strategy.
/// 
/// * Timelines contain multiple Entities (e.g. game objects) each containing a state track and an input track (e.g. that player's state and that player's input). When adding an entity, that entity will be controlled by its ITickable implementation
/// * You can scrub through the timeline using the Seek function. There may be constraints applied to preserve memory/CPU usage, see TimelineSettings.
/// * You can overwrite a state in the object's history using ConfirmStateAt, and those changes will be propagated to later states using the recorded inputs.
/// * Recorded inputs and states are in the Timeline.Entity members (inputTrack, stateTrack) with time-data pairs.
/// * Time can be in any format you like, recommended to be based on seconds. Times are internally stored as a double to suit a practically infinite spectrum. Time deltas, however, use floats for efficiency.
/// * See "settings" for a bunch of overrideables, such as limits on delta time and more.
/// </summary>
public class Timeline
{
    /// <summary>
    /// A target controlled by the timeline
    /// </summary>
    public class EntityBase
    {
        public Timeline owner;

        /// <summary>
        /// A name, useful for debugging
        /// </summary>
        public string name;

        /// <summary>
        /// The target, as a tickable base - usually you want to use the generic Entity target instead
        /// </summary>
        public ITickableBase targetBase;

        /// <summary>
        /// The lower this value, the earlier this entity will be ticked compared to the others. For reliable synchronisation, aim to set this value to something meaningful
        /// </summary>
        public int tickPriority
        {
            get => _tickPriority;
            set
            {
                _tickPriority = value;
                owner.needsToSortEntities = true;
            }
        }
        private int _tickPriority;

        /// <summary>
        /// The states for this entity, for rewinding/reverting. Entity has the specialised reference to this.
        /// </summary>
        public TimelineTrackBase stateTrackBase;

        /// <summary>
        /// The inputs for this entity, for ticking (generating future states). Entity has the specialised reference to this.
        /// </summary>
        public TimelineTrackBase inputTrackBase;

        /// <summary>
        /// The latest state time in the state track (equivalent to stateTrackBase.LatestTime)
        /// </summary>
        public double latestStateTime => stateTrackBase.LatestTime;

        /// <summary>
        /// The latest input time in the input track (equivalent to inputTrackBase.LatestTime)
        /// </summary>
        public double latestInputTime => inputTrackBase.LatestTime;

        // Caches whether the TState implements ITickerStateDebug
        private bool doesStateImplementDebug;

        /// <summary>
        /// Stores the current state at the given playback time
        /// </summary>
        public virtual void GenericStoreCurrentState(double time, bool doClearFutureStates, bool doReapplyConfirmedState) { }

        /// <summary>
        /// Applies the state at the index
        /// </summary>
        public virtual void GenericApplyState(int index) { }

        /// <summary>
        /// Runs a tick on the target tickable
        /// </summary>
        public virtual void GenericTick(float deltaTime, int currentInputIndex, int previousInputIndex, TickInfo tickInfo) { }

        /// <summary>
        /// Gets a debug string describing the latest input available at [time]
        /// </summary>
        public virtual string GetInputInfoAtTime(double time) => "";

        /// <summary>
        /// Gets a debug string describing the latest confirmed state available at [time]
        /// </summary>
        public virtual string GetStateInfoAtTime(double time) => "";

        public void CleanupHistory(double minTime, double maxTime)
        {
            // we always want to have a confirmed state ready for us among other things, so we tend to preserve at least one item in each list as the "last known"
            inputTrackBase.TrimBeforeExceptLatest(minTime);
            stateTrackBase.TrimBeforeExceptLatest(minTime);

            // trim states too far into the future as well - they often interfere with e.g. getting the latest valid input in the event of a major clock correction
            // this is a big source of UNEXPECTED ERRORS (oh no!), make sure you don't have inputs lying ridiculously far ahead for example!
            inputTrackBase.TrimAfter(maxTime);
            stateTrackBase.TrimAfter(maxTime);
        }

        public void ClearHistory()
        {
            inputTrackBase.Clear();
            stateTrackBase.Clear();
        }

        public override string ToString() => name;
    }

    /// <summary>
    /// A target entity controlled by the timeline
    /// </summary>
    public class Entity<TState, TInput> : EntityBase where TState : ITickerState<TState> where TInput : ITickerInput<TInput>
    {
        /// <summary>
        /// The target Tickable for this entity. Typically the in-game representation of the entity, implementing ITickable so that ApplyState(), Tick() etc can be called on it
        /// </summary>
        public ITickable<TState, TInput> target;

        public TimelineTrack<TState> stateTrack;
        public TimelineTrack<TInput> inputTrack;

        /// <summary>
        /// Returns the latest state in the state track. Equivalent to stateTrack.Latest
        /// </summary>
        public TState latestState => stateTrack.Latest;

        /// <summary>
        /// Returns the latest input in the input track. Equivalent to inputTrack.Latest
        /// </summary>
        public TInput latestInput => inputTrack.Latest;

        public Entity(Timeline owner, string name, ITickable<TState, TInput> target)
        {
            this.name = name;
            this.owner = owner;
            this.targetBase = this.target = target;
            this.stateTrackBase = this.stateTrack = new TimelineTrack<TState>();
            this.inputTrackBase = this.inputTrack = new TimelineTrack<TInput>();

            StoreCurrentState(owner.playbackTime, false);
        }

        /// <summary>
        /// Inserts a single input into the input history. Old inputs at the same time will be replaced.
        /// </summary>
        public void InsertInput(TInput input, double time)
        {
            // Inclusive because otherwise items at the same time won't be detected and will get replaced
            int closestPriorInputIndex = inputTrack.ClosestIndexBeforeInclusive(time);

            if (owner.settings.maxTickRate <= 0 || closestPriorInputIndex == -1
                || (owner.settings.maxTickRateConstraint == TimelineTickRateConstraint.Variable && time * owner.settings.maxTickRate - inputTrack.TimeAt(closestPriorInputIndex) * owner.settings.maxTickRate >= 0.999f)
                || (owner.settings.maxTickRateConstraint == TimelineTickRateConstraint.QuantizedTime && TimeTool.Quantize(time, owner.settings.maxTickRate) != TimeTool.Quantize(inputTrack.TimeAt(closestPriorInputIndex), owner.settings.maxTickRate)))
            {
                // Add current player input to input history
                inputTrack.Set(time, input);
            }
        }

        /// <summary>
        /// Inserts a single input into the input history quantized to the MaxInputRate
        /// </summary>
        public void InsertQuantizedInput(TInput input, double time)
        {
            InsertInput(input, TimeTool.Quantize(time, owner.settings.maxTickRate));
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
            return TickerInputPack<TInput>.MakeFromHistory(inputTrack, maxLength);
        }

        /// <summary>
        /// Confirms the current character state. Needed to teleport or otherwise influence movement (except where events are used).
        /// [doValidateFutureStates==true]: Clears future states
        /// [reapplyCurrentState==true]: Applies the state we just confirmed. Usual reason: Lossy compression meaning saved/loaded states aren't identical to actual state
        /// </summary>
        public TState StoreCurrentState(double time, bool doClearFutureStates = true, bool reapplyCurrentState = false)
        {
            TState state = target.MakeState();
            stateTrack.Set(time, state);

            if (doClearFutureStates)
                stateTrack.TrimAfter(time);
            if (reapplyCurrentState && !owner.isDebugPaused)
                target.ApplyState(state);

            return state;
        }

        /// <summary>
        /// Confirms the current character state but does not return the state (perhaps we don't know the generic type)
        /// </summary>
        public override void GenericStoreCurrentState(double time, bool doClearFutureStates, bool doReapplyConfirmedState)
        {
            StoreCurrentState(time, doClearFutureStates, doReapplyConfirmedState);
        }

        /// <summary>
        /// Applies the state at the index to the target
        /// </summary>
        public override void GenericApplyState(int index)
        {
            target.ApplyState(stateTrack[index]);
        }

        public override void GenericTick(float deltaTime, int currentInputIndex, int previousInputIndex, TickInfo tickInfo)
        {
            TInput inputToUse = default;

            if (currentInputIndex >= 0 && currentInputIndex < inputTrack.Count)
            {
                if (previousInputIndex >= 0 && previousInputIndex < inputTrack.Count)
                    inputToUse = inputTrack[currentInputIndex].WithDeltas(inputTrack[previousInputIndex]);
                else
                    inputToUse = inputTrack[currentInputIndex].WithDeltas(inputTrack[currentInputIndex]);
            }

            target.Tick(deltaTime, inputToUse, tickInfo);
        }

        /// <summary>
        /// Inserts a state into the history.
        /// 
        /// CAUTION: If the new state differs from the original, the states following that time will be cleared. Future Seek calls might regenerate those states.
        /// </summary>
        public void StoreStateAt(TState state, double time, float precision = 0.0001f)
        {
            if (owner.isDebugPaused)
                return;

            int index = stateTrack.IndexAt(time, precision);

            // If we have a state at this time and it's not correct 
            if (index == -1 || !stateTrack[index].Equals(state) || owner.settings.alwaysReconcile)
            {
                if (owner.settings.debugLogReconciles)
                {
                    Debug.Log(
                        $"Reconcile {name}:\n" +
                        $"Time: {time.ToString("F2")}\n" +
                        $"Index: {index}\n" +
                        $"Diffs: {(index != -1 ? owner.PrintStructDifferences("recv", "old", state, stateTrack[index]) : "N/A")}");
                }

                stateTrack.Set(time, state);
                stateTrack.TrimAfter(time);
            }
        }

        /// <summary>
        /// Gets a debug string describing the latest input available at [time]
        /// </summary>
        public override string GetInputInfoAtTime(double time)
        {
            int index = inputTrack.ClosestIndexBefore(time);
            if (index != -1)
                return GetStructDebugInfo(inputTrack[index]);
            else
                return "[N/A]";
        }

        /// <summary>
        /// Gets a debug string describing the latest confirmed state available at [time]
        /// </summary>
        public override string GetStateInfoAtTime(double time)
        {
            int index = stateTrack.ClosestIndexBefore(time);
            if (index != -1)
                return GetStructDebugInfo(stateTrack[index]);
            else
                return "[N/A]";
        }
    }

    /// <summary>
    /// All timeline that currently exist. This list may contain null gaps. The list is cleaned when new tickers are created.
    /// </summary>
    public static List<WeakReference<Timeline>> allTimelines = new List<WeakReference<Timeline>>();

    /// <summary>
    /// Entities controlled by this timeline
    /// </summary>
    protected readonly List<EntityBase> _entities = new List<EntityBase>();

    /// <summary>
    /// Entities controlled by this timeline
    /// </summary>
    public IReadOnlyList<EntityBase> entities => _entities;

    /// <summary>
    /// Useful name for this timeline (mostly handy for debugging)
    /// </summary>
    public string name { get; protected set; }

    /// <summary>
    /// The current playback time. This can be in any unit that matches the unit you use in the inputTrack and stateTracks, for example Time.time or Time.realtimeSinceStartup
    /// </summary>
    public double playbackTime { get; protected set; }

    /// <summary>
    /// The last time that was Seeked to. Similar to playbackTime, BUT it does not change during ConfirmStateAt. This is needed for isForward Usually you shouldn't care about this
    /// </summary>
    public double lastSeekTargetTime { get; protected set; }

    /// <summary>
    /// Whether the ticker is currently ticking in a Seek process
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
    /// Records the sequence of operations performed in the last Seek. Only updates when TimelineSeekFlags.DebugSequence is used during a seek. Used by debug tools such as TimelineDebugUI.
    /// </summary>
    public SeekOpSequence lastSeekDebugSequence { get; protected set; } = new SeekOpSequence();


    /// <summary>
    /// Flagged if entities need sorting for the next tick. Tick handles.
    /// </summary>
    private bool needsToSortEntities;

    /// <summary>
    /// Events shared across all entities.
    /// </summary>
    protected readonly TimelineTrack<TimelineEvent> eventTrack = new TimelineTrack<TimelineEvent>();

    /// <summary>
    /// Advanced settings for this ticker. Exposed as TickerSettings so that it is fully serializable for the inspector
    /// </summary>
    public TimelineSettings settings = TimelineSettings.Default;

    public Timeline(string name)
    {
        this.name = name;

        // Ideally we'd clean up all tickers whenever one is destroyed, but we don't track that yet
        CleanupNullsInAllTickers();

        allTimelines.Add(new WeakReference<Timeline>(this));
    }

    /// <summary>
    /// Creates a Timeline with a single target, binding a tickable to it. The target state is retrieved (MakeState()) and stored at the current time as an initial state.
    /// </summary>
    public static Timeline CreateSingle<TState, TInput>(string name, ITickable<TState, TInput> tickable, out Entity<TState, TInput> outEntity) where TState : ITickerState<TState> where TInput : ITickerInput<TInput>
    {
        Timeline timeline = new Timeline(name);
        outEntity = timeline.AddEntity(name, tickable, 0);
        return timeline;
    }

    /// <summary>
    /// Adds and returns a new entity to the timeline, binding a tickable to it. The tickable's state is retrieved (MakeState()) and stored at the current time as an initial state.
    /// </summary>
    public Entity<TState, TInput> AddEntity<TState, TInput>(string name, ITickable<TState, TInput> tickable, int tickPriority) where TState : ITickerState<TState> where TInput : ITickerInput<TInput>
    {
        int indexToInsertAt;
        for (indexToInsertAt = 0; indexToInsertAt < _entities.Count; indexToInsertAt++)
        {
            if (_entities[indexToInsertAt].tickPriority > tickPriority)
                break;
        }

        var entity = new Entity<TState, TInput>(this, name, tickable);
        entity.tickPriority = tickPriority;
        _entities.Insert(indexToInsertAt, entity);
        return entity;
    }

    /// <summary>
    /// Removes the entity with the given tickable target
    /// </summary>
    public void RemoveEntity(ITickableBase tickable)
    {
        _entities.RemoveAll(x => x.targetBase == tickable);
    }

    /// <summary>
    /// Seeks to the given time. Ticks going forward beyond the latest confirmed state will call Tick on the target, with the closest available inputs.
    /// New states up to that point may be confirmed into the timeline, unless DontConfirm is used.
    ///  
    /// If something goes wrong and the result is inaccurate - such as the seek limit being reached - Seek is still guaranteed to set playbackTime to the targetTime to avoid locking the object.
    /// </summary>
    public void Seek(double targetTime, TimelineSeekFlags flags = TimelineSeekFlags.None)
    {
        if (_entities.Count <= 0 || isDebugPaused)
            return;

        // Ensure entities are sorted before we run any ticks
        if (needsToSortEntities)
            SortEntities();

        double startTime = targetTime;

        // Find the starting time of the seek so we can move them all together. This should be the latest state (!?) commonly available in all state tracks, prior to targetTime
        foreach (EntityBase entity in _entities)
        {
            int priorStateIdx = entity.stateTrackBase.ClosestIndexBeforeInclusive(startTime);
            if (priorStateIdx != -1)
                startTime = Math.Min(entity.stateTrackBase.TimeAt(priorStateIdx), startTime);
        }

        startTime = TimeTool.Quantize(startTime, settings.fixedTickRate);

        SeekOpSequence debugSequence = (flags & TimelineSeekFlags.NoDebugSequence) != 0 ? null : lastSeekDebugSequence;

        debugSequence?.Clear();
        debugSequence?.AddOp(null, SeekOp.Type.DetermineStartTime, playbackTime, startTime);

        // Send all tickers back to the closest confirmed time available to them before startTime
        foreach (EntityBase entity in _entities)
        {
            int closestStateToStart = entity.stateTrackBase.ClosestIndexBeforeInclusive(startTime);

            if (closestStateToStart != -1)
            {
                entity.GenericApplyState(closestStateToStart);
            }
            else
            {
                // LF: what happens if the state is earlier than StartTime? This entity gets an older state compared to the others? Or we move the startTime back even further?
                // FOR NOW - we just take the current state and add the warning in the sequence
                entity.GenericStoreCurrentState(startTime, true, true);
                debugSequence?.AddOp(entity, SeekOp.Type.NoValidStartState);
            }

            entity.stateTrackBase.TrimAfter(startTime);
        }

        // Begin the ticking process
        double currentTime = startTime;
        float tickrateDelta = 1f / settings.fixedTickRate; // subtract a tiny amount so it doesn't overlap into previous quantized thing (PAIN)
        int numIterations = 0;

        // Run each proper tick
        while (currentTime < targetTime && numIterations <= settings.maxSeekIterations)
        {
            double nextTime = Math.Min(TimeTool.Quantize(currentTime + tickrateDelta + 0.00001f, settings.fixedTickRate), targetTime);
            bool canStoreNextState = nextTime != targetTime || targetTime == TimeTool.Quantize(targetTime, settings.fixedTickRate);

            // Warn if this is the last iteration we can handle
            if (numIterations == settings.maxSeekIterations && nextTime != targetTime)
            {
                debugSequence.AddOp(null, SeekOp.Type.ReachedMaxIterations, startTime, currentTime);
                nextTime = targetTime;
                canStoreNextState = true; // todo - otherwise possible freeze when max iterations is reached and that's no good
            }

            // Prepare tick info
            TickInfo tickInfo = new TickInfo()
            {
                isWholeTick = canStoreNextState,
                isReplaying = nextTime <= lastSeekTargetTime,
                seekFlags = flags,
                time = nextTime
            };

            isInTick = true;
            currentTickInfo = tickInfo;

            // Invoke events before the tick so that actual tick can become aware of and respond to latest events as quickly as possible
            for (int i = 0; i < eventTrack.Count; i++)
            {
                if (eventTrack.TimeAt(i) >= playbackTime && eventTrack.TimeAt(i) < nextTime)
                    eventTrack[i]?.Invoke(tickInfo);
            }

            // Run all the tickers for this interval
            foreach (EntityBase entity in _entities)
            {
                // Previous input is quantized to our tickrate, meaning if there are multiple inputs between a single interval in our low tickrate (ie tickrate < input rate), we accept the quantized one only
                // This is unique to the multi seek. In regular Seek, inputs will define the deltas, this one uses a fixed delta so needs to ensure it uses the inputs closest to those deltas
                // TODO - quantize all - end the suffering
                int currentInput = entity.inputTrackBase.ClosestIndexBeforeOrEarliestInclusive(TimeTool.Quantize(currentTime, settings.fixedTickRate));
                float currentDelta = (float)(nextTime - currentTime);
                int prevInput = entity.inputTrackBase.ClosestIndexBeforeOrEarliestInclusive(TimeTool.Quantize(currentTime - (tickrateDelta - 0.00001f), settings.fixedTickRate));

                debugSequence?.AddOp(entity, SeekOp.Type.Tick, currentTime, nextTime, prevInput, currentInput);

                if (currentDelta > settings.maxDeltaTime)
                {
                    debugSequence?.AddOp(entity, SeekOp.Type.DeltaTooBig, currentTime, currentDelta, prevInput, currentInput);
                    currentDelta = settings.maxDeltaTime;
                }

                try
                {
                    // Run the tick
                    entity.GenericTick(currentDelta, currentInput, prevInput, currentTickInfo);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // Finalise tick
            foreach (EntityBase entity in _entities)
            {
                // We confirm states at the end, so that different entities can influence each other without those influences being erased
                // e.g. character is simulated, then rocks are simulated, rocks impart a force on characters; we want the characters' new force to be reflected in their state
                if (canStoreNextState)
                    entity.GenericStoreCurrentState(nextTime, false, true);
            }

            isInTick = false;
            lastSeekTargetTime = nextTime;

            currentTime = nextTime;
            playbackTime = currentTime;
            numIterations++;
        }

        // Run history cleanups
        CleanupHistory();
    }

    /// <summary>
    /// Seeks forward by the given deltaTime, if possible
    /// </summary>
    public void SeekBy(float deltaTime, TimelineSeekFlags flags = TimelineSeekFlags.None) => Seek(playbackTime + deltaTime, flags);

    /// <summary>
    /// Enables or disables debug pause. When paused, the Seek() function may run, but will always tick to the time it was originally.
    /// </summary>
    public void SetDebugPaused(bool isDebugPaused)
    {
        this.isDebugPaused = isDebugPaused;
    }

    public void DebugTrimStatesAfter(double time)
    {
        foreach (EntityBase entity in _entities)
            entity.stateTrackBase.TrimAfter(time);
    }

    /// <summary>
    /// Draws the current character state
    /// </summary>
    public void DebugDrawCurrentState(Color colour)
    {
        //if (doesStateImplementDebug)
            //(target.MakeState() as ITickerStateDebug).DebugDraw(colour);
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

    private static string GetStructDebugInfo<TStruct>(TStruct value)
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
    /// Returns the time of the latest state in all entities
    /// </summary>
    public double GetTimeOfLatestConfirmedState()
    {
        double output = 0f;
        foreach (var entity in _entities)
            output = Math.Max(output, entity.stateTrackBase.LatestTime);
        return output;
    }

    /// <summary>
    /// Sorts entities so they can be ticked in the correct order
    /// </summary>
    private void SortEntities()
    {
        _entities.Sort((a, b) => a.tickPriority - b.tickPriority);
        needsToSortEntities = false;
    }

    /// <summary>
    /// Inserts a new event at the current playback time. It's great for receiving interactions from things outside the tick sequence if you need to.
    /// * An event is a function called at a certain point in the timeline before a tick.
    /// * The function is called again if the tick if replayed, meaning the change can be "baked" in to the state during reconcile.
    /// * * (Usually, a state change from an outside source would be "forgotten" during the next Seek() due to rewinding to old states)
    /// * Events should generally not be called from inside Tick() processes, unless the event is being called from one timeline to another
    /// 
    /// * For example: a jump pad implements OnTriggerEnter. When a player overlaps, it calls an event on the player that sets the player's y velocity to 10.
    /// * Due to a server correction, the player reconciles to about 0.5 seconds before the jump and replays back to the current time.
    /// * Without an event, the player won't jump at the right point in the timeline, if at all, because OnTriggerEnter only gets called during a new physics frame. Our reconcile could run straight through it.
    /// * However, with the event, the jump function is called again at the original time it was supposed to be called.
    /// 
    /// (todo: allow multiple events at the same moment in time--there is no reason that shouldn't be possible, just an unfinished implementation)
    /// </summary>
    public void CallEvent(TimelineEvent eventToCall)
    {
        int currentIndex = eventTrack.IndexAt(playbackTime, 0f);

        if (currentIndex != -1)
        {
            eventTrack[currentIndex] += eventToCall;
        }
        else
        {
            eventTrack.Set(playbackTime, eventToCall, 0f);
        }
    }

    /// <summary>
    /// Prunes old history that shouldn't be needed anymore
    /// </summary>
    protected void CleanupHistory()
    {
        double minTime = playbackTime - settings.historyLength;
        double maxTime = playbackTime + settings.historyLength;

        foreach (var entity in _entities)
            entity.CleanupHistory(minTime, maxTime);

        eventTrack.TrimBeforeExceptLatest(minTime);
        eventTrack.TrimAfter(maxTime);
    }

    /// <summary>
    /// Clears all timeline history
    /// </summary>
    private void ClearHistory()
    {
        foreach (var entity in _entities)
            entity.ClearHistory();

        eventTrack.Clear();
    }

    /// <summary>
    /// Clears invalid entries from allTickers
    /// </summary>
    private static void CleanupNullsInAllTickers()
    {
        allTimelines.RemoveAll(a => a == null);
    }
}