using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor harness that sets up city traffic by MEASURING the roads rather than
/// hard-coding coordinates, so it keeps working when the level changes.
///
/// How it works
/// ------------
/// 1. Probe the real road surface with per-collider downward raycasts.
///    Collider.Raycast is used rather than Physics.Raycast because the physics
///    broadphase is not dependable in edit mode.
/// 2. Mark a cell undrivable if anything else stands on it. In this scene
///    buildings sit ON the road slabs, so a lane laid purely on road surface
///    would drive straight through one.
/// 3. Split the drivable cells into connected road areas.
/// 4. In each area find the LARGEST CLEAR RECTANGLE. That is a well-defined
///    answer to "where can a car actually drive in a straight line", and it
///    degrades gracefully in a cluttered level, which fitting bands along rows
///    does not -- one obstacle in a row destroys the whole band.
/// 5. Lay lanes along the rectangle's long axis: two opposing lanes if it is
///    wide enough, otherwise a single one-way lane down the middle.
///
/// Entry points work from the Tools menu or from -executeMethod in batch mode.
/// All of them write TrafficHarness.log next to the project, so results are
/// readable without the Editor console.
/// </summary>
public static class TrafficSetupHarness
{
    const string ScenePath = "Assets/Scenes/AudreyTesting.unity";
    const string RoadsRootName = "roads";
    const string TrafficRootName = "Traffic";
    const string CarPrefabFolder = "Assets/Prefabs/Traffic";
    const string LogPath = "TrafficHarness.log";

    // Probing
    const float ProbeStep = 1.5f;
    const float ProbeTop = 500f;
    const float ObstacleClearance = 1.5f;
    const float ObstacleMinHeight = 0.3f;

    // The road tiles are a single mesh with a single material, so asphalt and
    // pavement cannot be told apart by material. They CAN be told apart by
    // height: kerbs and pavement sit proudly above the carriageway. Anything
    // within this much of the lowest road surface counts as drivable asphalt.
    const float AsphaltTolerance = 0.06f;

    // Road plate shape filter: wide and flat. Keeps buildings out.
    const float MaxRoadThickness = 2f;
    const float MinRoadFootprint = 5f;

    // Lane fitting
    const float MinLaneLength = 25f;
    const float MinTwoWayWidth = 9f;
    const float WaypointSpacing = 12f;
    const float EndInset = 3f;
    const float LaneOffsetFraction = 0.25f;

    static readonly string[] CarSources =
    {
        "Assets/EnvironmentAssets/MetroFleet/Prefabs/Civilian/MF_Sedan_01.prefab",
        "Assets/EnvironmentAssets/MetroFleet/Prefabs/Civilian/MF_Hatchback_01.prefab",
        "Assets/EnvironmentAssets/MetroFleet/Prefabs/Civilian/MF_SUV_01.prefab",
        "Assets/EnvironmentAssets/MetroFleet/Prefabs/Civilian/MF_Taxi_01.prefab",
        "Assets/EnvironmentAssets/MetroFleet/Prefabs/Civilian/MF_Pickup_01.prefab",
    };

    static readonly string[] RequiredLayers = { "Mech", "Vehicle", "Civilian", "Crushable" };

    // ------------------------------------------------------------------ entry points

    [MenuItem("Tools/Traffic/0 - Diagnose Roads Hierarchy")]
    public static void DiagnoseRoads()
    {
        ResetLog("DIAGNOSE ROADS HIERARCHY");
        if (!EnsureSceneOpen()) return;

        GameObject roadsRoot = GameObject.Find(RoadsRootName);
        if (roadsRoot == null) { Fail("No GameObject named '" + RoadsRootName + "'"); return; }

        Log("roads root at " + roadsRoot.transform.position.ToString("0.##")
            + "  direct children: " + roadsRoot.transform.childCount);

        foreach (Transform child in roadsRoot.transform)
        {
            Collider[] cols = child.GetComponentsInChildren<Collider>(true);
            Log(string.Format("TILE '{0}' active={1} pos={2} eulerY={3:0.#} scale={4} colliders={5}",
                child.name, child.gameObject.activeInHierarchy, child.position.ToString("0.##"),
                child.eulerAngles.y, child.localScale.ToString("0.##"), cols.Length));

            foreach (Collider c in cols)
                Log(string.Format("     '{0}' {1} centre={2} size={3}",
                    c.gameObject.name, c.GetType().Name,
                    c.bounds.center.ToString("0.##"), c.bounds.size.ToString("0.##")));
        }

        Log("DONE");
    }

    [MenuItem("Tools/Traffic/1 - Report Road Geometry")]
    public static void ReportRoadGeometry()
    {
        ResetLog("REPORT ROAD GEOMETRY");
        if (!EnsureSceneOpen()) return;

        List<Collider> roads = CollectRoadColliders();
        if (roads.Count == 0) { Fail("No road colliders found."); return; }

        Occupancy occ = Probe(roads);
        Log("Road tile colliders: " + roads.Count);
        Log(HeightHistogram(occ));
        Log(occ.ToAscii());

        List<Lane> lanes = FitLanes(occ);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Fitted " + lanes.Count + " lane(s):");
        foreach (Lane l in lanes) sb.AppendLine("  " + l.Describe());
        Log(sb.ToString());
        Log("DONE");
    }

