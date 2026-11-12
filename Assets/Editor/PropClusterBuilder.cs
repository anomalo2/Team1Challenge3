using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Groups touching sub-meshes into single breakable props.
///
/// The city is a merged FBX export, so a signpost arrives as several unrelated
/// renderers: the pole, the sign board, maybe a bracket. Making each one
/// breakable on its own is wrong -- snap the pole and the board hangs in the
/// air. Physically they are one object and should break, fall and be carried
/// as one.
///
/// So: take prop-sized renderers, join any whose bounds touch (within a small
/// gap), and treat each connected group as one prop. The group gets a single
/// parent, a single Rigidbody and CrushableObject, and a collider per part, so
/// the whole assembly topples together with the sign still on its post.
///
/// Size limits stop this swallowing the city: parts larger than MaxPartSize are
/// never candidates, and a cluster larger than MaxClusterSize is discarded
/// rather than welding a building to its neighbour.
/// </summary>
public static class PropClusterBuilder
{
    const string LogPath = "PropClusters.log";

    // A part bigger than this is scenery, not a prop.
    const float MaxPartSize = 12f;
    // A finished cluster bigger than this is probably a building; drop it.
    const float MaxClusterSize = 16f;
    // Parts whose bounds come within this of each other count as joined.
    const float JoinGap = 0.25f;
    // Ignore flat plates this thin and this wide: roads, pavements, markings.
    const float PlateThickness = 0.4f;
    const float PlateFootprint = 6f;

    const int MinPartsToReport = 1;

    class Cluster
    {
        public List<Renderer> Parts = new List<Renderer>();
        public Bounds Bounds;
        public int Triangles;
        public float NearestToCenter = float.MaxValue;
    }

    [MenuItem("Tools/Breakables/1 - Analyze Prop Clusters (read only)")]
    public static void AnalyzeClusters()
    {
        Run(false);
    }

    [MenuItem("Tools/Breakables/2 - Build Breakable Prop Clusters")]
    public static void BuildClusters()
    {
        Run(true);
    }

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
        log.AppendLine(apply ? "=== Build breakable prop clusters ===" : "=== Prop cluster analysis (read only) ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        Transform centre = FindCenter();
        Vector3 c = centre != null ? centre.position : Vector3.zero;

        float radius = 0f;
        string envR = System.Environment.GetEnvironmentVariable("OPT_RADIUS");
        if (!string.IsNullOrEmpty(envR)) float.TryParse(envR, out radius);

        log.AppendLine(radius > 0f ? "radius limit: " + radius : "radius limit: none");

        // Street furniture is NOT reliably under prop_. Lamp posts arrive as
        // unit_*. What identifies them is the mdl_ id, which is the model type:
        // every mdl_014 is the same 7.2x1.7 lamp post, placed many times. So
        // work out which mdl_ ids are prop-shaped and treat those as props too.
        HashSet<string> propModels = FindPropModels(log);

        int labelled = 0;
        foreach (PropLabel pl in Object.FindObjectsByType<PropLabel>(FindObjectsSortMode.None))
            if (pl.shouldBeBreakable) labelled++;

        log.AppendLine("renderers labelled breakable by PropLabel: " + labelled);

        // ---- candidates
        List<Renderer> candidates = new List<Renderer>();
        int rejectedFar = 0;
        int rejectedBig = 0, rejectedPlate = 0, rejectedDynamic = 0, rejectedNotProp = 0;

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;
            if (r is SkinnedMeshRenderer) { rejectedDynamic++; continue; }

            if (!IsFreeStanding(r)) { rejectedDynamic++; continue; }

            // The source model labels its own content: prop_* is street
            // furniture, unit_* is building, backdrop_* is distant scenery.
            // Trust that over guessing from size.
            // PropLabel is authoritative where it exists: it was set from the
            // identified model types, so it knows a street light is furniture
            // even though the exporter filed it under unit_.
            PropLabel label = r.GetComponent<PropLabel>();

            bool isBreakableProp = label != null
                ? label.shouldBeBreakable
                : IsProp(r, propModels);

            if (!isBreakableProp) { rejectedNotProp++; continue; }

            if (radius > 0f
                && Vector3.Distance(c, r.bounds.ClosestPoint(c)) > radius)
            {
                rejectedFar++;
                continue;
            }

            Vector3 size = r.bounds.size;
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (maxDim > MaxPartSize) { rejectedBig++; continue; }

            // flat and wide = road surface, pavement, painted marking
            if (size.y < PlateThickness && Mathf.Max(size.x, size.z) > PlateFootprint)
            {
                rejectedPlate++;
                continue;
            }

            candidates.Add(r);
        }

