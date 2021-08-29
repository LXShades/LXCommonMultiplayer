using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manually-controllable/tickable immediate sweep-based movement features for characters and objects
/// Similar to Unity's CharacterController, with more features and collision shapes.
/// </summary>
public class Movement : MonoBehaviour
{
    [Flags]
    public enum MoveFlags
    {
        None = 0,
        NoSlide = 1
    }

    [Header("Collision")]
    public bool enableCollision = true;

    [Header("Note: Colliders cannot currently rotate")]
    [Tooltip("List of colliders to be used in collision tests")]
    public Collider[] colliders = new Collider[0];

    [Tooltip("List of collision layers to interact with")]
    public LayerMask blockingCollisionLayers = ~0;

    [Header("Basic physics")]
    [Tooltip("If enabled, this component will control the object automatically on Update.")]
    public bool enableAutomaticPhysics = false;

    [Tooltip("Multiplier against gravity during basic physics simulation")]
    public float gravityMultiplier = 1f;

    [Tooltip("How much to bounce off objects during a basic physics simulation"), Range(0, 1)]
    public float bounceFactor = 0f;

    [Tooltip("When bouncing, this is a speed multiplier optionally reducing the speed where 0 does nothing and 1 cuts off all velocity"), Range(0, 1)]
    public float bounceFriction = 0;

    [Header("Advanced")]
    [Tooltip("Max number of collision steps to make. While the number of steps at maxCollisionStepSize varies depending on speed, this is an absolute maximum")]
    public int maxNumCollisionSteps = 3;

    [Tooltip("Size of a collision step before it is divided into another. Should be about half the size of the hitbox if using penetration testing.")]
    public float maxCollisionStepSize = 0.2f;

    [Tooltip("How much to pulls back the object when doing a sweep test. Sometimes a sweep test will pass the object through if it is started at the exact edge of the collision and this aims to prevent that")]
    public float sweepPullback = 0.05f;

    [Header("Debugging")]
    public bool debugDrawMovementShapes = false;

    /// <summary>
    /// The current velocity of the object
    /// </summary>
    [HideInInspector] public Vector3 velocity;


    private HashSet<IMovementCollisions> movementCollisions = new HashSet<IMovementCollisions>();

    // hit buffers for each function, avoiding allocations
    private RaycastHit[] moveHitBuffer = new RaycastHit[128];
    private RaycastHit[] colliderCastHitBuffer = new RaycastHit[128];

    Color[] debugColorByStage = new Color[] { Color.red, Color.green, Color.blue, Color.yellow };
    List<IMovementCollisions> cachedMovementCollisionComponents = new List<IMovementCollisions>(12);

    Collider[] nearbyColliderBuffer = new Collider[24];

    void Update()
    {
        if (enableAutomaticPhysics)
            SimulateBasicPhysics(Time.deltaTime, true);
    }

    /// <summary>
    /// Runs a default physics simulation (gravity, bounces, etc according to public parameters)
    /// </summary>
    public void SimulateBasicPhysics(float deltaTime, bool applyFinalMovement = true)
    {
        // Do the gravity
        velocity += Physics.gravity * deltaTime * gravityMultiplier;

        // Do the movement
        if (applyFinalMovement && Move(velocity * deltaTime, out RaycastHit hit))
        {
            Vector3 resistanceVector = hit.normal * (-Vector3.Dot(hit.normal, velocity) * (1f + bounceFactor));
            Vector3 frictionVector = -velocity * bounceFriction;

            frictionVector -= hit.normal * Vector3.Dot(hit.normal, frictionVector);

            velocity += resistanceVector + frictionVector;
        }
    }

