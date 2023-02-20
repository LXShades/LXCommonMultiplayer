using UnityEngine;

public static class PhysicsExtensions
{
    private const int kMaxHits = 128;

    private static RaycastHit[] hitBuffer = new RaycastHit[kMaxHits];
    private static Collider[] overlapBuffer = new Collider[kMaxHits];

    private static int[] collidingLayerMaskPerLayer = new int[32];
    private static bool[] hasCollidingLayerMaskPerLayer = new bool[32];

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
    /// Returns colliders overlapping this one
    /// </summary>
    public static int Overlap(Collider collider, QueryTriggerInteraction triggerInteraction, in Parameters parameters, Collider[] overlapsOut)
    {
        int numCollidersOut = 0;
        int numOverlaps = 0;
        Transform transform = collider.transform;

        switch (collider)
        {
            // todo: support collider include/exclude layers? (pain)
            case BoxCollider asBox:
                numOverlaps = Physics.OverlapBoxNonAlloc(transform.TransformPoint(asBox.center), VectorExtensions.Multiplied(asBox.size, transform.lossyScale) * 0.5f, overlapBuffer, transform.rotation, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            case SphereCollider asSphere:
                numOverlaps = Physics.OverlapSphereNonAlloc(transform.TransformPoint(asSphere.center), asSphere.radius, overlapBuffer, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            default:
                // note: if it's not supported, doesn't mean it's not possible, just means I haven't had to use it yet
                Debug.LogError($"[PhysicsExtensions.OverlapCollider] Unsupported collider type {collider.GetType()}, sorry!");
                return 0;
        }

        for (int i = 0; i < numOverlaps && numCollidersOut < overlapsOut.Length; i++)
        {
            Collider overlap = overlapBuffer[i];
            if (overlap != parameters.ignoreObject && overlap != collider)
                overlapsOut[numCollidersOut++] = overlap;
        }
        return numCollidersOut;
    }

    /// <summary>
    /// Returns whether a collider has any overlaps
    /// </summary>
    public static bool HasOverlap(Collider collider, QueryTriggerInteraction triggerInteraction, in Parameters parameters)
    {
        int numOverlaps = 0;
        Transform transform = collider.transform;
        switch (collider)
        {
            // todo: support collider include/exclude layers? (pain)
            case BoxCollider asBox:
                numOverlaps = Physics.OverlapBoxNonAlloc(transform.TransformPoint(asBox.center), VectorExtensions.Multiplied(asBox.size, transform.lossyScale) * 0.5f, overlapBuffer, transform.rotation, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            default:
                // note: if it's not supported, doesn't mean it's not possible, just means I haven't had to use it yet
                Debug.LogError($"[PhysicsExtensions.OverlapCollider] Unsupported collider type {collider.GetType()}, sorry!");
                return false;
        }

        return GetFilteredResult(overlapBuffer, numOverlaps, in parameters, collider);
    }

    public static int GetCollidingLayerMaskForLayer(int layer)
    {
        if (layer < 0 || layer > 32)
            return 0;

        if (!hasCollidingLayerMaskPerLayer[layer])
        {
            int mask = 0;
            for (int i = 0; i < 32; i++)
                mask |= Physics.GetIgnoreLayerCollision(layer, i) ? 0 : (1 << i);
            collidingLayerMaskPerLayer[layer] = mask;
        }

        return collidingLayerMaskPerLayer[layer];
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

    private static bool GetFilteredResult(Collider[] buffer, int numInBuffer, in Parameters parameters, Collider additionalIgnore)
    {
        for (int i = 0; i < numInBuffer; i++)
        {
            if (buffer[i] != additionalIgnore && buffer[i] != parameters.ignoreObject)
            {
                return true;
            }
        }

        return false;
    }
}
