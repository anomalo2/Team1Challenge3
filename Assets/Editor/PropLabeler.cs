using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Labels the city once so later work does not have to re-derive it.
///
/// Everything keys off the mdl_ field, which is the model TYPE in the source
/// export: every mdl_014 is the same lamp post. There are only a couple of
/// dozen types across thousands of objects, so identifying the types labels
/// the whole city.
///
/// Step 1  CatalogModelTypes - renders one thumbnail per model type into a
///         contact sheet and writes the measurements, so each type can be
///         named by eye.
/// Step 2  ApplyLabels - reads PropTypes.csv and stamps a PropLabel component
///         onto every instance, so future tools can query by type and category
///         instead of parsing names or guessing from size.
/// </summary>
public static class PropLabeler
{
    const string OutFolder = "Captures";
    const string LogPath = "PropTypes.log";
    const string MapPath = "PropTypes.csv";

    const int Tile = 192;
    const int Columns = 6;
    const int ScratchLayer = 31;
    static readonly Vector3 ScratchOrigin = new Vector3(0f, 20000f, 0f);

    class ModelType
    {
        public string Id;
        public int Count;
        public Vector3 AvgSize;
        public int Triangles;
        public Mesh SampleMesh;
        public Material[] SampleMaterials;
        public string SampleName;
        public float NearestToCenter = float.MaxValue;
        public string Prefix = "";
    }

    [MenuItem("Tools/Breakables/3 - Catalog Model Types")]
    public static void CatalogModelTypes() { Catalog(); }
    public static void CatalogBatch() { Catalog(); }

    [MenuItem("Tools/Breakables/4 - Apply Prop Labels")]
    public static void ApplyLabels() { Apply(); }
    public static void ApplyBatch() { Apply(); }

    // ---------------------------------------------------------------- catalog

    static void Catalog()
    {
        OpenScene();
        System.IO.Directory.CreateDirectory(OutFolder);

        Transform centre = FindCenter();
        Vector3 c = centre != null ? centre.position : Vector3.zero;

        Dictionary<string, ModelType> types = CollectTypes(c);

        List<ModelType> ordered = new List<ModelType>(types.Values);
        ordered.Sort((a, b) => b.Count.CompareTo(a.Count));

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Model types ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);
        log.AppendLine("model types: " + ordered.Count);
        log.AppendLine();
        log.AppendLine("Contact sheet reads left to right, top to bottom, " + Columns + " per row.");
        log.AppendLine();
        log.AppendLine("tile  mdl   count   tris  avg size (m)          nearest  prefix  sample");

        for (int i = 0; i < ordered.Count; i++)
        {
            ModelType t = ordered[i];
            log.AppendLine(string.Format("{0,4}  {1,-4} {2,6} {3,6}  {4,-20} {5,8:0.#}  {6,-9} {7}",
                i, t.Id, t.Count, t.Triangles, t.AvgSize.ToString("0.##"),
                t.NearestToCenter == float.MaxValue ? -1f : t.NearestToCenter,
                t.Prefix, t.SampleName));
        }

        // starter CSV, pre-filled with a guess so it is editable rather than blank
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("modelId,typeName,category,breakable");

        foreach (ModelType t in ordered)
        {
            string guess = GuessCategory(t);
            csv.AppendLine(string.Format("{0},{1},{2},{3}",
                t.Id, "mdl" + t.Id, guess,
                guess == "Building" || guess == "Ground" || guess == "Backdrop" ? "false" : "true"));
        }

        try { System.IO.File.WriteAllText(MapPath, csv.ToString()); } catch { }
        log.AppendLine();
        log.AppendLine("starter mapping written to " + MapPath + " - edit typeName/category then run Apply Prop Labels");

        RenderSheet(ordered, log);

        log.AppendLine("DONE");
        Debug.Log("[PropLabeler]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    static Dictionary<string, ModelType> CollectTypes(Vector3 c)
    {
        Dictionary<string, ModelType> types = new Dictionary<string, ModelType>();
        Dictionary<string, Vector3> sizeSums = new Dictionary<string, Vector3>();

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            string id = ModelId(r.gameObject.name);
            if (id == null) continue;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            ModelType t;
            if (!types.TryGetValue(id, out t))
            {
                t = new ModelType();
                t.Id = id;
                t.SampleMesh = mf.sharedMesh;
                t.SampleMaterials = r.sharedMaterials;
                t.SampleName = r.gameObject.name;

                string n = r.gameObject.name.ToLowerInvariant();
                int us = n.IndexOf('_');
                t.Prefix = us > 0 ? n.Substring(0, us) : n;

                int tris = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    tris += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
                t.Triangles = tris;

                types[id] = t;
                sizeSums[id] = Vector3.zero;
            }

            t.Count++;
            sizeSums[id] += r.bounds.size;

            float d = Vector3.Distance(c, r.bounds.ClosestPoint(c));
            if (d < t.NearestToCenter) t.NearestToCenter = d;
        }

        foreach (KeyValuePair<string, ModelType> kv in types)
            kv.Value.AvgSize = sizeSums[kv.Key] / Mathf.Max(1, kv.Value.Count);

        return types;
    }

