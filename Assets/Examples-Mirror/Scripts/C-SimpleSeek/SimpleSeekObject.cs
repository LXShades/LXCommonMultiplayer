using UnityEngine;


namespace MultiplayerToolset.Examples.Mirror
{
    /// <summary>
    /// Simple tickable object with a pre-determined set of inputs to cycle through
    /// </summary>
    public class SimpleSeekObject : MonoBehaviour, ITickable<SimpleSeekObject.Input, SimpleSeekObject.State>
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

        public TimelineList<Input> inputs;

        public float movementSpeed;

        private Ticker<Input, State> ticker;

        private void Awake()
        {
            ticker = new Ticker<Input, State>(this);

            TickerSettings settings = TickerSettings.Default;
            settings.historyLength = 10f;
            ticker.settings = settings;
        }

        private void Start()
        {
            inputs.Validate();
            for (int i = 0; i < inputs.Count; i++)
                ticker.InsertInput(inputs[i], inputs.TimeAt(i));
        }

        private void Update()
        {
            ticker.Seek(Time.time % inputs.LatestTime, Time.time % inputs.LatestTime, TickerSeekFlags.None);
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

        public ITickerBase GetTicker() => ticker;
    }
}