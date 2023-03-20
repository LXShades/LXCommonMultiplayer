using System;
using UnityEngine;

/// <summary>
/// Displays a debug timeline for the targetTicker
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TimelineDebugUI : MonoBehaviour
{
    public Timeline target { get; set; }

    [Header("Display")]
    public float timelineDisplayPeriod = 5f;

    public bool showServerTime = false;
    public Color32 playbackTimeColor = Color.green;
    public Color32 confirmedTimeColor = Color.blue;
    public Color32 stateColor = Color.cyan;
    public Color32 inputColor = Color.yellow;

    [Header("Hiearchy")]
    public UnityEngine.UI.Text targetNameText = null;

    public TimelineGraphic graphic { get; private set; }

    private void Start()
    {
        graphic = GetComponent<TimelineGraphic>();
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (target != null)
        {
            double latestConfirmedStateTime = target.GetTimeOfLatestConfirmedState();
            graphic.timeStart = (int)(Math.Max(target.playbackTime, latestConfirmedStateTime) / timelineDisplayPeriod) * timelineDisplayPeriod;
            graphic.timeEnd = graphic.timeStart + timelineDisplayPeriod;

            graphic.ClearDraw();

            graphic.DrawTick(target.playbackTime, 1.5f, 0.5f, playbackTimeColor, "PT", 0);
            graphic.DrawTick(latestConfirmedStateTime, 1.5f, 0.5f, confirmedTimeColor, "CT", 1);

            foreach (Timeline.EntityBase entity in target.entities)
            {
                TimelineTrackBase inputTrack = entity.inputTrackBase;
                TimelineTrackBase stateTrack = entity.stateTrackBase;

                for (int i = 0, e = inputTrack.Count; i < e; i++)
                    graphic.DrawTick(inputTrack.TimeAt(i), 1f, -1f, inputColor);
                for (int i = 0, e = stateTrack.Count; i < e; i++)
                    graphic.DrawTick(stateTrack.TimeAt(i), 0.5f, 0f, stateColor);
            }

            if (targetNameText)
                targetNameText.text = target.name;
        }
        else
        {
            graphic.ClearDraw();
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
