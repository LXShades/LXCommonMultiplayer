using System;
using UnityEngine;

/// <summary>
/// Displays a debug timeline for the targetTicker
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TimelineDebugViewerUI : MonoBehaviour
{
    public Timeline target { get; set; }

    [Header("Display")]
    public float displayPeriod = 5f;
    public float displayPeriodSmoothTime = 0.25f;

    private float targetDisplayPeriod;
    private float displayPeriodLerpProgress = 0f;

    public bool showAuthTime = false;
    public Color32 playbackTimeColor = Color.green;
    public float playbackTimeThickness = 3f;

    public float inputHeight = 1f;
    public float inputOffset = -1f;
    public float stateHeight = 1f;
    public float stateOffset = 0f;

    public Color32 stateColor = Color.cyan;
    public Color32 inputColor = Color.yellow;

    public Color32 initialHopColor = Color.red;
    public Color32 tickHopColor = Color.magenta;

    [Header("Hiearchy")]
    public UnityEngine.UI.Text targetNameText = null;

    public TimelineGraphic graphic { get; private set; }

    private void Start()
    {
        graphic = GetComponent<TimelineGraphic>();
        targetDisplayPeriod = displayPeriod;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (target != null)
        {
            displayPeriodLerpProgress = Mathf.Min(displayPeriodLerpProgress + Time.deltaTime / displayPeriodSmoothTime, 1f);

            double latestConfirmedStateTime = target.GetTimeOfLatestConfirmedState();
            graphic.timeStart = Mathf.Lerp(
                (int)(Math.Max(target.playbackTime, latestConfirmedStateTime) / displayPeriod) * displayPeriod,
                (int)(Math.Max(target.playbackTime, latestConfirmedStateTime) / targetDisplayPeriod) * targetDisplayPeriod,
                displayPeriodLerpProgress * 2);
            graphic.timeEnd = graphic.timeStart + Mathf.Lerp(displayPeriod, targetDisplayPeriod, displayPeriodLerpProgress * 2 - 1);

            graphic.ClearDraw();

            // Draw ticks
            graphic.DrawTick(target.playbackTime, 1.5f, 0f, playbackTimeColor, playbackTimeThickness, "Plybck", 0);
            graphic.DrawTick(latestConfirmedStateTime, 1.5f, 0f, stateColor, 1f, "State", 1);

            foreach (Timeline.EntityBase entity in target.entities)
            {
                TimelineTrackBase inputTrack = entity.inputTrackBase;
                TimelineTrackBase stateTrack = entity.stateTrackBase;

                for (int i = 0, e = inputTrack.Count; i < e; i++)
                    graphic.DrawTick(inputTrack.TimeAt(i), inputHeight, inputOffset, inputColor);
                for (int i = 0, e = stateTrack.Count; i < e; i++)
                    graphic.DrawTick(stateTrack.TimeAt(i), stateHeight, stateOffset, stateColor);
            }

            // Draw sequence events
            foreach (var seekOp in target.lastSeekDebugSequence)
            {
                if (seekOp.type == SeekOp.Type.DetermineStartState)
                    graphic.DrawHop(seekOp.sourceTime, seekOp.targetTime, initialHopColor, 1f, 2f);
                else if (seekOp.type == SeekOp.Type.Tick)
                    graphic.DrawHop(seekOp.sourceTime, seekOp.targetTime, tickHopColor, 1f, 0.5f);
            }

            if (targetNameText)
                targetNameText.text = target.name;
        }
        else
        {
            graphic.ClearDraw();
        }
    }

    public void SetDisplayPeriod(float targetDisplayPeriod)
    {
        displayPeriod = Mathf.Lerp(displayPeriod, targetDisplayPeriod, displayPeriodLerpProgress / displayPeriodSmoothTime);
        this.targetDisplayPeriod = targetDisplayPeriod;
        displayPeriodLerpProgress = 0f;
    }

    private GUIStyle hoverBoxStyle
    {
        get
        {
            if (_hoverBoxStyle == null)
            {
                _hoverBoxStyle = new GUIStyle(GUI.skin.box);
                _hoverBoxStyle.alignment = TextAnchor.UpperLeft;
                _hoverBoxStyle.wordWrap = true;
            }
            return _hoverBoxStyle;
        }
    }
    private GUIStyle _hoverBoxStyle;

    private void OnGUI()
    {
        if (graphic.rectTransform.rect.Contains(graphic.rectTransform.InverseTransformPoint(Input.mousePosition)) && target != null)
        {
            double time = graphic.TimeAtScreenX(Input.mousePosition.x);
            string lastInputInfo = "TODO";// target.GetInputInfoAtTime(time);
            string lastStateInfo = "TODO";// target.GetStateInfoAtTime(time);

            Rect infoBoxRect = new Rect(new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y), new Vector2(400f, 300f));
            string textToDisplay = $"Time: {time.ToString("F2")}s\nInput:\n{lastInputInfo}\nState:\n{lastStateInfo}";
            Vector2 size = hoverBoxStyle.CalcSize(new GUIContent(textToDisplay));

            infoBoxRect.size = size;

            if (infoBoxRect.xMax > Screen.width)
                infoBoxRect.x = Screen.width - infoBoxRect.width;
            if (infoBoxRect.yMax > Screen.height)
                infoBoxRect.yMax = Screen.height - infoBoxRect.height;

            GUI.Box(infoBoxRect, textToDisplay, hoverBoxStyle);
        }
    }
}
