using Mirror;
using System.Collections.Generic;
using UnityEngine;

public interface IPhysicsTick
{
    Rigidbody GetRigidbody();

    void PhysicsTick(float deltaTime, PhysicsPlayer.Input input, bool isRealtime);
}

public class PhysicsTickable : NetworkBehaviour, ITickable<PhysicsPlayer.Input, PhysicsTickable.State>
{
    public struct RbState : ITickerState<RbState>
    {
        public static RbState MakeFromRb(Rigidbody rbSource)
        {
            return new RbState()
            {
                position = rbSource.position,
                rotation = rbSource.rotation,
                velocity = rbSource.velocity,
                angularVelocity = rbSource.angularVelocity
            };
        }

        public void ApplyToRb(Rigidbody target)
        {
            target.position = position;
            target.rotation = rotation;
            target.velocity = velocity;
            target.angularVelocity = angularVelocity;
        }

        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;

        public void DebugDraw(Color colour) { }

        public bool Equals(RbState other) => false;
    }

    public struct State : ITickerState<State>
    {
        // 52 bytes per object, pain
        public RbState[] states;

        public void DebugDraw(Color colour) { }

        public bool Equals(State other) => false; // todo - but allow it to reconcile
    }

    public struct InputCollection : ITickerInput<InputCollection>
    {
        public PhysicsPlayer.Input[] input;
        public ushort[] targetObjects;

        public InputCollection GenerateLocal() => new InputCollection();
        public InputCollection WithDeltas(InputCollection previousInput) => this;
        public InputCollection WithoutDeltas() => this;
    }

    public TickerSettings tickerSettings = TickerSettings.Default;

    Ticker<PhysicsPlayer.Input, State> ticker = null;

    public GameObject physicsObjectPrefab;
    public int numPhysicsObjectsToGenerate = 10;

    private SortedList<uint, Rigidbody> physObjects = new SortedList<uint, Rigidbody>();

    public float clientExtrapolation = 0.5f;

    void Awake()
    {
        ticker = new Ticker<PhysicsPlayer.Input, State>(this);
        ticker.settings = tickerSettings;

        // disable auto physics simulation, we'll do that in Tick
        Physics.autoSimulation = false;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // generate physics objects to simulate and track
        for (int i = 0; i < numPhysicsObjectsToGenerate; i++)
        {
            GameObject physObj = Instantiate(physicsObjectPrefab);

            NetworkServer.Spawn(physObj);
        }

        TrackAllPhysicsObjects();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        TrackAllPhysicsObjects();
    }

    public void Tick(float deltaTime, PhysicsPlayer.Input input, bool isRealtime)
    {
        // run physics ticks
        for (int i = 0; i < physObjects.Count; i++)
        {
            Rigidbody physObj = physObjects.Values[i];

            if (physObj)
            {
                foreach (IPhysicsTick physTick in physObj.GetComponents<IPhysicsTick>())
                    physTick.PhysicsTick(deltaTime, input, isRealtime);
            }
            else if (hasAuthority) // server should cleanup invalid objects asap
            {
                physObjects.RemoveAt(i--);
            }
        }

        // simulate actual physics now
        Physics.Simulate(deltaTime);
    }

    public void TrackPhysicsObject(NetworkIdentity physicsObjectIdentity, Rigidbody physicsObjectRigidbody)
    {
        if (!physObjects.ContainsValue(physicsObjectRigidbody))
            physObjects.Add(physicsObjectIdentity.netId, physicsObjectRigidbody);
    }

    public void TrackAllPhysicsObjects()
    {
        foreach (KeyValuePair<uint, NetworkIdentity> identity in NetworkIdentity.spawned)
        {
            if (identity.Value.TryGetComponent(out Rigidbody identityRb))
                TrackPhysicsObject(identity.Value, identityRb);
        }
    }

    public State MakeState()
    {
        State state;

        state.states = new RbState[physObjects.Count];

        for (int i = 0; i < physObjects.Count; i++)
        {
            if (physObjects.Values[i] != null)
                state.states[i] = RbState.MakeFromRb(physObjects.Values[i]);
        }

        return state;
    }

    public void ApplyState(State state)
    {
        if (state.states.Length == physObjects.Count)
        {
            for (int i = 0; i < physObjects.Count; i++)
            {
                if (physObjects.Values[i])
                    state.states[i].ApplyToRb(physObjects.Values[i]);
            }

            // propagate changes to the physics system
            Physics.SyncTransforms();
        }
    }

    public ITickerBase GetTicker()
    {
        return ticker;
    }
}
