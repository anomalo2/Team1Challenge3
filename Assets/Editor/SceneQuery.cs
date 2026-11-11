using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Read-only diagnostics for two specific complaints:
///
///   1. "some of those are way down the road"
///      The radius filter keeps a collider if its NEAREST point is in range,
///      but a merged road mesh can start at your feet and run hundreds of
///      metres away, dragging all that collision in with it. This lists
///      colliders by how far their far end reaches.
///
///   2. "light posts no longer are included"
///      Finds tall thin geometry -- lamp posts, signposts, poles -- and reports
///      what each one actually is and whether it ended up breakable or
///      collidable, so it is clear why they were missed.
/// </summary>
public static class SceneQuery
{
    const string LogPath = "SceneQuery.log";

    [MenuItem("Tools/Capture/Query Scene (read only)")]
    public static void Query() { RunQuery(); }

    public static void QueryBatch() { RunQuery(); }

    static void RunQuery()
    {
        string wanted = System.Environment.GetEnvironmentVariable("OPT_SCENE");
        if (string.IsNullOrEmpty(wanted)) wanted = "Assets/Scenes/JohnTesting.unity";

        Scene active = SceneManager.GetActiveScene();

        // Never switch scenes on someone working in the editor: act on what
        // is open. Only batch mode falls back to OPT_SCENE.
        bool interactive = !Application.isBatchMode
                           && active.IsValid() && !string.IsNullOrEmpty(active.path);

        if (!interactive && (!active.IsValid() || active.path != wanted))
            EditorSceneManager.OpenScene(wanted, OpenSceneMode.Single);

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Scene query ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        Transform centre = FindCenter();
        Vector3 c = centre != null ? centre.position : Vector3.zero;
        log.AppendLine("center point: " + c.ToString("0.#"));
        log.AppendLine();

        // ---- 1. colliders that reach a long way out
        log.AppendLine("--- colliders whose FAR end is a long way from Center Point ---");
        log.AppendLine("   near     far   span   tris  name");

        List<Collider> cols = new List<Collider>(
            Object.FindObjectsByType<Collider>(FindObjectsSortMode.None));

        cols.Sort((a, b) => FarDistance(c, b).CompareTo(FarDistance(c, a)));

        long totalTris = 0, farTris = 0;
        int farCount = 0;

        foreach (Collider col in cols)
        {
            MeshCollider mc = col as MeshCollider;
            int tris = (mc != null && mc.sharedMesh != null) ? mc.sharedMesh.triangles.Length / 3 : 0;
            totalTris += tris;

            if (FarDistance(c, col) > 150f) { farCount++; farTris += tris; }
        }

        for (int i = 0; i < cols.Count && i < 20; i++)
        {
            Collider col = cols[i];
            MeshCollider mc = col as MeshCollider;
            int tris = (mc != null && mc.sharedMesh != null) ? mc.sharedMesh.triangles.Length / 3 : 0;

            log.AppendLine(string.Format("{0,7:0.#} {1,7:0.#} {2,6:0.#} {3,6} {4}",
                NearDistance(c, col), FarDistance(c, col),
                col.bounds.size.magnitude, tris, col.gameObject.name));
        }

        log.AppendLine();
        log.AppendLine(string.Format(
            "colliders reaching past 150m: {0}  carrying {1:n0} of {2:n0} collision triangles",
            farCount, farTris, totalTris));

        // ---- 2. tall thin things: lamp posts and friends
        log.AppendLine();
        log.AppendLine("--- tall thin geometry (lamp posts, signposts, poles) ---");
        log.AppendLine("  height  foot   dist  collider  breakable  name");

        List<Renderer> poles = new List<Renderer>();

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            Vector3 s = r.bounds.size;
            float foot = Mathf.Max(s.x, s.z);

            if (s.y < 3f || foot > 2.5f) continue;

            poles.Add(r);
        }

        poles.Sort((a, b) => NearDist(c, a).CompareTo(NearDist(c, b)));

        int withCollider = 0, breakable = 0;

        foreach (Renderer r in poles)
        {
            bool hasCol = r.GetComponent<Collider>() != null;
            bool isBreak = r.GetComponentInParent<CrushableObject>() != null;

            if (hasCol) withCollider++;
            if (isBreak) breakable++;
        }

        for (int i = 0; i < poles.Count && i < 25; i++)
        {
            Renderer r = poles[i];
            Vector3 s = r.bounds.size;

            log.AppendLine(string.Format("{0,8:0.#} {1,5:0.#} {2,6:0.#}  {3,-8} {4,-9}  {5}",
                s.y, Mathf.Max(s.x, s.z), NearDist(c, r),
                r.GetComponent<Collider>() != null ? "yes" : "NO",
                r.GetComponentInParent<CrushableObject>() != null ? "yes" : "NO",
                r.gameObject.name));
        }

        log.AppendLine();
        log.AppendLine(string.Format("tall thin objects: {0}  ({1} have a collider, {2} are breakable)",
            poles.Count, withCollider, breakable));

        // name prefixes of the poles, so we can see what family they belong to
        Dictionary<string, int> prefixes = new Dictionary<string, int>();
        foreach (Renderer r in poles)
        {
            string n = r.gameObject.name.ToLowerInvariant();
            int us = n.IndexOf('_');
            string key = us > 0 ? n.Substring(0, us) : n;

            prefixes[key] = prefixes.TryGetValue(key, out int v) ? v + 1 : 1;
        }

        log.AppendLine("name prefixes among them:");
        foreach (KeyValuePair<string, int> kv in prefixes)
            log.AppendLine("   " + kv.Key + ": " + kv.Value);

        log.AppendLine("DONE");
        Debug.Log("[SceneQuery]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    static float NearDistance(Vector3 c, Collider col)
    {
        return Vector3.Distance(c, col.bounds.ClosestPoint(c));
    }

    /// <summary>Distance to the furthest corner: how far this collider reaches.</summary>
    static float FarDistance(Vector3 c, Collider col)
    {
        Bounds b = col.bounds;
        float best = 0f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);

            float d = Vector3.Distance(c, corner);
            if (d > best) best = d;
        }

        return best;
    }

    static float NearDist(Vector3 c, Renderer r)
    {
        return Vector3.Distance(c, r.bounds.ClosestPoint(c));
    }

    static Transform FindCenter()
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            string n = go.name.ToLowerInvariant().Replace("_", " ");
            if (n.Contains("center point") || n.Contains("centre point")) return go.transform;
        }

        return null;
    }
}
