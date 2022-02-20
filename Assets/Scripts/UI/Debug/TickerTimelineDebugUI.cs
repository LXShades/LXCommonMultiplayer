using System;
using UnityEngine;

/// <summary>
/// Displays a debug timeline for the targetTicker
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TickerTimelineDebugUI : MonoBehaviour
{
    public TickerBase targetTicker { get; set; }

    public float timelineDisplayPeriod = 5f;

    public bool targetLocalPlayer = true;
    public bool showServerTime = false;
    public UnityEngine.UI.Text playerNameText = null;
    public Color32 playbackTimeColor = Color.green;
    public Color32 confirmedTimeColor = Color.blue;
    public Color32 stateColor = Color.cyan;
    public Color32 inputColor = Color.yellow;

    public TimelineGraphic timeline { get; private set; }

    private void Start()
    {
        timeline = GetComponent<TimelineGraphic>();
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (targetTicker != null)
        {
            timeline.timeStart = (int)(Math.Max(targetTicker.playbackTime, targetTicker.latestConfirmedStateTime) / timelineDisplayPeriod) * timelineDisplayPeriod;
            timeline.timeEnd = timeline.timeStart + timelineDisplayPeriod;

            timeline.ClearDraw();

            timeline.DrawTick(targetTicker.playbackTime, 1.5f, 0.5f, playbackTimeColor, "PT", 0);
            timeline.DrawTick(targetTicker.latestConfirmedStateTime, 1.5f, 0.5f, confirmedTimeColor, "CT", 1);

            for (int i = 0; i < targetTicker.inputTimelineBase.Count; i++)
                timeline.DrawTick(targetTicker.inputTimelineBase.TimeAt(i), 1f, -1f, inputColor);
            for (int i = 0; i < targetTicker.stateTimelineBase.Count; i++)
                timeline.DrawTick(targetTicker.stateTimelineBase.TimeAt(i), 0.5f, 0f, stateColor);

            if (playerNameText)
                playerNameText.text = targetTicker.targetName;
        }
        else
        {
            timeline.ClearDraw();
        }
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
        if (timeline.rectTransform.rect.Contains(timeline.rectTransform.InverseTransformPoint(Input.mousePosition)) && targetTicker != null)
        {
            double time = timeline.TimeAtScreenX(Input.mousePosition.x);
            string lastInputInfo = targetTicker.GetInputInfoAtTime(time);
            string lastStateInfo = targetTicker.GetStateInfoAtTime(time);

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
