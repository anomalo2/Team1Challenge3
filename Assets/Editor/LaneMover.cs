using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves traffic lanes from one scene to another, for when a path was drawn in
/// the wrong scene.
///
/// A TrafficRoute is only waypoint positions plus a loop flag, so the lane is
/// captured as world coordinates, removed from the source, and rebuilt in the
/// target. That survives the scene switch cleanly, which copying GameObjects
/// between scenes does not always do.
///
/// Any spawner pointing at a moved lane travels with it, so traffic keeps
/// working without being rewired by hand.
///
/// Controlled by environment variables so it can run in batch:
///   OPT_SRC_SCENE   default Assets/Scenes/AudreyTesting.unity
///   OPT_DST_SCENE   default Assets/Scenes/JohnTesting.unity
///   OPT_LANE_FILTER default Lane_New   (substring match, case-insensitive)
/// </summary>
public static class LaneMover
{
    const string LogPath = "LaneMove.log";

    class CapturedLane
    {
        public string Name;
        public bool Loop;
        public List<Vector3> Points = new List<Vector3>();

        // spawner settings, if one pointed at this lane
        public bool HadSpawner;
        public string SpawnerName;
        public float SpawnInterval = 5f;
        public float IntervalJitter = 2f;
        public int MaxAlive = 4;
        public float MinSpacing = 12f;
        public bool SpawnOnStart = true;
        public List<GameObject> CarPrefabs = new List<GameObject>();
    }

    [MenuItem("Tools/Traffic/6 - Move Lanes To Another Scene")]
    public static void MoveLanes() { Run(); }

    public static void MoveBatch() { Run(); }

    static void Run()
    {
        string src = Env("OPT_SRC_SCENE", "Assets/Scenes/AudreyTesting.unity");
        string dst = Env("OPT_DST_SCENE", "Assets/Scenes/JohnTesting.unity");
        string filter = Env("OPT_LANE_FILTER", "Lane_New").ToLowerInvariant();

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Move lanes between scenes ===");
        log.AppendLine("from:   " + src);
        log.AppendLine("to:     " + dst);
        log.AppendLine("filter: " + filter);

        if (src == dst) { log.AppendLine("ERROR: source and target are the same"); Finish(log); return; }

        // ---- capture from source
        Scene source = EditorSceneManager.OpenScene(src, OpenSceneMode.Single);
        if (!source.IsValid()) { log.AppendLine("ERROR: could not open " + src); Finish(log); return; }

        List<CapturedLane> captured = new List<CapturedLane>();
        List<GameObject> toDelete = new List<GameObject>();

        TrafficSpawner[] spawners = Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None);

