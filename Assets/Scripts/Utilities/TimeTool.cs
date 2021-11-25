using System.Runtime.InteropServices;
using UnityEngine;

public static class TimeTool
{
    [StructLayout(LayoutKind.Explicit)]
    private struct FloatHelper
    {
        [FieldOffset(0)] public float floatValue;
        [FieldOffset(0)] public uint uintValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleHelper
    {
        [FieldOffset(0)] public double doubleValue;
        [FieldOffset(0)] public ulong ulongValue;
    }

    /// <summary>
    /// Increments a float by the smallest possible unit
    /// </summary>
    private static float IncrementFloatMinutely(float value)
    {
        FloatHelper helper = new FloatHelper()
        {
            floatValue = value
        };
        helper.uintValue++;
        return helper.floatValue;
    }

    /// <summary>
    /// Increments a double by the smallest possible unit
    /// </summary>
    private static double IncrementDoubleMinutely(double value)
    {
        DoubleHelper helper = new DoubleHelper()
        {
            doubleValue = value
        };
        helper.ulongValue++;
        return helper.doubleValue;
    }

    /// <summary>
    /// Quantises a time into the given tick rate.
    /// </summary>
    public static float Quantize(float time, int ticksPerSecond)
    {
        // IncrementFloatMinutely is used so that Quantize() can be called on the result of another Quantize() and still return the same value. Without it they can return different values.
        Debug.Assert(ticksPerSecond > 0);
        return (long)(IncrementFloatMinutely(time * ticksPerSecond)) / (float)ticksPerSecond;
    }

    /// <summary>
    /// Quantises a time into the given tick rate
    /// </summary>
    public static double Quantize(double time, int ticksPerSecond)
    {
        Debug.Assert(ticksPerSecond > 0);
        return (long)(IncrementDoubleMinutely(time * ticksPerSecond)) / (double)ticksPerSecond;
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

    /// <summary>
    /// If the given time and delta time can be considered a tick at the rate of ticksPerSecond
    /// 
    /// Any kind of scaled or unscaled time can be provided as long as the deltaTime has already been applied to the time
    /// </summary>
    public static bool IsTick(double time, double deltaTime, int ticksPerSecond)
    {
        return (long)(time * ticksPerSecond) != (long)((time - deltaTime) * ticksPerSecond);
    }
}