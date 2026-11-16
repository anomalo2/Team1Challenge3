using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts every physics object in the scene under the sleep system, and makes
/// sure a director exists to drive it.
///
/// Applies to scene props and to the traffic car prefabs, so a parked car that
/// gets shoved stops being traffic and starts being physics.
/// </summary>
public static class SleepSetup
{
    const string LogPath = "SleepSetup.log";
    const string CarPrefabFolder = "Assets/Prefabs/Traffic";

    [MenuItem("Tools/Physics/Set Up Sleep System")]
    public static void Setup() { Run(); }
    public static void SetupBatch() { Run(); }

    static void Run()
    {
        Scene active = SceneManager.GetActiveScene();

        bool interactive = !Application.isBatchMode
                           && active.IsValid() && !string.IsNullOrEmpty(active.path);

        if (!interactive)
        {
            string wanted = System.Environment.GetEnvironmentVariable("OPT_SCENE");
            if (string.IsNullOrEmpty(wanted)) wanted = "Assets/Scenes/JohnTesting.unity";

            if (!active.IsValid() || active.path != wanted)
                active = EditorSceneManager.OpenScene(wanted, OpenSceneMode.Single);
        }

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Sleep system setup ===");
        log.AppendLine("scene: " + active.path);

        float radius = 90f;
        string envR = System.Environment.GetEnvironmentVariable("OPT_RADIUS");
        if (!string.IsNullOrEmpty(envR))
        {
            float v;
            // Stay inside the radius that actually has collision, or a woken
            // body drops through the world.
            if (float.TryParse(envR, out v)) radius = Mathf.Max(20f, v - 10f);
        }

        // ---- director
        PhysicsSleepDirector director = Object.FindFirstObjectByType<PhysicsSleepDirector>();

        if (director == null)
        {
            GameObject go = new GameObject("PhysicsSleepDirector");
            director = go.AddComponent<PhysicsSleepDirector>();
            log.AppendLine("created PhysicsSleepDirector");
        }

        director.wakeRadius = radius;
        director.hysteresis = 10f;
        director.slices = 8;

        GameObject centre = GameObject.Find("Center Point");
        if (centre != null && director.player == null) director.player = centre.transform;

        log.AppendLine("wake radius: " + radius + "  player: " +
            (director.player != null ? director.player.name : "<main camera at runtime>"));

        // ---- scene bodies
        int added = 0, already = 0, skipped = 0;

        foreach (Rigidbody rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            // The rig, civilians and anything animated manage themselves.
            if (rb.GetComponentInParent<PedestrianCrosser>() != null) { skipped++; continue; }
            if (rb.GetComponentInParent<Animator>() != null) { skipped++; continue; }
            // The mech's physics hands follow bones and must stay kinematic; the
            // XR rig manages itself.
            if (rb.GetComponent<RigidbodyFollowBone>() != null || rb.GetComponentInParent<MechLocomotion>() != null
                || rb.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) { skipped++; continue; }

            if (rb.GetComponent<SleepableBody>() != null) { already++; continue; }

            SleepableBody sb = rb.gameObject.AddComponent<SleepableBody>();
            sb.startAsleep = true;
            sb.settleTime = 3f;
            sb.settleSpeed = 0.15f;
            sb.wakeOnCollision = true;

            added++;
        }

        log.AppendLine(string.Format("scene bodies: {0} given SleepableBody, {1} already had one, {2} skipped",
            added, already, skipped));

        // ---- car prefabs
        int cars = 0;

        if (AssetDatabase.IsValidFolder(CarPrefabFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CarPrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null || prefab.GetComponent<Rigidbody>() == null) continue;
                if (prefab.GetComponent<SleepableBody>() != null) continue;

                SleepableBody sb = prefab.AddComponent<SleepableBody>();

                // A car is kinematic while it drives; CarController owns that.
                // It only becomes physics once something disturbs it.
                sb.startAsleep = false;
                sb.settleTime = 5f;
                sb.settleSpeed = 0.2f;
                sb.wakeOnCollision = true;

                EditorUtility.SetDirty(prefab);
                cars++;
            }
        }

        log.AppendLine("car prefabs given SleepableBody: " + cars);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        log.AppendLine("scene saved");

        log.AppendLine("DONE");
        Debug.Log("[SleepSetup]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
