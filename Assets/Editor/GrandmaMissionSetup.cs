using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up the help-her-across mission.
///
/// The crossing path is an ordinary TrafficRoute, so it is drawn with the same
/// click-in-the-scene tool as the traffic lanes -- one tool, one mental model,
/// and the route editor already handles snapping, reversing and resampling.
///
/// Order of work:
///   1  New Pedestrian Path   draw her route, kerb to far pavement
///   2  select the character and the path
///   3  Set Up Grandma Mission   wires the pedestrian, the challenge and scoring
/// </summary>
public static class GrandmaMissionSetup
{
    const string LogPath = "GrandmaMission.log";

    [MenuItem("Tools/Mission/1 - New Pedestrian Path")]
    public static void NewPedestrianPath()
    {
        GameObject go = new GameObject("CrossingPath");

        TrafficRoute route = go.AddComponent<TrafficRoute>();
        route.loop = false;                       // a crossing never loops
        route.waypoints = new List<Transform>();
        route.gizmoColor = new Color(1f, 0.85f, 0.2f);

        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(go, "New Pedestrian Path");
        Selection.activeGameObject = go;

        Debug.Log("[Mission] CrossingPath created. In the Inspector press Start Drawing, " +
                  "then click from her kerb to the far pavement. 3-5 points is plenty.");
    }

    [MenuItem("Tools/Mission/2 - Set Up Grandma Mission")]
    public static void SetUpMission()
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Grandma mission setup ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        // ---- find the pieces
        GameObject character = null;
        TrafficRoute path = null;
        StringBuilder rejected = new StringBuilder();

        foreach (GameObject go in Selection.gameObjects)
        {
            TrafficRoute r = go.GetComponent<TrafficRoute>();

            if (r != null && path == null) { path = r; continue; }

            string why = WhyNotACharacter(go);

            if (why == null)
            {
                if (character == null) character = go;
            }
            else rejected.AppendLine("  " + go.name + " - " + why);
        }

