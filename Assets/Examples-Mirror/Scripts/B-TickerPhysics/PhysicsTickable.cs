using Mirror;
using System.Collections.Generic;
using UnityEngine;

public class PhysicsTickable : NetworkBehaviour, ITickable<PhysicsTickable.Input, PhysicsTickable.State>
{
    public struct Input : ITickerInput<Input>
    {
        public Input GenerateLocal() => new Input();

        public Input WithDeltas(Input previousInput) => previousInput;

        public Input WithoutDeltas() => this;
    }

    public struct State : ITickerState<State>
    {
        // 52 bytes per object, pain
        public struct PhysState
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        public PhysState[] states;

        public void DebugDraw(Color colour) { }

        public bool Equals(State other) => false; // todo - but allow it to reconcile
    }

    Ticker<Input, State> ticker = null;

    public GameObject physicsObjectPrefab;
    public int numPhysicsObjectsToGenerate = 10;

    private List<Rigidbody> spawnedPhysObjects = new List<Rigidbody>();

    private float timeOnServer;
    private float timeOfLastServerUpdate;

    private int updatesPerSecond = 30;
    private int tickerPerSecond = 30;

    void Awake()
    {
        ticker = new Ticker<Input, State>(this);

        // disable auto physics simulation, we'll do that in Tick
        Physics.autoSimulation = false;

        // generate physics objects to simulate and track
        // we don't currently have specific object mapping when we save and load states, so we assume all of them to be identical types and refer to them by index
        for (int i = 0; i < numPhysicsObjectsToGenerate; i++)
        {
            GameObject physObj = Instantiate(physicsObjectPrefab);

            spawnedPhysObjects.Add(physObj.GetComponent<Rigidbody>());
        }
    }

    void Update()
    {
        if ((int)(Time.time * tickerPerSecond) != (int)((Time.time - Time.deltaTime) * updatesPerSecond))
            ticker.PushInput(new Input(), Time.time);

        if (NetworkServer.active)
            ticker.Seek(Time.time, Time.time);
        else
            ticker.Seek(timeOnServer + Time.time - timeOfLastServerUpdate, timeOnServer, TickerSeekFlags.IgnoreDeltas);

        if (NetworkServer.active && (int)(Time.time * updatesPerSecond) != (int)((Time.time - Time.deltaTime) * updatesPerSecond))
            RpcState(ticker.lastConfirmedState, ticker.confirmedStateTime, Time.time - ticker.confirmedStateTime);
    }

    public void Tick(float deltaTime, Input input, bool isRealtime)
    {
        Physics.Simulate(deltaTime);
    }
    
    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcState(State state, float time, float serverExtrapolation)
    {
        if (!NetworkServer.active)
        {
            ticker.Reconcile(state, time, TickerSeekFlags.DontConfirm);
            timeOnServer = time + serverExtrapolation;
            timeOfLastServerUpdate = Time.time;
        }
    }

    public State MakeState()
    {
        State state;

        state.states = new State.PhysState[spawnedPhysObjects.Count];

        for (int i = 0; i < spawnedPhysObjects.Count; i++)
        {
            state.states[i].position = spawnedPhysObjects[i].position;
            state.states[i].rotation = spawnedPhysObjects[i].rotation;
            state.states[i].velocity = spawnedPhysObjects[i].velocity;
            state.states[i].angularVelocity = spawnedPhysObjects[i].angularVelocity;
        }

        return state;
    }

    public void ApplyState(State state)
    {
        if (state.states.Length == spawnedPhysObjects.Count)
        {
            for (int i = 0; i < spawnedPhysObjects.Count; i++)
            {
                spawnedPhysObjects[i].position = state.states[i].position;
                spawnedPhysObjects[i].rotation = state.states[i].rotation;
                spawnedPhysObjects[i].velocity = state.states[i].velocity;
                spawnedPhysObjects[i].angularVelocity = state.states[i].angularVelocity;
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
