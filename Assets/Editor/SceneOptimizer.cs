using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Distance-based scene optimiser for mobile/standalone VR hardware.
///
/// Everything is measured from a "center point" object: the place the player
/// actually is. Objects and textures are then judged by how far they are from
/// it, so the budget gets spent on what the player can get close to.
///
/// SAFETY: Analyze is read-only and changes nothing. Each Apply action is a
/// separate, explicit button, and every one prints exactly what it will touch
/// before you press it. Apply actions register Undo where they can, but
/// texture import settings are ASSET-WIDE changes -- commit or stash first.
/// </summary>
public class SceneOptimizer : EditorWindow
{
    const string LogPath = "SceneOptimizeReport.log";

    // --- settings (persisted) ---
    GameObject centerOverride;

    float fullDetailRadius = 60f;
    float downscaleRadius = 120f;
    float transparentCullRadius = 150f;

    int nearMaxTextureSize = 1024;
    int backdropMaxTextureSize = 256;

    bool alsoDisableInsteadOfDelete = true;

    // --- cached analysis ---
    readonly List<TextureUse> textureUses = new List<TextureUse>();
    readonly List<RendererInfo> renderers = new List<RendererInfo>();
    bool analyzed;

    class TextureUse
    {
        public Texture Texture;
        public string Path;
        public float NearestUserDistance = float.MaxValue;
        public int UserCount;
        public long RuntimeBytes;
        public int MaxSize;
        public bool Mipmaps;
        public bool Readable;
        public bool Crunched;
        public bool HasImporter;
        public TextureImporterCompression Compression;
    }

    class RendererInfo
    {
        public Renderer Renderer;
        public float Distance;
        public int Triangles;
        public string Blend;        // Opaque / AlphaTest / Transparent
        public bool IsStatic;
    }

    /// <summary>
    /// Read-only analysis without opening the window, so it can be driven from
    /// -executeMethod in batch mode. Creates the window object but never shows
    /// it; Analyze() touches no GUI.
    /// </summary>
    public static void AnalyzeBatch()
    {
        OpenTargetScene();

        SceneOptimizer inst = ScriptableObject.CreateInstance<SceneOptimizer>();

        try { inst.Analyze(); }
        finally { Object.DestroyImmediate(inst); }
    }

    /// <summary>
    /// Dialog-free apply pass for batch mode. Does the safe, high-value work:
    /// compress textures, downscale backdrop-only textures, mark never-moving
    /// geometry static, enable GPU instancing, pull in the camera far clip.
    /// Does NOT delete anything.
    /// </summary>
    public static void ApplySafeOptimizationsBatch()
    {
        OpenTargetScene();

        SceneOptimizer inst = ScriptableObject.CreateInstance<SceneOptimizer>();
        inst.backdropMaxTextureSize = 512;   // atlas-friendly, see report notes

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Scene Optimizer: APPLY (safe pass) ===");

        try
        {
            inst.Analyze();

            // ---- textures: compression + backdrop downscale, one reimport each
            int compressed = 0, downscaled = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (TextureUse u in inst.textureUses)
                {
                    if (string.IsNullOrEmpty(u.Path)) continue;

                    TextureImporter ti = AssetImporter.GetAtPath(u.Path) as TextureImporter;
                    if (ti == null) continue;

                    bool dirty = false;

                    if (ti.textureCompression == TextureImporterCompression.Uncompressed)
                    {
                        ti.textureCompression = TextureImporterCompression.Compressed;
                        compressed++;
                        dirty = true;
                    }

                    bool backdropOnly = u.NearestUserDistance > inst.downscaleRadius;
                    if (backdropOnly && ti.maxTextureSize > inst.backdropMaxTextureSize)
                    {
                        log.AppendLine(string.Format("  downscale {0} -> {1}  {2}",
                            ti.maxTextureSize, inst.backdropMaxTextureSize, u.Path));
                        ti.maxTextureSize = inst.backdropMaxTextureSize;
                        downscaled++;
                        dirty = true;
                    }

                    if (dirty) ti.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            log.AppendLine("compressed: " + compressed + " texture(s)");
            log.AppendLine("downscaled: " + downscaled + " backdrop texture(s)");

            // ---- GPU instancing on every material in use
            HashSet<Material> seen = new HashSet<Material>();
            int instanced = 0;

            foreach (RendererInfo info in inst.renderers)
            {
                foreach (Material m in info.Renderer.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    if (m.enableInstancing) continue;

                    m.enableInstancing = true;
                    EditorUtility.SetDirty(m);
                    instanced++;
                }
            }
            log.AppendLine("GPU instancing enabled on " + instanced + " material(s)");

            // ---- static flags for never-moving geometry
            int madeStatic = 0;

            int skippedDynamic = 0;

            foreach (RendererInfo info in inst.renderers)
            {
                if (info.IsStatic) continue;

                if (!IsSafeToMakeStatic(info.Renderer)) { skippedDynamic++; continue; }

                GameObjectUtility.SetStaticEditorFlags(info.Renderer.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic);
                madeStatic++;
            }

            log.AppendLine("left dynamic (rig/animated/physics/traffic): " + skippedDynamic);
            log.AppendLine("marked static: " + madeStatic + " renderer(s)");

            // ---- camera far clip, sized to the scene rather than guessed
            float furthest = 0f;
            foreach (RendererInfo info in inst.renderers)
                furthest = Mathf.Max(furthest, info.Distance);

            float newFar = Mathf.Ceil((furthest + 50f) / 50f) * 50f;

            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.farClipPlane <= newFar) continue;

                log.AppendLine(string.Format("  camera '{0}' far clip {1:0} -> {2:0}",
                    cam.name, cam.farClipPlane, newFar));
                cam.farClipPlane = newFar;
                EditorUtility.SetDirty(cam);
            }
            log.AppendLine("furthest geometry from Center Point: " + furthest.ToString("0.#"));

            AssetDatabase.SaveAssets();

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("scene saved");
        }
        finally { Object.DestroyImmediate(inst); }

