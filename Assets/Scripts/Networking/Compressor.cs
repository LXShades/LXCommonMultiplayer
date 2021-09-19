using UnityEngine;

public static class Compressor
{
    const int kMask8 = ~(~0 << 8);
    const int kMultiplier8 = ~(~0 << (8 - 1));

    public static ushort CompressFloat16(float value, float min, float max)
    {
        return (ushort)((value - min) * 65535 / (max - min) + 0.49999f);
    }

    public static float DecompressFloat16(ushort value, float min, float max)
    {
        return min + value * (max - min) / 65535;
    }

    public static uint CompressQuaternion32(Quaternion quaternion)
    {
        int result = 0;

        result |= ((int)(quaternion.x * kMultiplier8) & kMask8);
        result |= ((int)(quaternion.y * kMultiplier8) & kMask8) << 8;
        result |= ((int)(quaternion.z * kMultiplier8) & kMask8) << 16;
        result |= ((int)(quaternion.w * kMultiplier8) & kMask8) << 24;

        return (uint)result;
    }

    public static Quaternion DecompressQuaternion32(uint quaternion)
    {
        Quaternion result = default;

        // convert to int to sign-extend
        result.x = ((int)quaternion << (32 - 8) >> (32 - 8)) / (float)kMultiplier8;
        result.y = ((int)quaternion << (24 - 8) >> (32 - 8)) / (float)kMultiplier8;
        result.z = ((int)quaternion << (16 - 8) >> (32 - 8)) / (float)kMultiplier8;
        result.w = ((int)quaternion << (8 - 8)  >> (32 - 8)) / (float)kMultiplier8;

        return Quaternion.Normalize(result);
    }

    public static uint CompressNormal24(Vector3 normalVector)
    {
        int result = 0;
        const float kBias = 0.5f / kMultiplier8;

        result |= ((int)((normalVector.x > 0f ? normalVector.x + kBias : normalVector.x - kBias) * kMultiplier8) & kMask8);
        result |= ((int)((normalVector.y > 0f ? normalVector.y + kBias : normalVector.y - kBias) * kMultiplier8) & kMask8) << 8;
        result |= ((int)((normalVector.z > 0f ? normalVector.z + kBias : normalVector.z - kBias) * kMultiplier8) & kMask8) << 16;

        return (uint)result;
    }

    public static Vector3 DecompressNormal24(uint normalVector)
    {
        Vector3 result = default;

        result.x = ((int)normalVector << (32 - 8) >> (32 - 8)) / (float)kMultiplier8;
        result.y = ((int)normalVector << (24 - 8) >> (32 - 8)) / (float)kMultiplier8;
        result.z = ((int)normalVector << (16 - 8) >> (32 - 8)) / (float)kMultiplier8;

        return result;
    }

    public static ushort CompressNormal16(Vector3 normalVector)
    {
        int result = 0;

        result |= ((int)(normalVector.x * 63f) & ~(~0 << 7));
        result |= ((int)(normalVector.z * 63f) & ~(~0 << 7)) << 7;
        result |= (normalVector.y >= 0f ? 1 : 0) << 14;

        return (ushort)result;
    }

    public static Vector3 DecompressNormal16(ushort _normalVector)
    {
        Vector3 result = default;
        int normalVector = (int)_normalVector; // we're working in an int context while shifting, usually
        float ySign = (normalVector & (1 << 14)) == 0 ? -1f : 1f;

        result.x = (normalVector << (32 - 7) >> (32 - 7)) / 63f;
        result.z = (normalVector << (25 - 7) >> (32 - 7)) / 63f;

        result.y = Mathf.Sqrt(1 - result.x * result.x - result.z * result.z) * ySign; // we know it's a normal so we can estimate one of the components (let's use the vertical one), all we need to know is the sign (positive/negative)

        return result;
    }

    public static ulong CompressVectorVariable(Vector3 vector, float minMaxValue, int numBits)
    {
        int numBitsPerComponent = numBits / 3;
        uint maskPerComponent = (uint)(1 << numBitsPerComponent) - 1;
        int minMaxPerComponent = (1 << (numBitsPerComponent - 1)) - 1;
        float multiplier = (float)minMaxPerComponent / minMaxValue;

        ulong output = 0;
        output |= (ulong)(System.Math.Min(System.Math.Max(vector.x * multiplier + 0.5f, -minMaxPerComponent), minMaxPerComponent)) & maskPerComponent;
        output |= ((ulong)(System.Math.Min(System.Math.Max(vector.y * multiplier + 0.5f, -minMaxPerComponent), minMaxPerComponent)) & maskPerComponent) << numBitsPerComponent;
        output |= ((ulong)(System.Math.Min(System.Math.Max(vector.z * multiplier + 0.5f, -minMaxPerComponent), minMaxPerComponent)) & maskPerComponent) << (numBitsPerComponent * 2);

        return output;
    }

    public static Vector3 DecompressVectorVariable(ulong vector, float minMaxValue, int numBits)
    {
        int numBitsPerComponent = numBits / 3;
        int maskPerComponent = (1 << numBitsPerComponent) - 1;
        int oppositeBits = 64 - numBitsPerComponent;
        float multiplier = minMaxValue / (float)((1 << (numBitsPerComponent - 1)) - 1);

        Vector3 output = default;
        output.x = multiplier * ((((long)vector & maskPerComponent) << oppositeBits) >> oppositeBits); // shift left and then right to sign-extend
        output.y = multiplier * ((((long)(vector >> numBitsPerComponent) & maskPerComponent) << oppositeBits) >> oppositeBits);
        output.z = multiplier * ((((long)(vector >> (numBitsPerComponent * 2)) & maskPerComponent) << oppositeBits) >> oppositeBits);
        return output;
    }

    public static long CompressVector64(Vector3 vector, float minMaxValue)
    {
        const int kBitsPerComponent = 64 / 3;
        const int kMaskPerComponent = (1 << kBitsPerComponent) - 1;

        return (long)(UnitFloatToBits(vector.x / minMaxValue, kBitsPerComponent) & kMaskPerComponent)
            | ((long)(UnitFloatToBits(vector.y / minMaxValue, kBitsPerComponent) & kMaskPerComponent) << kBitsPerComponent)
            | ((long)(UnitFloatToBits(vector.z / minMaxValue, kBitsPerComponent) & kMaskPerComponent) << (kBitsPerComponent * 2));
    }

    public static Vector3 DecompressVector64(long vector, float minMaxValue)
    {
        const int kBitsPerComponent = 64 / 3;
        const int kMaskPerComponent = (1 << kBitsPerComponent) - 1;

        return new Vector3(
            BitsToUnitFloat((int)(vector & kMaskPerComponent), kBitsPerComponent) * minMaxValue,
            BitsToUnitFloat((int)(vector >> kBitsPerComponent) & kMaskPerComponent, kBitsPerComponent) * minMaxValue,
            BitsToUnitFloat((int)(vector >> (kBitsPerComponent * 2)) & kMaskPerComponent, kBitsPerComponent) * minMaxValue);
    }

    public static int UnitFloatToBits(float value, int numBits)
    {
        int bitMask = ~(~0 << numBits);
        float multiplier = (1 << (numBits - 1)) - 1f;
        int result = (int)(value * multiplier);
        return (value >= 0f ? (result & ~(1 << (numBits - 1))) : (result | (1 << (numBits - 1)))) & bitMask;
    }

    public static float BitsToUnitFloat(int value, int numBits)
    {
        float multiplier = (1 << (numBits - 1)) - 1f;
        float result = (value << (32 - numBits) >> (32 - numBits)) / multiplier;
        return result;
    }
}