    /// <summary>
    /// Moves with collision checking. Can be a computationally expensive operation
    /// </summary>
    public bool Move(Vector3 offset, out RaycastHit hitOut, bool isRealtime = true, MoveFlags flags = MoveFlags.None)
    {
        hitOut = default;

        if (offset == Vector3.zero)
            return false;
        if (!enableCollision)
        {
            transform.position += offset;
            return false; // that was easy
        }

        const float kSkin = 0.005f;
        int numIterations = (flags & MoveFlags.NoSlide) != 0 ? 1 : 3; // final iteration always blocks without slide, so just use one iteration
        Vector3 currentMovement = offset;
        bool hasHitOccurred = false;

        movementCollisions.Clear();

        for (int iteration = 0; iteration < numIterations; iteration++)
        {
            RaycastHit hit;
            float currentMovementMagnitude = currentMovement.magnitude;
            float pullback = iteration == 0 ? sweepPullback : 0f;
            Vector3 normalMovement = currentMovement.normalized;

            int numHits = ColliderCast(moveHitBuffer, transform.position, normalMovement, currentMovementMagnitude + kSkin, blockingCollisionLayers, QueryTriggerInteraction.Collide, pullback, debugDrawMovementShapes, debugColorByStage[iteration]);
            float closestDist = currentMovementMagnitude + kSkin;
            int closestHitId = -1;

            // find closest blocking collider
            for (int i = 0; i < numHits; i++)
            {
                if (!moveHitBuffer[i].collider.isTrigger && moveHitBuffer[i].distance < closestDist)
                {
                    closestDist = moveHitBuffer[i].distance;
                    closestHitId = i;
                }

                // acknowledge all collided movementcollision objects
                moveHitBuffer[i].collider.GetComponents<IMovementCollisions>(cachedMovementCollisionComponents);
                foreach (IMovementCollisions movementCollision in cachedMovementCollisionComponents)
                    movementCollisions.Add(movementCollision);
            }

            // Identify the closest blocking collision
            if (closestHitId != -1)
            {
                hit = moveHitBuffer[closestHitId];
                hitOut = hit;
                hasHitOccurred = true;

                if (iteration < numIterations - 1)
                {
                    // use slidey slidey collision until the final one is hit
                    currentMovement += hit.normal * (-Vector3.Dot(hit.normal, currentMovement.normalized * (currentMovement.magnitude - hit.distance)) + kSkin);
                }
                else
                {
                    // final collision: block further movement entirely
                    currentMovement = currentMovement.normalized * (hit.distance - kSkin);
                }
            }
            else
            {
                break; // we passed safely without collision
            }
        }

        // Do the move
        transform.position += currentMovement;

        // Call collision callbacks
        foreach (IMovementCollisions collisions in movementCollisions)
            collisions.OnMovementCollidedBy(this, isRealtime);

        return hasHitOccurred;
    }

    public void MovePenetration(Vector3 offset, bool isRealtime)
    {
        if (!enableCollision)
        {
            transform.position += offset;
            return; // done
        }

        if (offset.sqrMagnitude == 0f)
            return; // no moving to do

        float offsetMagnitude = offset.magnitude;
        float colliderExtentFromCentre = CalculateColliderExtentFromOrigin() + offsetMagnitude * 0.5f;
        int numSteps = Mathf.Clamp(Mathf.CeilToInt(offsetMagnitude / maxCollisionStepSize), 1, maxNumCollisionSteps);
        Vector3 currentPosition = transform.position;
        Vector3 stepOffset = offset / numSteps;
        Vector3 midPoint = transform.position + offset * 0.5f;

        movementCollisions.Clear();

        // detect nearby colliders
        // we use a sphere overlap with the sphere being in the centre of our path and extending to encompass the path plus our collider
        int numCollidersToTest = Physics.OverlapSphereNonAlloc(midPoint, colliderExtentFromCentre, nearbyColliderBuffer, blockingCollisionLayers, QueryTriggerInteraction.Collide);

        for (int step = 0; step < numSteps; step++)
        {
            currentPosition += stepOffset;

            for (int iteration = 0; iteration < 3; iteration++)
            {
                foreach (Collider collider in colliders)
                {
                    for (int i = 0; i < numCollidersToTest; i++)
                    {
                        if (collider.gameObject == nearbyColliderBuffer[i].gameObject)
                            continue; // please don't collide with yourself

                        if (Physics.ComputePenetration(collider, currentPosition, transform.rotation, nearbyColliderBuffer[i], nearbyColliderBuffer[i].transform.position, nearbyColliderBuffer[i].transform.rotation, out Vector3 direction, out float distance))
                        {
                            nearbyColliderBuffer[i].GetComponents<IMovementCollisions>(cachedMovementCollisionComponents);
                            foreach (IMovementCollisions movementCollision in cachedMovementCollisionComponents)
                                movementCollisions.Add(movementCollision);

                            if (!nearbyColliderBuffer[i].isTrigger)
                            {
                                currentPosition += direction * (distance + 0.001f); // tiny buffer to fix capsule cast used for feet detection, annoying but pretty much necessary because casts are dumb

                                goto NextIteration;
                            }
                        }
                    }
                }
                break;
                NextIteration:;
            }
        }

        transform.position = currentPosition;

        foreach (IMovementCollisions collisions in movementCollisions)
            collisions.OnMovementCollidedBy(this, isRealtime);
    }