    [MenuItem("Tools/Traffic/2 - Build Traffic")]
    public static void BuildTraffic()
    {
        ResetLog("BUILD TRAFFIC");
        if (!EnsureSceneOpen()) return;

        EnsureLayers();

        List<Collider> roads = CollectRoadColliders();
        if (roads.Count == 0) { Fail("No road colliders found. Nothing built."); return; }

        Occupancy occ = Probe(roads);
        Log(HeightHistogram(occ));
        Log(occ.ToAscii());

        List<Lane> lanes = FitLanes(occ);
        if (lanes.Count == 0) { Fail("No drivable lanes fitted. Nothing built."); return; }

        foreach (Lane l in lanes) Log("  " + l.Describe());

        List<GameObject> carPrefabs = BuildCarPrefabs();
        if (carPrefabs.Count == 0) { Fail("No car prefabs built. Nothing built."); return; }

        GameObject old = GameObject.Find(TrafficRootName);
        if (old != null) Object.DestroyImmediate(old);   // re-running stays idempotent

        GameObject root = new GameObject(TrafficRootName);

        int vehicleLayer = LayerMask.NameToLayer("Vehicle");
        int civilianLayer = LayerMask.NameToLayer("Civilian");
        int mechLayer = LayerMask.NameToLayer("Mech");

        int obstacleMask = (1 << vehicleLayer) | (1 << mechLayer);
        int pedestrianMask = 1 << civilianLayer;

        foreach (Lane lane in lanes)
        {
            GameObject laneGo = new GameObject(lane.Name);
            laneGo.transform.SetParent(root.transform, true);

            TrafficRoute route = laneGo.AddComponent<TrafficRoute>();
            route.loop = false;
            route.waypoints = new List<Transform>();

            for (int i = 0; i < lane.Points.Count; i++)
            {
                GameObject wp = new GameObject("wp" + i.ToString("00"));
                wp.transform.SetParent(laneGo.transform, true);
                wp.transform.position = lane.Points[i];
                route.waypoints.Add(wp.transform);
            }

            GameObject spawnerGo = new GameObject(lane.Name + "_Spawner");
            spawnerGo.transform.SetParent(root.transform, true);
            spawnerGo.transform.position = lane.Points[0];

            TrafficSpawner spawner = spawnerGo.AddComponent<TrafficSpawner>();
            spawner.route = route;
            spawner.spawnWaypoint = 0;
            spawner.carPrefabs = new List<GameObject>(carPrefabs);
            spawner.spawnInterval = 5f;
            spawner.intervalJitter = 2f;
            spawner.maxAlive = Mathf.Max(2, Mathf.RoundToInt(lane.Length / 20f));
            spawner.minSpacing = 10f;
            spawner.spawnOnStart = true;
        }

        foreach (GameObject prefab in carPrefabs)
        {
            CarController cc = prefab.GetComponent<CarController>();
            if (cc == null) continue;

            cc.obstacleLayers = obstacleMask;
            cc.pedestrianLayers = pedestrianMask;
            cc.yieldToPedestrians = false;
            EditorUtility.SetDirty(prefab);
        }

        if (Object.FindFirstObjectByType<CityObjective>() == null)
        {
            GameObject objGo = new GameObject("CityObjective");
            objGo.transform.SetParent(root.transform, true);
            objGo.AddComponent<CityObjective>();
            Log("Added CityObjective");
        }

        AssetDatabase.SaveAssets();

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Log(string.Format("Built {0} lane(s), {1} car prefab(s). Scene saved.",
            lanes.Count, carPrefabs.Count));
        Log("DONE");
    }

    [MenuItem("Tools/Traffic/3 - Verify Traffic")]
    public static void VerifyTraffic()
    {
        ResetLog("VERIFY TRAFFIC");
        if (!EnsureSceneOpen()) return;

        int problems = 0;

        // --- layers ---
        foreach (string layer in RequiredLayers)
        {
            if (LayerMask.NameToLayer(layer) < 0)
            {
                Fail("layer missing: " + layer);
                problems++;
            }
            else Log("layer OK: " + layer + " = " + LayerMask.NameToLayer(layer));
        }

        // --- scene objects ---
        GameObject root = GameObject.Find(TrafficRootName);
        if (root == null) { Fail("No '" + TrafficRootName + "' object in the scene."); return; }

        TrafficRoute[] routes = root.GetComponentsInChildren<TrafficRoute>(true);
        TrafficSpawner[] spawners = root.GetComponentsInChildren<TrafficSpawner>(true);
        CityObjective objective = Object.FindFirstObjectByType<CityObjective>();

        Log(string.Format("routes={0} spawners={1} objective={2}",
            routes.Length, spawners.Length, objective != null));

        if (objective == null) { Fail("No CityObjective in the scene."); problems++; }

        // --- every waypoint must be on road that is not blocked ---
        List<Collider> roads = CollectRoadColliders();
        List<Collider> obstacles = CollectObstacles(roads);

        foreach (TrafficRoute route in routes)
        {
            if (!route.IsValid) { Fail(route.name + ": route invalid (needs 2+ waypoints)"); problems++; continue; }

            int bad = 0;

            for (int i = 0; i < route.waypoints.Count; i++)
            {
                Transform wp = route.waypoints[i];
                if (wp == null) { bad++; continue; }

                float y;
                bool onRoad = SampleRoad(roads, wp.position.x, wp.position.z, out y);
                bool blocked = onRoad && Blocked(obstacles, wp.position.x, y, wp.position.z);

                if (!onRoad)
                {
                    Fail(string.Format("{0} wp{1} at {2} is NOT over road",
                        route.name, i, wp.position.ToString("0.#")));
                    bad++;
                }
                else if (blocked)
                {
                    Fail(string.Format("{0} wp{1} at {2} is BLOCKED by something standing on the road",
                        route.name, i, wp.position.ToString("0.#")));
                    bad++;
                }
                else if (Mathf.Abs(wp.position.y - y) > 0.5f)
                {
                    Fail(string.Format("{0} wp{1} y={2:0.##} but road surface is {3:0.##}",
                        route.name, i, wp.position.y, y));
                    bad++;
                }
            }

            problems += bad;
            Log(string.Format("{0}: {1} waypoints, {2}",
                route.name, route.waypoints.Count, bad == 0 ? "all on clear road" : bad + " BAD"));
        }

        // --- spawners wired ---
        foreach (TrafficSpawner s in spawners)
        {
            if (s.route == null) { Fail(s.name + ": no route assigned"); problems++; }
            if (s.carPrefabs == null || s.carPrefabs.Count == 0) { Fail(s.name + ": no car prefabs"); problems++; }
            else
            {
                foreach (GameObject p in s.carPrefabs)
                    if (p == null) { Fail(s.name + ": null entry in carPrefabs"); problems++; }
            }
        }

        // --- car prefabs ---
        int vehicleLayer = LayerMask.NameToLayer("Vehicle");
        int civilianLayer = LayerMask.NameToLayer("Civilian");
        int mechLayer = LayerMask.NameToLayer("Mech");

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CarPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            CarController cc = prefab.GetComponent<CarController>();
            CrushableObject cr = prefab.GetComponent<CrushableObject>();
            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            Collider col = prefab.GetComponent<Collider>();

            List<string> issues = new List<string>();

            if (cc == null) issues.Add("no CarController");
            if (cr == null) issues.Add("no CrushableObject");
            if (rb == null) issues.Add("no Rigidbody");
            if (col == null) issues.Add("no Collider");
            if (prefab.layer != vehicleLayer) issues.Add("layer is " + LayerMask.LayerToName(prefab.layer));

            if (cc != null)
            {
                int wantObstacle = (1 << vehicleLayer) | (1 << mechLayer);
                if (cc.obstacleLayers.value != wantObstacle)
                    issues.Add("obstacleLayers=" + cc.obstacleLayers.value + " want " + wantObstacle);

                if (cc.pedestrianLayers.value != (1 << civilianLayer))
                    issues.Add("pedestrianLayers=" + cc.pedestrianLayers.value);

                if (cc.yieldToPedestrians) issues.Add("yieldToPedestrians is ON");
            }

            if (issues.Count == 0) Log("prefab OK: " + System.IO.Path.GetFileName(path));
            else
            {
                Fail(System.IO.Path.GetFileName(path) + ": " + string.Join(", ", issues));
                problems += issues.Count;
            }
        }