        foreach (TrafficRoute route in Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None))
        {
            if (!route.gameObject.name.ToLowerInvariant().Contains(filter)) continue;

            CapturedLane lane = new CapturedLane();
            lane.Name = route.gameObject.name;
            lane.Loop = route.loop;

            if (route.waypoints != null)
                foreach (Transform wp in route.waypoints)
                    if (wp != null) lane.Points.Add(wp.position);

            foreach (TrafficSpawner sp in spawners)
            {
                if (sp == null || sp.route != route) continue;

                lane.HadSpawner = true;
                lane.SpawnerName = sp.gameObject.name;
                lane.SpawnInterval = sp.spawnInterval;
                lane.IntervalJitter = sp.intervalJitter;
                lane.MaxAlive = sp.maxAlive;
                lane.MinSpacing = sp.minSpacing;
                lane.SpawnOnStart = sp.spawnOnStart;

                if (sp.carPrefabs != null) lane.CarPrefabs.AddRange(sp.carPrefabs);

                toDelete.Add(sp.gameObject);
                break;
            }

            captured.Add(lane);
            toDelete.Add(route.gameObject);

            log.AppendLine(string.Format("  captured '{0}': {1} waypoints, spawner {2}",
                lane.Name, lane.Points.Count, lane.HadSpawner ? "yes" : "no"));
        }

        if (captured.Count == 0)
        {
            log.AppendLine("nothing matched '" + filter + "' in " + src + " - nothing moved");
            Finish(log);
            return;
        }

        foreach (GameObject go in toDelete) if (go != null) Object.DestroyImmediate(go);

        EditorSceneManager.MarkSceneDirty(source);
        EditorSceneManager.SaveScene(source);
        log.AppendLine("removed " + captured.Count + " lane(s) from source, source saved");

        // ---- rebuild in target
        Scene target = EditorSceneManager.OpenScene(dst, OpenSceneMode.Single);
        if (!target.IsValid()) { log.AppendLine("ERROR: could not open " + dst); Finish(log); return; }

        List<GameObject> fallbackCars = FindCarPrefabs();
        int rebuilt = 0;

        foreach (CapturedLane lane in captured)
        {
            if (lane.Points.Count < 2)
            {
                log.AppendLine("  skipped '" + lane.Name + "': fewer than 2 waypoints");
                continue;
            }

            // A duplicate name here would be as confusing as it was in the
            // source scene, so make it unique on arrival.
            string name = lane.Name;
            if (GameObject.Find(name) != null) name = lane.Name + "_" + (rebuilt + 1).ToString("00");

            GameObject go = new GameObject(name);
            TrafficRoute route = go.AddComponent<TrafficRoute>();
            route.loop = lane.Loop;
            route.waypoints = new List<Transform>();

            for (int i = 0; i < lane.Points.Count; i++)
            {
                GameObject wp = new GameObject("wp" + i.ToString("00"));
                wp.transform.SetParent(go.transform, true);
                wp.transform.position = lane.Points[i];
                route.waypoints.Add(wp.transform);
            }

            float length = 0f;
            for (int i = 0; i < lane.Points.Count - 1; i++)
                length += Vector3.Distance(lane.Points[i], lane.Points[i + 1]);

            // Give it a spawner either way: a lane with no spawner has no cars,
            // and that is almost never what is wanted after a move.
            GameObject spGo = new GameObject(name + "_Spawner");
            spGo.transform.position = lane.Points[0];

            TrafficSpawner spawner = spGo.AddComponent<TrafficSpawner>();
            spawner.route = route;
            spawner.spawnWaypoint = 0;
            spawner.spawnInterval = lane.SpawnInterval;
            spawner.intervalJitter = lane.IntervalJitter;
            spawner.minSpacing = lane.MinSpacing;
            spawner.spawnOnStart = lane.SpawnOnStart;
            spawner.maxAlive = lane.HadSpawner
                ? lane.MaxAlive
                : Mathf.Clamp(Mathf.RoundToInt(length / 40f), 2, 6);

            List<GameObject> cars = lane.CarPrefabs.Count > 0 ? lane.CarPrefabs : fallbackCars;
            spawner.carPrefabs = new List<GameObject>(cars);

            log.AppendLine(string.Format("  rebuilt '{0}': {1} waypoints, {2:0.#} long, {3} car prefab(s), maxAlive {4}",
                name, lane.Points.Count, length, cars.Count, spawner.maxAlive));

            rebuilt++;
        }

        if (Object.FindFirstObjectByType<CityObjective>() == null)
        {
            GameObject objGo = new GameObject("CityObjective");
            objGo.AddComponent<CityObjective>();
            log.AppendLine("  added CityObjective to target");
        }

        EditorSceneManager.MarkSceneDirty(target);
        EditorSceneManager.SaveScene(target);

        log.AppendLine("rebuilt " + rebuilt + " lane(s) in target, target saved");
        Finish(log);
    }

    static List<GameObject> FindCarPrefabs()
    {
        List<GameObject> cars = new List<GameObject>();
        const string folder = "Assets/Prefabs/Traffic";

        if (!AssetDatabase.IsValidFolder(folder)) return cars;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null && p.GetComponent<CarController>() != null) cars.Add(p);
        }

        return cars;
    }

    static string Env(string key, string fallback)
    {
        string v = System.Environment.GetEnvironmentVariable(key);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    static void Finish(StringBuilder log)
    {
        log.AppendLine("DONE");
        Debug.Log("[LaneMover]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
