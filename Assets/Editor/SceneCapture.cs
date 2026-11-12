using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders the open scene to PNG files from a temporary camera, so the scene
/// can actually be LOOKED AT rather than inferred from bounds and names.
///
/// Three views, which between them answer most "what does it look like"
/// questions:
///   overview  - top-down orthographic of everything, good for layout and
///               for checking traffic lanes sit on the roads
///   player    - from Center Point at roughly head height, i.e. what the
///               player sees
///   oblique   - a 40 degree three-quarter view, good for massing and for
///               spotting holes where culled geometry used to be
///
/// Run with -executeMethod SceneCapture.CaptureBatch and WITHOUT -nographics
/// (rendering needs a graphics device). OPT_SCENE picks the scene.
/// </summary>
public static class SceneCapture
{
    const int Width = 1280;
    const int Height = 720;
    const string OutFolder = "Captures";

    [MenuItem("Tools/Capture/Capture Scene Views")]
    public static void CaptureBatch()
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

        // ---- scene bounds from real renderers
        Bounds all = new Bounds();
        bool init = false;
        List<Renderer> rends = new List<Renderer>();

        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r.GetType().Name == "ParticleSystemRenderer") continue;

            rends.Add(r);

            if (!init) { all = r.bounds; init = true; }
            else all.Encapsulate(r.bounds);
        }

        if (!init)
        {
            Debug.LogError("[Capture] nothing visible to capture.");
            return;
        }

        Debug.Log(string.Format("[Capture] scene {0}: {1} visible renderers, bounds centre {2} size {3}",
            wanted, rends.Count, all.center.ToString("0.#"), all.size.ToString("0.#")));

        float extent = Mathf.Max(all.size.x, all.size.z) * 0.5f;

        // ---- 1. top-down overview
        Render(new Vector3(all.center.x, all.max.y + extent + 50f, all.center.z),
               Quaternion.Euler(90f, 0f, 0f),
               true, extent * 1.05f,
               OutFolder + "/overview_top.png");

        // ---- 1b. same shot with the traffic lanes drawn in.
        // Waypoints are gizmos and gizmos do not render to a camera, so the
        // lanes are rebuilt here as real unlit geometry purely for the capture.
        GameObject overlay = BuildLaneOverlay(extent);

        if (overlay != null)
        {
            try
            {
                Render(new Vector3(all.center.x, all.max.y + extent + 50f, all.center.z),
                       Quaternion.Euler(90f, 0f, 0f),
                       true, extent * 1.05f,
                       OutFolder + "/overview_lanes.png");
            }
            finally { Object.DestroyImmediate(overlay); }
        }
        else Debug.LogWarning("[Capture] no TrafficRoute found, skipping lane overlay");

        // ---- 2. from Center Point, player height
        Transform centre = FindCenter();
        if (centre != null)
        {
            Vector3 eye = centre.position + Vector3.up * 1.6f;
            Vector3 look = new Vector3(all.center.x, eye.y, all.center.z);
            Quaternion rot = (look - eye).sqrMagnitude > 0.01f
                ? Quaternion.LookRotation((look - eye).normalized, Vector3.up)
                : Quaternion.identity;

            Render(eye, rot, false, 0f, OutFolder + "/player_view.png");
            Debug.Log("[Capture] player view from Center Point at " + centre.position.ToString("0.#"));
        }
        else Debug.LogWarning("[Capture] no Center Point found, skipping player view");

        // ---- 3. oblique three-quarter
        float dist = extent * 1.6f + 40f;
        Vector3 dir = new Vector3(0.6f, 0.8f, 0.6f).normalized;
        Vector3 pos = all.center + dir * dist;

        Render(pos, Quaternion.LookRotation((all.center - pos).normalized, Vector3.up),
               false, 0f, OutFolder + "/oblique.png");

        Debug.Log("[Capture] DONE - wrote 3 PNG(s) to " + OutFolder);
    }


    /// <summary>
    /// Rebuilds every TrafficRoute as solid unlit geometry so it shows up in a
    /// render. Temporary: the caller destroys it straight after the capture.
    /// </summary>
    static GameObject BuildLaneOverlay(float sceneExtent)
    {
        TrafficRoute[] routes = Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None);
        if (routes.Length == 0) return null;

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null) unlit = Shader.Find("Unlit/Color");
        if (unlit == null) unlit = Shader.Find("Sprites/Default");

        GameObject root = new GameObject("__LaneOverlay");

        // Scale markers to the scene so they read on a wide shot.
        float dot = Mathf.Max(1.5f, sceneExtent * 0.012f);
        float bar = dot * 0.45f;

        Color[] palette =
        {
            new Color(1f, 0.25f, 0.2f), new Color(0.2f, 1f, 0.35f),
            new Color(0.3f, 0.6f, 1f),  new Color(1f, 0.9f, 0.2f),
            new Color(1f, 0.4f, 1f),    new Color(0.2f, 1f, 1f),
        };

        for (int i = 0; i < routes.Length; i++)
        {
            TrafficRoute route = routes[i];
            if (route.waypoints == null || route.waypoints.Count < 1) continue;

            Material mat = new Material(unlit);
            Color col = palette[i % palette.Length];
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);

            for (int w = 0; w < route.waypoints.Count; w++)
            {
                Transform wp = route.waypoints[w];
                if (wp == null) continue;

                // waypoint marker, taller at the start so direction is readable
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, true);
                cube.transform.position = wp.position + Vector3.up * dot;
                cube.transform.localScale = Vector3.one * (w == 0 ? dot * 2f : dot);
                cube.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(cube.GetComponent<Collider>());

                if (w == 0 || route.waypoints[w - 1] == null) continue;

                // connecting bar, so the path between points is visible
                Vector3 a = route.waypoints[w - 1].position;
                Vector3 b = wp.position;
                Vector3 mid = (a + b) * 0.5f;
                float len = Vector3.Distance(a, b);
                if (len < 0.01f) continue;

                GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                link.transform.SetParent(root.transform, true);
                link.transform.position = mid + Vector3.up * dot;
                link.transform.rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                link.transform.localScale = new Vector3(bar, bar, len);
                link.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(link.GetComponent<Collider>());
            }
        }

        return root;
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

    static void Render(Vector3 position, Quaternion rotation, bool ortho, float orthoSize, string path)
    {
        GameObject camGo = new GameObject("__CaptureCam");
        Camera cam = camGo.AddComponent<Camera>();

        cam.transform.SetPositionAndRotation(position, rotation);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = orthoSize;
        else cam.fieldOfView = 60f;

        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 4000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        cam.allowHDR = false;
        cam.allowMSAA = false;

        RenderTexture rt = new RenderTexture(Width, Height, 24);
        RenderTexture previous = RenderTexture.active;

        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            tex.Apply();

            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            Debug.Log("[Capture] wrote " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Capture] render failed for " + path + ": " + e.Message);
        }
        finally
        {
            RenderTexture.active = previous;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}