        log.AppendLine("DONE");
        Debug.Log("[Optimize] apply pass:" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText("SceneOptimizeApply.log", log.ToString()); }
        catch { }
    }

    /// <summary>
    /// Scene the batch entries act on. Set OPT_SCENE to point at a copy so the
    /// shared scene is never modified by accident.
    /// </summary>
    static string TargetScenePath()
    {
        string env = System.Environment.GetEnvironmentVariable("OPT_SCENE");
        return string.IsNullOrEmpty(env) ? "Assets/Scenes/AudreyTesting.unity" : env;
    }

    static void OpenTargetScene()
    {
        string path = TargetScenePath();
        Scene active = SceneManager.GetActiveScene();

        // Never switch scenes on someone working in the editor.
        if (!Application.isBatchMode && active.IsValid() && !string.IsNullOrEmpty(active.path))
            return;

        if (!active.IsValid() || active.path != path)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    /// <summary>
    /// Interactive cull on whatever scene is currently open. Names the scene in
    /// the confirmation, so a shared scene cannot be culled by mistake.
    /// </summary>
    [MenuItem("Tools/Optimize/Cull Distant Transparent (active scene)")]
    public static void CullDistantTransparentActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();

        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Cull transparent", "Open and save a scene first.", "OK");
            return;
        }

        SceneOptimizer inst = ScriptableObject.CreateInstance<SceneOptimizer>();

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Scene Optimizer: CULL DISTANT TRANSPARENT ===");
        log.AppendLine("scene: " + scene.path);

        try
        {
            inst.Analyze();

            List<GameObject> targets = new List<GameObject>();
            int tris = 0;

            foreach (RendererInfo info in inst.renderers)
            {
                if (info.Blend == "Opaque") continue;
                if (info.Distance <= inst.transparentCullRadius) continue;

                targets.Add(info.Renderer.gameObject);
                tris += info.Triangles;

                log.AppendLine(string.Format("  {0,8:0.#} away  {1,-12} {2,6} tris  {3}",
                    info.Distance, info.Blend, info.Triangles, Path(info.Renderer)));
            }

            log.AppendLine(string.Format("cull radius: {0}", inst.transparentCullRadius));

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Cull transparent",
                    "Nothing transparent beyond " + inst.transparentCullRadius + ".", "OK");
                log.AppendLine("nothing to do");
            }
            else
            {
                StringBuilder preview = new StringBuilder();
                for (int i = 0; i < targets.Count && i < 15; i++)
                    preview.AppendLine("  " + targets[i].name);
                if (targets.Count > 15) preview.AppendLine("  ... and " + (targets.Count - 15) + " more");

                string nl = System.Environment.NewLine;
                string message =
                    "SCENE: " + scene.path + nl + nl
                    + targets.Count + " transparent/cutout renderer(s) beyond "
                    + inst.transparentCullRadius + " (" + tris.ToString("n0")
                    + " triangles) will be DISABLED." + nl + nl
                    + "Reversible: re-enable them in the Hierarchy, or undo." + nl + nl
                    + preview;

                bool ok = EditorUtility.DisplayDialog("Cull distant transparent models",
                    message, "Disable them", "Cancel");

                if (!ok)
                {
                    log.AppendLine("cancelled by user");
                }
                else
                {
                    Undo.RecordObjects(targets.ToArray(), "Cull Distant Transparent");
                    foreach (GameObject go in targets) go.SetActive(false);

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);

                    log.AppendLine(string.Format("disabled: {0} renderer(s), {1:n0} triangles",
                        targets.Count, tris));
                    log.AppendLine("scene saved");
                }
            }
        }
        finally { Object.DestroyImmediate(inst); }

        log.AppendLine("DONE");
        Debug.Log("[Optimize] cull pass:" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText("SceneCullReport.log", log.ToString()); }
        catch { }
    }

    /// <summary>
    /// Dialog-free cull of transparent/cutout geometry beyond the cull radius.
    /// DISABLES rather than deletes: same runtime saving, but reversible, so the
    /// visual cost can be judged and undone. Delete is available in the window.
    /// </summary>
    public static void CullDistantTransparentBatch()
    {
        OpenTargetScene();

        SceneOptimizer inst = ScriptableObject.CreateInstance<SceneOptimizer>();

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Scene Optimizer: CULL DISTANT TRANSPARENT ===");
        log.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        try
        {
            inst.Analyze();

            List<GameObject> targets = new List<GameObject>();
            int tris = 0;

            foreach (RendererInfo info in inst.renderers)
            {
                if (info.Blend == "Opaque") continue;
                if (info.Distance <= inst.transparentCullRadius) continue;

                targets.Add(info.Renderer.gameObject);
                tris += info.Triangles;

                log.AppendLine(string.Format("  {0,8:0.#} away  {1,-12} {2,6} tris  {3}",
                    info.Distance, info.Blend, info.Triangles, Path(info.Renderer)));
            }

            log.AppendLine(string.Format("cull radius: {0}", inst.transparentCullRadius));
            log.AppendLine(string.Format("disabled: {0} renderer(s), {1:n0} triangles",
                targets.Count, tris));

            foreach (GameObject go in targets) go.SetActive(false);

            if (targets.Count > 0)
            {
                Scene scene = SceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                log.AppendLine("scene saved");
            }
        }
        finally { Object.DestroyImmediate(inst); }

        log.AppendLine("DONE");
        Debug.Log("[Optimize] cull pass:" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText("SceneCullReport.log", log.ToString()); }
        catch { }
    }

    [MenuItem("Tools/Optimize/Scene Optimizer (mobile)")]
    public static void Open()
    {
        GetWindow<SceneOptimizer>("Scene Optimizer");
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Measures the scene outward from 'center point'. Analyze is read-only. " +
            "Apply buttons each say what they will change first.",
            MessageType.Info);

        EditorGUILayout.LabelField("Center", EditorStyles.boldLabel);
        centerOverride = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Center Point", "Leave empty to auto-find an object named 'center point'."),
            centerOverride, typeof(GameObject), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Radii", EditorStyles.boldLabel);
        fullDetailRadius = EditorGUILayout.FloatField(
            new GUIContent("Full Detail", "Inside this, nothing is touched."), fullDetailRadius);
        downscaleRadius = EditorGUILayout.FloatField(
            new GUIContent("Downscale Beyond", "Textures used only beyond this get shrunk."), downscaleRadius);
        transparentCullRadius = EditorGUILayout.FloatField(
            new GUIContent("Cull Transparent Beyond", "Transparent/cutout models beyond this are removal candidates."),
            transparentCullRadius);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Texture budgets", EditorStyles.boldLabel);
        nearMaxTextureSize = EditorGUILayout.IntPopup("Near Max", nearMaxTextureSize,
            new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
        backdropMaxTextureSize = EditorGUILayout.IntPopup("Backdrop Max", backdropMaxTextureSize,
            new[] { "64", "128", "256", "512" }, new[] { 64, 128, 256, 512 });

        EditorGUILayout.Space();
        GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
        if (GUILayout.Button("ANALYZE (read only)", GUILayout.Height(30))) Analyze();
        GUI.backgroundColor = Color.white;

        if (!analyzed)
        {
            EditorGUILayout.HelpBox("Run Analyze first. Apply buttons stay disabled until then.",
                MessageType.None);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Apply (changes things)", EditorStyles.boldLabel);

        alsoDisableInsteadOfDelete = EditorGUILayout.ToggleLeft(
            new GUIContent("Disable instead of delete (reversible)",
                "Safer: deactivates the objects rather than destroying them."),
            alsoDisableInsteadOfDelete);

        GUI.backgroundColor = new Color(1f, 0.9f, 0.6f);
        if (GUILayout.Button("Downscale backdrop textures")) ApplyTextureDownscale();
        if (GUILayout.Button("Texture hygiene (mipmaps on, read/write off, compress)")) ApplyTextureHygiene();
        if (GUILayout.Button("Mark distant objects static (batching)")) ApplyStaticFlags();

        GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
        if (GUILayout.Button(alsoDisableInsteadOfDelete
                ? "Disable distant transparent models"
                : "DELETE distant transparent models"))
            RemoveDistantTransparent();

        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Report written to " + LogPath, EditorStyles.miniLabel);
    }

    // ---------------------------------------------------------------- analysis

    Transform FindCenter()
    {
        if (centerOverride != null) return centerOverride.transform;

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            string n = go.name.ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (n.Contains("center point") || n.Contains("centre point")
                || n == "centerpoint" || n == "centrepoint")
                return go.transform;
        }

        return null;
    }

    void Analyze()
    {
        textureUses.Clear();
        renderers.Clear();
        analyzed = false;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Scene Optimizer report ===");
        sb.AppendLine("scene: " + SceneManager.GetActiveScene().path);

        Transform center = FindCenter();
        if (center == null)
        {
            sb.AppendLine();
            sb.AppendLine("ERROR: no 'center point' object found in the open scene.");
            sb.AppendLine("Create an empty named 'center point', or assign one in the window.");
            Write(sb.ToString());
            return;
        }

        Vector3 c = center.position;
        sb.AppendLine("center point: '" + center.name + "' at " + c.ToString("0.##"));
        sb.AppendLine(string.Format("radii: full detail {0}, downscale beyond {1}, cull transparent beyond {2}",
            fullDetailRadius, downscaleRadius, transparentCullRadius));
        sb.AppendLine();

        Dictionary<Texture, TextureUse> byTexture = new Dictionary<Texture, TextureUse>();

        int totalTris = 0;
        int opaque = 0, alphaTest = 0, transparent = 0;

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            // Distance to the object's surface, so a big nearby building is not
            // judged by its pivot being far away.
            float dist = Vector3.Distance(c, r.bounds.ClosestPoint(c));

            RendererInfo info = new RendererInfo();
            info.Renderer = r;
            info.Distance = dist;
            info.Triangles = TriangleCount(r);
            info.Blend = "Opaque";
            info.IsStatic = r.gameObject.isStatic;

            totalTris += info.Triangles;

            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;

                string cls = Classify(m);
                if (cls == "Transparent") info.Blend = "Transparent";
                else if (cls == "AlphaTest" && info.Blend != "Transparent") info.Blend = "AlphaTest";

                foreach (Texture t in TexturesOf(m))
                {
                    TextureUse use;
                    if (!byTexture.TryGetValue(t, out use))
                    {
                        use = NewTextureUse(t);
                        byTexture[t] = use;
                    }

                    use.UserCount++;
                    if (dist < use.NearestUserDistance) use.NearestUserDistance = dist;
                }
            }

            if (info.Blend == "Transparent") transparent++;
            else if (info.Blend == "AlphaTest") alphaTest++;
            else opaque++;

            renderers.Add(info);
        }

        textureUses.AddRange(byTexture.Values);
        textureUses.Sort((a, b) => b.RuntimeBytes.CompareTo(a.RuntimeBytes));
        renderers.Sort((a, b) => b.Triangles.CompareTo(a.Triangles));

        long totalBytes = 0;
        foreach (TextureUse u in textureUses) totalBytes += u.RuntimeBytes;

        sb.AppendLine(string.Format("renderers: {0}  (opaque {1}, alpha-test {2}, transparent {3})",
            renderers.Count, opaque, alphaTest, transparent));
        sb.AppendLine(string.Format("triangles: {0:n0}", totalTris));
        sb.AppendLine(string.Format("unique textures: {0}, runtime texture memory: {1:0.0} MB",
            textureUses.Count, totalBytes / 1048576f));
        sb.AppendLine();

        // ---- textures by distance band ----
        sb.AppendLine("--- textures, largest first ---");
        sb.AppendLine("  nearest = distance from center point to the closest object using it");
        long downscaleSaving = 0;
        int downscaleCount = 0;

        foreach (TextureUse u in textureUses)
        {
            bool backdrop = u.HasImporter && u.NearestUserDistance > downscaleRadius;
            int target = backdrop ? backdropMaxTextureSize : nearMaxTextureSize;
            bool wouldShrink = u.MaxSize > target;

            if (backdrop && wouldShrink)
            {
                downscaleCount++;
                // area scales with the square of the dimension
                float ratio = (float)(target * target) / (u.MaxSize * u.MaxSize);
                downscaleSaving += (long)(u.RuntimeBytes * (1f - ratio));
            }

            sb.AppendLine(string.Format(
                "  {0,7:0.0} MB  max {1,5}  nearest {2,8:0.#}  users {3,3}  {4}{5}{6}{7}  {8}",
                u.RuntimeBytes / 1048576f, u.MaxSize,
                u.NearestUserDistance == float.MaxValue ? -1f : u.NearestUserDistance,
                u.UserCount,
                u.Mipmaps ? "" : "[no mips] ",
                u.Readable ? "[read/write ON] " : "",
                !u.HasImporter ? "[embedded, no import settings] "
                    : (u.Compression == TextureImporterCompression.Uncompressed ? "[UNCOMPRESSED] " : ""),
                backdrop && wouldShrink ? "-> " + target : "",
                u.Path));
        }

        sb.AppendLine();
        sb.AppendLine(string.Format("DOWNSCALE CANDIDATES: {0} textures, about {1:0.0} MB saved",
            downscaleCount, downscaleSaving / 1048576f));
        sb.AppendLine("  (only textures whose every user is beyond the downscale radius are listed -");
        sb.AppendLine("   a texture shared with something near the player is left alone)");
        sb.AppendLine();

        // ---- transparent models beyond the cull radius ----
        sb.AppendLine("--- transparent / cutout models beyond " + transparentCullRadius + " ---");
        int cullCount = 0;
        int cullTris = 0;

        foreach (RendererInfo info in renderers)
        {
            if (info.Blend == "Opaque") continue;
            if (info.Distance <= transparentCullRadius) continue;

            cullCount++;
            cullTris += info.Triangles;

            sb.AppendLine(string.Format("  {0,8:0.#} away  {1,-12} {2,7:n0} tris  {3}",
                info.Distance, info.Blend, info.Triangles, Path(info.Renderer)));
        }

        sb.AppendLine(string.Format("CULL CANDIDATES: {0} renderers, {1:n0} triangles", cullCount, cullTris));
        sb.AppendLine("  transparent draws are the expensive ones on mobile GPUs (overdraw, no early-Z)");
        sb.AppendLine();

        // ---- heaviest meshes ----
        sb.AppendLine("--- heaviest 12 renderers by triangles ---");
        for (int i = 0; i < renderers.Count && i < 12; i++)
            sb.AppendLine(string.Format("  {0,8:n0} tris  {1,8:0.#} away  {2}static  {3}",
                renderers[i].Triangles, renderers[i].Distance,
                renderers[i].IsStatic ? "" : "NON-", Path(renderers[i].Renderer)));

        sb.AppendLine();
        AppendMobileChecks(sb, c);

        Write(sb.ToString());
        analyzed = true;
    }

    void AppendMobileChecks(StringBuilder sb, Vector3 center)
    {
        sb.AppendLine("--- general mobile checks ---");

        // lights
        int realtime = 0, shadowed = 0;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.lightmapBakeType != LightmapBakeType.Baked) realtime++;
            if (l.shadows != LightShadows.None) shadowed++;
        }
        sb.AppendLine(string.Format("  lights: {0} realtime/mixed, {1} casting shadows", realtime, shadowed));
        if (realtime > 1)
            sb.AppendLine("    -> mobile wants ONE realtime directional light. Bake the rest.");
        if (shadowed > 1)
            sb.AppendLine("    -> more than one shadow caster is expensive. Shadows from the sun only.");

        // non-static opaque geometry blocks batching
        int nonStaticNear = 0;
        foreach (RendererInfo info in renderers)
            if (!info.IsStatic && info.Blend == "Opaque" && info.Renderer.GetComponent<Animator>() == null)
                nonStaticNear++;

        sb.AppendLine("  non-static opaque renderers: " + nonStaticNear);
        if (nonStaticNear > 0)
            sb.AppendLine("    -> marking never-moving geometry Static enables batching. " +
                          "Use the 'Mark distant objects static' button.");

        // instancing
        HashSet<Material> mats = new HashSet<Material>();
        int noInstancing = 0;
        foreach (RendererInfo info in renderers)
            foreach (Material m in info.Renderer.sharedMaterials)
                if (m != null && mats.Add(m) && !m.enableInstancing) noInstancing++;

        sb.AppendLine(string.Format("  unique materials: {0} ({1} without GPU instancing)",
            mats.Count, noInstancing));
        if (mats.Count > 40)
            sb.AppendLine("    -> each unique material is a draw call. Atlas/merge where you can.");

        // skinned meshes
        int skinned = 0;
        foreach (SkinnedMeshRenderer smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            skinned++;
            if (smr.quality == SkinQuality.Auto || (int)smr.quality > 2)
                sb.AppendLine("    -> " + Path(smr) + ": skin quality " + smr.quality + ", 2 bones is plenty on mobile");
        }
        sb.AppendLine("  skinned mesh renderers: " + skinned);

        // reflection probes / cameras
        sb.AppendLine("  reflection probes: " +
            Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length);

        foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            sb.AppendLine(string.Format("  camera '{0}': far clip {1:0.#}, HDR {2}, MSAA {3}",
                cam.name, cam.farClipPlane, cam.allowHDR, cam.allowMSAA));

            if (cam.farClipPlane > 500f)
                sb.AppendLine("    -> far clip " + cam.farClipPlane.ToString("0") +
                              " draws a lot of nothing. Pull it in to just past your backdrop.");
        }

        // render pipeline asset, read by reflection so this file needs no URP reference
        RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
        if (rp != null)
        {
            sb.AppendLine("  render pipeline: " + rp.name + " (" + AssetDatabase.GetAssetPath(rp) + ")");

            System.Type t = rp.GetType();
            string[] interesting = { "msaaSampleCount", "shadowDistance", "supportsHDR", "renderScale", "shadowCascadeCount" };

            foreach (string prop in interesting)
            {
                System.Reflection.PropertyInfo pi = t.GetProperty(prop);
                if (pi == null) continue;

                object val = null;
                try { val = pi.GetValue(rp); } catch { }
                if (val != null) sb.AppendLine("    " + prop + " = " + val);
            }

            sb.AppendLine("    -> mobile targets: MSAA 2 or 4, shadow distance under ~50, " +
                          "1 shadow cascade, HDR off, render scale <= 1.");
        }

        sb.AppendLine();
        sb.AppendLine("--- ordering advice ---");
        sb.AppendLine("  1. texture hygiene + downscale (biggest memory win, lowest risk)");
        sb.AppendLine("  2. pull the camera far clip in, cut realtime lights/shadows to one");
        sb.AppendLine("  3. mark static, enable instancing");
        sb.AppendLine("  4. cull distant transparent models (do this last, it is the destructive one)");
    }

    // ---------------------------------------------------------------- apply

    void ApplyTextureDownscale()
    {
        List<TextureUse> targets = new List<TextureUse>();

        foreach (TextureUse u in textureUses)
        {
            if (u.NearestUserDistance <= downscaleRadius) continue;
            if (string.IsNullOrEmpty(u.Path)) continue;
            if (u.MaxSize <= backdropMaxTextureSize) continue;

            targets.Add(u);
        }

        if (targets.Count == 0) { Debug.Log("[Optimize] Nothing to downscale."); return; }

        StringBuilder list = new StringBuilder();
        foreach (TextureUse u in targets)
            list.AppendLine("  " + u.MaxSize + " -> " + backdropMaxTextureSize + "  " + u.Path);

        if (!EditorUtility.DisplayDialog("Downscale backdrop textures",
                string.Format("{0} texture import settings will change to max {1}.\n\n" +
                              "This edits .meta files for the whole project and triggers reimport.\n\n{2}",
                    targets.Count, backdropMaxTextureSize,
                    list.Length > 900 ? list.ToString(0, 900) + "..." : list.ToString()),
                "Downscale", "Cancel"))
            return;

        int done = 0;
        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (TextureUse u in targets)
            {
                TextureImporter ti = AssetImporter.GetAtPath(u.Path) as TextureImporter;
                if (ti == null) continue;

                ti.maxTextureSize = backdropMaxTextureSize;
                ti.SaveAndReimport();
                done++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Debug.Log("[Optimize] Downscaled " + done + " backdrop texture(s). Re-run Analyze to see the new totals.");
        analyzed = false;
    }

    void ApplyTextureHygiene()
    {
        List<string> changed = new List<string>();

        foreach (TextureUse u in textureUses)
        {
            if (string.IsNullOrEmpty(u.Path)) continue;

            TextureImporter ti = AssetImporter.GetAtPath(u.Path) as TextureImporter;
            if (ti == null) continue;

            bool dirty = false;

            if (ti.isReadable) { ti.isReadable = false; dirty = true; }          // halves memory
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }     // fixes shimmer + bandwidth
            if (ti.textureCompression == TextureImporterCompression.Uncompressed)
            {
                ti.textureCompression = TextureImporterCompression.Compressed;
                dirty = true;
            }

            if (dirty) changed.Add(u.Path);
        }

        if (changed.Count == 0) { Debug.Log("[Optimize] Texture settings already clean."); return; }

        if (!EditorUtility.DisplayDialog("Texture hygiene",
                changed.Count + " texture(s) will get: Read/Write off, mipmaps on, compression on.\n\n" +
                "Read/Write off halves memory. Mipmaps cost 33% more memory but save far more bandwidth.\n\n" +
                "This edits .meta files project-wide.",
                "Apply", "Cancel"))
            return;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (string path in changed)
            {
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                ti.isReadable = false;
                ti.mipmapEnabled = true;
                if (ti.textureCompression == TextureImporterCompression.Uncompressed)
                    ti.textureCompression = TextureImporterCompression.Compressed;

                ti.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        Debug.Log("[Optimize] Texture hygiene applied to " + changed.Count + " texture(s).");
        analyzed = false;
    }

    void ApplyStaticFlags()
    {
        List<GameObject> targets = new List<GameObject>();

        foreach (RendererInfo info in renderers)
        {
            if (info.IsStatic) continue;
            if (info.Distance <= fullDetailRadius) continue;

            if (!IsSafeToMakeStatic(info.Renderer)) continue;

            targets.Add(info.Renderer.gameObject);
        }

        if (targets.Count == 0) { Debug.Log("[Optimize] Nothing to mark static."); return; }

        if (!EditorUtility.DisplayDialog("Mark static",
                targets.Count + " distant object(s) beyond " + fullDetailRadius +
                " will be marked Static for batching.\n\nAnimators, rigidbodies, cars and pedestrians are skipped.",
                "Mark Static", "Cancel"))
            return;

        Undo.RecordObjects(targets.ToArray(), "Mark Static");

        foreach (GameObject go in targets)
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        Debug.Log("[Optimize] Marked " + targets.Count + " object(s) static.");
        analyzed = false;
    }

    void RemoveDistantTransparent()
    {
        List<GameObject> targets = new List<GameObject>();
        int tris = 0;

        foreach (RendererInfo info in renderers)
        {
            if (info.Blend == "Opaque") continue;
            if (info.Distance <= transparentCullRadius) continue;

            targets.Add(info.Renderer.gameObject);
            tris += info.Triangles;
        }

        if (targets.Count == 0) { Debug.Log("[Optimize] No distant transparent models found."); return; }

        StringBuilder list = new StringBuilder();
        for (int i = 0; i < targets.Count && i < 25; i++) list.AppendLine("  " + Path(targets[i]));
        if (targets.Count > 25) list.AppendLine("  ... and " + (targets.Count - 25) + " more");

        string verb = alsoDisableInsteadOfDelete ? "DISABLED" : "DELETED";

        if (!EditorUtility.DisplayDialog("Cull distant transparent models",
                string.Format("{0} renderer(s) ({1:n0} triangles) beyond {2} will be {3}.\n\n{4}",
                    targets.Count, tris, transparentCullRadius, verb, list),
                alsoDisableInsteadOfDelete ? "Disable" : "Delete", "Cancel"))
            return;

        if (alsoDisableInsteadOfDelete)
        {
            Undo.RecordObjects(targets.ToArray(), "Disable Distant Transparent");
            foreach (GameObject go in targets) go.SetActive(false);
        }
        else
        {
            foreach (GameObject go in targets) Undo.DestroyObjectImmediate(go);
        }

        Debug.Log("[Optimize] " + targets.Count + " distant transparent object(s) " + verb.ToLowerInvariant() + ".");
        analyzed = false;
    }

    /// <summary>
    /// Only geometry that provably never moves may be marked static. Static
    /// batching bakes transforms, so flagging a moving object makes it render
    /// in the wrong place. Walks the whole parent chain, because a static flag
    /// on a child of a moving rig is just as broken.
    /// </summary>
    static bool IsSafeToMakeStatic(Renderer r)
    {
        if (r == null) return false;
        if (r is SkinnedMeshRenderer) return false;   // always animated

        Transform t = r.transform;

        while (t != null)
        {
            GameObject go = t.gameObject;

            if (go.GetComponent<Animator>() != null) return false;
            if (go.GetComponent<Animation>() != null) return false;
            if (go.GetComponent<Rigidbody>() != null) return false;
            if (go.GetComponent<CharacterController>() != null) return false;
            if (go.GetComponent<Camera>() != null) return false;
            if (go.GetComponent<CarController>() != null) return false;
            if (go.GetComponent<PedestrianCrosser>() != null) return false;
            if (go.GetComponent<CrushableObject>() != null) return false;
            if (go.GetComponent<MechGrabber>() != null) return false;
            if (go.GetComponent<MechFootCrusher>() != null) return false;
            if (go.GetComponent<ModelDisplacement>() != null) return false;
            if (go.GetComponent<HandIK>() != null) return false;

            // The XR rig and its interactors move every frame.
            string n = go.name.ToLowerInvariant();
            if (n.Contains("xr origin") || n.Contains("xr rig") || n.Contains("camera offset")
                || n.Contains("controller") || n.Contains("interactor") || n.Contains("hand"))
                return false;

            t = t.parent;
        }

        return true;
    }

    // ---------------------------------------------------------------- helpers

    static TextureUse NewTextureUse(Texture t)
    {
        TextureUse use = new TextureUse();
        use.Texture = t;
        use.Path = AssetDatabase.GetAssetPath(t);
        use.RuntimeBytes = Profiler.GetRuntimeMemorySizeLong(t);

        TextureImporter ti = string.IsNullOrEmpty(use.Path)
            ? null
            : AssetImporter.GetAtPath(use.Path) as TextureImporter;

        if (ti != null)
        {
            use.MaxSize = ti.maxTextureSize;
            use.Mipmaps = ti.mipmapEnabled;
            use.Readable = ti.isReadable;
            use.Compression = ti.textureCompression;
            use.Crunched = ti.crunchedCompression;
            use.HasImporter = true;
        }
        else
        {
            use.MaxSize = Mathf.Max(t.width, t.height);
            use.Mipmaps = true;
        }

        return use;
    }

    static IEnumerable<Texture> TexturesOf(Material m)
    {
        List<Texture> found = new List<Texture>();
        if (m == null || m.shader == null) return found;

        int count = m.shader.GetPropertyCount();

        for (int i = 0; i < count; i++)
        {
            if (m.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;

            string name = m.shader.GetPropertyName(i);
            if (!m.HasProperty(name)) continue;

            Texture t = m.GetTexture(name);
            if (t != null && !found.Contains(t)) found.Add(t);
        }

        return found;
    }

    static string Classify(Material m)
    {
        if (m == null) return "Opaque";

        if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) return "Transparent";
        if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) return "AlphaTest";

        string tag = m.GetTag("RenderType", false, "");
        if (tag == "Transparent") return "Transparent";
        if (tag == "TransparentCutout") return "AlphaTest";

        int q = m.renderQueue;
        if (q >= 3000) return "Transparent";
        if (q >= 2450) return "AlphaTest";

        return "Opaque";
    }

    static int TriangleCount(Renderer r)
    {
        Mesh mesh = null;

        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf != null) mesh = mf.sharedMesh;

        SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
        if (smr != null) mesh = smr.sharedMesh;

        if (mesh == null) return 0;

        int tris = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetIndexCount(s) / 3);

        return tris;
    }

    static string Path(Component c)
    {
        if (c == null) return "<null>";

        string path = c.gameObject.name;
        Transform t = c.transform.parent;

        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }

        return path;
    }

    static string Path(GameObject go)
    {
        return go == null ? "<null>" : Path(go.transform);
    }

    static void Write(string text)
    {
        Debug.Log("[Optimize]\n" + text);
        try { System.IO.File.WriteAllText(LogPath, text); }
        catch (System.Exception e) { Debug.LogWarning("[Optimize] log write failed: " + e.Message); }
    }
}
