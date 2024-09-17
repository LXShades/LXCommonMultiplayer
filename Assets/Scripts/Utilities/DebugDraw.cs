using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extended simple 3D debug draw functions supporting various shapes, without Gizmos or editor required
/// </summary>
public static class DebugDraw
{
    /// <summary>
    /// DrawStyles. You can use them conventionally or use a preset e.g. DrawStyle.Default and modify it e.g. DrawStyle.Default.Color(Color.red)
    /// 
    /// DrawStyles implicitly 
    /// </summary>
    public struct Style
    {
        /// <summary>
        /// Thickness of the line in approx pixels-ish. Only works if the ThickLineShader is available.
        /// </summary>
        public float thickness { get; private set; }
        /// <summary>
        /// Color of the line
        /// </summary>
        public Color color { get; private set; }
        /// <summary>
        /// If above 0, the line may persist for a while
        /// </summary>
        public float duration { get; private set; }

        public static Style DefaultWhite = new Style()
        {
            color = UnityEngine.Color.white,
            thickness = 3f,
            duration = 0,
        };

        public static Style Inherit = new Style()
        {
            color = new Color(0, 0, 0, 0),
            thickness = -1,
            duration = -1
        };

        public static Style Thick = new Style()
        {
            color = new Color(0, 0, 0, 0),
            thickness = 5f,
            duration = -1
        };

        public static Style Persistent = new Style()
        {
            color = new Color(0, 0, 0, 0),
            thickness = -1,
            duration = 5f
        };

        public static implicit operator Style(Color colorIn) => Style.Inherit.Color(colorIn);

        public Style Thickness(float thicknessIn) => new Style() { thickness = thicknessIn, color = color, duration = duration };
        public Style Color(Color colorIn) => new Style() { thickness = thickness, color = colorIn, duration = duration };
        public Style Duration(float durationIn) => new Style() { thickness = thickness, color = color, duration = durationIn };

        public Style Merge(Style next) => new Style()
        {
            duration = next.duration == -1 ? this.duration : next.duration,
            thickness = next.thickness == -1 ? this.thickness : next.thickness,
            color = next.color.a == 0 ? this.color : next.color
        };
    }

    private class DebugShape
    {
        public List<Vector3> points = new List<Vector3>();
        public Style style;
        public float creationTime;
    }

    private static Material lineMaterial
    {
        get
        {
            if (_lineMaterial == null)
            {
                Shader shaderToUse = Shader.Find("Unlit/UnityMultiplayerEssentials/ThickLineShader");

                if (shaderToUse == null)
                {
                    Debug.LogWarning("[UnityMultiplayerEssentials.DebugDraw] For better debug lines in builds, add Unlit/UnityMultiplayerEssentials/ThickLineShader to Always Included Shaders. Using fallback.");
                    shaderToUse = Shader.Find("Hidden/Internal-Colored");
                }
                _lineMaterial = new Material(shaderToUse);
                _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            return _lineMaterial;
        }
    }
    private static Material _lineMaterial;

    private static bool isDrawCallbackActive = false;

    const float kRadsInCircle = Mathf.PI * 2f;

    private static List<DebugShape> currentDebugShapes = new List<DebugShape>();
    private static List<DebugShape> previousDebugShapes = new List<DebugShape>();
    private static List<DebugShape> preserveDebugShapes = new List<DebugShape>();
    private static List<DebugShape> debugShapePool = new List<DebugShape>();

    private static List<Camera> ignoredCameras = new List<Camera>();

    private static bool hasBufferedPausedShapes = false;

    private static double currentBufferTime = -1; // hack to detect when _all_ cameras have finished rendering in multi-camera setup

    /// <summary>
    /// Default style. When merged with another style via a PushStyle or temprorily with a draw call, some properties can be overridden
    /// </summary>
    private static List<Style> styleStack = new List<Style>(8) { Style.DefaultWhite };

    [Obsolete("Prefer PushStyle/PopStyle")]
    public static float lineThickness
    {
        get => styleStack[styleStack.Count - 1].thickness;
        set => styleStack[styleStack.Count - 1] = styleStack[styleStack.Count - 1].Thickness(value);
    }

    /// <summary>
    /// Draws a line between start and end
    /// </summary>
    public static void DrawLine(Vector3 start, Vector3 end, Style style)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);

