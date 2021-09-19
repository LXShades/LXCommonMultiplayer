using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides core character movement functionality with "loopy" 3D movement support
/// </summary>
public class CharacterMovement : Movement
{
    public struct GroundInfo
    {
        public bool isOnGround;
        public bool isSlipping;
        public bool isLoopy;
        public Vector3 normal;
        public Vector3 hitPoint;
        public Vector3 loopyNormal;
        public Vector3 slipVector;
    }

    /// <summary>
    /// Up vector.
    /// </summary>
    public Vector3 up { get; set; }
    public Vector3 forward { get; set; }
    public Vector3 right => Vector3.Cross(up, forward).normalized;

    [Header("[CharMovement] Gravity")]
    public float gravity = 10f;

    [Header("[CharMovement] Grounding")]
    public LayerMask groundLayers = ~0;
    public float groundSphereTestRadius = 0.25f;
    public float groundTestDistanceThreshold = 0.05f;
    public float groundEscapeThreshold = 3f;

    [Header("[CharMovement] Slip")]
    public bool enableSlip = true;
    public float slipRadius = 0.25f;
    public float slipVelocity = 5f;

    [Header("[CharMovement] Loopy")]
    public bool enableLoopy = true;
    public bool enableLoopyPushdown = true;
    [Tooltip("Whether to auto-adjust the forward vector when the up vector changes")]
    public bool enableLoopyForwardAdjustment = true;
    public bool enableLoopySmoothNormals = true;
    public float loopyGroundTestDistance = 0.5f;
    public float loopyPushdownNeutralLimit = 0.2f;

    [Header("[CharMovement] Stepping")]
    public bool enableStepUp = false;
    public float stepHeight = 0.2f;
    public Vector3 stepLocalFootOffset = Vector3.zero;

    [Header("[CharMovement] Collision Velocity Response")]
    public bool enableCollisionsAffectVelocity = true;
    public bool enableCollisionsDontAffectVerticalVelocity = true;

    public void ApplyCharacterGravity(in GroundInfo groundInfo, float deltaTime)
    {
        // Grounding and gravity
        if (groundInfo.isOnGround && Vector3.Dot(groundInfo.normal, velocity) < groundEscapeThreshold)
            velocity = velocity.AlongPlane(groundInfo.normal); // along gravity vector, may be different for wall running
        else
        {
            if (groundInfo.isSlipping && enableSlip)
            {
                // Apply slip vector if there is one
                velocity.SetAlongAxis(groundInfo.slipVector, slipVelocity);
            }

            // Gravity - only apply when not on ground, otherwise slipping occurs
            velocity += new Vector3(0f, -gravity * deltaTime, 0f);
        }
    }

