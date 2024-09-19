using System;
using System.Text.RegularExpressions;
using UnityEngine;
using static PhysicsExtensions;

public static class PhysicsExtensions
{
    private const int kMaxHits = 128;

    private static RaycastHit[] hitBuffer = new RaycastHit[kMaxHits];
    private static Collider[] overlapBuffer = new Collider[kMaxHits];
    private static Collider[] overlapBufferB = new Collider[kMaxHits];

    private static int[] collidingLayerMaskPerLayer = new int[32];
    private static bool[] hasCollidingLayerMaskPerLayer = new bool[32];

    public struct Parameters
    {
        /// <summary>
        /// Ignores this object
        /// </summary>
        public GameObject ignoreObject;
        public Func<Collider, bool> ignoreMatching;
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
    /// Performs a sweep test using our current colliders
    /// </summary>
    public static int ColliderCast(Collider collider, Vector3 startPosition, Vector3 castVector, RaycastHit[] hitsOut, int layerMask, QueryTriggerInteraction queryTriggerInteraction, in Parameters parameters, float pullback = 0f)
    {
        float castMaxDistance = castVector.magnitude;

        if (castMaxDistance <= 0f)
            return 0;

        Vector3 castDirection = castVector / castMaxDistance;

        if (pullback > 0f)
        {
            startPosition -= castDirection * pullback;
            castMaxDistance += pullback;
        }

        Matrix4x4 toWorld = collider.transform.localToWorldMatrix;
        Vector3 offset = startPosition - collider.transform.position;
        toWorld.m03 += offset.x;
        toWorld.m13 += offset.y;
        toWorld.m23 += offset.z;

        Vector3 myUp = toWorld.MultiplyVector(Vector3.up);
        Vector3 myRight = toWorld.MultiplyVector(Vector3.right);
        Vector3 myForward = toWorld.MultiplyVector(Vector3.forward);
        Vector3 myLossyScale = collider.transform.lossyScale;
        int numHits = 0;

        switch (collider)
        {
            case SphereCollider sphere:
                float radius = sphere.radius * Mathf.Max(Mathf.Max(myLossyScale.x, myLossyScale.y), myLossyScale.z);

                numHits = Physics.SphereCastNonAlloc(toWorld.MultiplyPoint(sphere.center), radius, castDirection, hitBuffer, castMaxDistance, layerMask, queryTriggerInteraction);
                break;
            case CapsuleCollider capsule:
            {
                Vector3 up = (capsule.direction == 0 ? myRight : (capsule.direction == 1 ? myUp : myForward)) * (Mathf.Max(capsule.height * 0.5f - capsule.radius, 0));
                Vector3 center = toWorld.MultiplyPoint(capsule.center);

                numHits = Physics.CapsuleCastNonAlloc(
                    center + up, center - up,
                    capsule.radius * Mathf.Max(Mathf.Max(myLossyScale.x, myLossyScale.y), myLossyScale.z),
                    castDirection, hitBuffer, castMaxDistance, layerMask, queryTriggerInteraction);
                break;
            }
            case CharacterController controller:
            {
                Vector3 up = myUp * (Mathf.Max(controller.height * 0.5f - controller.radius, 0));
                Vector3 center = toWorld.MultiplyPoint(controller.center);

                numHits = Physics.CapsuleCastNonAlloc(
                    center + up, center - up,
                    controller.radius * Mathf.Max(Mathf.Max(myLossyScale.x, myLossyScale.y), myLossyScale.z),
                    castDirection, hitBuffer, castMaxDistance, layerMask, queryTriggerInteraction);
                break;
            }
            case BoxCollider box:
                numHits = Physics.BoxCastNonAlloc(
                    toWorld.MultiplyPoint(box.center),
                    new Vector3(box.size.x * myLossyScale.x, box.size.y * myLossyScale.y, box.size.z * myLossyScale.z) * 0.5f,
                    castDirection, hitBuffer, collider.transform.rotation, // todo: consistency between toWorld and collider to world
                    castMaxDistance, layerMask, queryTriggerInteraction);
                break;
            default:
                Debug.LogError($"[PhysicsExtensions.OverlapCollider] Unsupported collider type {collider.GetType()}, sorry!");
                return 0;
        }

        return GetFilteredResults(collider, hitBuffer, numHits, parameters, hitsOut);
    }

    /// <summary>
    /// Returns colliders overlapping this one
    /// </summary>
    public static int Overlap(Collider collider, QueryTriggerInteraction triggerInteraction, in Parameters parameters, Collider[] overlapsOut)
    {
        return Overlap(collider.transform.position, collider, triggerInteraction, in parameters, overlapsOut);
    }

