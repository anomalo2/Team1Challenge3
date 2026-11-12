using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Works out WHAT the scene is made of when the object names are useless.
///
/// The city is a merged FBX export whose children are named things like
/// unit_017_inst_008_mdl_004_lod_000. Those names say nothing, but copies of
/// the same prop SHARE A MESH. So grouping renderers by shared mesh recovers
/// the real catalogue: "this mesh appears 41 times" is a prop placed 41 times.
///
/// It then renders one isolated thumbnail per distinct mesh into a contact
/// sheet, so each group can be identified by eye and the whole group labelled
/// at once -- rename it, tag it, or make all 41 copies breakable.
///
/// Isolation works by cloning the mesh onto a scratch layer far from the scene
/// and culling everything else, so nothing in the scene is touched.
/// </summary>
public static class MeshCatalog
{
    const string OutFolder = "Captures";
    const string LogPath = "MeshCatalog.log";

    const int Tile = 192;
    const int Columns = 6;
    const int MaxGroups = 48;          // one sheet's worth, biggest groups first
    const int ScratchLayer = 31;       // assumed unused; only the temp clone goes here
    static readonly Vector3 ScratchOrigin = new Vector3(0f, 20000f, 0f);

    class Group
    {
        public Mesh Mesh;
        public Material[] Materials;
        public int Count;
        public int Triangles;
        public Vector3 Size;
        public string SamplePath;
        public float NearestToCenter = float.MaxValue;
        public int ActiveCount;
        public string Signature;
    }

    [MenuItem("Tools/Capture/Catalog Scene Meshes")]
    public static void BuildCatalog()
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

        System.IO.Directory.CreateDirectory(OutFolder);

        Transform centre = FindCenter();
        Vector3 c = centre != null ? centre.position : Vector3.zero;