    /// <summary>
    /// Performs a sweep test using our current colliders
    /// </summary>
    public int ColliderCast(RaycastHit[] hitsOut, Vector3 startPosition, Vector3 castDirection, float castMaxDistance, int layers, QueryTriggerInteraction queryTriggerInteraction, float pullback = 0f, bool drawDebug = false, Color drawDebugColor = default)
    {
        Matrix4x4 toWorld = transform.localToWorldMatrix;
        int numHits = 0;

        castDirection.Normalize();

        if (pullback > 0f)
        {
            startPosition -= castDirection * pullback;
            castMaxDistance += pullback;
        }

        toWorld = Matrix4x4.Translate(startPosition - transform.position) * toWorld;

        foreach (Collider collider in colliders)
        {
            int colliderNumHits = 0;

            if (collider is SphereCollider sphere)
            {
                float radius = sphere.radius * Mathf.Max(Mathf.Max(transform.lossyScale.x, transform.lossyScale.y), transform.lossyScale.z);

                colliderNumHits = Physics.SphereCastNonAlloc(
                    toWorld.MultiplyPoint(sphere.center),
                    radius,
                    castDirection,
                    colliderCastHitBuffer,
                    castMaxDistance,
                    layers,
                    queryTriggerInteraction);
            }
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 up = (capsule.direction == 0 ? transform.right : (capsule.direction == 1 ? transform.up : transform.forward)) * (Mathf.Max(capsule.height * 0.5f - capsule.radius, 0));
                Vector3 center = toWorld.MultiplyPoint(capsule.center);

                colliderNumHits = Physics.CapsuleCastNonAlloc(
                    center + up, center - up,
                    capsule.radius * Mathf.Max(Mathf.Max(transform.lossyScale.x, transform.lossyScale.y), transform.lossyScale.z),
                    castDirection,
                    colliderCastHitBuffer,
                    castMaxDistance,
                    layers,
                    queryTriggerInteraction);
            }
            else
            {
                continue; // couldn't detect collider type
            }

            for (int i = 0; i < colliderNumHits && numHits < hitsOut.Length; i++)
            {
                if (colliderCastHitBuffer[i].collider.gameObject == collider.gameObject)
                    continue; // avoid colliding with self...
                if (Vector3.Dot(colliderCastHitBuffer[i].normal, castDirection) > 0.01f)
                    continue; // in pullback collisions, don't collide with things we wouldn't actually crash into
                if (colliderCastHitBuffer[i].distance <= 0f)
                    continue; // this type of collision normally seems to happen when we're already inside them, which doesn't work nicely with pullback collisions

                // don't collide with children of self either
                for (Transform currentTransform = colliderCastHitBuffer[i].transform; currentTransform != null; currentTransform = currentTransform.parent)
                {
                    if (currentTransform.gameObject == collider.gameObject)
                        goto Skip;
                }

                // test passed, add this hit
                hitsOut[numHits] = colliderCastHitBuffer[i];
                hitsOut[numHits].distance -= pullback;
                numHits++;
                Skip:;
            }
        }

        return numHits;
    }

    private float CalculateColliderExtentFromOrigin()
    {
        float extent = 0f;
        foreach (Collider collider in colliders)
        {
            if (collider is SphereCollider colliderAsSphere)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsSphere.center + Vector3.up * colliderAsSphere.radius)));
            }
            else if (collider is CapsuleCollider colliderAsCapsule)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsCapsule.center + Vector3.up * (colliderAsCapsule.height * 0.5f + colliderAsCapsule.radius))));
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsCapsule.center - Vector3.up * (colliderAsCapsule.height * 0.5f + colliderAsCapsule.radius))));
            }
            else if (collider is BoxCollider colliderAsBox)
            {
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsBox.center + colliderAsBox.size)));
                extent = Mathf.Max(extent, Vector3.Distance(transform.position, transform.TransformPoint(colliderAsBox.center - colliderAsBox.size)));
            }
        }

        return extent;
    }

    private void OnValidate()
    {
        int numValidColliders = 0;

        foreach (Collider collider in colliders)
        {
            if ((collider is CapsuleCollider) || (collider is SphereCollider))
                numValidColliders++;
        }

        if (numValidColliders != colliders.Length)
        {
            Collider[] collidersNew = new Collider[numValidColliders];
            int numRemoved = 0;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (!((colliders[i] is CapsuleCollider) || (colliders[i] is SphereCollider)))
                    numRemoved++;
                else
                    collidersNew[i - numRemoved] = colliders[i];
            }

            colliders = collidersNew;
            Debug.LogError($"{gameObject.name}: Movement component currently only supports CapsuleColliders and SphereColliders. Incompatible colliders have been removed.", gameObject);
        }

        if (colliders.Length == 0 && enableCollision)
            Debug.LogWarning($"{gameObject.name}: Movement component collider list is empty. Collision will be ignored.", gameObject);
    }
}

public interface IMovementCollisions
{
    void OnMovementCollidedBy(Movement source, bool isRealtime);
}