using UnityEngine;

/// <summary>
/// Provides core character movement functionality
/// </summary>
public class CharacterMovement : MonoBehaviour
{
    protected Movement movement;

    public Vector2 input = new Vector3(0f, 1f);
    public bool inputJump = false;

    protected Vector3 up;
    protected Vector3 forward;
    protected Vector3 right => Vector3.Cross(up, forward).normalized;

    [Header("Velocities")]
    public float acceleration = 10f;
    public float friction = 10f;
    public float gravity = 10f;

    [Header("Grounding")]
    public float groundSphereTestRadius = 0.25f;
    public float groundTestDistanceThreshold = 0.05f;
    public float groundEscapeThreshold = 3f;
    public float slipRadius = 0.25f;
    public float slipVelocity = 5f;

    [Header("Loopy")]
    public float loopyGroundTestDistance = 0.5f;
    public float loopyPushdownMinRadius = 1f;
    public float loopyPushdownDegreesRequired = 5f;
    public float loopyPushdownNeutralLimit = 0.2f;

    [Header("Enable")]
    public bool enableSlip = true;
    public bool enableFriction = true;
    public bool enableCollisionsAffectVelocity = true;
    public bool enableLoopy = true;
    public bool enableLoopyPushdown = true;

    protected virtual void Awake()
    {
        movement = GetComponent<Movement>();
    }

