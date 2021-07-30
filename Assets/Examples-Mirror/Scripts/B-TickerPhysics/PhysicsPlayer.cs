using Mirror;
using UnityEngine;

public class PhysicsPlayer : NetworkBehaviour, IPhysicsTick
{
    public struct Input : ITickerInput<Input>
    {
        public float horizontal;
        public float vertical;

        public Input GenerateLocal()
        {
            return new Input()
            {
                horizontal = UnityEngine.Input.GetAxisRaw("Horizontal"),
                vertical = UnityEngine.Input.GetAxisRaw("Vertical")
            };
        }

        public Input WithDeltas(Input previousInput) => previousInput;

        public Input WithoutDeltas() => this;
    }

    public float accelerationSpeed = 8f;

    public Input latestInput;

    private Rigidbody rb;

    public int updatesPerSecond = 30;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        FindObjectOfType<PhysicsTickable>().TrackPhysicsObject(netIdentity, rb);
    }
    public override void OnStartServer()
    {
        base.OnStartServer();
        FindObjectOfType<PhysicsTickable>().TrackPhysicsObject(netIdentity, rb);
    }

    private void Update()
    {
        if (hasAuthority && (int)(Time.time * updatesPerSecond) != (int)((Time.time - Time.deltaTime) * updatesPerSecond))
        {
            CmdInput(new Input[1] { new Input().GenerateLocal() }, new float[1] { Time.time });
        }
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdInput(PhysicsPlayer.Input[] inputs, float[] times)
    {
        // send the inputs to the target player
        latestInput = inputs[0];
    }

    public void PhysicsTick(float deltaTime, PhysicsPlayer.Input input, bool isRealtime)
    {
        input = latestInput; // hack...

        Vector3 moveDirection = Camera.main.transform.right * input.horizontal + Camera.main.transform.forward * input.vertical;
        moveDirection.y = 0f;

        // if we get reconciliation issues, we should consider that we possibly don't have a valid FixedDeltaTime setup and AddForce might not work correctly
        if (moveDirection.sqrMagnitude > 0f)
            rb.AddForce(moveDirection.normalized * (accelerationSpeed * deltaTime), ForceMode.Impulse);
    }

    public Rigidbody GetRigidbody() => rb;
}
