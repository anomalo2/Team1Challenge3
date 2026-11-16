using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies this round of fixes to the open scene (JohnTesting in batch mode):
///   1. props   - rebuilds the empty breakable holders so props are real
///                rigid bodies the mech can knock over and break
///   2. people  - swaps the old TrafficRoute-based crossing for the
///                PedestrianCrossings prefab with four crossings around the
///                four-way intersection, walked one Grandma at a time
///   3. bounds  - invisible walls around the intersection and a kill box
///                under the map
/// Each step is safe to run again.
/// </summary>
public static class ChallengeSceneSetup
{
    // The four-way intersection, read off a top-down capture of JohnTesting.
    static readonly Vector3 AreaCentre = new Vector3(-131f, 0f, -4f);
    static readonly Vector3 AreaSize = new Vector3(70f, 60f, 70f);

    // One loop around the block: each crossing starts on the corner where the
    // previous one ended. (start x, start z, end x, end z)
    static readonly float[][] Crossings =
    {
        new[] { -141f, -11.5f, -141f,   2.5f },  // west leg,  south -> north (zebra)
        new[] { -139f,   5.5f, -123.5f, 5.5f },  // north leg, west -> east  (zebra)
        new[] { -118f,   2.5f, -118f, -11f   },  // east leg,  north -> south (clear of the mech's start)
        new[] { -124.8f, -12.3f, -139f, -12.3f }, // south leg, east -> west (starts on the kerb, not the
                                                  // parapet over the sunken road just south-east of it)
    };

    [MenuItem("Tools/Challenge/Apply Scene Setup (props, pedestrians, bounds)")]
    public static void Menu()
    {
        if (!EditorUtility.DisplayDialog("Apply scene setup",
                "Repairs breakable props, replaces the crossing setup with the pedestrian prefabs, " +
                "and adds play-area walls and a kill box to the open scene.", "Apply", "Cancel")) return;
        Apply(SceneManager.GetActiveScene());
    }

    public static void Batch()
    {
        string wanted = System.Environment.GetEnvironmentVariable("OPT_SCENE");
        if (string.IsNullOrEmpty(wanted)) wanted = "Assets/Scenes/JohnTesting.unity";
        Apply(EditorSceneManager.OpenScene(wanted, OpenSceneMode.Single));
    }

    static void Apply(Scene scene)
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Challenge scene setup: " + scene.path + " ===");

        // ---- prefabs first: the props want the dust effect, the spawner wants Grandma
        PedestrianTools.RebuildPrefabs();