    static string GuessCategory(ModelType t)
    {
        float foot = Mathf.Max(t.AvgSize.x, t.AvgSize.z);
        float h = t.AvgSize.y;

        if (t.Prefix == "backdrop") return "Backdrop";
        if (h > 12f || foot > 12f) return "Building";
        if (h < 0.5f && foot > 4f) return "Ground";
        if (h >= 4f && foot <= 2.5f) return "StreetLight";
        if (h >= 1.2f && foot <= 3f) return "StreetFurniture";

        return "Unknown";
    }

    static void RenderSheet(List<ModelType> ordered, StringBuilder log)
    {
        int shown = Mathf.Min(ordered.Count, 48);
        int rows = Mathf.CeilToInt(shown / (float)Columns);

        Texture2D sheet = new Texture2D(Columns * Tile, rows * Tile, TextureFormat.RGB24, false);
        Color32[] clear = new Color32[sheet.width * sheet.height];
        for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(24, 26, 32, 255);
        sheet.SetPixels32(clear);

        GameObject lightGo = new GameObject("__LabelLight");
        Light key = lightGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

        try
        {
            for (int i = 0; i < shown; i++)
            {
                Color32[] thumb = RenderThumb(ordered[i]);
                if (thumb == null) continue;

                int col = i % Columns;
                int row = i / Columns;
                sheet.SetPixels32(col * Tile, sheet.height - (row + 1) * Tile, Tile, Tile, thumb);
            }

            sheet.Apply();
            string path = OutFolder + "/model_types.png";
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            log.AppendLine("contact sheet: " + path);
        }
        finally
        {
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(sheet);
        }
    }

    static Color32[] RenderThumb(ModelType t)
    {
        GameObject temp = new GameObject("__Thumb");
        temp.layer = ScratchLayer;
        temp.transform.position = ScratchOrigin;

        temp.AddComponent<MeshFilter>().sharedMesh = t.SampleMesh;
        MeshRenderer mr = temp.AddComponent<MeshRenderer>();
        if (t.SampleMaterials != null && t.SampleMaterials.Length > 0)
            mr.sharedMaterials = t.SampleMaterials;

        GameObject camGo = new GameObject("__ThumbCam");
        camGo.layer = ScratchLayer;
        Camera cam = camGo.AddComponent<Camera>();

        RenderTexture rt = new RenderTexture(Tile, Tile, 24);
        RenderTexture prev = RenderTexture.active;

        try
        {
            Bounds b = t.SampleMesh.bounds;
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            Vector3 target = ScratchOrigin + b.center;
            Vector3 pos = target + new Vector3(0.7f, 0.5f, 0.7f).normalized * radius * 2.8f;

            cam.transform.SetPositionAndRotation(pos,
                Quaternion.LookRotation((target - pos).normalized, Vector3.up));
            cam.cullingMask = 1 << ScratchLayer;
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

            Color32[] px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PropLabeler] thumb failed mdl_" + t.Id + ": " + e.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(temp);
        }
    }

    // ---------------------------------------------------------------- apply

    static void Apply()
    {
        OpenScene();

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Apply prop labels ===");

        if (!System.IO.File.Exists(MapPath))
        {
            log.AppendLine("ERROR: " + MapPath + " not found. Run 'Catalog Model Types' first.");
            Debug.LogError("[PropLabeler] " + MapPath + " missing");
            try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
            return;
        }

        Dictionary<string, string[]> map = new Dictionary<string, string[]>();
        foreach (string line in System.IO.File.ReadAllLines(MapPath))
        {
            if (string.IsNullOrEmpty(line) || line.StartsWith("modelId")) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 4) continue;

            map[parts[0].Trim()] = parts;
        }

        log.AppendLine("mappings: " + map.Count);

        Transform centre = FindCenter();
        Dictionary<string, ModelType> types = CollectTypes(centre != null ? centre.position : Vector3.zero);

        int labelled = 0, skipped = 0;

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            string id = ModelId(r.gameObject.name);
            if (id == null) { skipped++; continue; }

            string[] row;
            if (!map.TryGetValue(id, out row)) { skipped++; continue; }

            PropLabel label = r.GetComponent<PropLabel>();
            if (label == null) label = r.gameObject.AddComponent<PropLabel>();

            label.modelId = id;
            label.typeName = row[1].Trim();

            PropLabel.PropCategory cat;
            label.category = System.Enum.TryParse(row[2].Trim(), true, out cat)
                ? cat : PropLabel.PropCategory.Unknown;

            bool breakable;
            label.shouldBeBreakable = bool.TryParse(row[3].Trim(), out breakable) && breakable;

            ModelType t;
            if (types.TryGetValue(id, out t))
            {
                label.typicalSize = t.AvgSize;
                label.instancesInScene = t.Count;
            }

            labelled++;
        }

        log.AppendLine("labelled: " + labelled + " renderer(s)");
        log.AppendLine("skipped (no mdl_ id or no mapping): " + skipped);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        log.AppendLine("scene saved");

        log.AppendLine("DONE");
        Debug.Log("[PropLabeler]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    // ---------------------------------------------------------------- shared

    static string ModelId(string name)
    {
        int i = name.IndexOf("mdl_", System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;

        int start = i + 4, end = i + 4;
        while (end < name.Length && char.IsDigit(name[end])) end++;

        return end > start ? name.Substring(start, end - start) : null;
    }

    static void OpenScene()
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
