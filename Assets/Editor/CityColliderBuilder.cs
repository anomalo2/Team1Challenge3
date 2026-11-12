using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives the imported city collision. The GLB arrives with none at all, so the
/// mech has no ground to stand on, no walls to stop it, and any prop that goes
/// dynamic falls through the floor.
///
/// Two collider types, chosen by what the geometry is for:
///
///   GROUND and low detail  -> MeshCollider
///       Roads, pavements, kerbs and especially RAMPS. A box would flatten a
///       ramp into a level slab and cars would drive through the slope, so
///       anything walkable gets the real surface.
///
///   BUILDINGS (tall)       -> BoxCollider shell
///       A mech only needs to be stopped by a building, not to feel its window
///       mullions. A box is the cheapest collider there is and approximating
///       the mass is enough.
///
///   BACKDROP               -> nothing
///       Distant scenery that can never be reached.
///
/// Run Analyze first; it changes nothing and prints the split.
/// </summary>
public static class CityColliderBuilder
{
    const string LogPath = "CityColliders.log";

    /// <summary>Above this height a piece is treated as a building and gets a box.</summary>
    const float BuildingHeight = 4f;
    /// <summary>Pieces smaller than this in every axis are detail; skip them.</summary>
    const float MinInterestingSize = 0.3f;
    /// <summary>A mesh with more triangles than this gets a box even if low, to bound cost.</summary>
    const int MaxMeshColliderTriangles = 4000;

    /// <summary>
    /// A gap this large (as a fraction of the mesh's extent along an axis) means
    /// the mesh contains separate lumps -- parts of different buildings welded
    /// into one mesh by the export. A box round that seals off the street
    /// between them, so those get an exact collider instead.
    /// </summary>
    const float DisjointGapFraction = 0.22f;

    /// <summary>Beyond this a box is never trusted, however full it looks.</summary>
    const float MaxBoxDimension = 25f;

    /// <summary>Colliders are only created within this of Center Point. 0 = everywhere.</summary>
    static float RadiusLimit
    {
        get
        {
            string env = System.Environment.GetEnvironmentVariable("OPT_RADIUS");
            float v;
            return !string.IsNullOrEmpty(env) && float.TryParse(env, out v) ? v : 0f;
        }
    }

    [MenuItem("Tools/Colliders/1 - Analyze City Colliders (read only)")]
    public static void Analyze() { Run(false); }

    [MenuItem("Tools/Colliders/2 - Build City Colliders")]
    public static void Build() { Run(true); }

    public static void AnalyzeBatch() { Run(false); }
    public static void BuildBatch() { Run(true); }

    static void Run(bool apply)
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
        log.AppendLine(apply ? "=== Build city colliders ===" : "=== City collider analysis (read only) ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        int meshCount = 0, boxCount = 0;
        int skipBackdrop = 0, skipTiny = 0, skipHas = 0, skipOther = 0, skipFar = 0;
        int sparseRescued = 0;
        long meshTris = 0;

        float radius = RadiusLimit;
        Transform centre = FindCenter();
        Vector3 centrePos = centre != null ? centre.position : Vector3.zero;

        if (radius > 0f && centre == null)
        {
            log.AppendLine("ERROR: OPT_RADIUS set but no Center Point found.");
            Finish(log);
            return;
        }

        log.AppendLine(radius > 0f
            ? "radius limit: " + radius + " from " + centrePos.ToString("0.#")
            : "radius limit: none (whole city)");

        List<Renderer> all = new List<Renderer>(
            Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));

        foreach (Renderer r in all)
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;
            if (r is SkinnedMeshRenderer) { skipOther++; continue; }

            string n = r.gameObject.name.ToLowerInvariant();

            // Distant scenery is unreachable; collision there is pure waste.
            if (n.StartsWith("backdrop_")) { skipBackdrop++; continue; }

            // Props were given their own colliders by PropClusterBuilder, and
            // the rig, vehicles and civilians manage their own.
            if (!IsCityGeometry(r)) { skipOther++; continue; }

            if (r.GetComponent<Collider>() != null) { skipHas++; continue; }

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { skipOther++; continue; }

            // Out of reach of the player: collision there can never be touched.
            if (radius > 0f
                && Vector3.Distance(centrePos, r.bounds.ClosestPoint(centrePos)) > radius)
            {
                skipFar++;
                continue;
            }

            Mesh mesh = mf.sharedMesh;
            Vector3 size = r.bounds.size;

