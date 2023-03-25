using System;

public interface ITickableBase
{
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
public interface ITickable<TState, TInput> : ITickableBase
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
/// Qualifies a struct or class as a ticker input. TOwner is the struct or class type itself.
/// 
/// The ticker input can be e.g a set of player controls specified by booleans (isJumpButtonDown) and floats (horizontalMovement).
/// 
/// This is sent to a tickable component's Simulate() function during a seek.
/// </summary>
public interface ITickerInput<TOwner>
{
    /// <summary>
    /// Returns an input with optional deltas compared to the previous input. e.g. btnJumpPressed, btnJumpReleased
    /// When WithDeltas is called on the owner with the owner as the parameter, that can be considered WithoutDeltas.
    /// </summary>
    public TOwner WithDeltas(TOwner previousInput);
}

/// <summary>
/// Makes a class or struct usable as a ticker state snapshot.
/// 
/// These states can be inserted into a Ticker's timeline, which can then rewind or fast-forward an object's state if desired.
/// 
/// This should contain everything you want to synchronise, replay, or reconcile.
/// </summary>
public interface ITickerState<TState> : IEquatable<TState>
{
}

/// <summary>
/// Adds debugging functionality to an ITickerState, if desired
/// </summary>
public interface ITickerStateDebug
{
    void DebugDraw(UnityEngine.Color colour);
}

/// <summary>
/// An empty ticker input. Can be used with tickers that don't actually require any input. In these cases inputs can either not be used at all, or used to influence deltas
/// (as there are ticks that can deliberately follow the specific deltas between inputs when seeking, to allow variable input rates)
/// </summary>
public struct NullTickerInput : ITickerInput<NullTickerInput>
{
    public NullTickerInput WithDeltas(NullTickerInput previousInput) => default;
}

/// <summary>
/// Helper class for collecting recent inputs from an input timeline so they can be sent to a server or client
/// </summary>
public struct TickerInputPack<TInput>
{
    public TickerInputPack(TInput[] input, double[] times)
    {
        this.inputs = input;
        this.times = times;
    }

    public TInput[] inputs;
    public double[] times;

    /// <summary>
    /// Makes an InputPack from a given input history
    /// </summary>
    /// <returns></returns>
    public static TickerInputPack<TInput> MakeFromHistory(TimelineTrack<TInput> inputTimeline, float sendBufferLength)
    {
        int startIndex = inputTimeline.ClosestIndexBeforeOrEarliest(inputTimeline.LatestTime - sendBufferLength);

        if (startIndex != -1)
        {
            TInput[] inputs = new TInput[startIndex + 1];
            double[] times = new double[startIndex + 1];

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
            times = new double[0]
        };
    }
}