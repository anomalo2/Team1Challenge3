using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draw traffic paths by clicking in the Scene view.
///
/// Why this exists: auto-detecting lanes from road geometry only works when the
/// roads have colliders and a clean shape. Hand-drawing is faster, exact, and
/// works on anything -- including glTF/GLB imports, which arrive with no
/// colliders at all.
///
/// Clicks snap to scene surfaces when there is something to snap to, and fall
/// back to a flat plane at Draw Height when there is not. So it works whether
/// or not your roads have colliders.
///
/// Workflow:
///   Tools > Traffic > New Traffic Path
///   press Start Drawing, click along the road
///   Make Opposite Lane, then Create Spawner
/// </summary>
[CustomEditor(typeof(TrafficRoute))]
public class TrafficRouteEditor : Editor
{
    const string CarPrefabFolder = "Assets/Prefabs/Traffic";

    static bool drawMode;

    // Persisted between selections so the tool keeps your settings.
    static float drawHeight
    {
        get { return EditorPrefs.GetFloat("Traffic.DrawHeight", 0.5f); }
        set { EditorPrefs.SetFloat("Traffic.DrawHeight", value); }
    }

    static float laneOffset
    {
        get { return EditorPrefs.GetFloat("Traffic.LaneOffset", 4f); }
        set { EditorPrefs.SetFloat("Traffic.LaneOffset", value); }
    }

    static float resampleSpacing
    {
        get { return EditorPrefs.GetFloat("Traffic.Resample", 12f); }
        set { EditorPrefs.SetFloat("Traffic.Resample", value); }
    }

    TrafficRoute Route { get { return (TrafficRoute)target; } }

    // ---------------------------------------------------------------- menu

    [MenuItem("Tools/Traffic/New Traffic Path", priority = 10)]
    public static void NewTrafficPath()
    {
        GameObject go = new GameObject("Lane_New");

        TrafficRoute route = go.AddComponent<TrafficRoute>();
        route.loop = false;
        route.waypoints = new List<Transform>();

        // Drop it near the view so it is not miles away.
        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(go, "New Traffic Path");
        Selection.activeGameObject = go;

        drawMode = true;

        Debug.Log("[Traffic] New path created. Click in the Scene view to add waypoints. " +
                  "Press Esc or the Stop Drawing button when done.");
    }