        log.AppendLine(string.Format(
            "candidate parts: {0}  (rejected: {1} not a prop model, {2} too big, {3} flat plates, {4} dynamic, {5} beyond radius)",
            candidates.Count, rejectedNotProp, rejectedBig, rejectedPlate, rejectedDynamic, rejectedFar));

        // ---- union-find on touching bounds
        int n = candidates.Count;
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        Bounds[] grown = new Bounds[n];
        for (int i = 0; i < n; i++)
        {
            grown[i] = candidates[i].bounds;
            grown[i].Expand(JoinGap);
        }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (grown[i].Intersects(candidates[j].bounds)) Union(parent, i, j);

        Dictionary<int, Cluster> byRoot = new Dictionary<int, Cluster>();

        for (int i = 0; i < n; i++)
        {
            int rootIndex = Find(parent, i);

            Cluster cl;
            if (!byRoot.TryGetValue(rootIndex, out cl))
            {
                cl = new Cluster();
                cl.Bounds = candidates[i].bounds;
                byRoot[rootIndex] = cl;
            }
            else cl.Bounds.Encapsulate(candidates[i].bounds);

            cl.Parts.Add(candidates[i]);
        }

        // ---- filter + measure
        List<Cluster> keep = new List<Cluster>();
        int tooBig = 0;

