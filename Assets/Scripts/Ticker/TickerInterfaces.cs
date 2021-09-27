using System;

/// <summary>
/// ITickerBase allows you to use a Ticker even if you don't know TState or TInput.
/// </summary>
public interface ITickerBase
{
    public void Seek(float targetTime, float realtimePlaybackTime, TickerSeekFlags flags = TickerSeekFlags.None);
    public void SeekBy(float deltaTime, float realtimePlaybackTime);

    public void SetDebugPaused(bool isDebugPaused);

    /// <summary>
    /// The name of the target component or struct
    /// </summary>
    public string targetName { get; }

    /// <summary>
    /// The current playback time, based on no specific point of reference, but is expected to use the same time format as input and event history
    /// </summary>
    public float playbackTime { get; }

    /// <summary>
    /// The realtime playback during the last Seek. This is mainly for debugging and doesn't affect current state
    /// </summary>
    public float realtimePlaybackTime { get; }

    /// <summary>
    /// The current confirmed state time - the non-extrapolated playback time of the last input-confirmed state
    /// </summary>
    public float confirmedStateTime { get; }

    /// <summary>
    /// Whether the ticker is temporarily paused. When paused, the Seek() function may run, but will always tick to the time it was originally
    /// </summary>
    public bool isDebugPaused { get; }

    /// <summary>
    /// Input history in this Ticker
    /// </summary>
    public TimelineListBase inputTimelineBase { get; }

    /// <summary>
    /// State history in this Ticker
    /// </summary>
    public TimelineListBase stateTimelineBase { get; }
}

/// <summary>
/// You can cast and use ITickerStateFunctions when you know what TState is but don't know what TInput is
/// </summary>
public interface ITickerStateFunctions<TState> where TState : ITickerState<TState>
{
    public void ConfirmStateAt(TState state, float time);
    public void Reconcile(TState pastState, float pastStateTime, TickerSeekFlags seekFlags);
}

/// <summary>
/// You can cast and use ITickerInputFunctions when you know what TInput is but don't know what TState is
/// </summary>
public interface ITickerInputFunctions<TInput> where TInput : ITickerInput<TInput>
{
    public void InsertInput(TInput input, float time);
    public void InsertInputPack(TickerInputPack<TInput> inputPack);
    public TickerInputPack<TInput> MakeInputPack(float maxLength);
}

/// <summary>
/// Enables you to get the ticker out of a tickable regardless of generic types
/// </summary>
public interface ITickableBase
{
    /// <summary>
    /// Should return this object's Ticker instance with its chosen TInput and TState.
    /// </summary>
    ITickerBase GetTicker();
} 

/// <summary>
/// Qualifies something as tickable.
/// 
/// This class can be ticked, reverted to a previous state, and "Seek" to an earlier _or_ future time in its history
/// 
/// Inputs are used to Tick and generate future states. Earlier states are stores and can be loaded. A Seek is able to revert to an earlier time, or extrapolate to a later time, based either on recorded inputs or the last known input.
/// 
/// This interface should implement MakeState(), ApplyState() and Tick().
/// </summary>
public interface ITickable<TInput, TState> : ITickableBase
{
    /// <summary>
    /// Ticks the object. In a networked game, you may put most important gameplay things in this function, as though it were an Update function.
    /// 
    /// * Do NOT use Time.deltaTime, or anything from Time (except debugging info)!
    /// * Do not read live inputs from here (except, again, debugging). Put everything you need into TInput and ensure it is passed into the ticker timeline.
    /// * Always remember Tick() may be called during states in the past, where Time.deltaTime may completely inaccurate.
    /// * Always remember Tick() may be called multiple times in a single frame. As such, avoid playing sounds or spawning objects unless isRealtime is true.
    /// * You can still use Update for things that don't affect gameplay, such as visual effects.
    /// </summary>
    void Tick(float deltaTime, TInput input, TickInfo tickInfo);

    /// <summary>
    /// Used to restore to a previous state by the ticker. Store all important ticker-affected information here.
    /// </summary>
    TState MakeState();

    /// <summary>
    /// Used to restore a previous state by the ticker. Apply all important ticker-affected information here.
    /// Remember that for anything that affects physics, you may need to call Physics.SyncTransforms() or just turn Physics.autoSyncTransforms on so that positional chanegs are propagated into the physics system.
    /// </summary>
    void ApplyState(TState state);
}


/// <summary>
/// Qualifies a struct or class as a ticker input.
/// 
/// The ticker input can be e.g a set of player controls specified by booleans (isJumpButtonDown) and floats (horizontalMovement).
/// 
/// This is sent to a tickable component's Simulate() function during a seek.
/// </summary>
public interface ITickerInput<TOwner>
{
    /// <summary>
    /// Returns an input representing current live input state
    /// </summary>
    public TOwner GenerateLocal();

    /// <summary>
    /// Returns an input with optional deltas compared to the previous input. e.g. btnJumpPressed, btnJumpReleased
    /// When WithDeltas is called on the owner with the owner as the parameter, that can be considered WithoutDeltas.
    /// </summary>
    public TOwner WithDeltas(TOwner previousInput);
}

/// <summary>
/// Qualifies a stuct or class as a ticker snapshot.
/// 
/// This is used to revert a tickable component to an earlier state for potential resimulation.
/// 
/// To work reliably, this snapshot should be able to store and load all simulatable state from a tickable component.
/// </summary>
public interface ITickerState<TState> : IEquatable<TState>
{
    void DebugDraw(UnityEngine.Color colour);
}

public struct TickerInputPack<TInput>
{
    public TickerInputPack(TInput[] input, float[] times)
    {
        this.inputs = input;
        this.times = times;
    }

    public TInput[] inputs;
    public float[] times;

    /// <summary>
    /// Makes an InputPack from a given input history
    /// </summary>
    /// <returns></returns>
    public static TickerInputPack<TInput> MakeFromHistory(TimelineList<TInput> inputTimeline, float sendBufferLength)
    {
        int startIndex = inputTimeline.ClosestIndexBeforeOrEarliest(inputTimeline.LatestTime - sendBufferLength);

        if (startIndex != -1)
        {
            TInput[] inputs = new TInput[startIndex + 1];
            float[] times = new float[startIndex + 1];

            for (int i = startIndex; i >= 0; i--)
            {
                times[i] = inputTimeline.TimeAt(i);
                inputs[i] = inputTimeline[i];
            }

            return new TickerInputPack<TInput>()
            {
                inputs = inputs,
                times = times
            };
        }

        return new TickerInputPack<TInput>()
        {
            inputs = new TInput[0],
            times = new float[0]
        };
    }
}