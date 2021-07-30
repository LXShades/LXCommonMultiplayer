using UnityEngine;

public static class TimeTool
{
    /// <summary>
    /// Quantises a time into the given tick rate
    /// </summary>
    public static float Quantize(float time, int ticksPerSecond)
    {
        Debug.Assert(ticksPerSecond > 0);
        return (long)(time * ticksPerSecond) / (float)ticksPerSecond;
    }

    /// <summary>
    /// If the given time and delta time can be considered a tick at the rate of ticksPerSecond
    /// 
    /// Any kind of scaled or unscaled time can be provided as long as the deltaTime has already been applied to the time
    /// </summary>
    public static bool IsTick(float time, float deltaTime, int ticksPerSecond)
    {
        return (long)(time * ticksPerSecond) != (long)((time - deltaTime) * ticksPerSecond);
    }
}