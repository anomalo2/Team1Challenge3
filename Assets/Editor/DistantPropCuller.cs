using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes scenery props that are too far away to matter.
///
/// Distance alone is the wrong test: a bollard at 120 m is a couple of pixels,
/// while a billboard at 250 m is still part of the skyline. So the test is
/// APPARENT SIZE -- how large the prop actually appears from the Center Point.
/// Anything below the threshold is deleted; anything that still reads as part
/// of the view is kept however distant it is.
///
/// Only objects carrying a PropLabel are considered, so buildings, roads and
/// the backdrop are structurally out of reach of this pass. Categories that
/// make up the skyline can be spared outright.
/// </summary>
public static class DistantPropCuller
{
    const string LogPath = "DistantProps.log";

    /// <summary>Nothing closer than this is ever removed, whatever its size.</summary>
    const float SafeRadius = 100f;

    /// <summary>
    /// size / distance below which a prop is not worth drawing.
    /// 0.02 is roughly one degree: about a pixel or two on a headset.
    /// </summary>
    const float ApparentSizeThreshold = 0.02f;

    [MenuItem("Tools/Optimize/1 - Analyze Distant Props (read only)")]
    public static void Analyze() { Run(false); }

    [MenuItem("Tools/Optimize/2 - Remove Distant Props")]
    public static void Remove() { Run(true); }

    public static void AnalyzeBatch() { Run(false); }
    public static void RemoveBatch() { Run(true); }

    static void Run(bool apply)
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
        log.AppendLine(apply ? "=== Remove distant props ===" : "=== Distant props (read only) ===");
        log.AppendLine("scene: " + active.path);

        GameObject centreGo = GameObject.Find("Center Point");
        if (centreGo == null) { log.AppendLine("ERROR: no Center Point"); Finish(log); return; }

        Vector3 centre = centreGo.transform.position;

        float safeRadius = SafeRadius;
        string env = System.Environment.GetEnvironmentVariable("OPT_RADIUS");
        if (!string.IsNullOrEmpty(env))
        {
            float v;
            if (float.TryParse(env, out v)) safeRadius = v;
        }

        log.AppendLine(string.Format("center {0}  safe radius {1}  apparent-size cut {2}",
            centre.ToString("0.#"), safeRadius, ApparentSizeThreshold));

        // Skyline categories stay: they read at distance and removing them
        // leaves obvious holes in the view.
        HashSet<PropLabel.PropCategory> keepAlways = new HashSet<PropLabel.PropCategory>
        {
            PropLabel.PropCategory.Building,
            PropLabel.PropCategory.Backdrop,
            PropLabel.PropCategory.Ground,
        };

        List<GameObject> doomed = new List<GameObject>();
        Dictionary<string, int> byType = new Dictionary<string, int>();
        Dictionary<int, int> bands = new Dictionary<int, int>();

        int considered = 0, keptClose = 0, keptVisible = 0, keptCategory = 0, keptClaimed = 0;
        int tris = 0;

        foreach (PropLabel label in Object.FindObjectsByType<PropLabel>(FindObjectsSortMode.None))
        {
            Renderer r = label.GetComponent<Renderer>();
            if (r == null) continue;

            considered++;

            if (keepAlways.Contains(label.category)) { keptCategory++; continue; }

            // Anything already made breakable is inside the play area.
            if (label.GetComponentInParent<CrushableObject>() != null) { keptClaimed++; continue; }

            float distance = Vector3.Distance(centre, r.bounds.ClosestPoint(centre));
            if (distance <= safeRadius) { keptClose++; continue; }

            Vector3 size = r.bounds.size;
            float biggest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float apparent = biggest / Mathf.Max(1f, distance);

            if (apparent >= ApparentSizeThreshold) { keptVisible++; continue; }

            doomed.Add(label.gameObject);

            string key = string.IsNullOrEmpty(label.typeName) ? "mdl" + label.modelId : label.typeName;
            byType[key] = byType.TryGetValue(key, out int c) ? c + 1 : 1;

            int band = Mathf.FloorToInt(distance / 50f) * 50;
            bands[band] = bands.TryGetValue(band, out int b) ? b + 1 : 1;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    tris += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
        }

        log.AppendLine();
        log.AppendLine(string.Format("labelled props: {0}", considered));
        log.AppendLine(string.Format("  kept, inside {0}m:        {1}", safeRadius, keptClose));
        log.AppendLine(string.Format("  kept, still visible:       {0}", keptVisible));
        log.AppendLine(string.Format("  kept, skyline category:    {0}", keptCategory));
        log.AppendLine(string.Format("  kept, already breakable:   {0}", keptClaimed));
        log.AppendLine(string.Format("  TO REMOVE:                 {0}  ({1:n0} triangles)", doomed.Count, tris));

        log.AppendLine();
        log.AppendLine("by type:");
        foreach (KeyValuePair<string, int> kv in byType)
            log.AppendLine(string.Format("  {0,-24} {1}", kv.Key, kv.Value));

        log.AppendLine();
        log.AppendLine("by distance band:");
        List<int> keys = new List<int>(bands.Keys);
        keys.Sort();
        foreach (int k in keys)
            log.AppendLine(string.Format("  {0,4}-{1,4}m  {2}", k, k + 50, bands[k]));

        if (!apply)
        {
            log.AppendLine();
            log.AppendLine("READ ONLY - nothing removed.");
            Finish(log);
            return;
        }

        foreach (GameObject go in doomed)
            if (go != null) Undo.DestroyObjectImmediate(go);

        log.AppendLine();
        log.AppendLine("removed: " + doomed.Count + " prop renderer(s)");

        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        log.AppendLine("scene saved");

        Finish(log);
    }

    static void Finish(StringBuilder log)
    {
        log.AppendLine("DONE");
        Debug.Log("[DistantProps]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
