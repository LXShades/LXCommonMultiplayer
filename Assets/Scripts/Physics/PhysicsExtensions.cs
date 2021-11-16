using UnityEngine;

public static class PhysicsExtensions
{
    private const int kMaxHits = 128;

    private static RaycastHit[] hitBuffer = new RaycastHit[kMaxHits];

    public struct Parameters
    {
        /// <summary>
        /// Ignores this object
        /// </summary>
        public GameObject ignoreObject;
    }

    /// <summary>
    /// Raycast with extension parameters
    /// </summary>
    public static bool Raycast(Vector3 start, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction triggerInteraction, in Parameters parameters)
    {
        int numHits = Physics.RaycastNonAlloc(new Ray(start, direction), hitBuffer, maxDistance, layerMask, triggerInteraction);
        return GetFilteredResult(hitBuffer, numHits, out hitInfo, in parameters);
    }

    /// <summary>
    /// SphereCast with extension parameters
    /// </summary>
    public static bool SphereCast(Vector3 start, Vector3 direction, float radius, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction triggerInteraction, in Parameters parameters)
    {
        int numHits = Physics.SphereCastNonAlloc(new Ray(start, direction), radius, hitBuffer, maxDistance, layerMask, triggerInteraction);
        return GetFilteredResult(hitBuffer, numHits, out hitInfo, in parameters);
    }

    /// <summary>
    /// CapsuleCast with extension parameters
    /// </summary>
    public static bool CapsuleCast(Vector3 pointA, Vector3 pointB, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction triggerInteraction, in Parameters parameters)
    {
        int numHits = Physics.CapsuleCastNonAlloc(pointA, pointB, radius, direction, hitBuffer, maxDistance, layerMask, triggerInteraction);
        return GetFilteredResult(hitBuffer, numHits, out hitInfo, in parameters);
    }

    /// <summary>
    /// Returns a normal with an infinitely thin line at the position. This can be useful as collisions with a rounded collider will result in a "rounded" normal when hitting against corners and that is sometimes undesirable.
    /// </summary>
    public static bool GetFlatNormal(ref Vector3 inOutNormal, Collider collider, Vector3 position, Vector3 travelDirection, Vector3 castDirection)
    {
        // this is horrible, but we want a line that'll almost definitely hit the surface regardless of normal. so we just pick least likely direction we'd assume the plane to face
        Vector3 castNml = castDirection.NormalizedWithSqrTolerance();
        Vector3 castPosition = position + travelDirection.NormalizedWithSqrTolerance() * 0.001f;
        bool wasSuccessful = collider.Raycast(new Ray(castPosition - castNml * 0.01f, castNml), out RaycastHit hitInfo, 0.02f);
        
        if (wasSuccessful)
            inOutNormal = Vector3.Dot(hitInfo.normal, castNml) < 0 ? hitInfo.normal : -hitInfo.normal; // I DON'T KNOW WHY THIS HAPPENS SOMETIMES THE NORMAL IS FRICKEN UPSIDE DOWN???

        return wasSuccessful;
    }

    private static bool GetFilteredResult(RaycastHit[] buffer, int numInBuffer, out RaycastHit hitInfo, in Parameters parameters)
    {
        if (numInBuffer == 0)
        {
            hitInfo = default;
            return false;
        }
        else
        {
            float closestDistance = float.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < numInBuffer; i++)
            {
                if (buffer[i].collider.gameObject != parameters.ignoreObject && buffer[i].distance < closestDistance)
                {
                    closestIndex = i;
                    closestDistance = buffer[i].distance;
                }
            }

            if (closestIndex != -1)
            {
                hitInfo = buffer[closestIndex];
                return true;
            }
            else
            {
                hitInfo = default;
                return false;
            }
        }
    }
}
