using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// Draws a timeline with "Ticks" that can be added to it
/// Drawing is fairly immediate. You can draw to it by calling ClearDraw() followed by all ticks you'd like to add to it.
/// </summary>
public class TimelineGraphic : MaskableGraphic
{
    private struct Tick
    {
        public double time;
        public float heightScale;
        public float offset;
        public float width;
        public Color32 color;
        public string label;
        public int labelLine;
    }

    public struct Hop
    {
        public double startTime;
        public double endTime;
        public float width;
        public float offset;
        public float height;
        public Color32 color;
    }

    private VertexHelper vh;

    [Header("Timeline")]
    public Color32 timelineColour = new Color32(255, 255, 255, 255);
    public float timelineThickness = 5;

    public double timeStart = 10;
    public double timeEnd = 20;

    [Header("Ticks")]
    public float tickHeight = 20;

    [Header("Labels")]
    public int labelSize = 14;

    private List<Tick> ticks = new List<Tick>(64);
    private List<Hop> hops = new List<Hop>(64);

    private GUIStyle labelStyleLeft;
    private GUIStyle labelStyleRight;
    private GUIStyle labelStyleCentre;

    public double timePerScreenX => (timeEnd - timeStart) / rectTransform.rect.width / rectTransform.lossyScale.x;

    public double TimeAtScreenX(float screenX)
    {
        Vector3 relativePosition = rectTransform.InverseTransformPoint(new Vector3(screenX, 0, 0)) - new Vector3(rectTransform.rect.min.x, 0, 0);
        return timeStart + relativePosition.x * (timeEnd - timeStart) / rectTransform.rect.width;
    }

    /// <summary>
    /// Prepares the timeline for drawing
    /// </summary>
    public void ClearDraw()
    {
        ticks.Clear();
        hops.Clear();
        SetVerticesDirty();
    }

    /// <summary>
    /// Inserts a tick into the timeline.
    /// * HeightScale multiplies the height compared to the standard (1f) tick
    /// * Offset adds a vertical offset compared to the standard tick
    /// * An optional label can appear on the tick
    /// * labelLine defines the "line number" that the label can appear at, allowing multiple labels to share a spot without overlapping
    /// </summary>
    public void DrawTick(double time, float heightScale, float offset, Color32 color, float width = 1f, string label = "", int labelLine = 0)
    {
        if (time < timeStart || time > timeEnd)
            return;

        ticks.Add(new Tick()
        {
            time = time,
            heightScale = heightScale,
            offset = offset,
            width = width,
            color = color,
            label = label,
            labelLine = labelLine
        });
    }

    /// <summary>
    /// Inserts a hop (curved arrow pointing from one time to another) into the timeline
    /// </summary>
    public void DrawHop(double startTime, double endTime, Color32 color, float verticalOffset, float height = 1f, float width = 1f)
    {
        if (System.Math.Max(startTime, endTime) < timeStart || System.Math.Min(startTime, endTime) > timeEnd)
            return;

        hops.Add(new Hop()
        {
            startTime = startTime,
            endTime = endTime,
            color = color,
            width = width,
            height = height,
            offset = verticalOffset
        });
    }

    private void DrawTickInternal(double time, float heightScale, float offset, Color32 color, float width)
    {
        Vector2 centre = new Vector2((float)(rectTransform.rect.xMin + (time - timeStart) / (timeEnd - timeStart) * rectTransform.rect.width), rectTransform.rect.center.y);

        centre.x = Mathf.Round(centre.x);
        centre.y = Mathf.Round(centre.y);

        DrawQuadInternal(
            centre + new Vector2(0f, tickHeight / 2 * (-heightScale - offset)),
            centre + new Vector2(width, tickHeight / 2 * (heightScale - offset)),
            color);
    }

    // Draws a quad into the current VertexHelper UI
    private void DrawQuadInternal(Vector2 min, Vector2 max, Color32 color)
    {
        UIVertex vert = new UIVertex();
        vert.color = color;
        int root = vh.currentVertCount;

        vert.position = new Vector3(min.x, max.y, 0f);
        vert.uv0 = new Vector2(0, 1);
        vh.AddVert(vert);
        vert.position = new Vector3(max.x, min.y, 0f);
        vert.uv0 = new Vector2(1, 0);
        vh.AddVert(vert);
        vert.position = new Vector3(min.x, min.y, 0f);
        vert.uv0 = new Vector2(0, 0);
        vh.AddVert(vert);
        vert.position = new Vector3(max.x, max.y, 0f);
        vert.uv0 = new Vector2(0, 0);
        vh.AddVert(vert);

        vh.AddTriangle(root + 0, root + 1, root + 2);
        vh.AddTriangle(root + 1, root + 3, root + 0);
    }