    /// <summary>
    /// Returns colliders that would overlap the given collider if it were at the given position
    /// </summary>
    public static int Overlap(Vector3 position, Collider collider, QueryTriggerInteraction triggerInteraction, in Parameters parameters, Collider[] overlapsOut)
    {
        int numOverlaps = 0;
        Transform transform = collider.transform;

        switch (collider)
        {
            // todo: support collider include/exclude layers? (pain)
            case BoxCollider asBox:
                numOverlaps = Physics.OverlapBoxNonAlloc(position + transform.TransformVector(asBox.center), VectorExtensions.Multiplied(asBox.size, transform.lossyScale) * 0.5f, overlapBuffer, transform.rotation, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            case SphereCollider asSphere:
                numOverlaps = Physics.OverlapSphereNonAlloc(position + transform.TransformVector(asSphere.center), asSphere.radius, overlapBuffer, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            case CapsuleCollider asCapsule:
            {
                // todo: non uniform scaling fixes
                float halfHeight = Mathf.Min(asCapsule.radius, asCapsule.height * 0.5f);
                Vector3 tipOffset = transform.TransformVector(asCapsule.direction == 0 ? new Vector3(halfHeight, 0f, 0f) : (asCapsule.direction == 1 ? new Vector3(0f, halfHeight, 0f) : new Vector3(0f, 0f, halfHeight)));
                Vector3 center = position + transform.TransformVector(asCapsule.center);
                numOverlaps = Physics.OverlapCapsuleNonAlloc(center - tipOffset, center + tipOffset, asCapsule.radius, overlapBuffer, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            }
            case CharacterController asController:
            {
                // todo: non uniform scaling fixes
                float halfHeight = Mathf.Min(asController.radius, asController.height * 0.5f);
                Vector3 tipOffset = transform.TransformVector(new Vector3(0f, halfHeight, 0f));
                Vector3 center = position + transform.TransformVector(asController.center);
                numOverlaps = Physics.OverlapCapsuleNonAlloc(center - tipOffset, center + tipOffset, asController.radius, overlapBuffer, GetCollidingLayerMaskForLayer(collider.gameObject.layer), triggerInteraction);
                break;
            }
            default:
                // note: if it's not supported, doesn't mean it's not possible, just means I haven't had to use it yet
                Debug.LogError($"[PhysicsExtensions.OverlapCollider] Unsupported collider type {collider.GetType()}, sorry!");
                return 0;
        }

        return GetFilteredResults(collider, overlapBuffer, numOverlaps, parameters, overlapsOut);
    }

    /// <summary>
    /// Returns whether a collider has any overlaps
    /// </summary>
    public static bool HasOverlap(Collider collider, QueryTriggerInteraction triggerInteraction, in Parameters parameters)
    {
        return Overlap(collider, triggerInteraction, in parameters, overlapBufferB) > 0;
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

    public static bool TryGetClosestHit(RaycastHit[] hits, int numInBuffer, out RaycastHit closestHit)
    {
        if (numInBuffer == 0)
        {
            closestHit = default;
            return false;
        }
        else
        {
            float closestDistance = float.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < numInBuffer; i++)
            {
                if (hits[i].distance < closestDistance)
                {
                    closestIndex = i;
                    closestDistance = hits[i].distance;
                }
            }

            closestHit = hits[closestIndex];
            return true;
        }
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

    private static bool GetFilteredResult(Collider instigatingCollider, Collider[] buffer, int numInBuffer, in Parameters parameters)
    {
        for (int i = 0; i < numInBuffer; i++)
        {
            if (buffer[i] != instigatingCollider && buffer[i] != parameters.ignoreObject && (parameters.ignoreMatching == null || !parameters.ignoreMatching.Invoke(buffer[i])))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetFilteredResults(Collider instigatingCollider, RaycastHit[] allHits, int numInBuffer, in Parameters parameters, RaycastHit[] filteredHitsOut)
    {
        int numOut = 0;
        for (int i = 0; i < numInBuffer && numOut < filteredHitsOut.Length; i++)
        {
            if (allHits[i].collider.gameObject != parameters.ignoreObject && allHits[i].collider.gameObject != instigatingCollider.gameObject && allHits[i].distance != 0 && (parameters.ignoreMatching == null || !parameters.ignoreMatching.Invoke(allHits[i].collider)))
                filteredHitsOut[numOut++] = allHits[i];
        }

        return numOut;
    }

    private static int GetFilteredResults(Collider instigatingCollider, Collider[] allOverlaps, int numInBuffer, in Parameters parameters, Collider[] filteredOverlapsOut)
    {
        int numOut = 0;
        for (int i = 0; i < numInBuffer && numOut < filteredOverlapsOut.Length; i++)
        {
            if (allOverlaps[i].gameObject != parameters.ignoreObject && allOverlaps[i].gameObject != instigatingCollider.gameObject && (parameters.ignoreMatching == null || !parameters.ignoreMatching.Invoke(allOverlaps[i])))
                filteredOverlapsOut[numOut++] = allOverlaps[i];
        }

        return numOut;
    }
}