        if (path == null)
        {
            foreach (TrafficRoute r in Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None))
                if (r.gameObject.name.ToLowerInvariant().Contains("crossing")) { path = r; break; }
        }

        if (character == null || path == null)
        {
            string msg = "Select the CHARACTER and her CrossingPath, then run this again.\n\n" +
                         "character: " + (character != null ? character.name : "not found") + "\n" +
                         "path: " + (path != null ? path.gameObject.name : "not found");

            if (rejected.Length > 0)
                msg += "\n\nRejected from the selection:\n" + rejected;

            msg += "\n\nA character needs a SkinnedMeshRenderer or an Animator. " +
                   "Use 'Tools > Mission > 0 - Create Grandma' if you have not placed one.";

            EditorUtility.DisplayDialog("Grandma mission", msg, "OK");
            log.AppendLine("ABORTED - " + msg.Replace("\n", " "));
            Finish(log);
            return;
        }

        if (!path.IsValid)
        {
            EditorUtility.DisplayDialog("Grandma mission",
                "The crossing path needs at least 2 waypoints. Draw it first.", "OK");
            log.AppendLine("ABORTED - path has fewer than 2 waypoints");
            Finish(log);
            return;
        }

        log.AppendLine("character: " + character.name);
        log.AppendLine("path: " + path.gameObject.name + " (" + path.Count + " waypoints)");

        Undo.RegisterFullObjectHierarchyUndo(character, "Set Up Grandma Mission");

        int civilianLayer = LayerMask.NameToLayer("Civilian");
        int vehicleLayer = LayerMask.NameToLayer("Vehicle");

        if (civilianLayer >= 0)
        {
            character.layer = civilianLayer;
            foreach (Transform t in character.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = civilianLayer;
        }

        // ---- body + collider
        Rigidbody rb = character.GetComponent<Rigidbody>();
        if (rb == null) rb = character.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.isKinematic = true;          // she walks by transform until knocked down

        if (character.GetComponent<Collider>() == null)
        {
            Bounds b = LocalBounds(character);

            CapsuleCollider cap = character.AddComponent<CapsuleCollider>();
            cap.center = b.center;
            cap.height = Mathf.Max(1f, b.size.y);
            cap.radius = Mathf.Max(0.2f, Mathf.Min(b.size.x, b.size.z) * 0.5f);
        }

        // ---- she can be picked up, but never destroyed
        CrushableObject crush = character.GetComponent<CrushableObject>();
        if (crush == null) crush = character.AddComponent<CrushableObject>();

        crush.displayName = "Elderly pedestrian";
        crush.canBeGrabbed = true;
        crush.canBeDestroyed = false;        // enforced again in PedestrianCrosser
        crush.canExplode = false;
        crush.countsAsPropertyDamage = false;
        crush.crushResistance = 400f;
        crush.structuralIntegrity = 8f;
        crush.sustainedCrushRate = 0.02f;
        crush.squashWhenCrushed = false;     // never deform a person

        // ---- the crossing behaviour
        PedestrianCrosser crosser = character.GetComponent<PedestrianCrosser>();
        if (crosser == null) crosser = character.AddComponent<PedestrianCrosser>();

        crosser.crossRoute = path;
        crosser.walkSpeed = 0.9f;
        crosser.retreatSpeed = 1.6f;
        crosser.waitsForGap = true;
        crosser.safeGapDistance = 18f;
        crosser.patience = 12f;
        crosser.freezesWhenCarNear = true;
        crosser.panicDistance = 8f;
        crosser.knockdownSpeed = 4f;
        crosser.knockdownDuration = 2.5f;
        crosser.useRagdoll = true;

        if (vehicleLayer >= 0) crosser.vehicleLayers = 1 << vehicleLayer;

        // start her at the kerb, facing across
        character.transform.position = path.GetPosition(0);

        Vector3 facing = path.GetPosition(1) - path.GetPosition(0);
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f)
            character.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

        bool hasRagdoll = character.GetComponentsInChildren<Rigidbody>(true).Length > 1;
        log.AppendLine("ragdoll rig: " + (hasRagdoll ? "found" : "NONE - she will tumble as one body"));

        // ---- the objective wrapper
        GameObject challengeGo = GameObject.Find("CrosswalkChallenge");
        if (challengeGo == null)
        {
            challengeGo = new GameObject("CrosswalkChallenge");
            challengeGo.transform.position = path.GetPosition(0);
            Undo.RegisterCreatedObjectUndo(challengeGo, "Create CrosswalkChallenge");
        }

        CrosswalkChallenge challenge = challengeGo.GetComponent<CrosswalkChallenge>();
        if (challenge == null) challenge = challengeGo.AddComponent<CrosswalkChallenge>();

        challenge.pedestrian = crosser;
        challenge.armRadius = 30f;
        challenge.maxScares = 3;
        challenge.timeLimit = 0f;

        GameObject centre = GameObject.Find("Center Point");
        if (centre != null && challenge.player == null) challenge.player = centre.transform;

        challenge.trafficDuringChallenge = new List<TrafficSpawner>(
            Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None));

        log.AppendLine("challenge armed at " + challenge.armRadius +
                       ", traffic spawners wired: " + challenge.trafficDuringChallenge.Count);

        if (Object.FindFirstObjectByType<CityObjective>() == null)
        {
            GameObject objGo = new GameObject("CityObjective");
            objGo.AddComponent<CityObjective>();
            Undo.RegisterCreatedObjectUndo(objGo, "Create CityObjective");
            log.AppendLine("added CityObjective");
        }

        EditorUtility.SetDirty(character);
        EditorSceneManagerMarkDirty();

        log.AppendLine("DONE");
        Finish(log);

        if (Application.isBatchMode) return;

        EditorUtility.DisplayDialog("Grandma mission",
            "Set up on '" + character.name + "'.\n\n" +
            "Ragdoll rig: " + (hasRagdoll ? "found" : "none - she tumbles as one body") + "\n\n" +
            "She waits for a gap, steps out after 12s regardless, and on a hard hit " +
            "ragdolls then blinks back to the start.", "OK");
    }

    /// <summary>
    /// Why this object is not a person, or null if it is one.
    ///
    /// Guessing "anything with a renderer" once turned a parked car into the
    /// grandmother. A character has a skinned mesh or an animator; a vehicle,
    /// a labelled prop or a lane marker is never the right answer.
    /// </summary>
    static string WhyNotACharacter(GameObject go)
    {
        if (go.GetComponent<CarController>() != null
            || go.GetComponentInParent<CarController>() != null)
            return "it is a vehicle (has CarController)";

        string n = go.name.ToLowerInvariant();
        if (n.StartsWith("mf_") || n.StartsWith("traffic_mf_"))
            return "it is a MetroFleet vehicle";

        if (go.GetComponent<PropLabel>() != null)
            return "it is labelled scenery, not a person";

        if (go.GetComponent<TrafficSpawner>() != null)
            return "it is a traffic spawner";

        bool skinned = go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
        bool animated = go.GetComponentInChildren<Animator>(true) != null;

        if (!skinned && !animated)
            return "no SkinnedMeshRenderer or Animator, so it is not a character rig";

        return null;
    }

    [MenuItem("Tools/Mission/0 - Create Grandma")]
    public static void CreateGrandma()
    {
        string[] candidates =
        {
            "Assets/CharacterAssets/Eve By J.Gonzales.fbx",
            "Assets/CharacterAssets/Dreyar By M.Aure (1).fbx",
        };

        GameObject source = null;
        foreach (string path in candidates)
        {
            source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source != null) break;
        }

        if (source == null)
        {
            EditorUtility.DisplayDialog("Create Grandma",
                "No character FBX found in Assets/CharacterAssets.", "OK");
            return;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (go == null) return;

        go.name = "Grandma";

        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(go, "Create Grandma");
        Selection.activeGameObject = go;

        Debug.Log("[Mission] Created 'Grandma' from " + source.name +
                  ". Now select her AND the CrossingPath, then run step 2.");
    }

    /// <summary>
    /// Whole mission in one batch call: crossing path, character, wiring.
    /// Used when there is no editor session to select things in.
    /// </summary>
    public static void FullAutoBatch()
    {
        string wanted = System.Environment.GetEnvironmentVariable("OPT_SCENE");
        if (string.IsNullOrEmpty(wanted)) wanted = "Assets/Scenes/JohnTesting.unity";

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || active.path != wanted)
            active = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                wanted, UnityEditor.SceneManagement.OpenSceneMode.Single);

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Grandma mission: full auto ===");
        log.AppendLine("scene: " + active.path);

        // 1. a crossing to walk
        TrafficRoute path = FindCrossingRoute();

        if (path == null)
        {
            path = BuildCrossingAcrossNearestLane(log);
            if (path == null) { log.AppendLine("ERROR: could not place a crossing"); Finish(log); return; }
        }
        else log.AppendLine("using existing crossing: " + path.gameObject.name);

        // 2. the character
        OldManSetup.SetupBatch();

        GameObject character = GameObject.Find("Grandma");
        if (character == null) { log.AppendLine("ERROR: no Grandma in the scene"); Finish(log); return; }

        // 3. wiring - reuse the interactive path by driving the selection,
        //    which works in batch mode too
        Selection.objects = new Object[] { character, path.gameObject };
        log.AppendLine("selected: " + character.name + " + " + path.gameObject.name);

        SetUpMission();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(active);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);
        log.AppendLine("scene saved");

        Finish(log);
    }

    static TrafficRoute FindCrossingRoute()
    {
        foreach (TrafficRoute r in Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("crossing") || n.Contains("pedestrian")) return r;
        }

        return null;
    }

    /// <summary>
    /// Lays a crossing at right angles over the traffic lane nearest the
    /// Center Point, which is where the player will be. Waypoints are dropped
    /// onto the real road surface so she does not walk through it.
    /// </summary>
    static TrafficRoute BuildCrossingAcrossNearestLane(StringBuilder log)
    {
        GameObject centreGo = GameObject.Find("Center Point");
        Vector3 centre = centreGo != null ? centreGo.transform.position : Vector3.zero;

        TrafficRoute bestLane = null;
        int bestIndex = 0;
        float bestDistance = float.MaxValue;

        foreach (TrafficRoute r in Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None))
        {
            if (!r.IsValid) continue;

            for (int i = 0; i < r.Count; i++)
            {
                float d = Vector3.Distance(centre, r.GetPosition(i));
                if (d >= bestDistance) continue;

                bestDistance = d;
                bestLane = r;
                bestIndex = i;
            }
        }

        if (bestLane == null) { log.AppendLine("no traffic lane to cross"); return null; }

        Vector3 on = bestLane.GetPosition(bestIndex);

        int other = bestIndex + 1 < bestLane.Count ? bestIndex + 1 : Mathf.Max(0, bestIndex - 1);
        Vector3 along = bestLane.GetPosition(other) - on;
        along.y = 0f;

        if (along.sqrMagnitude < 0.0001f) along = Vector3.forward;
        along.Normalize();

        Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

        log.AppendLine(string.Format("crossing '{0}' near waypoint {1} ({2:0.#} from Center Point)",
            bestLane.gameObject.name, bestIndex, bestDistance));

        GameObject go = new GameObject("CrossingPath");
        TrafficRoute route = go.AddComponent<TrafficRoute>();
        route.loop = false;
        route.gizmoColor = new Color(1f, 0.85f, 0.2f);
        route.waypoints = new List<Transform>();

        const float halfWidth = 11f;
        const int steps = 4;

        for (int i = 0; i <= steps; i++)
        {
            float t = Mathf.Lerp(-halfWidth, halfWidth, i / (float)steps);
            Vector3 point = on + across * t;

            // Drop onto whatever surface is actually there.
            RaycastHit hit;
            if (Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out hit, 80f))
                point.y = hit.point.y;
            else
                point.y = on.y;

            GameObject wp = new GameObject("wp" + i.ToString("00"));
            wp.transform.SetParent(go.transform, true);
            wp.transform.position = point;
            route.waypoints.Add(wp.transform);
        }

        log.AppendLine("built CrossingPath: " + route.Count + " waypoints, "
                       + (halfWidth * 2f) + "m wide");

        return route;
    }

    static void EditorSceneManagerMarkDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            SceneManager.GetActiveScene());
    }

    static Bounds LocalBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(Vector3.up, Vector3.one);

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

    static void Finish(StringBuilder log)
    {
        Debug.Log("[Mission]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