        output.points.Add(start);
        output.points.Add(end);
    }

    /// <summary>
    /// Draws a box between the given min and max
    /// </summary>
    public static void DrawBox(Vector3 min, Vector3 max, Style style)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);

        // top square
        output.points.Add(max);
        output.points.Add(new Vector3(max.x, max.y, min.z));
        output.points.Add(new Vector3(max.x, max.y, min.z));
        output.points.Add(new Vector3(min.x, max.y, min.z));
        output.points.Add(new Vector3(min.x, max.y, min.z));
        output.points.Add(new Vector3(min.x, max.y, max.z));
        output.points.Add(new Vector3(min.x, max.y, max.z));
        output.points.Add(max);

        // bottom square
        output.points.Add(new Vector3(max.x, min.y, max.z));
        output.points.Add(new Vector3(max.x, min.y, min.z));
        output.points.Add(new Vector3(max.x, min.y, min.z));
        output.points.Add(new Vector3(min.x, min.y, min.z));
        output.points.Add(min);
        output.points.Add(new Vector3(min.x, min.y, max.z));
        output.points.Add(new Vector3(min.x, min.y, max.z));
        output.points.Add(new Vector3(max.x, min.y, max.z));

        // pillars
        output.points.Add(new Vector3(max.x, max.y, max.z));
        output.points.Add(new Vector3(max.x, min.y, max.z));
        output.points.Add(new Vector3(max.x, max.y, min.z));
        output.points.Add(new Vector3(max.x, min.y, min.z));
        output.points.Add(new Vector3(min.x, max.y, min.z));
        output.points.Add(new Vector3(min.x, min.y, min.z));
        output.points.Add(new Vector3(min.x, max.y, max.z));
        output.points.Add(new Vector3(min.x, min.y, max.z));
    }

    /// <summary>
    /// Draws a bounds struct
    /// </summary>
    public static void DrawBounds(Bounds bounds, Style style) => DrawBox(bounds.min, bounds.max, style);

    /// <summary>
    /// Draws a horizontally flat grid between start and end
    /// </summary>
    public static void DrawHorizontalGrid(Vector3 start, Vector3 end, float y, Style style, int numDivisions)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);

        Vector3 startToEnd = end - start;
        for (int i = 0; i <= numDivisions; i++)
        {
            output.points.Add(new Vector3(start.x + startToEnd.x * i / numDivisions, y, start.z));
            output.points.Add(new Vector3(start.x + startToEnd.x * i / numDivisions, y, end.z));
            output.points.Add(new Vector3(start.x, y, start.z + startToEnd.z * i / numDivisions));
            output.points.Add(new Vector3(end.x, y, start.z + startToEnd.z * i / numDivisions));
        }
    }

    /// <summary>
    /// Draws an arrow between start and end
    /// </summary>
    public static void DrawArrow(Vector3 start, Vector3 end, Style style)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);

        float length = Vector3.Distance(start, end);
        float arrowHeadRatio = 0.2f;
        float arrowWidth = length * arrowHeadRatio;
        Vector3 direction = (end - start) / length;
        Vector3 localRight = direction.y > -0.99f && direction.y < 0.99f ? Vector3.Cross(direction, Vector3.up).normalized : Vector3.right;
        Vector3 localUp = Vector3.Cross(direction, localRight).normalized;
        Vector3 arrowSection = end - direction * (length * arrowHeadRatio);

        output.points.Add(start);
        output.points.Add(end);

        output.points.Add(arrowSection + localUp * arrowWidth);
        output.points.Add(end);
        output.points.Add(arrowSection - localUp * arrowWidth);
        output.points.Add(end);
        output.points.Add(arrowSection + localRight * arrowWidth);
        output.points.Add(end);
        output.points.Add(arrowSection - localRight * arrowWidth);
        output.points.Add(end);

    }

    /// <summary>
    /// Draws a cross at the specified position
    /// </summary>
    public static void DrawCross(Vector3 position, float crossSize, Style style)
    {
        RequestDrawThisFrame();

        float halfSize = crossSize * 0.5f;
        DebugShape output = GetNewShape(style);

        output.points.Add(position + new Vector3(halfSize, 0f, 0f));
        output.points.Add(position - new Vector3(halfSize, 0f, 0f));
        output.points.Add(position + new Vector3(0f, halfSize, 0f));
        output.points.Add(position - new Vector3(0f, halfSize, 0f));
        output.points.Add(position + new Vector3(0f, 0f, halfSize));
        output.points.Add(position - new Vector3(0f, 0f, halfSize));
    }

    /// <summary>
    /// Draws a sphere of the given world position and radius
    /// </summary>
    public static void DrawSphere(Vector3 position, float radius, Style style, int numLongitudeSegments = 4, int numCircleSegments = 16)
    {
        RequestDrawThisFrame();

        float radsPerLongitude = kRadsInCircle / numLongitudeSegments;
        float radsPerCircleSegment = kRadsInCircle / numCircleSegments;
        DebugShape output = GetNewShape(style);

        // longitude circles
        for (float longitude = 0f; longitude < kRadsInCircle; longitude += radsPerLongitude)
        {
            Vector3 lo = new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);
            Vector3 last = position + new Vector3(0f, radius, 0f);

            for (float latitude = radsPerCircleSegment; latitude < kRadsInCircle - 0.001f; latitude += radsPerCircleSegment)
            {
                Vector3 next = position + new Vector3(0f, Mathf.Cos(latitude) * radius, 0f) + lo * Mathf.Sin(latitude);

                output.points.Add(last);
                output.points.Add(next);

                last = next;
            }
        }

        // centre circle
        Vector3 last2 = position + new Vector3(0f, 0f, radius);
        for (float longitude = radsPerCircleSegment; longitude < kRadsInCircle - 0.001f; longitude += radsPerCircleSegment)
        {
            Vector3 next = position + new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);

            output.points.Add(last2);
            output.points.Add(next);

            last2 = next;
        }
    }

    /// <summary>
    /// Draws a top-down circle of the given world position and radius
    /// </summary>
    public static void DrawCircle(Vector3 position, float radius, Style style, int numSegments = 16)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);
        float radsPerLongitude = kRadsInCircle / numSegments;
        Vector3 last = position + new Vector3(0f, 0f, radius);

        for (float longitude = radsPerLongitude; longitude < kRadsInCircle - 0.001f; longitude += radsPerLongitude)
        {
            Vector3 next = position + new Vector3(Mathf.Sin(longitude) * radius, 0f, Mathf.Cos(longitude) * radius);
            output.points.Add(last);
            output.points.Add(next);
            last = next;
        }
    }

    /// <summary>
    /// Draws an up-oriented circle of the given world position and radius
    /// </summary>
    public static void DrawCircle(Vector3 position, float radius, Style style, Vector3 up, int numSegments = 16)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);
        float radsPerLongitude = kRadsInCircle / numSegments;
        Vector3 right = Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.99f ? Vector3.Cross(up, Vector3.up) : Vector3.right;
        Vector3 forward = Vector3.Cross(right, up);
        Vector3 last = position + forward * radius;

        for (float longitude = radsPerLongitude; longitude <= kRadsInCircle + 0.001f; longitude += radsPerLongitude)
        {
            Vector3 next = position + (right * Mathf.Sin(longitude) + forward * Mathf.Cos(longitude)) * radius;
            output.points.Add(last);
            output.points.Add(next);
            last = next;
        }
    }

    /// <summary>
    /// Draws a capsule of the given world start/end points and radius
    /// </summary>
    public static void DrawCapsule(Vector3 start, Vector3 end, float radius, Style style, int numLongitudeSegments = 4, int numTipSegments = 8)
    {
        RequestDrawThisFrame();

        DebugShape output = GetNewShape(style);

        Vector3 localUp = (end - start).normalized;
        Vector3 localRight = localUp.y > -0.99f && localUp.y < 0.99f ? Vector3.Cross(Vector3.up, localUp).normalized : Vector3.right;
        Vector3 localForward = Vector3.Cross(localUp, localRight).normalized;
        float radsPerLongitude = kRadsInCircle / numLongitudeSegments;
        float radsPerCircleSegment = kRadsInCircle / numTipSegments / 4f;

        // Unity capsules start on the tips instead of the centre of the sphere at the start and end, which is what we'll be using
        Vector3 sphereCentresToTips = localUp * Mathf.Min(radius, Vector3.Distance(start, end) / 2f);
        start += sphereCentresToTips;
        end -= sphereCentresToTips;

        // Begin draw
        for (float longitude = 0f; longitude < kRadsInCircle - 0.001f; longitude += radsPerLongitude)
        {
            Vector3 toCircleEdge = (localRight * Mathf.Sin(longitude) + localForward * Mathf.Cos(longitude)) * radius;
            Vector3 lastStartSeg = start + toCircleEdge;
            Vector3 lastEndSeg = end + toCircleEdge;

            // connect the start and end
            output.points.Add(lastStartSeg);
            output.points.Add(lastEndSeg);

            // curve both the start and end tips
            for (float rads = radsPerCircleSegment; rads < kRadsInCircle / 4f; rads += radsPerCircleSegment)
            {
                Vector3 up = localUp * (Mathf.Sin(rads) * radius), fwd = toCircleEdge * Mathf.Cos(rads);
                Vector3 nextStartSeg = start - up + fwd;
                Vector3 nextEndSeg = end + up + fwd;

                output.points.Add(lastStartSeg);
                output.points.Add(nextStartSeg);
                output.points.Add(lastEndSeg);
                output.points.Add(nextEndSeg);

                lastStartSeg = nextStartSeg;
                lastEndSeg = nextEndSeg;
            }
        }
    }

    public static void DrawCapsuleFromCentres(Vector3 start, Vector3 end, float radius, Style style, int numLongitudeSegments = 4, int numTipSegments = 8)
    {
        Vector3 startToEnd = (end - start).normalized;
        DrawCapsule(start - startToEnd * radius, end + startToEnd * radius, radius, style, numLongitudeSegments, numTipSegments);
    }

    /// <summary>
    /// Draws a collider at its current position
    /// </summary>
    public static void DrawCollider(Collider collider, Style style)
    {
        DrawCollider(collider.transform.position, collider, style);
    }

    /// <summary>
    /// Draws a collider at an overridden position
    /// </summary>
    public static void DrawCollider(Vector3 position, Collider collider, Style style)
    {
        Vector3 lossyScale = collider.transform.lossyScale;
        switch (collider)
        {
            case CapsuleCollider capsule:
            {
                float halfHeight = capsule.height * 0.5f;
                Vector3 tipOffset = capsule.transform.TransformVector(capsule.direction == 0 ? new Vector3(halfHeight, 0f, 0f) : (capsule.direction == 1 ? new Vector3(0f, halfHeight, 0f) : new Vector3(0f, 0f, halfHeight)));
                DrawCapsule(position - tipOffset, position + tipOffset, capsule.radius, style);
                break;
            }
            case CharacterController controller:
            {
                float halfHeight = Mathf.Min(controller.radius, controller.height * 0.5f);
                Vector3 tipOffset = controller.transform.TransformVector(new Vector3(0f, halfHeight, 0f));
                DrawCapsule(position - tipOffset, position + tipOffset, controller.radius, style);
                break;
            }
            case SphereCollider sphere:
                DrawSphere(sphere.transform.position, Math.Max(Math.Max(lossyScale.x, lossyScale.y), lossyScale.z), style);
                break;
        }
    }
    public static void DrawCharacterController(CharacterController controller, Style style)
    {
        Vector3 scale = controller.transform.lossyScale;
        float controllerRadius = controller.radius * Mathf.Max(scale.x, scale.z), controllerHeight = controller.height * scale.y;
        Vector3 center = controller.transform.TransformPoint(controller.center.x, controller.center.y, controller.center.z);
        DrawCapsule(center + Vector3.up * (controllerHeight * 0.5f), center - Vector3.up * (controllerHeight * 0.5f), controllerRadius, style);
    }

    public static void PushStyle(Style style)
    {
        styleStack.Add(styleStack[styleStack.Count - 1].Merge(style));
    }

    public static void PopStyle()
    {
        if (styleStack.Count > 1)
            styleStack.RemoveAt(styleStack.Count - 1);
        else
            Debug.LogError("DebugDraw.PopStyle(): style stack is empty, Pop called too many times compared to Push. Ignoring.");
    }

    /// <summary>
    /// If a camera is set as ignored, it won't render debug shapes
    /// </summary>
    public static void SetCameraIgnored(Camera cam, bool shouldIgnore)
    {
        if (shouldIgnore)
        {
            if (!ignoredCameras.Contains(cam))
            {
                ignoredCameras.Add(cam);

                // this is a good time to remove dead camera entries
                ignoredCameras.RemoveAll(x => x == null);
            }
        }
        else
            ignoredCameras.Remove(cam);
    }

    private static void RequestDrawThisFrame()
    {
        StartNewShapeBufferIfNewFrame();

        if (!isDrawCallbackActive)
        {
            Camera.onPostRender -= OnFinalRenderDebugShapes;
            Camera.onPostRender += OnFinalRenderDebugShapes;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= OnPauseStateChanged;
            UnityEditor.EditorApplication.pauseStateChanged += OnPauseStateChanged;
#endif
            isDrawCallbackActive = true;
        }
    }

    private static DebugShape GetNewShape(Style styleIn)
    {
        if (hasBufferedPausedShapes)
        {
            // Cleanup the buffered paused shapes. This can happen in paused mode during a step
            debugShapePool.AddRange(previousDebugShapes);
            previousDebugShapes.Clear();

            previousDebugShapes.AddRange(currentDebugShapes);
            currentDebugShapes.Clear();

            hasBufferedPausedShapes = false;
        }

        if (debugShapePool.Count > 0)
        {
            DebugShape output = debugShapePool[debugShapePool.Count - 1];
            debugShapePool.RemoveAt(debugShapePool.Count - 1);
            currentDebugShapes.Add(output);

            output.points.Clear();
            output.style = styleStack[styleStack.Count - 1].Merge(styleIn);
            output.creationTime = Time.time;

            return output;
        }
        else
        {
            DebugShape output = new DebugShape();
            currentDebugShapes.Add(output);
            return output;
        }
    }

    private static void StartNewShapeBufferIfNewFrame()
    {
        // Don't start new frame buffer if we're not on a new frame
        if (Time.unscaledTimeAsDouble == currentBufferTime)
            return; // <-- EARLY OUT - we're not on a new frame so won't make a new buffer

        currentBufferTime = Time.unscaledTimeAsDouble;

        // Clear old shape buffer now
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPaused)
        {
#endif
            preserveDebugShapes.Clear();

            // Collect 'preserved' persistent shapes to move into the new buffer
            float currentTime = Time.time;
            for (int i = currentDebugShapes.Count - 1; i >= 0; i--)
            {
                if (currentDebugShapes[i].style.duration > 0)
                {
                    if (currentTime - currentDebugShapes[i].creationTime <= currentDebugShapes[i].style.duration)
                    {
                        // move this persistent shape into Preserve so that it'll appear in the next frame
                        preserveDebugShapes.Add(currentDebugShapes[i]);
                        currentDebugShapes.RemoveAt(i);
                    }
                }
            }

            debugShapePool.AddRange(previousDebugShapes);
            previousDebugShapes.Clear();

            previousDebugShapes.AddRange(currentDebugShapes);
            currentDebugShapes.Clear();

            currentDebugShapes.AddRange(preserveDebugShapes);

            styleStack.Clear();
            styleStack.Add(Style.DefaultWhite);
#if UNITY_EDITOR
        }
        else
        {
            // todo: check this still works
            // this can happen in the event of a step
            hasBufferedPausedShapes = true;
        }