        Dictionary<string, Group> groups = new Dictionary<string, Group>();

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            Mesh mesh = null;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;

            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) mesh = smr.sharedMesh;

            if (mesh == null) continue;

            int tris = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++) tris += (int)(mesh.GetIndexCount(sm) / 3);

            // A merged FBX export welds every instance into its OWN mesh, so
            // mesh identity finds no copies. Identical props still share their
            // triangle count, their size and their material, and that triple is
            // a reliable fingerprint for "this is the same prop again".
            string sig = Signature(tris, mesh.bounds.size, r.sharedMaterials);

            Group g;
            if (!groups.TryGetValue(sig, out g))
            {
                g = new Group();
                g.Mesh = mesh;
                g.Materials = r.sharedMaterials;
                g.SamplePath = PathOf(r.transform);
                g.Triangles = tris;
                g.Size = mesh.bounds.size;
                g.Signature = sig;

                groups[sig] = g;
            }

            g.Count++;
            if (r.enabled && r.gameObject.activeInHierarchy) g.ActiveCount++;

            float d = Vector3.Distance(c, r.bounds.ClosestPoint(c));
            if (d < g.NearestToCenter) g.NearestToCenter = d;
        }

        List<Group> ordered = new List<Group>(groups.Values);
        ordered.Sort((a, b) => b.Count.CompareTo(a.Count));

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Mesh catalogue ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);
        log.AppendLine("distinct PROP TYPES (by shape+material signature): " + ordered.Count);
        log.AppendLine();
        log.AppendLine("Contact sheet reads LEFT TO RIGHT, TOP TO BOTTOM, " + Columns + " per row.");
        log.AppendLine("Tile #N below matches tile N on the sheet.");
        log.AppendLine();
        log.AppendLine("  #  copies  active   tris  size (m)              nearest  signature (tris|material|size)  <- sample");

        int shown = Mathf.Min(MaxGroups, ordered.Count);

        for (int i = 0; i < shown; i++)
        {
            Group g = ordered[i];
            log.AppendLine(string.Format("{0,3}  {1,6}  {2,6}  {3,5}  {4,-20}  {5,7:0.#}  {6}  <- {7}",
                i, g.Count, g.ActiveCount, g.Triangles,
                g.Size.ToString("0.##"),
                g.NearestToCenter == float.MaxValue ? -1f : g.NearestToCenter,
                g.Signature, g.SamplePath));
        }

        if (ordered.Count > shown)
        {
            log.AppendLine();
            log.AppendLine("(" + (ordered.Count - shown) + " more distinct meshes not shown)");
        }

        // ---- contact sheet
        int rows = Mathf.CeilToInt(shown / (float)Columns);
        Texture2D sheet = new Texture2D(Columns * Tile, rows * Tile, TextureFormat.RGB24, false);

        Color32 bg = new Color32(24, 26, 32, 255);
        Color32[] clear = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < clear.Length; i++) clear[i] = bg;
        sheet.SetPixels32(clear);

        GameObject lightGo = new GameObject("__CatalogLight");
        Light keyLight = lightGo.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

        try
        {
            for (int i = 0; i < shown; i++)
            {
                Color32[] thumb = RenderThumb(ordered[i]);
                if (thumb == null) continue;

                int col = i % Columns;
                int row = i / Columns;

                // Texture2D origin is bottom-left; we want reading order top-down.
                int x = col * Tile;
                int y = sheet.height - (row + 1) * Tile;

                sheet.SetPixels32(x, y, Tile, Tile, thumb);
            }

            sheet.Apply();

            string sheetPath = OutFolder + "/mesh_catalog.png";
            System.IO.File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
            log.AppendLine();
            log.AppendLine("contact sheet: " + sheetPath + "  (" + sheet.width + "x" + sheet.height + ")");
        }
        finally
        {
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(sheet);
        }

        log.AppendLine("DONE");
        Debug.Log("[MeshCatalog]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    /// <summary>
    /// Renders one mesh in isolation: clone it far above the scene on a scratch
    /// layer, point a camera that culls everything else at it, read back pixels.
    /// </summary>
    static Color32[] RenderThumb(Group g)
    {
        GameObject temp = new GameObject("__CatalogThumb");
        temp.layer = ScratchLayer;
        temp.transform.position = ScratchOrigin;

        MeshFilter mf = temp.AddComponent<MeshFilter>();
        mf.sharedMesh = g.Mesh;

        MeshRenderer mr = temp.AddComponent<MeshRenderer>();
        if (g.Materials != null && g.Materials.Length > 0) mr.sharedMaterials = g.Materials;

        GameObject camGo = new GameObject("__CatalogCam");
        camGo.layer = ScratchLayer;
        Camera cam = camGo.AddComponent<Camera>();

        RenderTexture rt = new RenderTexture(Tile, Tile, 24);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Bounds b = g.Mesh.bounds;
            float radius = Mathf.Max(0.05f, b.extents.magnitude);

            Vector3 target = ScratchOrigin + b.center;
            Vector3 dir = new Vector3(0.7f, 0.5f, 0.7f).normalized;
            Vector3 pos = target + dir * radius * 2.8f;

            cam.transform.SetPositionAndRotation(pos,
                Quaternion.LookRotation((target - pos).normalized, Vector3.up));

            cam.cullingMask = 1 << ScratchLayer;    // nothing from the scene
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.15f, 0.18f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 20f + 10f;
            cam.fieldOfView = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, Tile, Tile), 0, 0);
            tex.Apply();

            Color32[] pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);

            return pixels;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MeshCatalog] thumb failed for " + g.Mesh.name + ": " + e.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(temp);
        }
    }

    /// <summary>Fingerprint for "same prop": triangle count, size to the cm, first material.</summary>
    static string Signature(int tris, Vector3 size, Material[] mats)
    {
        string mat = (mats != null && mats.Length > 0 && mats[0] != null) ? mats[0].name : "nomat";

        return string.Format("{0}|{1}|{2:0.00}x{3:0.00}x{4:0.00}",
            tris, mat, size.x, size.y, size.z);
    }

    static Transform FindCenter()
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            string n = go.name.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (n.Contains("center point") || n.Contains("centre point")) return go.transform;
        }

        return null;
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        Transform p = t.parent;

        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }

        return path;
    }
}
