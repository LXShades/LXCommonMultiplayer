using UnityEngine;

/// <summary>
/// A robust timer class that can be started and stopped with a given duration
/// </summary>
public class Timer
{
    /// <summary>
    /// Returns the amount of time left before the timer expires, in seconds
    /// </summary>
    public float timeLeft => Mathf.Max(finishedTime - Time.time, 0.0f);

    /// <summary>
    /// Returns the time since the timer started, in seconds
    /// </summary>
    public float timeSinceStart => startTime > -1f ? Time.time - startTime : -1f;

    /// <summary>
    /// The progress of the timer, starting at 0 and ending at 1 when the timer is finished
    /// </summary>
    public float progress
    {
        get
        {
            if (finishedTime - startTime > 0.0f)
            {
                return Mathf.Clamp01((Time.time - startTime) / (finishedTime - startTime));
            }
            else
            {
                return 1.0f;
            }
        }
    }

    /// <summary>
    /// Whether the timer is still ticking
    /// </summary>
    public bool isRunning => Time.time < finishedTime;

    /// <summary>The Time.time value when this timer was last started</summary>
    public float startTime { get; private set; } = 0.0f;

    /// <summary>The Time.time value when this timer will be finished</summary>
    public float finishedTime { get; private set; } = 0.0f;

    /// <summary>Returns whether the timer has just finished on this frame</summary>
    public bool hasJustFinished => Time.time >= finishedTime && Time.time - Time.deltaTime < finishedTime;

    /// <summary>
    /// Starts the timer with the given duration
    /// </summary>
    /// <param name="seconds">Number of seconds to count down from</param>
    public void Start(float seconds)
    {
        // Set the start and finished time
        startTime = Time.time;
        finishedTime = Time.time + seconds;
    }

    /// <summary>
    /// Starts the timer with no duration. (timeSinceStart can still be measured)
    /// </summary>
    public void Start()
    {
        startTime = Time.time;
        finishedTime = Time.time;
    }

    /// <summary>
    /// Stops and resets the timer
    /// </summary>
    public void Stop()
    {
        startTime = -1.0f;
        finishedTime = -1.0f;
    }
}