    public void ApplyCharacterVelocity(in GroundInfo groundInfo, float deltaTime, bool isRealtime)
    {
        // Do final movement
        Vector3 preMovePosition = transform.position;
        Vector3 preFootPosition = transform.TransformPoint(stepLocalFootOffset);
        Vector3 moveOffset = velocity * deltaTime;

        bool collisionOccurred = Move(moveOffset, out Movement.Hit hitOut, isRealtime);

        if (collisionOccurred)
        {
            if (enableStepUp && hitOut.hasPoint)
            {
                if (TryFindStepAlongMovement(preFootPosition, moveOffset, hitOut, out float stepUpHeight))
                {
                    // Try the move again, but step up this time
                    // Todo: Collision checking
                    transform.position = preMovePosition + up * stepUpHeight;

                    collisionOccurred = Move(moveOffset, out hitOut, isRealtime);
                }
            }
        }

        if (collisionOccurred && enableCollisionsAffectVelocity)
        {
            Vector3 velocityImpactNormal = hitOut.normal;

            if (Vector3.Dot(velocityImpactNormal, velocity) < 0f) // movement callbacks can affect velocity, so double-check that we're still colliding _against_ it before we cancel out all speed on that plane
            {
                float originalAlongUp = 0f;

                if (enableCollisionsDontAffectVerticalVelocity)
                    originalAlongUp = Vector3.Dot(velocity, up);

                velocity.SetAlongAxis(velocityImpactNormal, 0f);

                if (enableCollisionsDontAffectVerticalVelocity)
                    velocity.SetAlongAxis(up, originalAlongUp);
            }
        }

        Vector3 previousUp = up;
        if (groundInfo.isLoopy)
        {
            up = groundInfo.loopyNormal;

            if (enableLoopyPushdown)
            {
                // Apply downward force if we're able
                // we test for 1. ground far below us and 2. a satisfactory degree change
                // raycastLength is just long enough for us to pull an almost-90 degree turn, I could not find a reliable and useful way to calculate this so we just test as far as we can
                // and then check the degree difference after the raycast hits
                float raycastLength = Vector3.Distance(preMovePosition, transform.position);
                float raycastPullback = 0.01f;

                if (Physics.Raycast(new Ray(transform.position + up * raycastPullback, -up), out RaycastHit rayHit, raycastLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    // we might not want to push down if...
                    //  a) normals are the same but platform distances are different
                    //  b) normals rotate in the opposite direction we'd expect (eg we're moving forward, but rotation is going backward -- we shouldn't be pushing _down_ in that scenario)
                    //  c) we're trying to move upwards, beyond the ground escape velocity

                    bool situationA = Vector3.Dot(rayHit.normal, groundInfo.loopyNormal) >= 0.99f && rayHit.distance - raycastPullback > loopyPushdownNeutralLimit;
                    bool situationB = Vector3.Dot(rayHit.normal - groundInfo.loopyNormal, velocity) < 0f;
                    bool situationC = Vector3.Dot(up, velocity) >= groundEscapeThreshold;

                    if (!situationA && !situationB && !situationC)
                    {
                        // to get as close to the ground as possible in prep for the next frame, we need to move with our rotation simulating our final up vector
                        // todo we could do a collidercast? but the normals won't be as nice so maybe not
                        transform.rotation = Quaternion.LookRotation(forward.AlongPlane(rayHit.normal), rayHit.normal);
                        Move(-up * (rayHit.distance + raycastPullback), out _, true, Movement.MoveFlags.NoSlide);

                        velocity = velocity.AlongPlane(rayHit.normal);
                    }
                }
            }
        }
        else
            up = Vector3.up;

        if (enableLoopy && enableLoopyForwardAdjustment && previousUp != up)
        {
            forward = Quaternion.FromToRotation(previousUp, up) * forward;
        }
    }

    public void CalculateGroundInfo(out GroundInfo output)
    {
        bool hasGroundCastHit = Physics.SphereCast(new Ray(transform.position + up * groundSphereTestRadius, -up), groundSphereTestRadius, out RaycastHit groundHit, Mathf.Max(groundSphereTestRadius, loopyGroundTestDistance), groundLayers, QueryTriggerInteraction.Ignore);
        output = new GroundInfo()
        {
            isOnGround = false,
            isSlipping = false,
            isLoopy = false,
            normal = Vector3.up,
            loopyNormal = Vector3.up,
            slipVector = Vector3.zero
        };

        if (hasGroundCastHit)
        {
            // anywhere on the circle could be hit, we'll just test if the contact point is "close enough" to the vertical distance test threshold
            Vector3 slipVectorUnnormalized = (transform.position - groundHit.point).AlongPlane(up);
            float primaryDistance = -Vector3.Dot(groundHit.point - transform.position, up);
            float secondaryDistance = slipVectorUnnormalized.magnitude;

            output.hitPoint = groundHit.point;

            if (primaryDistance < groundTestDistanceThreshold)
            {
                output.isOnGround = true;

                // We need to provide a normal that isn't affected by the sphere cast. Two reasons:
                // * When flattening our velocity to the ground, the sphere cast causes corners to boost us when that's not what we really want
                // * It also causes our acceleration direction (if clamping to groundNormal) to be incorrectly based on the corner instead, making us climb corners when we push towards them.
                //   We don't really want to do that either (we should either be blocked or step up if that's enabled)
                // todo centre this cast based on the last cast's hit point so that we can latch onto the most relevant surface
                if (Physics.Raycast(transform.position + up * 0.01f, -up, out RaycastHit rayHit, groundTestDistanceThreshold + 0.01f, groundLayers, QueryTriggerInteraction.Ignore))
                    output.normal = rayHit.normal;
                else
                    output.normal = Vector3.up;
            }
            else if (secondaryDistance > slipRadius)
            {
                output.isSlipping = true && enableSlip;
                output.slipVector = slipVectorUnnormalized / secondaryDistance; // normalizes it (secondaryDistance = slipVectorUnnormalized.magnitude)
            }

            if (enableLoopy && primaryDistance <= loopyGroundTestDistance)
            {
                if (Physics.Raycast(new Ray(transform.position + up * 0.01f, -up), out RaycastHit rayHit, loopyGroundTestDistance, groundLayers, QueryTriggerInteraction.Ignore) && rayHit.distance <= loopyGroundTestDistance)
                {
                    output.isLoopy = true;

                    output.loopyNormal = enableLoopySmoothNormals ? GetSmoothestNormalFromCollision(rayHit) : rayHit.normal;
                }
            }
        }
    }

    private List<RaycastHit> stepHitBuffer = new List<RaycastHit>();
    public bool TryFindStepAlongMovement(Vector3 originalFootPosition, Vector3 attemptedMovementOffset, in Hit hit, out float outStepHeight)
    {
        if (hit.hasPoint)
        {
            float height = Vector3.Dot(hit.point - originalFootPosition, up);

            if (height >= -0.01f && height <= stepHeight)
            {
                // Check if it's a flat surface that continues flat from the point we hit at
                if (Physics.Raycast(hit.point + attemptedMovementOffset.AlongPlane(up).normalized * 0.05f + up * 0.05f, -up, out RaycastHit rayHit, 0.1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // Only step up if the steps are upright, avoid stepping on slopes
                    if (Vector3.Dot(rayHit.normal, up) >= 0.95f)
                    {
                        outStepHeight = Vector3.Dot(rayHit.point - originalFootPosition, up);
                        return true;
                    }
                }
            }
        }

        outStepHeight = 0f;
        return false;
    }

    private static List<Vector3> normalBuffer = new List<Vector3>();
    private static List<int> triangleBuffer = new List<int>();

    /// <summary>
    /// Tries to get a smooth normal from a collider. If successful, returns the smoothed normal; otherwise, returns the hit's normal as supplied.
    /// </summary>
    public Vector3 GetSmoothestNormalFromCollision(in RaycastHit hit)
    {
        if (hit.collider is MeshCollider meshCollider
                && meshCollider.sharedMesh.isReadable)
        {
            int index = hit.triangleIndex * 3;
            Mesh mesh = meshCollider.sharedMesh;

            mesh.GetNormals(normalBuffer);

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int start = mesh.GetSubMesh(i).indexStart;
                int count = mesh.GetSubMesh(i).indexCount;

                if (index >= start && index < start + count)
                {
                    mesh.GetIndices(triangleBuffer, i);

                    Vector3 smoothNormal = normalBuffer[triangleBuffer[index - start]] * hit.barycentricCoordinate.x +
                        normalBuffer[triangleBuffer[index - start + 1]] * hit.barycentricCoordinate.y +
                        normalBuffer[triangleBuffer[index - start + 2]] * hit.barycentricCoordinate.z;

                    return meshCollider.transform.TransformDirection(smoothNormal).normalized;
                }
            }
        }

        return hit.normal;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        DrawGizmoCircle(transform.TransformPoint(stepLocalFootOffset), groundSphereTestRadius, Color.blue);

        if (enableStepUp)
        {
            // Feet area
            DrawGizmoCircle(transform.TransformPoint(stepLocalFootOffset + new Vector3(0, stepHeight, 0)), groundSphereTestRadius, Color.green);
        }
    }

    private void DrawGizmoCircle(Vector3 position, float radius, Color color)
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12;
            float nextAngle = (i + 1) * Mathf.PI * 2f / 12;
            Debug.DrawLine(position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius, position + new Vector3(Mathf.Sin(nextAngle), 0f, Mathf.Cos(nextAngle)) * radius, color);
        }
    }
#endif
}
