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

    protected Vector3 up;
    protected Vector3 forward;
    protected Vector3 right => Vector3.Cross(up, forward).normalized;

    [Header("[CharMovement] Gravity")]
    public float gravity = 10f;

    [Header("[CharMovement] Grounding")]
    public float groundSphereTestRadius = 0.25f;
    public float groundTestDistanceThreshold = 0.05f;
    public float groundEscapeThreshold = 3f;
    public float slipRadius = 0.25f;
    public float slipVelocity = 5f;

    [Header("[CharMovement] Loopy")]
    public bool enableLoopy = true;
    public float loopyGroundTestDistance = 0.5f;
    public float loopyPushdownMinRadius = 1f;
    public float loopyPushdownDegreesRequired = 5f;
    public float loopyPushdownNeutralLimit = 0.2f;

    [Header("[CharMovement] Enable")]
    public bool enableSlip = true;
    public bool enableCollisionsAffectVelocity = true;
    public bool enableLoopyPushdown = true;

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

    public void ApplyCharacterVelocity(in GroundInfo groundInfo, float deltaTime)
    {
        // Do final movement
        Vector3 preMovePosition = transform.position;
        if (Move(velocity * deltaTime, out Movement.Hit hitOut))
        {
            if (enableCollisionsAffectVelocity && deltaTime > 0f)
            {
                //movement.velocity = (transform.position - positionBeforeMovement) / deltaTime;
                velocity.SetAlongAxis(hitOut.normal, 0f);
            }
        }

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

                    if (!situationA && !situationB)
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
    }

    public void CalculateGroundInfo(out GroundInfo output)
    {
        bool hasGroundCastHit = Physics.SphereCast(new Ray(transform.position + up * groundSphereTestRadius, -up), groundSphereTestRadius, out RaycastHit groundHit, Mathf.Max(groundSphereTestRadius, loopyGroundTestDistance), ~0, QueryTriggerInteraction.Ignore);
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
                output.normal = groundHit.normal;
            }
            else if (secondaryDistance > slipRadius)
            {
                output.isSlipping = true;
                output.slipVector = slipVectorUnnormalized / secondaryDistance; // normalizes it (secondaryDistance = slipVectorUnnormalized.magnitude)
            }

            if (enableLoopy && primaryDistance <= loopyGroundTestDistance)
            {
                if (Physics.Raycast(new Ray(transform.position + up * 0.01f, -up), out RaycastHit rayHit, loopyGroundTestDistance, ~0, QueryTriggerInteraction.Ignore) && rayHit.distance <= loopyGroundTestDistance)
                {
                    output.isLoopy = true;
                    output.loopyNormal = rayHit.normal;
                }
            }
        }
    }
}
