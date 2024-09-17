using System;
using System.Collections.Generic;
using UnityEngine;

public static class GizmoExtensions
{
    private static List<Vector3> segments = new List<Vector3>();

    public static void DrawCircle(Vector3 centre, float radius, int numSegments = 16)
    {
        segments.Clear();
        float sin = 0, cos = 1;
        for (int i = 0; i < numSegments; i++)
        {
            float nextSin = Mathf.Sin(i * Mathf.PI * 2 / numSegments);
            float nextCos = Mathf.Cos(i * Mathf.PI * 2 / numSegments);
            segments.Add(centre + new Vector3(nextSin, 0f, nextCos) * radius);
            cos = nextCos;
            sin = nextSin;
        }

        // todo: no alloc version.
        Gizmos.DrawLineStrip(segments.ToArray(), true);
    }
}