        Log(problems == 0 ? "VERIFY PASSED - no problems" : "VERIFY FAILED - " + problems + " problem(s)");
        Log("DONE");
    }

    [MenuItem("Tools/Traffic/4 - Verify Drawn Lanes")]
    public static void VerifyDrawnLanes()
    {
        ResetLog("VERIFY DRAWN LANES");
        if (!EnsureSceneOpen()) return;

        TrafficRoute[] routes = Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None);
        TrafficSpawner[] spawners = Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None);

        Log("routes found: " + routes.Length + ", spawners: " + spawners.Length);

        int problems = 0;
        int warnings = 0;

        foreach (TrafficRoute route in routes)
        {
            Log("");
            Log("--- " + route.gameObject.name + " ---");

            if (route.waypoints == null || route.waypoints.Count < 2)
            {
                Fail("  needs at least 2 waypoints (has " +
                     (route.waypoints == null ? 0 : route.waypoints.Count) + ")");
                problems++;
                continue;
            }

            int nulls = 0;
            foreach (Transform wp in route.waypoints) if (wp == null) nulls++;
            if (nulls > 0) { Fail("  " + nulls + " null waypoint slot(s)"); problems++; }

            List<Vector3> pts = new List<Vector3>();
            foreach (Transform wp in route.waypoints) if (wp != null) pts.Add(wp.position);

            // length + spacing
            float total = 0f, minSeg = float.MaxValue, maxSeg = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = Vector3.Distance(pts[i], pts[i + 1]);
                total += d;
                minSeg = Mathf.Min(minSeg, d);
                maxSeg = Mathf.Max(maxSeg, d);
            }

            // Height: total spread is NOT a fault, because a lane may legitimately
            // ramp up to a higher level. What matters is the per-segment
            // GRADIENT - a ramp climbs steadily, a mis-click spikes and returns.
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 pt in pts) { minY = Mathf.Min(minY, pt.y); maxY = Mathf.Max(maxY, pt.y); }

            float steepestGrade = 0f;
            int steepestAt = -1;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 d = pts[i + 1] - pts[i];
                float run = new Vector2(d.x, d.z).magnitude;
                if (run < 0.01f) continue;

                float grade = Mathf.Abs(d.y) / run;
                if (grade > steepestGrade) { steepestGrade = grade; steepestAt = i; }
            }

            // sharpest corner - CarController steers at turnSpeed deg/sec
            float sharpest = 0f;
            int sharpestAt = -1;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i] - pts[i - 1];
                Vector3 b = pts[i + 1] - pts[i];
                a.y = 0f; b.y = 0f;

                if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) continue;

                float ang = Vector3.Angle(a, b);
                if (ang > sharpest) { sharpest = ang; sharpestAt = i; }
            }

            Log(string.Format("  {0} waypoints, {1:0.#} long, segments {2:0.#}..{3:0.#}",
                pts.Count, total, minSeg, maxSeg));
            Log(string.Format("  height {0:0.##}..{1:0.##} (climb {2:0.##}, steepest grade {3:0.##})",
                minY, maxY, maxY - minY, steepestGrade));
            Log(string.Format("  sharpest corner {0:0.#} deg{1}", sharpest,
                sharpestAt >= 0 ? " at wp" + sharpestAt : ""));
            Log("  loop: " + route.loop);

            if (maxSeg > 30f)
            {
                Log("  WARN: a " + maxSeg.ToString("0.#") +
                    " gap between waypoints - cars will cut the corner. Add a point or Resample Evenly.");
                warnings++;
            }

            if (minSeg < 1.5f)
            {
                Log("  WARN: waypoints " + minSeg.ToString("0.##") +
                    " apart - that close can make steering jitter. Resample Evenly.");
                warnings++;
            }

            if (steepestGrade > 0.6f)
            {
                Log(string.Format("  WARN: gradient {0:0.##} ({1:0.#} deg) at wp{2} - steeper than a drivable ramp. " +
                    "Check that waypoint is on the road and not on a roof or wall.",
                    steepestGrade, Mathf.Atan(steepestGrade) * Mathf.Rad2Deg, steepestAt));
                warnings++;
            }
            else if (maxY - minY > 1f)
            {
                Log(string.Format("  note: climbs {0:0.##} over the lane, steepest {1:0.#} deg - " +
                    "reads as a ramp to another level, which is fine.",
                    maxY - minY, Mathf.Atan(steepestGrade) * Mathf.Rad2Deg));
            }

            if (sharpest > 90f)
            {
                Log("  WARN: " + sharpest.ToString("0.#") +
                    " deg corner - sharper than the cars steer comfortably (turnSpeed 120 deg/s). " +
                    "Round it off with an extra waypoint.");
                warnings++;
            }

            // spawner wired to this route?
            bool hasSpawner = false;
            foreach (TrafficSpawner sp in spawners)
                if (sp.route == route) { hasSpawner = true; break; }

            if (!hasSpawner)
            {
                Log("  WARN: no spawner points at this lane, so no cars will appear on it. " +
                    "Use 'Create Spawner For This Lane'.");
                warnings++;
            }
            else Log("  spawner: OK");
        }

        // lanes that sit on top of each other cause head-on traffic
        for (int i = 0; i < routes.Length; i++)
        {
            for (int j = i + 1; j < routes.Length; j++)
            {
                if (!routes[i].IsValid || !routes[j].IsValid) continue;

                float closest = float.MaxValue;
                foreach (Transform a in routes[i].waypoints)
                {
                    if (a == null) continue;
                    foreach (Transform b in routes[j].waypoints)
                    {
                        if (b == null) continue;
                        closest = Mathf.Min(closest, Vector3.Distance(a.position, b.position));
                    }
                }

                if (closest < 2f)
                {
                    Log("");
                    Log(string.Format("  WARN: '{0}' and '{1}' pass within {2:0.##} in 3D - cars may meet head-on " +
                        "unless that is a junction where they are meant to cross. Increase Lane Offset if not.",
                        routes[i].gameObject.name, routes[j].gameObject.name, closest));
                    warnings++;
                }
            }
        }

        // spawners with nothing wired
        foreach (TrafficSpawner sp in spawners)
        {
            if (sp.route == null) { Fail("spawner '" + sp.gameObject.name + "' has no route"); problems++; }

            if (sp.carPrefabs == null || sp.carPrefabs.Count == 0)
            {
                Fail("spawner '" + sp.gameObject.name + "' has no car prefabs");
                problems++;
            }
        }

        Log("");
        Log(string.Format("RESULT: {0} problem(s), {1} warning(s)", problems, warnings));
        Log(problems == 0 && warnings == 0
            ? "ALL GOOD - lanes are ready to drive"
            : (problems == 0 ? "USABLE - see warnings above" : "NEEDS FIXING - see errors above"));
        Log("DONE");
    }

    [MenuItem("Tools/Traffic/5 - Add Spawners To All Lanes")]
    public static void AddSpawnersToAllLanes()
    {
        ResetLog("ADD SPAWNERS TO ALL LANES");
        if (!EnsureSceneOpen()) return;

        EnsureLayers();

        List<GameObject> cars = new List<GameObject>();

        if (AssetDatabase.IsValidFolder(CarPrefabFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CarPrefabFolder }))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (prefab != null && prefab.GetComponent<CarController>() != null) cars.Add(prefab);
            }
        }

        if (cars.Count == 0)
        {
            Fail("No car prefabs in " + CarPrefabFolder + ". Nothing to spawn.");
            return;
        }

        Log("car prefabs: " + cars.Count);

        // Make sure the prefabs' layer masks match the layers that exist now.
        int vehicleLayer = LayerMask.NameToLayer("Vehicle");
        int civilianLayer = LayerMask.NameToLayer("Civilian");
        int mechLayer = LayerMask.NameToLayer("Mech");

        foreach (GameObject prefab in cars)
        {
            CarController cc = prefab.GetComponent<CarController>();
            if (cc == null) continue;

            cc.obstacleLayers = (1 << vehicleLayer) | (1 << mechLayer);
            cc.pedestrianLayers = 1 << civilianLayer;
            cc.yieldToPedestrians = false;
            EditorUtility.SetDirty(prefab);
        }

        TrafficRoute[] routes = Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None);

        // Redrawing a lane deletes its route, which leaves its spawner pointing
        // at nothing. Those spawn no cars and quietly clutter the scene, so
        // clear them out before wiring the lanes that are actually here now.
        int orphansRemoved = 0;

        foreach (TrafficSpawner sp in Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None))
        {
            if (sp == null) continue;
            if (sp.route != null) continue;

            Log("removed orphaned spawner '" + sp.gameObject.name + "' (its lane is gone)");
            Undo.DestroyObjectImmediate(sp.gameObject);
            orphansRemoved++;
        }

        TrafficSpawner[] existing = Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None);

        int added = 0;

        foreach (TrafficRoute route in routes)
        {
            if (!route.IsValid)
            {
                Log("skipped '" + route.gameObject.name + "': fewer than 2 waypoints");
                continue;
            }

            bool already = false;
            foreach (TrafficSpawner sp in existing)
                if (sp.route == route) { already = true; break; }

            if (already)
            {
                Log("'" + route.gameObject.name + "' already has a spawner");
                continue;
            }

            // Lane length drives how many cars are sensible on it.
            float length = 0f;
            for (int i = 0; i < route.waypoints.Count - 1; i++)
            {
                if (route.waypoints[i] == null || route.waypoints[i + 1] == null) continue;
                length += Vector3.Distance(route.waypoints[i].position, route.waypoints[i + 1].position);
            }

            // Paths made with 'New Traffic Path' all arrive called Lane_New, so
            // disambiguate rather than creating several identically named
            // spawners that nobody can tell apart in the Hierarchy.
            string spawnerName = route.gameObject.name + "_Spawner";
            if (GameObject.Find(spawnerName) != null)
                spawnerName = route.gameObject.name + "_Spawner_" + added.ToString("00");

            GameObject go = new GameObject(spawnerName);
            Undo.RegisterCreatedObjectUndo(go, "Add Spawner");

            go.transform.SetParent(route.transform.parent, true);
            go.transform.position = route.GetPosition(0);

            TrafficSpawner spawner = go.AddComponent<TrafficSpawner>();
            spawner.route = route;
            spawner.spawnWaypoint = 0;
            spawner.carPrefabs = new List<GameObject>(cars);
            spawner.spawnInterval = 5f;
            spawner.intervalJitter = 2f;

            // Capped deliberately: these car prefabs are ~176 GameObjects each,
            // so a long lane asking for 18 cars would cost more than it is worth.
            spawner.maxAlive = Mathf.Clamp(Mathf.RoundToInt(length / 40f), 2, 6);
            spawner.minSpacing = 12f;
            spawner.spawnOnStart = true;

            Log(string.Format("added spawner for '{0}' ({1:0.#} long, maxAlive {2})",
                route.gameObject.name, length, spawner.maxAlive));
            added++;
        }

        if (Object.FindFirstObjectByType<CityObjective>() == null)
        {
            GameObject objGo = new GameObject("CityObjective");
            Undo.RegisterCreatedObjectUndo(objGo, "Add CityObjective");
            objGo.AddComponent<CityObjective>();
            Log("added CityObjective");
        }

        AssetDatabase.SaveAssets();

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Log("orphaned spawners removed: " + orphansRemoved);
        Log("spawners added: " + added + ". Scene saved.");
        Log("DONE");
    }

    // ------------------------------------------------------------------ scene / layers

    static bool EnsureSceneOpen()
    {
        string wanted = System.Environment.GetEnvironmentVariable("OPT_SCENE");
        if (string.IsNullOrEmpty(wanted)) wanted = ScenePath;

        Scene active = SceneManager.GetActiveScene();

        // Never switch scenes on someone working in the editor: act on what is
        // open. Only batch mode, which starts with no scene loaded, falls back
        // to OPT_SCENE. Switching here once discarded a user's unsaved lanes.
        if (!Application.isBatchMode && active.IsValid() && !string.IsNullOrEmpty(active.path))
            return true;

        if (active.IsValid() && active.path == wanted) return true;

        Scene opened = EditorSceneManager.OpenScene(wanted, OpenSceneMode.Single);
        if (opened.IsValid()) return true;

        Fail("Could not open " + wanted);
        return false;
    }

    static void EnsureLayers()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) { Fail("Could not load TagManager.asset"); return; }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        foreach (string wanted in RequiredLayers)
        {
            if (LayerExists(layers, wanted)) { Log("Layer '" + wanted + "' already exists"); continue; }

            bool placed = false;

            for (int i = 8; i < layers.arraySize; i++)   // 0-7 belong to Unity
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = wanted;
                placed = true;
                Log("Created layer '" + wanted + "' at index " + i);
                break;
            }

            if (!placed) Fail("No free layer slot for '" + wanted + "'");
        }

        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    static bool LayerExists(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return true;

        return false;
    }

    // ------------------------------------------------------------------ probing

    /// <summary>
    /// Road plates are identified by SHAPE, not by name. The tiles are prefab
    /// instances whose names live in prefab modifications, so a name-prefix
    /// filter is unreliable; a wide flat slab, on the other hand, is a road.
    /// Buildings are excluded because they are tall.
    /// </summary>
    static List<Collider> CollectRoadColliders()
    {
        List<Collider> result = new List<Collider>();

        Log("active scene: " + SceneManager.GetActiveScene().path);

        GameObject roadsRoot = GameObject.Find(RoadsRootName);

        // Fall back to the whole scene if there is no 'roads' root, so a
        // reorganised hierarchy does not silently yield nothing.
        Collider[] candidates;
        if (roadsRoot != null)
        {
            Log("'" + RoadsRootName + "' found, children: " + roadsRoot.transform.childCount);
            candidates = roadsRoot.GetComponentsInChildren<Collider>(true);
        }
        else
        {
            Log("no '" + RoadsRootName + "' object; scanning the whole scene for road plates");
            candidates = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        }

        Log("collider candidates: " + candidates.Length);

        foreach (Collider c in candidates)
        {
            Bounds b = c.bounds;

            bool flat = b.size.y <= MaxRoadThickness;
            bool wide = b.size.x >= MinRoadFootprint && b.size.z >= MinRoadFootprint;

            if (flat && wide && !c.isTrigger)
            {
                result.Add(c);
                Log(string.Format("  road plate '{0}' centre={1} size={2}",
                    c.gameObject.name, b.center.ToString("0.##"), b.size.ToString("0.##")));
            }
            else
            {
                Log(string.Format("  skipped '{0}' size={1} ({2}{3}{4})",
                    c.gameObject.name, b.size.ToString("0.##"),
                    flat ? "" : "too tall ",
                    wide ? "" : "too small ",
                    c.isTrigger ? "trigger" : ""));
            }
        }

        return result;
    }

    static List<Collider> CollectObstacles(List<Collider> roads)
    {
        HashSet<Collider> roadSet = new HashSet<Collider>(roads);
        List<Collider> obstacles = new List<Collider>();

        foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (roadSet.Contains(c)) continue;
            if (c.isTrigger) continue;
            if (c.bounds.size.y < ObstacleMinHeight) continue;

            obstacles.Add(c);
        }

        return obstacles;
    }

    class Occupancy
    {
        public bool[,] Cells;      // drivable asphalt, nothing standing on it
        public bool[,] HasRoad;    // road tile surface, whatever its height
        public float[,] Heights;
        public float MinRoadY;
        public float MinX, MinZ, Step;
        public int CountX, CountZ;

        public float WorldX(int xi) { return MinX + xi * Step; }
        public float WorldZ(int zi) { return MinZ + zi * Step; }

        public bool At(int xi, int zi)
        {
            if (xi < 0 || zi < 0 || xi >= CountX || zi >= CountZ) return false;
            return Cells[xi, zi];
        }

        public string ToAscii()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format(
                "Drivable map step={0} x:[{1:0.#}..{2:0.#}] z:[{3:0.#}..{4:0.#}]",
                Step, MinX, WorldX(CountX - 1), MinZ, WorldZ(CountZ - 1)));
            sb.AppendLine("'#' drivable asphalt  '=' raised kerb/pavement  'x' blocked by something standing on it  '.' no road");

            for (int zi = CountZ - 1; zi >= 0; zi--)
            {
                sb.Append(string.Format("{0,8:0.#} |", WorldZ(zi)));
                for (int xi = 0; xi < CountX; xi++)
                {
                    if (Cells[xi, zi]) sb.Append('#');
                    else if (!HasRoad[xi, zi]) sb.Append('.');
                    else if (Heights[xi, zi] > MinRoadY + AsphaltTolerance) sb.Append('=');
                    else sb.Append('x');
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Height distribution of the road surface. Asphalt and pavement show up as
    /// separate clusters, which is what makes the height split usable.
    /// </summary>
    static string HeightHistogram(Occupancy occ)
    {
        Dictionary<int, int> buckets = new Dictionary<int, int>();
        int total = 0;

        for (int xi = 0; xi < occ.CountX; xi++)
            for (int zi = 0; zi < occ.CountZ; zi++)
            {
                if (!occ.HasRoad[xi, zi]) continue;

                int key = Mathf.RoundToInt(occ.Heights[xi, zi] * 100f);
                buckets[key] = buckets.TryGetValue(key, out int c) ? c + 1 : 1;
                total++;
            }

        List<int> keys = new List<int>(buckets.Keys);
        keys.Sort();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Road surface height histogram (" + total + " cells, lowest = " +
                      occ.MinRoadY.ToString("0.###") + "):");

        foreach (int k in keys)
        {
            float y = k / 100f;
            bool asphalt = y <= occ.MinRoadY + AsphaltTolerance;
            int bar = Mathf.Max(1, Mathf.RoundToInt(buckets[k] * 50f / Mathf.Max(1, total)));

            sb.AppendLine(string.Format("  y={0,6:0.###} {1,6} cells {2,-5} {3}",
                y, buckets[k], asphalt ? "ROAD" : "raised", new string('#', bar)));
        }

        return sb.ToString();
    }

    static Occupancy Probe(List<Collider> roads)
    {
        List<Collider> obstacles = CollectObstacles(roads);

        Bounds b = roads[0].bounds;
        for (int i = 1; i < roads.Count; i++) b.Encapsulate(roads[i].bounds);

        Occupancy occ = new Occupancy();
        occ.Step = ProbeStep;
        occ.MinX = b.min.x;
        occ.MinZ = b.min.z;
        occ.CountX = Mathf.Max(1, Mathf.CeilToInt(b.size.x / ProbeStep) + 1);
        occ.CountZ = Mathf.Max(1, Mathf.CeilToInt(b.size.z / ProbeStep) + 1);
        occ.Cells = new bool[occ.CountX, occ.CountZ];
        occ.HasRoad = new bool[occ.CountX, occ.CountZ];
        occ.Heights = new float[occ.CountX, occ.CountZ];

        // Pass 1: where is road surface, and how low does it get?
        float minY = float.MaxValue;

        for (int xi = 0; xi < occ.CountX; xi++)
        {
            for (int zi = 0; zi < occ.CountZ; zi++)
            {
                float y;
                if (!SampleRoad(roads, occ.WorldX(xi), occ.WorldZ(zi), out y)) continue;

                occ.HasRoad[xi, zi] = true;
                occ.Heights[xi, zi] = y;
                if (y < minY) minY = y;
            }
        }

        occ.MinRoadY = minY == float.MaxValue ? 0f : minY;

        // Pass 2: drivable = asphalt (not a raised kerb) and nothing on it.
        for (int xi = 0; xi < occ.CountX; xi++)
        {
            for (int zi = 0; zi < occ.CountZ; zi++)
            {
                if (!occ.HasRoad[xi, zi]) continue;

                float wx = occ.WorldX(xi);
                float wz = occ.WorldZ(zi);
                float y = occ.Heights[xi, zi];

                bool asphalt = y <= occ.MinRoadY + AsphaltTolerance;
                occ.Cells[xi, zi] = asphalt && !Blocked(obstacles, wx, y, wz);
            }
        }

        return occ;
    }

    static bool Blocked(List<Collider> obstacles, float x, float roadY, float z)
    {
        Vector3 probe = new Vector3(x, roadY + ObstacleClearance, z);

        // Bounds test is conservative on purpose: it keeps lanes a little clear
        // of walls rather than scraping along them.
        for (int i = 0; i < obstacles.Count; i++)
            if (obstacles[i].bounds.Contains(probe)) return true;

        return false;
    }

    static bool SampleRoad(List<Collider> roads, float x, float z, out float y)
    {
        Ray ray = new Ray(new Vector3(x, ProbeTop, z), Vector3.down);
        bool found = false;
        y = 0f;

        for (int i = 0; i < roads.Count; i++)
        {
            RaycastHit hit;
            if (!roads[i].Raycast(ray, out hit, ProbeTop * 2f)) continue;

            if (!found || hit.point.y > y) { y = hit.point.y; found = true; }
        }

        return found;
    }

    // ------------------------------------------------------------------ lane fitting

    class Lane
    {
        public string Name;
        public List<Vector3> Points = new List<Vector3>();
        public float Length;
        public bool Horizontal;
        public bool TwoWay;

        public string Describe()
        {
            return string.Format("{0}: {1} wp, {2:0.#} long, axis={3}, {4}, {5} -> {6}",
                Name, Points.Count, Length, Horizontal ? "X" : "Z",
                TwoWay ? "two-way" : "one-way",
                Points.Count > 0 ? Points[0].ToString("0.#") : "-",
                Points.Count > 0 ? Points[Points.Count - 1].ToString("0.#") : "-");
        }
    }

    static List<Lane> FitLanes(Occupancy occ)
    {
        List<Lane> lanes = new List<Lane>();

        int[,] area = new int[occ.CountX, occ.CountZ];
        int areaCount = 0;
        Stack<Vector2Int> stack = new Stack<Vector2Int>();

        for (int xi = 0; xi < occ.CountX; xi++)
        {
            for (int zi = 0; zi < occ.CountZ; zi++)
            {
                if (!occ.At(xi, zi) || area[xi, zi] != 0) continue;

                areaCount++;
                area[xi, zi] = areaCount;
                stack.Push(new Vector2Int(xi, zi));

                while (stack.Count > 0)
                {
                    Vector2Int p = stack.Pop();

                    TryFlood(occ, area, p.x + 1, p.y, areaCount, stack);
                    TryFlood(occ, area, p.x - 1, p.y, areaCount, stack);
                    TryFlood(occ, area, p.x, p.y + 1, areaCount, stack);
                    TryFlood(occ, area, p.x, p.y - 1, areaCount, stack);
                }
            }
        }

        Log("Connected road areas: " + areaCount);

        for (int id = 1; id <= areaCount; id++)
        {
            int rx0, rz0, rx1, rz1;
            if (!LargestRectangle(occ, area, id, out rx0, out rz0, out rx1, out rz1)) continue;

            float worldX0 = occ.WorldX(rx0);
            float worldX1 = occ.WorldX(rx1);
            float worldZ0 = occ.WorldZ(rz0);
            float worldZ1 = occ.WorldZ(rz1);

            float sizeX = worldX1 - worldX0;
            float sizeZ = worldZ1 - worldZ0;

            bool horizontal = sizeX >= sizeZ;
            float along = horizontal ? sizeX : sizeZ;
            float across = horizontal ? sizeZ : sizeX;

            Log(string.Format(
                "area {0}: largest clear rect x[{1:0.#}..{2:0.#}] z[{3:0.#}..{4:0.#}] = {5:0.#} x {6:0.#}",
                id, worldX0, worldX1, worldZ0, worldZ1, sizeX, sizeZ));

            if (along < MinLaneLength)
            {
                Log("  skipped: longest clear run is only " + along.ToString("0.#"));
                continue;
            }

            float centre = horizontal ? (worldZ0 + worldZ1) * 0.5f : (worldX0 + worldX1) * 0.5f;
            float from = (horizontal ? worldX0 : worldZ0) + EndInset;
            float to = (horizontal ? worldX1 : worldZ1) - EndInset;

            int hx = Mathf.Clamp((rx0 + rx1) / 2, 0, occ.CountX - 1);
            int hz = Mathf.Clamp((rz0 + rz1) / 2, 0, occ.CountZ - 1);
            float y = occ.Heights[hx, hz];

            bool twoWay = across >= MinTwoWayWidth;
            float offset = twoWay ? across * LaneOffsetFraction : 0f;
            int directions = twoWay ? 2 : 1;

            for (int d = 0; d < directions; d++)
            {
                bool forward = d == 0;

                Lane lane = new Lane();
                lane.Name = string.Format("Lane_{0}{1}{2}",
                    horizontal ? "EW" : "NS", id, twoWay ? (forward ? "_A" : "_B") : "");
                lane.Horizontal = horizontal;
                lane.TwoWay = twoWay;
                lane.Length = to - from;

                // Right-hand traffic: the lane sits to the right of travel.
                // right(+X) = -Z, right(-X) = +Z, right(+Z) = +X, right(-Z) = -X
                float lateral = horizontal
                    ? centre + (forward ? -offset : offset)
                    : centre + (forward ? offset : -offset);

                int steps = Mathf.Max(1, Mathf.FloorToInt((to - from) / WaypointSpacing));

                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float pos = forward ? Mathf.Lerp(from, to, t) : Mathf.Lerp(to, from, t);

                    lane.Points.Add(horizontal
                        ? new Vector3(pos, y, lateral)
                        : new Vector3(lateral, y, pos));
                }

                lanes.Add(lane);
            }
        }

        return lanes;
    }

    static void TryFlood(Occupancy occ, int[,] area, int xi, int zi, int id, Stack<Vector2Int> stack)
    {
        if (xi < 0 || zi < 0 || xi >= occ.CountX || zi >= occ.CountZ) return;
        if (!occ.Cells[xi, zi] || area[xi, zi] != 0) return;

        area[xi, zi] = id;
        stack.Push(new Vector2Int(xi, zi));
    }

    /// <summary>
    /// Largest all-clear axis-aligned rectangle inside one connected area.
    /// Classic largest-rectangle-in-histogram, run row by row.
    /// </summary>
    static bool LargestRectangle(Occupancy occ, int[,] area, int id,
        out int bx0, out int bz0, out int bx1, out int bz1)
    {
        bx0 = bz0 = bx1 = bz1 = 0;
        int best = 0;

        int[] heights = new int[occ.CountX];

        for (int zi = 0; zi < occ.CountZ; zi++)
        {
            for (int xi = 0; xi < occ.CountX; xi++)
                heights[xi] = (occ.Cells[xi, zi] && area[xi, zi] == id) ? heights[xi] + 1 : 0;

            Stack<int> stack = new Stack<int>();

            for (int xi = 0; xi <= occ.CountX; xi++)
            {
                int h = xi < occ.CountX ? heights[xi] : 0;

                while (stack.Count > 0 && heights[stack.Peek()] > h)
                {
                    int top = stack.Pop();
                    int height = heights[top];
                    int left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                    int width = xi - left;
                    int cells = height * width;

                    if (cells > best && height > 0 && width > 0)
                    {
                        best = cells;
                        bx0 = left;
                        bx1 = xi - 1;
                        bz0 = zi - height + 1;
                        bz1 = zi;
                    }
                }

                stack.Push(xi);
            }
        }

        return best > 0;
    }

    // ------------------------------------------------------------------ car prefabs

    static List<GameObject> BuildCarPrefabs()
    {
        List<GameObject> built = new List<GameObject>();

        if (!AssetDatabase.IsValidFolder(CarPrefabFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            AssetDatabase.CreateFolder("Assets/Prefabs", "Traffic");
        }

        int vehicleLayer = LayerMask.NameToLayer("Vehicle");

        foreach (string src in CarSources)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (source == null) { Log("Missing car source: " + src); continue; }

            string outPath = CarPrefabFolder + "/Traffic_" + source.name + ".prefab";

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (inst == null) continue;

            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot,
                InteractionMode.AutomatedAction);

            inst.name = "Traffic_" + source.name;
            SetLayerRecursive(inst, vehicleLayer);

            Bounds local = LocalRendererBounds(inst);
            Log(string.Format("  {0}: bounds centre={1} size={2} minY={3:0.###}",
                source.name, local.center.ToString("0.##"), local.size.ToString("0.##"), local.min.y));

            BoxCollider box = inst.GetComponent<BoxCollider>();
            if (box == null) box = inst.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;

            Rigidbody rb = inst.GetComponent<Rigidbody>();
            if (rb == null) rb = inst.AddComponent<Rigidbody>();
            rb.mass = 1500f;
            rb.isKinematic = true;

            CarController car = inst.GetComponent<CarController>();
            if (car == null) car = inst.AddComponent<CarController>();
            car.maxSpeed = 11f;
            car.sensorHeight = Mathf.Max(0.4f, local.size.y * 0.5f);
            car.sensorRadius = Mathf.Max(0.8f, local.size.x * 0.45f);
            car.despawnAtEnd = true;

            CrushableObject crush = inst.GetComponent<CrushableObject>();
            if (crush == null) crush = inst.AddComponent<CrushableObject>();
            crush.displayName = source.name;
            crush.canBeDestroyed = true;
            crush.countsAsPropertyDamage = true;
            crush.propertyValue = 25f;
            crush.crushResistance = 8000f;
            crush.structuralIntegrity = 60f;
            crush.canExplode = true;
            crush.explodeForceThreshold = 50000f;

            if (inst.GetComponent<CrushOnImpact>() == null) inst.AddComponent<CrushOnImpact>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, outPath);
            Object.DestroyImmediate(inst);

            if (saved != null)
            {
                built.Add(saved);
                Log("Car prefab: " + outPath);
            }
        }

        return built;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;

        go.layer = layer;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    static Bounds LocalRendererBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

        Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
        bool init = false;
        Bounds result = new Bounds();

        foreach (Renderer r in rends)
        {
            Bounds w = r.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? w.min.x : w.max.x,
                    (i & 2) == 0 ? w.min.y : w.max.y,
                    (i & 4) == 0 ? w.min.z : w.max.z);

                Vector3 p = toLocal.MultiplyPoint3x4(corner);

                if (!init) { result = new Bounds(p, Vector3.zero); init = true; }
                else result.Encapsulate(p);
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ logging

    static void ResetLog(string title)
    {
        try { System.IO.File.WriteAllText(LogPath, "=== TrafficHarness: " + title + " ===\n"); }
        catch (System.Exception e) { Debug.LogWarning("[TrafficHarness] log init: " + e.Message); }
    }

    static void Log(string msg)
    {
        Debug.Log("[TrafficHarness] " + msg);
        try { System.IO.File.AppendAllText(LogPath, msg + "\n"); } catch { }
    }

    static void Fail(string msg)
    {
        Debug.LogError("[TrafficHarness] " + msg);
        try { System.IO.File.AppendAllText(LogPath, "ERROR: " + msg + "\n"); } catch { }
    }
}
