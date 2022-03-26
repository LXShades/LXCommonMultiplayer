using System;
using System.Collections.Generic;
using UnityEngine;
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
/// Usage example: using (MultiSeek seek = MakeNew()) { seek.Add(tickerA); seek.Add(tickerB); seek.Run() }
/// 
/// WIP: Events not supported, some flags unsupported, just making what's needed for my project atm
/// </summary>
/// </summary>
public class MultiSeek : IDisposable
{
    public struct Operation
    {
        public TickerBase target;
    }

    public List<Operation> operations { get; private set; }

    /// <summary>
    /// The target time to seek to by default
    /// </summary>
    public double targetTime;

    /// <summary>
    /// The fixed quantised tickrate to use when seeking. Default value = 30
    /// </summary>
    public int tickRate;

    /// <summary>
    /// The default flags used for the seek. Default value = None
    /// </summary>
    public TickerSeekFlags flags;

    /// <summary>
    /// The maximum number of iterations per-target during the MultiSeek. Default value = 15
    /// </summary>
    public int maxIterations;

    /// <summary>
    /// Max allowed delta time in a tick. Clamps unexpected gaps. Default value = 1/tickRate * 2
    /// </summary>
    public float maxDeltaTime;

    /// <summary>
    /// Grabs a new MultiSeek, pooled
    /// </summary>
    public static MultiSeek MakeNew(double targetTime, int tickRate = 30, TickerSeekFlags flags = TickerSeekFlags.None)
    {
        MultiSeek output;
        if (multiSeekPool.Count > 0)
        {
            output = multiSeekPool[multiSeekPool.Count - 1];
            multiSeekPool.RemoveAt(multiSeekPool.Count - 1);
        }
        else
        {
            output = new MultiSeek();
        }

        output.targetTime = targetTime;
        output.tickRate = tickRate;
        output.flags = flags;
        output.maxIterations = 15;
        output.maxDeltaTime = 1f / tickRate * 2f;

        return output;
    }

    public void Dispose()
    {
        multiSeekPool.Add(this);
    }

    public void Add(TickerBase target)
    {
        if (target.isDebugPaused)
            return; // don't sign up debug-paused tickers for ticking
#if DEBUG
        Debug.Assert(operations.FindIndex(a => a.target == target) == -1);
#endif
        operations.Add(new Operation()
        {
            target = target
        });
    }

    public void Run()
    {
        TickerBase.SeekMultiple(this);
    }

    // Pooling stuff follows
    private MultiSeek() { operations = new List<Operation>(); }

    private const int kMultiSeekPoolSize = 64;

    private static List<MultiSeek> multiSeekPool
    {
        get
        {
            if (_multiSeekPool == null)
            {
                _multiSeekPool = new List<MultiSeek>(kMultiSeekPoolSize);
                for (int i = 0; i < kMultiSeekPoolSize; i++)
                    _multiSeekPool.Add(new MultiSeek());
            }
            return _multiSeekPool;
        }
    }
    private static List<MultiSeek> _multiSeekPool;
}
