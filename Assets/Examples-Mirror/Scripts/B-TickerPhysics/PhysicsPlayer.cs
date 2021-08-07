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
    }

    public float accelerationSpeed = 8f;

    private Rigidbody rb;

    private PhysicsTickable physicsTickable;

    private readonly TimelineList<Input> myInputs = new TimelineList<Input>();

    public int updatesPerSecond = 30;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        physicsTickable = FindObjectOfType<PhysicsTickable>();
        physicsTickable.TrackPhysicsObject(netIdentity, rb);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        physicsTickable = FindObjectOfType<PhysicsTickable>();
        physicsTickable.TrackPhysicsObject(netIdentity, rb);
    }

    private void Update()
    {
        if (TimeTool.IsTick(Time.unscaledTime, Time.unscaledDeltaTime, updatesPerSecond))
        {
            // add inputs to our input history, then send recent inputs to server
            if (hasAuthority)
            {
                myInputs.Set(physicsTickable.GetTicker().playbackTime, new Input().GenerateLocal());

                int numInputsToSend = Mathf.Min(5, myInputs.Count);
                Input[] inputs = new Input[numInputsToSend];
                float[] times = new float[numInputsToSend];
                for (int i = 0; i < numInputsToSend; i++)
                {
                    inputs[i] = myInputs[i];
                    times[i] = myInputs.TimeAt(i);
                }

                CmdInput(inputs, times);
            }

            // for other players to predict other players, they need to know the latest input
            if (NetworkServer.active)
            {
                RpcLatestInput(myInputs.Latest, myInputs.LatestTime);
            }
        }
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdInput(Input[] inputs, float[] times)
    {
        if (inputs != null && times != null && inputs.Length == times.Length)
        {
            for (int i = 0; i < inputs.Length; i++)
                myInputs.Set(times[i], inputs[i]);
        }

        // avoid overloading inputs
        TrimInputs();
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcLatestInput(Input input, float time)
    {
        myInputs.Set(time, input);

        // avoid overloading inputs
        TrimInputs();
    }

    private void TrimInputs()
    {
        float time = physicsTickable.GetTicker().playbackTime;

        myInputs.Trim(time - 3f, time + 3f);
    }

    public void PhysicsTick(float deltaTime, Input input, bool isRealtime)
    {
        Vector3 moveDirection = Camera.main.transform.right * input.horizontal + Camera.main.transform.forward * input.vertical;
        moveDirection.y = 0f;

        // if we get reconciliation issues, we should consider that we possibly don't have a valid FixedDeltaTime setup and AddForce might not work correctly
        if (moveDirection.sqrMagnitude > 0f)
        {
            rb.AddForce(moveDirection.normalized * (accelerationSpeed * deltaTime), ForceMode.Impulse);
            //rb.velocity += moveDirection.normalized * (accelerationSpeed * deltaTime);
        }
    }

    public Input GetInputAtTime(float time)
    {
        int index = myInputs.ClosestIndexBeforeOrEarliest(TimeTool.Quantize(time, physicsTickable.inputsPerSecond));

        if (index != -1)
            return myInputs[index];
        else
            return default;
    }

    public Rigidbody GetRigidbody() => rb;
}
