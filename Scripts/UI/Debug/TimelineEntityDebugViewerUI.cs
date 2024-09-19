using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.UI;

public class TimelineEntityDebugViewerUI : MonoBehaviour
{
    /// <summary>
    /// Timeline owning the entity
    /// </summary>
    public Timeline timeline;

    /// <summary>
    /// Entity that this viewer is debugging
    /// </summary>
    public Timeline.EntityBase entity;
    
    /// <summary>
    /// Displays the name of the entity
    /// </summary>
    public Text entityName;

    /// <summary>
    /// Holds the actual timeline where that entity's events are displayed
    /// </summary>
    public TimelineGraphic timelineUI;

    private static GUIStyle hoverBoxStyle
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
    private static GUIStyle _hoverBoxStyle;

    private void OnGUI()
    {
        if (timelineUI.rectTransform.rect.Contains(timelineUI.rectTransform.InverseTransformPoint(Input.mousePosition)) && entity != null)
        {
            double time = timelineUI.TimeAtScreenX(Input.mousePosition.x);
            string lastInputInfo = entity.GetInputInfoAtTime(time);
            string lastStateInfo = entity.GetStateInfoAtTime(time);

            string textToDisplay = $"Playback: {timeline.playbackTime.ToString("F2")}\nHovered: {time.ToString("F2")}s\nInput:\n{lastInputInfo}\nState:\n{lastStateInfo}";
            Vector2 size = new Vector2(400f, hoverBoxStyle.CalcHeight(new GUIContent(textToDisplay), 400f));
            Rect infoBoxRect = new Rect(new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y), size);

            if (infoBoxRect.xMax > Screen.width)
                infoBoxRect.x = Screen.width - infoBoxRect.width;
            if (infoBoxRect.yMax > Screen.height)
            {
                var ok = Screen.height - Input.mousePosition.y - infoBoxRect.height;
                infoBoxRect.y = ok;
            }
            if (infoBoxRect.y < 0)
                infoBoxRect.y = 0;
            if (infoBoxRect.x < 0)
                infoBoxRect.x = 0;

            GUI.Box(infoBoxRect, textToDisplay, hoverBoxStyle);
        }
    }
}
