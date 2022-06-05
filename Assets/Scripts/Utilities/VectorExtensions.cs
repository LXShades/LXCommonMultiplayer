using UnityEngine;

public static class VectorExtensions
{
    /// <summary>
    /// Creates a vector with all components initialised to inAllComponents
    /// </summary>
    public static Vector3 AllComponents(float inAllComponents)
    {
        return new Vector3(inAllComponents, inAllComponents, inAllComponents);
    }

    /// <summary>
    /// Returns a component-wise multiplied vector
    /// </summary>
    public static Vector3 Multiplied(in this Vector3 vec, in Vector3 other)
    {
        return new Vector3(vec.x * other.x, vec.y * other.y, vec.z * other.z);
    }

    /// <summary>
    /// Returns the horizontal components (x,z) of the vector
    /// </summary>
    public static Vector3 Horizontal(in this Vector3 vec)
    {
        return new Vector3(vec.x, 0, vec.z);
    }

    /// <summary>
    /// Sets the horizontal component of the vector only
    /// </summary>
    public static void SetHorizontal(ref this Vector3 vec, Vector3 value)
    {
        vec.x = value.x;
        vec.z = value.z;
    }

    /// <summary>
    /// Returns how far along the axis the vector goes. The axis does not need to be normalized
    /// </summary>
    public static float AlongAxis(in this Vector3 vec, Vector3 axis)
    {
        float mag = axis.sqrMagnitude;
        if (mag <= 0.999f || mag >= 1.001f)
            axis.Normalize();

        return Vector3.Dot(vec, axis);
    }

    /// <summary>
    /// Sets how far along the axis the vector goes by adding or subtracting the axis. The axis does not need to be normalized
    /// </summary>
    public static void SetAlongAxis(ref this Vector3 vec, Vector3 axis, float magnitude)
    {
        float mag = axis.sqrMagnitude;
        if (mag <= 0.999f || mag >= 1.001f)
            axis.Normalize();

        vec = vec + axis * (magnitude - Vector3.Dot(vec, axis));
    }

    /// <summary>
    /// Sets how far along the axis the vector goes by removing the vector until it no longer goes along the axis and adding the axis until magnitude is met.
    /// </summary>
    public static void HardStopAxis(ref this Vector3 vec, Vector3 axis)
    {
        if (Vector3.Dot(vec, axis) < 0f)
            vec = Vector3.zero;
    }

    /// <summary>
    /// Sets the component of the vector going along the plane with a normal of planeNormal, ignoring components that go along the planeNormal itself
    /// For example, if planeNormal is Vector3.up, this is the same as SetHorizontal.
    /// </summary>
    public static void SetAlongPlane(ref this Vector3 vec, Vector3 planeNormal, Vector3 valueAlongPlane)
    {
        float mag = planeNormal.sqrMagnitude;
        if (mag <= 0.999f || mag >= 1.001f)
            planeNormal.Normalize();

        vec = valueAlongPlane + planeNormal * (Vector3.Dot(vec, planeNormal) - Vector3.Dot(valueAlongPlane, planeNormal));
    }

    /// <summary>
    /// Gets the component of the vector going along the plane with a normal of planeNormal, ignoring components that go along the planeNormal itself.
    /// For example, if planeNormal is Vector3.up, this is the same as Horizontal.
    /// </summary>
    /// <param name="vec"></param>
    /// <param name="planeNormal"></param>
    /// <returns></returns>
    public static Vector3 AlongPlane(in this Vector3 vec, Vector3 planeNormal)
    {
        float mag = planeNormal.sqrMagnitude;
        if (mag <= 0.999f || mag >= 1.001f)
            planeNormal.Normalize();

        return vec - planeNormal * Vector3.Dot(vec, planeNormal);
    }

    /// <summary>
    /// Returns the vector normalized if outside the given squared length tolerance
    /// </summary>
    public static Vector3 NormalizedWithSqrTolerance(in this Vector3 vec, float sqrTolerance = 0.0001f)
    {
        return Mathf.Abs(vec.sqrMagnitude - 1f) <= sqrTolerance ? vec : vec.normalized;
    }

    /// <summary>
    /// Pushes the sphere at spherePosition of radius 'radius' away from pointToAvoid if it overlaps the point.
    /// The pushback goes as far along 'normal' as needed to push the point out of the sphere.
    /// If the sphere would collide with the point if the sphere were going along an infinite line against normal, i.e. goes beyond it, the pushback will still occur
    /// </summary>
    public static Vector3 SphereAvoidPointAlongNormalUnclamped(Vector3 spherePosition, float sphereRadius, Vector3 pointToAvoid, Vector3 normal)
    {
        normal.Normalize();

        Vector3 sphereOnPlane = spherePosition + normal * Vector3.Dot(pointToAvoid - spherePosition, normal);
        float planarDistance = Vector3.Distance(sphereOnPlane, pointToAvoid) / sphereRadius;

        if (planarDistance >= 0f && planarDistance < 1f)
        {
            float pushAwayAmount = Mathf.Sqrt(1f - planarDistance * planarDistance) * sphereRadius;
            float alongNormal = Vector3.Dot(spherePosition - pointToAvoid, normal);

            if (alongNormal < pushAwayAmount)
                spherePosition = sphereOnPlane + normal * pushAwayAmount;
        }

        return spherePosition;
    }

    /// <summary>
    /// Tries to "slip" a sphere colliding against a corner at CornerPosition.
    /// This assumes a vertical wall where the top edge is cornerPosition that the sphere has collided with.
    /// spherePosition is the starting position of the sphere
    /// sphereStep is the vector the sphere is moving along e.g. velocity * deltaTime
    /// The output is an updated vector that tries to preserve the step movement but slide it down the wall
    /// </summary>
    public static Vector3 SphereSlipCornerXZ(Vector3 spherePosition, float sphereRadius, Vector3 sphereStep, Vector3 cornerPosition, Vector3 wallNormal, float wallPadding = 0.01f)
    {
        wallNormal.Normalize();

        // Try and touch the wall, but keep the y movement going (note: we assume it's a wall, proper slope support would be better when possible)
        Vector3 nextSpherePosition = spherePosition + sphereStep;
        if (nextSpherePosition.y < cornerPosition.y)
        {
            // we slide down the wall, further down than the point of collision, so we assume the whole wall pushes us away
            if (Vector3.Dot(nextSpherePosition - cornerPosition, wallNormal) < sphereRadius + wallPadding)
            {
                return nextSpherePosition + wallNormal * (Vector3.Dot(cornerPosition - nextSpherePosition, wallNormal) + (sphereRadius + wallPadding));
            }
            else
            {
                return nextSpherePosition;
            }
        }
        else
        {
            // we slide down the wall a bit, but still being pushed away by the point of collision
            return VectorExtensions.SphereAvoidPointAlongNormalUnclamped(nextSpherePosition, sphereRadius + wallPadding, cornerPosition, wallNormal);
        }

        return nextSpherePosition;
    }

    /// <summary>
    /// Returns the Vector2 as a Vector3 with z=0f
    /// </summary>
    public static Vector3 ToVector3(in this Vector2 vec)
    {
        return new Vector3(vec.x, vec.y, 0f);
    }

    /// <summary>
    /// Returns the Vector3 as a Vector2
    /// </summary>
    public static Vector2 ToVector2(in this Vector3 vec)
    {
        return new Vector2(vec.x, vec.y);
    }
}