        foreach (Cluster cl in byRoot.Values)
        {
            Vector3 s = cl.Bounds.size;
            if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) > MaxClusterSize) { tooBig++; continue; }

            foreach (Renderer r in cl.Parts)
            {
                cl.Triangles += TriangleCount(r);
                float d = Vector3.Distance(c, r.bounds.ClosestPoint(c));
                if (d < cl.NearestToCenter) cl.NearestToCenter = d;
            }

            if (cl.Parts.Count >= MinPartsToReport) keep.Add(cl);
        }

        keep.Sort((a, b) => b.Parts.Count.CompareTo(a.Parts.Count));

        int multi = 0;
        foreach (Cluster cl in keep) if (cl.Parts.Count > 1) multi++;

        log.AppendLine(string.Format("clusters: {0}  ({1} are multi-part assemblies, {2} discarded as too large)",
            keep.Count, multi, tooBig));
        log.AppendLine();
        log.AppendLine("--- 30 largest assemblies (these are the ones parenting matters for) ---");
        log.AppendLine(" parts  tris  size (m)             nearest  sample part");

        for (int i = 0; i < keep.Count && i < 30; i++)
        {
            Cluster cl = keep[i];
            log.AppendLine(string.Format("{0,6} {1,5}  {2,-20} {3,7:0.#}  {4}",
                cl.Parts.Count, cl.Triangles, cl.Bounds.size.ToString("0.##"),
                cl.NearestToCenter == float.MaxValue ? -1f : cl.NearestToCenter,
                cl.Parts[0].gameObject.name));
        }

        if (!apply)
        {
            log.AppendLine();
            log.AppendLine("READ ONLY - nothing changed. Run '2 - Build Breakable Prop Clusters' to apply.");
            Finish(log);
            return;
        }

        // ---- apply
        GameObject root = GameObject.Find("BreakableProps");
        if (root == null) root = new GameObject("BreakableProps");

        int crushLayer = LayerMask.NameToLayer("Crushable");
        int built = 0;

        foreach (Cluster cl in keep)
        {
            GameObject holder = new GameObject("Prop_" + DominantTypeName(cl.Parts) + "_" + built.ToString("000"));
            holder.transform.SetParent(root.transform, true);

            // Pivot at the base so a toppling prop rotates about its foot.
            holder.transform.position = new Vector3(cl.Bounds.center.x, cl.Bounds.min.y, cl.Bounds.center.z);

            foreach (Renderer r in cl.Parts)
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
                r.transform.SetParent(holder.transform, true);   // keeps world pose

                if (crushLayer >= 0) r.gameObject.layer = crushLayer;

                // A collider per part, so the whole assembly is solid.
                if (r.GetComponent<Collider>() == null)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
                        bc.center = mf.sharedMesh.bounds.center;
                        bc.size = mf.sharedMesh.bounds.size;
                    }
                }
            }

            if (crushLayer >= 0) holder.layer = crushLayer;

            Rigidbody rb = holder.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(cl.Bounds.size.magnitude * 25f, 15f, 800f);

            // Kinematic while intact. Two reasons: the city geometry has no
            // colliders, so a live body would fall through the world; and 321
            // sleeping-but-live bodies cost more than they are worth on mobile.
            // CrushableObject flips this to false the moment the prop is
            // destroyed, and MechGrabber does the same when it is thrown, so
            // props still tumble when it matters.
            rb.isKinematic = true;

            // Strength from what the thing IS, not how big it is. A traffic
            // cone should scatter; a palm tree should resist.
            PropLabel.PropCategory category = DominantCategory(cl.Parts);
            float resistance, integrity, mass, crushedHeight;
            CategoryStrength(category, out resistance, out integrity, out mass, out crushedHeight);

            rb.mass = mass;

            CrushableObject crush = holder.AddComponent<CrushableObject>();
            crush.displayName = holder.name;
            crush.canBeDestroyed = true;
            crush.canBeGrabbed = true;
            crush.countsAsPropertyDamage = true;
            crush.propertyValue = Mathf.Clamp(cl.Bounds.size.magnitude * 6f, 5f, 80f);
            crush.crushResistance = resistance;
            crush.structuralIntegrity = integrity;
            crush.sustainedCrushRate = 0.02f;
            crush.impactCrushRate = 0.004f;
            crush.canExplode = false;
            crush.squashWhenCrushed = true;
            crush.crushedHeightScale = crushedHeight;

            holder.AddComponent<CrushOnImpact>().minImpactSpeed = 2.5f;

            built++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        log.AppendLine();
        log.AppendLine("built: " + built + " breakable prop assemblies under 'BreakableProps'");
        log.AppendLine("scene saved");

        Finish(log);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Which mdl_ ids are street furniture rather than building?
    ///
    /// The mdl_ field is the model type: every instance of mdl_014 is the same
    /// 7.2 x 1.7 lamp post. So measure each model type once and judge the TYPE,
    /// not each object. A type is furniture if it is consistently small-footed
    /// and man-sized or taller. Judging by name prefix misses lamp posts, which
    /// the exporter files under unit_.
    /// </summary>
    static HashSet<string> FindPropModels(StringBuilder log)
    {
        Dictionary<string, List<Vector3>> sizes = new Dictionary<string, List<Vector3>>();

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string id = ModelId(r.gameObject.name);
            if (id == null) continue;

            List<Vector3> list;
            if (!sizes.TryGetValue(id, out list)) { list = new List<Vector3>(); sizes[id] = list; }

            list.Add(r.bounds.size);
        }

        HashSet<string> result = new HashSet<string>();

        foreach (KeyValuePair<string, List<Vector3>> kv in sizes)
        {
            List<Vector3> list = kv.Value;

            Vector3 sum = Vector3.zero;
            foreach (Vector3 v in list) sum += v;
            Vector3 avg = sum / list.Count;

            float foot = Mathf.Max(avg.x, avg.z);

            // Small footprint, at least knee height, not a tower.
            bool furniture = foot <= 3f && avg.y >= 1.2f && avg.y <= 12f;

            if (furniture) result.Add(kv.Key);
        }

        log.AppendLine("model types judged to be street furniture: " + result.Count
                       + " of " + sizes.Count);

        return result;
    }

    /// <summary>The mdl_NNN field, which identifies the model type.</summary>
    static string ModelId(string name)
    {
        int i = name.IndexOf("mdl_", System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;

        int start = i + 4;
        int end = start;
        while (end < name.Length && char.IsDigit(name[end])) end++;

        return end > start ? name.Substring(start, end - start) : null;
    }

    /// <summary>
    /// Street furniture: anything under prop_/chevron_, plus any unit_ whose
    /// MODEL TYPE measured as furniture. backdrop_ is never a prop.
    /// </summary>
    static bool IsProp(Renderer r, HashSet<string> propModels)
    {
        string n = r.gameObject.name.ToLowerInvariant();

        if (n.StartsWith("backdrop_")) return false;
        if (n.StartsWith("prop_") || n.StartsWith("chevron_")) return true;

        string id = ModelId(n);
        return id != null && propModels.Contains(id);
    }

    /// <summary>Not already claimed, not part of a rig, vehicle or civilian.</summary>
    static bool IsFreeStanding(Renderer r)
    {
        Transform t = r.transform;

        while (t != null)
        {
            GameObject go = t.gameObject;

            if (go.GetComponent<CrushableObject>() != null) return false;
            if (go.GetComponent<Animator>() != null) return false;
            if (go.GetComponent<Camera>() != null) return false;
            if (go.GetComponent<CarController>() != null) return false;
            if (go.GetComponent<PedestrianCrosser>() != null) return false;
            if (go.GetComponent<TrafficRoute>() != null) return false;

            string n = go.name.ToLowerInvariant();
            if (n.Contains("xr origin") || n.Contains("controller") || n.Contains("interactor"))
                return false;

            // MetroFleet vehicles are traffic, not scenery. Without this a
            // parked bus clusters into one 91-part "prop".
            if (n.StartsWith("mf_") || n.StartsWith("traffic_mf_")) return false;

            t = t.parent;
        }

        return true;
    }

    /// <summary>Most common category among the parts of an assembly.</summary>
    static PropLabel.PropCategory DominantCategory(List<Renderer> parts)
    {
        Dictionary<PropLabel.PropCategory, int> votes = new Dictionary<PropLabel.PropCategory, int>();

        foreach (Renderer r in parts)
        {
            PropLabel pl = r.GetComponent<PropLabel>();
            if (pl == null) continue;

            votes[pl.category] = votes.TryGetValue(pl.category, out int v) ? v + 1 : 1;
        }

        PropLabel.PropCategory best = PropLabel.PropCategory.Unknown;
        int bestVotes = 0;

        foreach (KeyValuePair<PropLabel.PropCategory, int> kv in votes)
            if (kv.Value > bestVotes) { bestVotes = kv.Value; best = kv.Key; }

        return best;
    }

    static string DominantTypeName(List<Renderer> parts)
    {
        Dictionary<string, int> votes = new Dictionary<string, int>();

        foreach (Renderer r in parts)
        {
            PropLabel pl = r.GetComponent<PropLabel>();
            if (pl == null || string.IsNullOrEmpty(pl.typeName)) continue;

            votes[pl.typeName] = votes.TryGetValue(pl.typeName, out int v) ? v + 1 : 1;
        }

        string best = "Unknown";
        int bestVotes = 0;

        foreach (KeyValuePair<string, int> kv in votes)
            if (kv.Value > bestVotes) { bestVotes = kv.Value; best = kv.Key; }

        return best;
    }

    /// <summary>
    /// How tough each kind of thing is. Resistance is the force below which
    /// nothing at all happens, so these numbers decide what a mech can brush
    /// past and what it has to mean to destroy.
    /// </summary>
    static void CategoryStrength(PropLabel.PropCategory c,
        out float resistance, out float integrity, out float mass, out float crushedHeight)
    {
        switch (c)
        {
            case PropLabel.PropCategory.Barrier:          // cones, drums, hazard fences
                resistance = 500f;  integrity = 3f;  mass = 20f;  crushedHeight = 0.15f; break;

            case PropLabel.PropCategory.StreetFurniture:  // benches, bins, billboards
                resistance = 1000f; integrity = 6f;  mass = 50f;  crushedHeight = 0.25f; break;

            case PropLabel.PropCategory.TrafficControl:   // bollards, signals, posts
                resistance = 1500f; integrity = 7f;  mass = 90f;  crushedHeight = 0.2f;  break;

            case PropLabel.PropCategory.Utility:          // AC units, cabinets
                resistance = 1500f; integrity = 8f;  mass = 60f;  crushedHeight = 0.3f;  break;

            case PropLabel.PropCategory.StreetLight:      // lamp posts
                resistance = 1800f; integrity = 9f;  mass = 130f; crushedHeight = 0.2f;  break;

            case PropLabel.PropCategory.Vegetation:       // trees, big shrubs
                resistance = 2500f; integrity = 14f; mass = 300f; crushedHeight = 0.35f; break;

            default:
                resistance = 1500f; integrity = 8f;  mass = 60f;  crushedHeight = 0.25f; break;
        }
    }

    static string DescribeSize(Vector3 s)
    {
        float h = s.y;
        float foot = Mathf.Max(s.x, s.z);

        if (h > 4f && foot < 2f) return "Tall";      // lamp post, signpost
        if (h < 0.8f) return "Low";                  // kerb clutter, markings
        if (foot > 3f) return "Wide";                // billboard, bench run
        return "Small";
    }

    static int TriangleCount(Renderer r)
    {
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 0;

        int tris = 0;
        for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
            tris += (int)(mf.sharedMesh.GetIndexCount(s) / 3);

        return tris;
    }

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb) parent[rb] = ra;
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
        Debug.Log("[PropClusters]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
