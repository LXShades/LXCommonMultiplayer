using UnityEngine;


namespace UnityMultiplayerEssentials.Examples.Mirror
{
    /// <summary>
    /// Simple tickable object with a pre-determined set of inputs to cycle through
    /// </summary>
    public class SimpleSeekObject : MonoBehaviour, ITickable<SimpleSeekObject.State, SimpleSeekObject.Input>
    {
        [System.Serializable]
        public struct Input : ITickerInput<Input>
        {
            public Vector2 movementDirection;

            public Input GenerateLocal() => new Input(); // we don't read real inputs for this example

            public Input WithDeltas(Input previousInput) => this; // no applicable deltas

            public Input WithoutDeltas() => this;
        }

        public struct State : ITickerState<State>
        {
            public Vector3 position;

            public void DebugDraw(Color colour) { }

            public bool Equals(State other) => false;
        }

        public TimelineTrack<Input> inputs;

        public float movementSpeed;

        private Timeline timeline;
        private Timeline.Entity<State, Input> timelineEntity;

        private void Awake()
        {
            timeline = Timeline.CreateSingle(name, this, out timelineEntity);

            TimelineSettings settings = TimelineSettings.Default;
            settings.historyLength = 10f;
            timeline.settings = settings;
        }

        private void Start()
        {
            inputs.Validate();
            for (int i = 0; i < inputs.Count; i++)
                timelineEntity.InsertInput(inputs[i], inputs.TimeAt(i));
        }

        private void Update()
        {
            timeline.Seek(Time.time % inputs.LatestTime, TimelineSeekFlags.None);
        }

        public void Tick(float deltaTime, Input input, TickInfo tickInfo)
        {
            transform.position += new Vector3(input.movementDirection.x, 0f, input.movementDirection.y) * (movementSpeed * deltaTime);
        }

        public State MakeState()
        {
            return new State()
            {
                position = transform.position
            };
        }

        public void ApplyState(State state)
        {
            transform.position = state.position;
        }
    }
}