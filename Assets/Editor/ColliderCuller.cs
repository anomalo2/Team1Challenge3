using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps collision only where the player can actually reach it.
///
/// Giving the whole city collision is correct for a desktop build and far too
/// much for a phone or a standalone headset: every collider costs memory and
/// broadphase work whether or not anything ever touches it. The player is in a
/// mech standing at Center Point, so collision past a certain radius can never
/// be hit and is pure overhead.
///
/// Analyze prints how many colliders and collision triangles sit in each
/// distance band, so the radius is picked from data. Strip then removes
/// everything beyond OPT_RADIUS.
///
/// Deliberately spares the rig, vehicles and civilians, which carry their own
/// collision and move around.
/// </summary>
public static class ColliderCuller
{
    const string LogPath = "ColliderBudget.log";
    static readonly float[] Bands = { 50f, 75f, 100f, 125f, 150f, 200f, 250f, 400f };

    [MenuItem("Tools/Colliders/3 - Analyze Collider Budget (read only)")]
    public static void Analyze() { Run(false, 0f); }

    public static void AnalyzeBatch() { Run(false, 0f); }

    public static void StripBatch()
    {
        float radius = 120f;
        string env = System.Environment.GetEnvironmentVariable("OPT_RADIUS");
        if (!string.IsNullOrEmpty(env)) float.TryParse(env, out radius);

        Run(true, radius);
    }

    static void Run(bool apply, float radius)
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
        log.AppendLine(apply ? "=== Strip distant colliders ===" : "=== Collider budget (read only) ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        Transform centre = FindCenter();
        if (centre == null) { log.AppendLine("ERROR: no Center Point"); Finish(log); return; }

        Vector3 c = centre.position;
        log.AppendLine("center point: " + c.ToString("0.#"));

        List<Collider> all = new List<Collider>();
        foreach (Collider col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (col.isTrigger) continue;
            if (!IsStrippable(col)) continue;

            all.Add(col);
        }

        log.AppendLine("strippable colliders: " + all.Count);
        log.AppendLine();

        if (!apply)
        {
            log.AppendLine("  radius   keep   strip   keep-mesh  keep-boxes  keep-tris");

            foreach (float band in Bands)
            {
                int keep = 0, strip = 0, km = 0, kb = 0;
                long tris = 0;

                foreach (Collider col in all)
                {
                    if (Distance(c, col) <= band)
                    {
                        keep++;
                        MeshCollider mc = col as MeshCollider;
                        if (mc != null)
                        {
                            km++;
                            if (mc.sharedMesh != null) tris += mc.sharedMesh.triangles.Length / 3;
                        }
                        else kb++;
                    }
                    else strip++;
                }

                log.AppendLine(string.Format("{0,8:0}  {1,5}  {2,6}  {3,10}  {4,10}  {5,9:n0}",
                    band, keep, strip, km, kb, tris));
            }

            log.AppendLine();
            log.AppendLine("READ ONLY - nothing changed.");
            Finish(log);
            return;
        }

        // ---- strip
        int removed = 0, kept = 0;
        long keptTris = 0;

        foreach (Collider col in all)
        {
            if (Distance(c, col) <= radius)
            {
                kept++;
                MeshCollider mc = col as MeshCollider;
                if (mc != null && mc.sharedMesh != null) keptTris += mc.sharedMesh.triangles.Length / 3;
                continue;
            }

            Object.DestroyImmediate(col);
            removed++;
        }

        log.AppendLine("radius: " + radius);
        log.AppendLine("kept: " + kept + "  (" + keptTris.ToString("n0") + " collision triangles)");
        log.AppendLine("removed: " + removed);

        // A kinematic body with no collider does nothing; drop those too.
        int bodiesRemoved = 0;
        foreach (Rigidbody rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (rb.GetComponentInChildren<Collider>(true) != null) continue;
            if (!IsStrippableGameObject(rb.gameObject)) continue;

            Object.DestroyImmediate(rb);
            bodiesRemoved++;
        }

        log.AppendLine("orphaned rigidbodies removed: " + bodiesRemoved);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        log.AppendLine("scene saved");

        Finish(log);
    }

    static float Distance(Vector3 c, Collider col)
    {
        return Vector3.Distance(c, col.bounds.ClosestPoint(c));
    }

    static bool IsStrippable(Collider col)
    {
        return IsStrippableGameObject(col.gameObject);
    }

    /// <summary>Scenery and props only. The rig, vehicles and civilians keep theirs.</summary>
    static bool IsStrippableGameObject(GameObject go)
    {
        Transform t = go.transform;

        while (t != null)
        {
            GameObject g = t.gameObject;

            if (g.GetComponent<CarController>() != null) return false;
            if (g.GetComponent<PedestrianCrosser>() != null) return false;
            if (g.GetComponent<Camera>() != null) return false;
            if (g.GetComponent<Animator>() != null) return false;

            string n = g.name.ToLowerInvariant();
            if (n.Contains("xr origin") || n.Contains("controller") || n.Contains("interactor")) return false;
            if (n.StartsWith("mf_") || n.StartsWith("traffic_mf_")) return false;

            t = t.parent;
        }

        return true;
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

    static void Finish(StringBuilder log)
    {
        log.AppendLine("DONE");
        Debug.Log("[ColliderBudget]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