    private void DrawLineInternal(Vector2 start, Vector2 end, Color32 color, float width)
    {
        UIVertex vert = new UIVertex();
        vert.color = color;
        vert.uv0 = new Vector2(0f, 1f); // note: don't bother supporting UVs for this one it's 10pm and we don't even expect to texture these

        width *= 0.5f; // because we both add and subtract

        Vector2 forward = (start - end).normalized;
        Vector3 right = new Vector3(forward.y, -forward.x, 0f) * width;
        int root = vh.currentVertCount;

        vert.position = new Vector3(start.x, start.y, 0f) + right; vh.AddVert(vert);
        vert.position = new Vector3(start.x, start.y, 0f) - right; vh.AddVert(vert);
        vert.position = new Vector3(end.x, end.y, 0f) - right; vh.AddVert(vert);
        vert.position = new Vector3(end.x, end.y, 0f) + right; vh.AddVert(vert);

        vh.AddTriangle(root + 0, root + 1, root + 2);
        vh.AddTriangle(root + 0, root + 2, root + 3);
    }

    private void DrawHopInternal(Hop hop)
    {
        Vector2 start = new Vector2((float)(rectTransform.rect.xMin + (hop.startTime - timeStart) / (timeEnd - timeStart) * rectTransform.rect.width), rectTransform.rect.center.y + hop.offset * tickHeight);
        Vector2 end = new Vector2((float)(rectTransform.rect.xMin + (hop.endTime - timeStart) / (timeEnd - timeStart) * rectTransform.rect.width), rectTransform.rect.center.y + hop.offset * tickHeight);
        Vector2 up = Vector2.up * (tickHeight * hop.height);
        Vector2 horizontalWidth = new Vector2(0, hop.width / 2);
        Vector2 verticalWidth = new Vector2(hop.width / 2, 0);

        // draw short bits going up to long bit
        DrawQuadInternal(start + verticalWidth, start + up - verticalWidth, hop.color);
        DrawQuadInternal(end - verticalWidth, end + up + verticalWidth, hop.color);

        // draw long bit
        DrawQuadInternal(start + up - horizontalWidth, end + up + horizontalWidth, hop.color);

        // and the pointy bits
        DrawLineInternal(end, new Vector2(end.x - up.y / 4f, end.y + up.y / 4f), hop.color, hop.width);
        DrawLineInternal(end, new Vector2(end.x + up.y / 4f, end.y + up.y / 4f), hop.color, hop.width);
    }

    // Handles main UI
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);

        this.vh = vh;
        vh.Clear();
        UIVertex vert = new UIVertex();
        vert.color = new Color32(255, 0, 0, 255);

        float minX = rectTransform.rect.xMin, minY = rectTransform.rect.yMin;
        float maxX = rectTransform.rect.xMax, maxY = rectTransform.rect.yMax;
        float centreY = rectTransform.rect.center.y;

        // draw main timeline
        DrawQuadInternal(new Vector2(minX, centreY - timelineThickness / 2), new Vector2(maxX, centreY + timelineThickness / 2), timelineColour);

        // draw ticks
        foreach (Tick tick in ticks)
            DrawTickInternal(tick.time, tick.heightScale, tick.offset, tick.color, tick.width);

        // draw hops
        foreach (Hop hop in hops)
            DrawHopInternal(hop);
    }

    // Handles labels
    private void OnGUI()
    {
        //if (labelStyleCentre == null)
        {
            InitStyles();
        }

        // get screen coords
        Vector3 topLeft = rectTransform.TransformPoint(rectTransform.rect.xMin, rectTransform.rect.yMin, 0f);
        Vector3 bottomRight = rectTransform.TransformPoint(rectTransform.rect.xMax, rectTransform.rect.yMax, 0f);

        topLeft.y = Screen.height - topLeft.y;
        bottomRight.y = Screen.height - bottomRight.y;

        Rect pixelRect = new Rect(topLeft, bottomRight - topLeft);

        // draw beginning/end time
        GUI.contentColor = timelineColour;
        GUI.Label(new Rect(topLeft.x, pixelRect.center.y, 0, 0), timeStart.ToString("F2"), labelStyleRight);
        GUI.Label(new Rect(bottomRight.x, pixelRect.center.y, 0, 0), timeEnd.ToString("F2"), labelStyleLeft);

        // draw tick labels
        foreach (Tick tick in ticks)
        {
            if (!string.IsNullOrEmpty(tick.label))
            {
                GUI.contentColor = tick.color;
                GUI.Label(new Rect((float)(pixelRect.xMin + pixelRect.width * (tick.time - timeStart) / (timeEnd - timeStart)), pixelRect.center.y + tickHeight / 2f * tick.heightScale + labelSize * 3 / 4 + tick.labelLine * (labelSize + 2), 0, 0), tick.label, labelStyleCentre);
            }
        }
    }

    // Initialises GUI styles
    private void InitStyles()
    {
        labelStyleLeft = new GUIStyle();
        labelStyleLeft.normal.textColor = Color.white;
        labelStyleLeft.alignment = TextAnchor.MiddleLeft;
        labelStyleLeft.fontSize = labelSize;

        labelStyleCentre = new GUIStyle();
        labelStyleCentre.normal.textColor = Color.white;
        labelStyleCentre.alignment = TextAnchor.MiddleCenter;
        labelStyleCentre.fontSize = labelSize;

        labelStyleRight = new GUIStyle();
        labelStyleRight.normal.textColor = Color.white;
        labelStyleRight.alignment = TextAnchor.MiddleRight;
        labelStyleRight.fontSize = labelSize;
    }
}