            if (size.x < MinInterestingSize && size.y < MinInterestingSize && size.z < MinInterestingSize)
            {
                skipTiny++;
                continue;
            }

            int tris = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetIndexCount(s) / 3);

            // Tall = building shell. Low = walkable surface, keep it accurate.
            bool useBox = size.y > BuildingHeight || tris > MaxMeshColliderTriangles;

            // ...but only if a box is actually a fair description of the mesh.
            if (useBox)
            {
                Vector3 mb = mesh.bounds.size;
                bool tooBig = Mathf.Max(mb.x, Mathf.Max(mb.y, mb.z)) > MaxBoxDimension;

                if (tooBig || IsDisjoint(mesh))
                {
                    useBox = false;       // disjoint or hollow: use the real shape
                    sparseRescued++;
                }
            }

            if (apply)
            {
                if (useBox)
                {
                    BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
                    bc.center = mesh.bounds.center;
                    bc.size = mesh.bounds.size;
                }
                else
                {
                    MeshCollider mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = false;   // static concave is fine and exact
                }
            }

            if (useBox) boxCount++;
            else { meshCount++; meshTris += tris; }
        }

        log.AppendLine();
        log.AppendLine(string.Format("MeshColliders (walkable surfaces, ramps): {0}  [{1:n0} triangles]",
            meshCount, meshTris));
        log.AppendLine(string.Format("BoxColliders  (building shells):          {0}", boxCount));
        log.AppendLine(string.Format("skipped: {0} backdrop, {1} tiny, {2} already had one, {3} other, {4} beyond radius",
            skipBackdrop, skipTiny, skipHas, skipOther, skipFar));
        log.AppendLine(string.Format(
            "boxes rejected as unfair (disjoint or oversized, given exact mesh instead): {0}", sparseRescued));
        log.AppendLine();
        log.AppendLine("rule: height > " + BuildingHeight + "m or > " + MaxMeshColliderTriangles +
                       " tris => box shell, otherwise exact mesh");

        if (!apply)
        {
            log.AppendLine();
            log.AppendLine("READ ONLY - nothing changed.");
        }
        else
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            log.AppendLine("scene saved");
        }

        log.AppendLine("DONE");
        Debug.Log("[CityColliders]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    /// <summary>
    /// Is the mesh actually several separate lumps?
    ///
    /// Measuring how full the bounding box is does NOT work here: these are
    /// low-poly buildings, so a perfectly solid one may have only eight
    /// vertices and any density test calls it empty. What distinguishes "one
    /// building" from "parts of two buildings" is a GAP -- a stretch along some
    /// axis containing no geometry at all, with geometry on both sides. That is
    /// the street the box would wall off.
    /// </summary>
    static bool IsDisjoint(Mesh mesh)
    {
        Vector3[] verts = mesh.vertices;
        if (verts == null || verts.Length < 8) return false;

        return HasGap(verts, 0) || HasGap(verts, 1) || HasGap(verts, 2);
    }

    static bool HasGap(Vector3[] verts, int axis)
    {
        float[] values = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++) values[i] = verts[i][axis];

        System.Array.Sort(values);

        float extent = values[values.Length - 1] - values[0];
        if (extent < 0.5f) return false;

        float biggest = 0f;
        for (int i = 1; i < values.Length; i++)
        {
            float gap = values[i] - values[i - 1];
            if (gap > biggest) biggest = gap;
        }

        return biggest > extent * DisjointGapFraction;
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
        Debug.Log("[CityColliders]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    /// <summary>City shell only: not props, rig, vehicles or civilians.</summary>
    static bool IsCityGeometry(Renderer r)
    {
        Transform t = r.transform;

        while (t != null)
        {
            GameObject go = t.gameObject;

            if (go.GetComponent<CrushableObject>() != null) return false;
            if (go.GetComponent<CarController>() != null) return false;
            if (go.GetComponent<PedestrianCrosser>() != null) return false;
            if (go.GetComponent<Camera>() != null) return false;
            if (go.GetComponent<Animator>() != null) return false;

            string n = go.name.ToLowerInvariant();

            if (n == "breakableprops") return false;
            if (n.Contains("xr origin") || n.Contains("controller") || n.Contains("interactor")) return false;
            if (n.StartsWith("mf_") || n.StartsWith("traffic_mf_")) return false;

            t = t.parent;
        }

        return true;
    }
}