    // ---------------------------------------------------------------- inspector

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            drawMode
                ? "DRAWING: click in the Scene view to add waypoints.\nShift+click near a waypoint deletes it. Esc to stop."
                : "Press Start Drawing, then click along the road in the Scene view.",
            drawMode ? MessageType.Info : MessageType.None);

        GUI.backgroundColor = drawMode ? new Color(1f, 0.6f, 0.6f) : new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button(drawMode ? "Stop Drawing" : "Start Drawing", GUILayout.Height(32)))
        {
            drawMode = !drawMode;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clicking", EditorStyles.boldLabel);
        drawHeight = EditorGUILayout.FloatField(
            new GUIContent("Draw Height (Y)", "Used when the click does not hit any surface. Set this to your road surface height."),
            drawHeight);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Path tools", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reverse")) ReversePath();
            if (GUILayout.Button("Snap to Surface")) SnapAllToSurface();
            if (GUILayout.Button("Clear")) ClearPath();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            resampleSpacing = EditorGUILayout.FloatField("Spacing", resampleSpacing);
            if (GUILayout.Button("Resample Evenly")) Resample();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Make it traffic", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            laneOffset = EditorGUILayout.FloatField(
                new GUIContent("Lane Offset", "Sideways distance to the opposite lane."), laneOffset);

            if (GUILayout.Button("Make Opposite Lane")) MakeOppositeLane();
        }

        if (GUILayout.Button("Create Spawner For This Lane", GUILayout.Height(24)))
            CreateSpawner();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            string.Format("{0} waypoints, {1:0.#} long", Route.Count, PathLength()),
            EditorStyles.miniLabel);

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }

    // ---------------------------------------------------------------- scene view

    void OnSceneGUI()
    {
        TrafficRoute route = Route;
        if (route == null) return;

        EnsureList(route);

        DrawHud();
        DrawWaypointHandles(route);

        if (!drawMode) return;

        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            drawMode = false;
            e.Use();
            Repaint();
            return;
        }

        // Left click adds a point. Shift+click removes the nearest one.
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Vector3 point;
            if (TryPickPoint(e.mousePosition, out point))
            {
                if (e.shift) RemoveNearest(route, point);
                else AddWaypoint(route, point);

                e.Use();
                SceneView.RepaintAll();
            }
        }
    }

    void DrawHud()
    {
        Handles.BeginGUI();

        GUILayout.BeginArea(new Rect(10, 10, 260, drawMode ? 86 : 54), GUI.skin.box);
        GUILayout.Label(drawMode ? "DRAWING TRAFFIC PATH" : "Traffic path selected",
            EditorStyles.boldLabel);

        if (drawMode)
        {
            GUILayout.Label("Click = add waypoint", EditorStyles.miniLabel);
            GUILayout.Label("Shift+click = delete nearest", EditorStyles.miniLabel);
            GUILayout.Label("Esc = stop drawing", EditorStyles.miniLabel);
        }
        else
        {
            GUILayout.Label("Press Start Drawing in the Inspector", EditorStyles.miniLabel);
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    void DrawWaypointHandles(TrafficRoute route)
    {
        for (int i = 0; i < route.waypoints.Count; i++)
        {
            Transform wp = route.waypoints[i];
            if (wp == null) continue;

            float size = HandleUtility.GetHandleSize(wp.position);

            Handles.color = i == 0 ? Color.green
                : (i == route.waypoints.Count - 1 ? Color.red : Color.cyan);

            EditorGUI.BeginChangeCheck();

            Vector3 moved = Handles.FreeMoveHandle(
                wp.position, size * 0.12f, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wp, "Move Waypoint");
                wp.position = moved;
            }

            Handles.Label(wp.position + Vector3.up * size * 0.3f, i.ToString());
        }

        // Direction arrows so one-way lanes are readable at a glance.
        Handles.color = Color.yellow;
        for (int i = 0; i < route.waypoints.Count - 1; i++)
        {
            Transform a = route.waypoints[i];
            Transform b = route.waypoints[i + 1];
            if (a == null || b == null) continue;

            Handles.DrawLine(a.position, b.position);

            Vector3 mid = (a.position + b.position) * 0.5f;
            Vector3 dir = (b.position - a.position);
            if (dir.sqrMagnitude < 0.0001f) continue;

            dir.Normalize();
            float s = HandleUtility.GetHandleSize(mid) * 0.5f;
            Handles.ConeHandleCap(0, mid, Quaternion.LookRotation(dir), s * 0.5f, EventType.Repaint);
        }
    }

    /// <summary>
    /// Snap to scene geometry if there is any, otherwise drop onto a flat plane
    /// at Draw Height. The fallback is what makes this work on collider-less
    /// imports such as GLB roads.
    /// </summary>
    bool TryPickPoint(Vector2 mousePosition, out Vector3 point)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

        object snapped = HandleUtility.RaySnap(ray);
        if (snapped is RaycastHit)
        {
            point = ((RaycastHit)snapped).point;
            return true;
        }

        Plane ground = new Plane(Vector3.up, new Vector3(0f, drawHeight, 0f));
        float enter;

        if (ground.Raycast(ray, out enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    // ---------------------------------------------------------------- edits

    static void EnsureList(TrafficRoute route)
    {
        if (route.waypoints == null) route.waypoints = new List<Transform>();
    }

    void AddWaypoint(TrafficRoute route, Vector3 position)
    {
        GameObject wp = new GameObject("wp" + route.waypoints.Count.ToString("00"));
        Undo.RegisterCreatedObjectUndo(wp, "Add Waypoint");

        wp.transform.SetParent(route.transform, true);
        wp.transform.position = position;

        Undo.RecordObject(route, "Add Waypoint");
        route.waypoints.Add(wp.transform);

        Renumber(route);
        EditorUtility.SetDirty(route);
    }

    void RemoveNearest(TrafficRoute route, Vector3 near)
    {
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < route.waypoints.Count; i++)
        {
            if (route.waypoints[i] == null) continue;

            float d = (route.waypoints[i].position - near).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = i; }
        }

        if (best < 0) return;

        Transform doomed = route.waypoints[best];

        Undo.RecordObject(route, "Delete Waypoint");
        route.waypoints.RemoveAt(best);

        if (doomed != null) Undo.DestroyObjectImmediate(doomed.gameObject);

        Renumber(route);
        EditorUtility.SetDirty(route);
    }

    void ClearPath()
    {
        TrafficRoute route = Route;
        EnsureList(route);

        Undo.RecordObject(route, "Clear Path");

        for (int i = route.waypoints.Count - 1; i >= 0; i--)
            if (route.waypoints[i] != null)
                Undo.DestroyObjectImmediate(route.waypoints[i].gameObject);

        route.waypoints.Clear();
        EditorUtility.SetDirty(route);
        SceneView.RepaintAll();
    }

    void ReversePath()
    {
        TrafficRoute route = Route;
        EnsureList(route);

        Undo.RecordObject(route, "Reverse Path");
        route.waypoints.Reverse();

        Renumber(route);
        EditorUtility.SetDirty(route);
        SceneView.RepaintAll();
    }

    void SnapAllToSurface()
    {
        TrafficRoute route = Route;
        EnsureList(route);

        foreach (Transform wp in route.waypoints)
        {
            if (wp == null) continue;

            Ray down = new Ray(wp.position + Vector3.up * 50f, Vector3.down);
            object hit = HandleUtility.RaySnap(down);

            Undo.RecordObject(wp, "Snap Waypoint");

            if (hit is RaycastHit) wp.position = ((RaycastHit)hit).point;
            else wp.position = new Vector3(wp.position.x, drawHeight, wp.position.z);
        }

        SceneView.RepaintAll();
    }

    /// <summary>Rebuild the path with evenly spaced waypoints along the same shape.</summary>
    void Resample()
    {
        TrafficRoute route = Route;
        EnsureList(route);

        if (route.waypoints.Count < 2 || resampleSpacing <= 0.01f) return;

        List<Vector3> original = new List<Vector3>();
        foreach (Transform wp in route.waypoints)
            if (wp != null) original.Add(wp.position);

        float total = 0f;
        for (int i = 0; i < original.Count - 1; i++)
            total += Vector3.Distance(original[i], original[i + 1]);

        int steps = Mathf.Max(1, Mathf.RoundToInt(total / resampleSpacing));

        List<Vector3> resampled = new List<Vector3>();
        for (int s = 0; s <= steps; s++)
            resampled.Add(PointAlong(original, (total * s) / steps));

        ClearPath();

        foreach (Vector3 p in resampled) AddWaypoint(Route, p);

        SceneView.RepaintAll();
    }

    static Vector3 PointAlong(List<Vector3> pts, float distance)
    {
        float travelled = 0f;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            float seg = Vector3.Distance(pts[i], pts[i + 1]);

            if (travelled + seg >= distance)
            {
                float t = seg <= 0.0001f ? 0f : (distance - travelled) / seg;
                return Vector3.Lerp(pts[i], pts[i + 1], t);
            }

            travelled += seg;
        }

        return pts[pts.Count - 1];
    }

    /// <summary>Mirror this lane sideways and reverse it, giving the other direction.</summary>
    void MakeOppositeLane()
    {
        TrafficRoute route = Route;
        EnsureList(route);

        if (route.Count < 2)
        {
            Debug.LogWarning("[Traffic] Draw at least 2 waypoints first.");
            return;
        }

        GameObject go = new GameObject(route.gameObject.name + "_Opposite");
        Undo.RegisterCreatedObjectUndo(go, "Make Opposite Lane");

        go.transform.SetParent(route.transform.parent, true);

        TrafficRoute other = go.AddComponent<TrafficRoute>();
        other.loop = route.loop;
        other.waypoints = new List<Transform>();

        // Walk backwards, offsetting to the right of the ORIGINAL travel
        // direction so the two lanes sit side by side, not on top of each other.
        for (int i = route.waypoints.Count - 1; i >= 0; i--)
        {
            Transform wp = route.waypoints[i];
            if (wp == null) continue;

            int next = Mathf.Min(i + 1, route.waypoints.Count - 1);
            int prev = Mathf.Max(i - 1, 0);

            Vector3 forward = route.waypoints[next] != null && route.waypoints[prev] != null
                ? route.waypoints[next].position - route.waypoints[prev].position
                : Vector3.forward;

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            GameObject nwp = new GameObject("wp" + other.waypoints.Count.ToString("00"));
            nwp.transform.SetParent(go.transform, true);
            nwp.transform.position = wp.position + right * laneOffset;

            other.waypoints.Add(nwp.transform);
        }

        Selection.activeGameObject = go;
        Debug.Log("[Traffic] Created '" + go.name + "' running the other way, offset " + laneOffset + ".");
    }

    void CreateSpawner()
    {
        TrafficRoute route = Route;

        if (route.Count < 2)
        {
            Debug.LogWarning("[Traffic] Draw at least 2 waypoints first.");
            return;
        }

        GameObject go = new GameObject(route.gameObject.name + "_Spawner");
        Undo.RegisterCreatedObjectUndo(go, "Create Spawner");

        go.transform.SetParent(route.transform.parent, true);
        go.transform.position = route.GetPosition(0);

        TrafficSpawner spawner = go.AddComponent<TrafficSpawner>();
        spawner.route = route;
        spawner.spawnWaypoint = 0;
        spawner.carPrefabs = FindCarPrefabs();
        spawner.spawnInterval = 5f;
        spawner.intervalJitter = 2f;
        spawner.maxAlive = Mathf.Max(2, Mathf.RoundToInt(PathLength() / 20f));
        spawner.minSpacing = 10f;
        spawner.spawnOnStart = true;

        if (spawner.carPrefabs.Count == 0)
            Debug.LogWarning("[Traffic] No car prefabs found in " + CarPrefabFolder +
                             ". Run Tools > Traffic > 2 - Build Traffic once to generate them, " +
                             "or assign them by hand.");

        Selection.activeGameObject = go;
        Debug.Log("[Traffic] Spawner created for '" + route.gameObject.name + "' with " +
                  spawner.carPrefabs.Count + " car prefab(s).");
    }

    static List<GameObject> FindCarPrefabs()
    {
        List<GameObject> cars = new List<GameObject>();

        if (!AssetDatabase.IsValidFolder(CarPrefabFolder)) return cars;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CarPrefabFolder }))
        {
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null && p.GetComponent<CarController>() != null) cars.Add(p);
        }

        return cars;
    }

    void Renumber(TrafficRoute route)
    {
        for (int i = 0; i < route.waypoints.Count; i++)
            if (route.waypoints[i] != null)
                route.waypoints[i].gameObject.name = "wp" + i.ToString("00");
    }

    float PathLength()
    {
        TrafficRoute route = Route;
        if (route.waypoints == null) return 0f;

        float total = 0f;
        for (int i = 0; i < route.waypoints.Count - 1; i++)
        {
            if (route.waypoints[i] == null || route.waypoints[i + 1] == null) continue;
            total += Vector3.Distance(route.waypoints[i].position, route.waypoints[i + 1].position);
        }

        return total;
    }
}