        // ---- 1. props
        PropClusterBuilder.BreakEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PedestrianTools.DustPath);
        PropClusterBuilder.RepairEmptyHolders();
        int assemblies = 0, parts = 0;
        foreach (PropAssembly pa in Object.FindObjectsByType<PropAssembly>(FindObjectsSortMode.None))
        {
            assemblies++;
            parts += pa.parts.Count;
            if (pa.breakEffect == null) { pa.breakEffect = PropClusterBuilder.BreakEffectPrefab; EditorUtility.SetDirty(pa); }
        }
        log.AppendLine("props: " + assemblies + " PropAssembly holders, " + parts + " parts");
        SleepSetup.SetupBatch();

        // ---- 2. pedestrians: remove the old setup
        int removed = 0;
        foreach (PedestrianCrosser p in Object.FindObjectsByType<PedestrianCrosser>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { Undo.DestroyObjectImmediate(p.gameObject); removed++; }
        foreach (CrosswalkChallenge c in Object.FindObjectsByType<CrosswalkChallenge>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { Undo.DestroyObjectImmediate(c.gameObject); removed++; }
        foreach (TrafficRoute r in Object.FindObjectsByType<TrafficRoute>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (!n.Contains("crossing") && !n.Contains("pedestrian")) continue;
            foreach (TrafficSpawner sp in Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None))
                if (sp.route == r) Undo.DestroyObjectImmediate(sp.gameObject);
            Undo.DestroyObjectImmediate(r.gameObject);
            removed++;
        }
        foreach (PedestrianSpawner s in Object.FindObjectsByType<PedestrianSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { Undo.DestroyObjectImmediate(s.gameObject); removed++; }
        log.AppendLine("removed old pedestrian objects: " + removed);

        // ---- 2b. new setup from the prefab, like a person would drag it in
        GameObject spawnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PedestrianTools.SpawnerPrefabPath);
        GameObject spawnerGo = (GameObject)PrefabUtility.InstantiatePrefab(spawnerPrefab, scene);
        spawnerGo.transform.position = PedestrianTools.GroundAt(AreaCentre);
        PedestrianSpawner spawner = spawnerGo.GetComponent<PedestrianSpawner>();

        List<PedestrianPath> existing = new List<PedestrianPath>(spawnerGo.GetComponentsInChildren<PedestrianPath>());
        for (int i = 0; i < Crossings.Length; i++)
        {
            float[] c = Crossings[i];
            PedestrianPath path = i < existing.Count ? existing[i] : PedestrianTools.AddPathTo(spawner, AreaCentre);
            path.name = "Crossing " + (i + 1);
            path.Refresh();
            path.transform.position = PedestrianTools.GroundAt(new Vector3(c[0], 0f, c[1]));
            path.points[0].position = PedestrianTools.GroundAt(new Vector3(c[0], 2f, c[1]));
            path.points[path.points.Count - 1].position = PedestrianTools.GroundAt(new Vector3(c[2], 2f, c[3]));
            log.AppendLine(string.Format("  {0}: {1} -> {2} ({3:0.#} m)", path.name,
                path.StartPosition.ToString("0.0"), path.EndPosition.ToString("0.0"), path.Length));
        }

        // ---- 3. bounds
        Replace<PlayAreaBounds>("PlayArea");
        GameObject area = new GameObject("PlayArea");
        area.transform.position = AreaCentre;
        PlayAreaBounds bounds = area.AddComponent<PlayAreaBounds>();
        bounds.size = AreaSize;
        log.AppendLine("play area: centre " + AreaCentre + " size " + AreaSize);

        Replace<KillZone>("KillZone");
        GameObject kill = new GameObject("KillZone");
        kill.transform.position = new Vector3(AreaCentre.x, -40f, AreaCentre.z);
        BoxCollider kb = kill.AddComponent<BoxCollider>();
        kb.size = new Vector3(1500f, 20f, 1500f);
        kb.isTrigger = true;
        kill.AddComponent<KillZone>();
        log.AppendLine("kill zone: top at y " + (kill.transform.position.y + 10f));

        // ---- 4. mech: HumanToMechBoneMirror's 'gaze' gate is optional (null =
        // always mirror). In this scene the arms must track all the time, so
        // make sure it is empty - with it set, looking down at the mech's
        // hands froze them.
        int cleared = 0;
        foreach (HumanToMechBoneMirror m in Object.FindObjectsByType<HumanToMechBoneMirror>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(m);
            SerializedProperty gaze = so.FindProperty("gaze");
            if (gaze == null || gaze.objectReferenceValue == null) continue;
            gaze.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            cleared++;
        }
        log.AppendLine("bone mirrors set to always mirror (gaze gate cleared): " + cleared);

        // ---- 4b. the sleep system must never own the mech's bodies. An earlier
        // run gave the mech's physics hands a SleepableBody; on any touch it made
        // them dynamic, so they flailed and smashed props from the first frame.
        int stripped = 0;
        foreach (SleepableBody sb in Object.FindObjectsByType<SleepableBody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sb.GetComponent<RigidbodyFollowBone>() == null && sb.GetComponentInParent<MechLocomotion>() == null
                && sb.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() == null) continue;
            Rigidbody body = sb.GetComponent<Rigidbody>();
            Undo.DestroyObjectImmediate(sb);
            if (body != null) body.isKinematic = true;
            stripped++;
        }
        log.AppendLine("SleepableBody removed from mech/rig bodies: " + stripped);

        // ---- 5. view range: see further down the streets
        foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cam.farClipPlane >= FarClip) continue;
            log.AppendLine(string.Format("camera '{0}' far clip {1:0} -> {2:0}", cam.name, cam.farClipPlane, FarClip));
            Undo.RecordObject(cam, "Far clip");
            cam.farClipPlane = FarClip;
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("scene saved");

        Debug.Log("[ChallengeSetup]\n" + log);
        System.IO.File.WriteAllText("ChallengeSetup.log", log.ToString());
    }

    // The whole city fits inside ~600 m of the intersection.
    const float FarClip = 1000f;

    static void Replace<T>(string name) where T : Component
    {
        foreach (T t in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(t.gameObject);
    }
}
