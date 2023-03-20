using Mirror;
using System.Collections.Generic;
using UnityEngine;


namespace UnityMultiplayerEssentials.Examples.Mirror
{
    public interface IPhysicsTick
    {
        Rigidbody GetRigidbody();

        void PhysicsTick(float deltaTime, PhysicsPlayer.Input input);

        PhysicsPlayer.Input GetInputAtTime(double time);
    }

    public class PhysicsTickable : NetworkBehaviour, ITickable<PhysicsTickable.State, PhysicsTickable.Input>
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

            const ulong kMask48 = ~(~0L << 48);
            const ulong kMask40 = ~(~0L << 40);
            const ulong kMask32 = ~0U; // << 32 produces no shift at all, C# quirk.
            const ulong kMask24 = ~(~0 << 24);
            const ulong kMask16 = ~(~0 << 16);

            public Vector3 position
            {
                get => Compressor.DecompressVectorVariable((compressedB >> 32 & kMask32) | ((ulong)compressedC << 32), 20f, 48);
                set
                {
                    ulong valCompressed = Compressor.CompressVectorVariable(value, 20f, 48);
                    compressedB = (compressedB & kMask32) | (valCompressed << 32);
                    compressedC = (ushort)(valCompressed >> 32);
                }
            }

            public Quaternion rotation
            {
                get => Compressor.DecompressQuaternion32(rotationCompressed);
                set => rotationCompressed = Compressor.CompressQuaternion32(value);
            }

            public Vector3 velocity
            {
                get => Compressor.DecompressVectorVariable((compressedA >> 48) | ((compressedB & kMask32) << 16), 20f, 48);
                set
                {
                    ulong valCompressed = Compressor.CompressVectorVariable(value, 20f, 48);
                    compressedA = (compressedA & kMask48) | (valCompressed << 48);              // valCompressed:0-16 to compressedA:48-64
                    compressedB = ((valCompressed >> 16) & kMask32) | (compressedB & ~kMask32); // valCompressed:16-48 to compressedB:0-32
                }
            }

            public Vector3 angularVelocity
            {
                get => Compressor.DecompressVectorVariable(compressedA & kMask48, 20f, 48);
                set
                {
                    compressedA = (Compressor.CompressVectorVariable(value, 20f, 48) & kMask48) | (compressedA & ~kMask48);
                }
            }

            // these below compressed variables are the ones read/written by Mirror (Mirror does not read the properties above, it works with actual data only)
            // we use unsigned ints where possible because we do a lot of shifting. right-shifts do funky arithmetics on signed ints that we don't want
            public uint rotationCompressed;
            public ulong compressedA;
            public ulong compressedB;
            public ushort compressedC;

            public void DebugDraw(Color colour) { }

            public bool Equals(RbState other) => false;
        }

        public struct State : ITickerState<State>
        {
            public RbState[] states;

            public void DebugDraw(Color colour) { }

            public bool Equals(State other) => false; // todo - but allow it to reconcile
        }

        public struct Input : ITickerInput<Input>
        {
            public Input GenerateLocal() => this;

            public Input WithDeltas(Input previousInput) => this;
        }

        public GameObject physicsObjectPrefab;
        public int numPhysicsObjectsToGenerate = 10;

        public int inputsPerSecond = 30;

        /// <summary>List of physics objects tracked by this tickable, ordered by netId. Only contains networked objects.</summary>
        private SortedList<uint, Rigidbody> physObjects = new SortedList<uint, Rigidbody>();

        public float clientExtrapolation = 0.5f;

        void Awake()
        {
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

        public void Tick(float deltaTime, Input input, TickInfo tickInfo)
        {
            // run physics ticks
            for (int i = 0; i < physObjects.Count; i++)
            {
                Rigidbody physObj = physObjects.Values[i];

                if (physObj)
                {
                    foreach (IPhysicsTick physTick in physObj.GetComponents<IPhysicsTick>())
                    {
                        physTick.PhysicsTick(deltaTime, physTick.GetInputAtTime(tickInfo.time));
                    }
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
    }
}