    public void Tick(float deltaTime)
    {
        Physics.SyncTransforms();

        // Do floor test
        bool hasGroundCastHit = Physics.SphereCast(new Ray(transform.position + up * groundSphereTestRadius, -up), groundSphereTestRadius, out RaycastHit groundHit, Mathf.Max(groundSphereTestRadius, loopyGroundTestDistance), ~0, QueryTriggerInteraction.Ignore);
        bool isOnGround = false;
        bool isSlipping = false;
        bool isLoopy = false;
        Vector3 groundNormal = Vector3.up;
        Vector3 loopyNormal = Vector3.up;
        Vector3 slipVector = Vector3.zero;

        if (hasGroundCastHit)
        {
            // anywhere on the circle could be hit, we'll just test if the contact point is "close enough" to the vertical distance test threshold
            float primaryDistance = -Vector3.Dot(groundHit.point - transform.position, up);
            float secondaryDistance = (transform.position - groundHit.point).AlongPlane(up).magnitude;

            Debug.DrawLine(transform.position, groundHit.point, Color.red);
            if (primaryDistance < groundTestDistanceThreshold)
            {
                isOnGround = true;
                groundNormal = groundHit.normal;
            }
            else if (secondaryDistance > slipRadius)
            {
                isSlipping = true;
                slipVector = (transform.position - groundHit.point).AlongPlane(up).normalized;
            }

            if (enableLoopy && primaryDistance <= loopyGroundTestDistance)
            {
                if (Physics.Raycast(new Ray(transform.position + up * 0.01f, -up), out RaycastHit rayHit, loopyGroundTestDistance, ~0, QueryTriggerInteraction.Ignore) && rayHit.distance <= loopyGroundTestDistance)
                {
                    isLoopy = true;
                    loopyNormal = rayHit.normal;

                    Debug.DrawLine(transform.position + new Vector3(0, 2f, 0f), transform.position + new Vector3(0, 2f, 0) + loopyNormal, Color.red);
                }
            }
        }

        Debug.DrawLine(transform.position + up * 2f, transform.position - up * 2f, isOnGround ? Color.green : Color.blue);
        DrawCircle(transform.position, groundSphereTestRadius, Color.white);

        // Accelerate
        Vector3 inputDir = Vector3.ClampMagnitude(forward.AlongPlane(groundNormal).normalized * input.y + right.AlongPlane(groundNormal).normalized * input.x, 1f);
        movement.velocity += inputDir * ((acceleration + friction) * deltaTime);

        // Friction
        Vector3 groundVelocity = movement.velocity.AlongPlane(groundNormal);
        float groundVelocityMagnitude = groundVelocity.magnitude;

        if (groundVelocityMagnitude > 0f && enableFriction)
            movement.velocity -= groundVelocity * Mathf.Min(friction * deltaTime / groundVelocityMagnitude, 1f);

        // Jump
        if (isOnGround && inputJump)
            movement.velocity.SetAlongAxis(up, 9.8f);

        // Grounding and gravity
        if (isOnGround && Vector3.Dot(groundHit.normal, movement.velocity) < groundEscapeThreshold)
            movement.velocity = movement.velocity.AlongPlane(groundNormal); // along gravity vector, may be different for wall running
        else
        {
            if (isSlipping && enableSlip)
            {
                // Apply slip vector if there is one
                movement.velocity.SetAlongAxis(slipVector, slipVelocity);
            }

            // Gravity - only apply when not on ground, otherwise slipping occurs
            movement.velocity += new Vector3(0f, -gravity * deltaTime, 0f);
        }

        // Do final movement
        Vector3 preMovePosition = transform.position;
        if (movement.Move(movement.velocity * deltaTime, out Movement.Hit hitOut))
        {
            if (enableCollisionsAffectVelocity && deltaTime > 0f)
            {
                //movement.velocity = (transform.position - positionBeforeMovement) / deltaTime;
                movement.velocity.SetAlongAxis(hitOut.normal, 0f);
            }
        }

        if (isLoopy)
        {
            up = loopyNormal;

            if (enableLoopyPushdown)
            {
                // Apply downward force if we're able
                // we test for 1. ground far below us and 2. a satisfactory degree change
                // raycastLength is just long enough for us to pull an almost-90 degree turn, I could not find a reliable and useful way to calculate this so we just test as far as we can
                // and then check the degree difference after the raycast hits
                float raycastLength = Vector3.Distance(preMovePosition, transform.position);
                float raycastPullback = 0.01f;

                Debug.DrawLine(preMovePosition, transform.position, Color.white);
                Debug.DrawLine(transform.position + transform.forward * 0.02f, transform.position + transform.forward * 0.02f - up * raycastLength, Color.white);
                Debug.DrawLine(transform.position + transform.forward * 0.02f - up * raycastLength, preMovePosition, Color.white);

                if (Physics.Raycast(new Ray(transform.position + up * raycastPullback, -up), out RaycastHit rayHit, raycastLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    // we might not want to push down if...
                    //  a) normals are the same but platform distances are different
                    //  b) normals rotate in the opposite direction we'd expect (eg we're moving forward, but rotation is going backward -- we shouldn't be pushing _down_ in that scenario)
                    //  c) we're trying to move upwards, beyond the ground escape velocity

                    bool situationA = Vector3.Dot(rayHit.normal, loopyNormal) >= 0.99f && rayHit.distance - raycastPullback > loopyPushdownNeutralLimit;
                    bool situationB = Vector3.Dot(rayHit.normal - loopyNormal, movement.velocity) < 0f;
                    bool situationC = Vector3.Dot(up, movement.velocity) >= groundEscapeThreshold;

                    if (!situationA && !situationB)
                    {
                        // to get as close to the ground as possible in prep for the next frame, we need to move with our rotation simulating our final up vector
                        // todo we could do a collidercast
                        transform.rotation = Quaternion.LookRotation(forward.AlongPlane(rayHit.normal), rayHit.normal);
                        movement.Move(-up * (rayHit.distance + raycastPullback), out _, true, Movement.MoveFlags.NoSlide);

                        movement.velocity = movement.velocity.AlongPlane(rayHit.normal);
                    }
                }
            }
        }
        else
            up = Vector3.up;

        transform.rotation = Quaternion.LookRotation(forward.AlongPlane(up), up);
    }

    private void DrawCircle(Vector3 position, float radius, Color color)
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12;
            float nextAngle = (i + 1) * Mathf.PI * 2f / 12;
            Debug.DrawLine(position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius, position + new Vector3(Mathf.Sin(nextAngle), 0f, Mathf.Cos(nextAngle)) * radius, color);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
    }
}
