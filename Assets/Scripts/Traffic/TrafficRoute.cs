using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lane. Make an empty GameObject, drop child empties along the road centre
/// in order, and they become the path. Used by cars AND by the old lady, so a
/// crossing is just another short route.
///
/// Deliberately not NavMesh: traffic wants to stay in its lane and the crossing
/// challenge wants to be repeatable, and both of those are easier to author and
/// debug as waypoints you can see in the scene view.
/// </summary>
public class TrafficRoute : MonoBehaviour
{
    [Tooltip("Ordered points along the lane. Leave empty to auto-fill from child objects on Awake.")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("Cars loop back to the start instead of despawning at the end.")]
    public bool loop = true;

    [Header("Scene view")]
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f);
    public float gizmoRadius = 0.5f;

    public int Count => waypoints.Count;
    public bool IsValid => waypoints.Count >= 2;

    void Awake()
    {
        if (waypoints.Count == 0) CollectChildren();
    }

    /// <summary>Fill the list from child objects, in hierarchy order.</summary>
    public void CollectChildren()
    {
        waypoints.Clear();
        foreach (Transform child in transform) waypoints.Add(child);
    }

    public Transform GetWaypoint(int index)
    {
        if (waypoints.Count == 0) return null;
        return waypoints[Mathf.Clamp(index, 0, waypoints.Count - 1)];
    }

    public Vector3 GetPosition(int index)
    {
        Transform t = GetWaypoint(index);
        return t != null ? t.position : transform.position;
    }

    /// <summary>Next index along the route, or -1 if the route has ended.</summary>
    public int NextIndex(int current)
    {
        int next = current + 1;

        if (next < waypoints.Count) return next;
        return loop ? 0 : -1;
    }

    public Vector3 EndPosition => GetPosition(waypoints.Count - 1);

    /// <summary>Index of the waypoint nearest a world position. Used to resume a walk.</summary>
    public int NearestIndex(Vector3 worldPos)
    {
        int best = 0;
        float bestDist = float.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;

            float d = (waypoints[i].position - worldPos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        Gizmos.color = gizmoColor;

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;

            Gizmos.DrawWireSphere(waypoints[i].position, gizmoRadius);

            int next = i + 1;
            if (next >= waypoints.Count)
            {
                if (!loop) break;
                next = 0;
            }

            if (waypoints[next] == null) continue;

            Vector3 a = waypoints[i].position;
            Vector3 b = waypoints[next].position;
            Gizmos.DrawLine(a, b);

            // Direction tick, so you can see which way traffic flows.
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = (b - a).normalized;
            Gizmos.DrawLine(mid, mid - dir * 1.5f + Vector3.up * 0.75f);
        }
    }
}
