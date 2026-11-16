using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A walking route for a pedestrian: kerb, across the road, far pavement.
///
/// This is deliberately NOT a TrafficRoute. It used to be one, and every
/// traffic tool (Add Spawners To All Lanes, Create Spawner, the lane editor)
/// treated the crossing as a lane and spawned cars down it. A separate type
/// means no traffic code can ever see a pedestrian path.
///
/// HOW TO USE
///   Drag Assets/Prefabs/Pedestrians/PedestrianPath.prefab into the scene
///   (or GameObject > Pedestrians > Pedestrian Path). Drag the green START and
///   red END spheres in the Scene view onto the two kerbs. That is it.
///   Points are the children, in hierarchy order; add a middle point from the
///   Inspector if the route needs to bend.
/// </summary>
[DisallowMultipleComponent]
public class PedestrianPath : MonoBehaviour
{
    [Tooltip("Ordered points from the starting kerb to the far pavement. Leave empty to use the child objects in order.")]
    public List<Transform> points = new List<Transform>();

    [Header("Scene view")]
    public Color color = new Color(1f, 0.85f, 0.2f);

    public int Count { get { Refresh(); return points.Count; } }
    public bool IsValid => Count >= 2;
    public Vector3 StartPosition => GetPosition(0);
    public Vector3 EndPosition => GetPosition(Count - 1);

    void Awake() { Refresh(); }

    /// <summary>Use the children when no explicit list was given, and drop dead entries.</summary>
    public void Refresh()
    {
        if (points == null) points = new List<Transform>();
        points.RemoveAll(p => p == null);

        if (points.Count == 0)
            foreach (Transform child in transform) points.Add(child);
    }

    public Vector3 GetPosition(int index)
    {
        if (points == null || points.Count == 0) return transform.position;
        Transform t = points[Mathf.Clamp(index, 0, points.Count - 1)];
        return t != null ? t.position : transform.position;
    }

    /// <summary>Next index along the path, or -1 at the end.</summary>
    public int NextIndex(int current)
    {
        int next = current + 1;
        return next < Count ? next : -1;
    }

    public int NearestIndex(Vector3 worldPos)
    {
        int best = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < Count; i++)
        {
            float d = (GetPosition(i) - worldPos).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = i; }
        }

        return best;
    }

    /// <summary>Facing at the start: towards the second point, flattened.</summary>
    public Quaternion StartRotation
    {
        get
        {
            Vector3 f = GetPosition(1) - GetPosition(0);
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(f.normalized, Vector3.up) : transform.rotation;
        }
    }

    public float Length
    {
        get
        {
            float total = 0f;
            for (int i = 0; i < Count - 1; i++) total += Vector3.Distance(GetPosition(i), GetPosition(i + 1));
            return total;
        }
    }

    /// <summary>The path whose START is nearest a position, within maxDistance. Null if none.</summary>
    public static PedestrianPath FindNearestStart(Vector3 position, float maxDistance)
    {
        PedestrianPath best = null;
        float bestDist = maxDistance * maxDistance;

        foreach (PedestrianPath p in FindObjectsByType<PedestrianPath>(FindObjectsSortMode.None))
        {
            if (!p.IsValid) continue;
            float d = (p.StartPosition - position).sqrMagnitude;
            if (d <= bestDist) { bestDist = d; best = p; }
        }

        return best;
    }

    void OnDrawGizmos()
    {
        Refresh();
        if (points.Count == 0) return;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i].position;
            Gizmos.color = i == 0 ? Color.green : (i == points.Count - 1 ? Color.red : color);
            Gizmos.DrawSphere(p + Vector3.up * 0.15f, 0.35f);

            if (i == points.Count - 1) break;

            // Dashed line with arrow ticks so the walking direction is obvious.
            Vector3 a = p + Vector3.up * 0.15f;
            Vector3 b = points[i + 1].position + Vector3.up * 0.15f;
            Gizmos.color = color;
            float len = Vector3.Distance(a, b);
            int dashes = Mathf.Max(1, Mathf.FloorToInt(len / 0.8f));
            for (int d = 0; d < dashes; d += 2)
                Gizmos.DrawLine(Vector3.Lerp(a, b, d / (float)dashes), Vector3.Lerp(a, b, Mathf.Min(1f, (d + 1) / (float)dashes)));

            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.6f;
            Gizmos.DrawLine(mid, mid - dir * 0.9f + side);
            Gizmos.DrawLine(mid, mid - dir * 0.9f - side);
        }
    }
}