#endif
    }

    private static void OnFinalRenderDebugShapes(Camera cam)
    {
        // Draw the debug shapes here, if there are any
        if (currentDebugShapes.Count > 0)
        {
            if (ignoredCameras.Contains(cam))
                return; // <--- Early out: This camera is not included for debug draws

#pragma warning disable CS0618
            lineMaterial.SetFloat("_LineThickness", lineThickness);
            lineMaterial.SetPass(0);
#pragma warning restore CS0618

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            GL.Begin(GL.LINES);

            foreach (DebugShape shape in currentDebugShapes)
            {
                GL.Color(shape.style.color);
                GL.TexCoord(new Vector3(shape.style.thickness, 0f, 0f));

                for (int i = 0, e = shape.points.Count / 2 * 2; i < e; i++)
                {
                    GL.Vertex(shape.points[i]);
                }
            }

            GL.End();
            GL.PopMatrix();

            // Clear out remaining expired shapes, continuously, just in case no shape functions are called next frame.
            StartNewShapeBufferIfNewFrame();
        }
        else
        {
            // Shapes have run out, deactivate the draw callback
            Camera.onPostRender -= OnFinalRenderDebugShapes;
            isDrawCallbackActive = false;
        }
    }

#if UNITY_EDITOR
    private static void OnPauseStateChanged(UnityEditor.PauseState pauseState)
    {
        // Preserve the last frame's shapes
        if (pauseState == UnityEditor.PauseState.Paused)
        {
            if (currentDebugShapes.Count == 0 && previousDebugShapes.Count > 0)
            {
                currentDebugShapes.AddRange(previousDebugShapes);
                previousDebugShapes.Clear();
            }

            if (currentDebugShapes.Count > 0)
                RequestDrawThisFrame();
        }
    }
#endif
}