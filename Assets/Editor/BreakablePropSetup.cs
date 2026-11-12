using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns scenery props into things that break when something hits them hard
/// enough: the mech walking into them, a car thrown at them, another prop.
///
/// Strength comes from a per-type preset matched on the prop's name, so a
/// wheelie bin crumples and a stone block does not. Everything routes through
/// CrushableObject, so the same force model covers hands, feet, stomps,
/// thrown cars and prop-on-prop collisions.
///
/// Two entry points:
///   Create Breakable Prop Prefabs - makes breakable variants of the Town Pack
///       props into Assets/Prefabs/Breakables, ready to drop into a scene.
///   Make Selected Breakable - converts whatever is selected in the scene or
///       Project, for props already placed.
/// </summary>
public static class BreakablePropSetup
{
    const string OutputFolder = "Assets/Prefabs/Breakables";
    const string LogPath = "BreakablePropsReport.log";

    /// <summary>Tuning per prop type. Force figures are newtons, matching CrushableObject.</summary>
    class Preset
    {
        public string[] Keywords;
        public string Label;
        public float Resistance;     // below this, nothing happens at all
        public float Integrity;      // how much crush work it absorbs
        public float Value;          // repair bill
        public float Mass;
        public float CrushedHeight;  // how flat it goes
        public bool Explodes;
    }

    // Ordered: first keyword match wins, so put specific before generic.
    static readonly Preset[] Presets =
    {
        new Preset { Keywords = new[]{"traffic_light","trafficlight"}, Label = "traffic light",
                     Resistance = 1500f, Integrity = 8f,  Value = 45f, Mass = 120f, CrushedHeight = 0.2f },
        new Preset { Keywords = new[]{"street_light","streetlight","lamp"}, Label = "street light",
                     Resistance = 1500f, Integrity = 8f,  Value = 40f, Mass = 110f, CrushedHeight = 0.2f },
        new Preset { Keywords = new[]{"hydrant"}, Label = "fire hydrant",
                     Resistance = 4000f, Integrity = 14f, Value = 60f, Mass = 90f,  CrushedHeight = 0.35f },
        new Preset { Keywords = new[]{"bin","trash","bucket"}, Label = "bin",
                     Resistance = 600f,  Integrity = 4f,  Value = 8f,  Mass = 25f,  CrushedHeight = 0.2f },
        new Preset { Keywords = new[]{"bench","chair","table"}, Label = "bench",
                     Resistance = 1200f, Integrity = 7f,  Value = 15f, Mass = 60f,  CrushedHeight = 0.25f },
        new Preset { Keywords = new[]{"air_conditioning","air_con","vent"}, Label = "AC unit",
                     Resistance = 1800f, Integrity = 9f,  Value = 25f, Mass = 70f,  CrushedHeight = 0.3f },
        new Preset { Keywords = new[]{"bush","shrub","flower","plant"}, Label = "bush",
                     Resistance = 200f,  Integrity = 2f,  Value = 4f,  Mass = 12f,  CrushedHeight = 0.15f },
        new Preset { Keywords = new[]{"tree"}, Label = "tree",
                     Resistance = 3500f, Integrity = 18f, Value = 30f, Mass = 400f, CrushedHeight = 0.3f },
        new Preset { Keywords = new[]{"stone","rock","concrete","barrier","block"}, Label = "stone/barrier",
                     Resistance = 7000f, Integrity = 28f, Value = 10f, Mass = 600f, CrushedHeight = 0.5f },
        new Preset { Keywords = new[]{"sign","post","pole","bollard","fence","mailbox","meter"}, Label = "post/sign",
                     Resistance = 1300f, Integrity = 7f,  Value = 20f, Mass = 70f,  CrushedHeight = 0.2f },
    };

    static readonly Preset Fallback = new Preset
    {
        Keywords = new string[0], Label = "generic prop",
        Resistance = 1500f, Integrity = 8f, Value = 15f, Mass = 60f, CrushedHeight = 0.25f
    };

    // Town Pack props worth having breakable versions of.
    static readonly string[] SourceFolders =
    {
        "Assets/EnvironmentAssets/SimplePoly - Town Pack/Prefabs/Props",
        "Assets/EnvironmentAssets/SimplePoly - Town Pack/Prefabs/Nature",
    };

    // ---------------------------------------------------------------- menu

    [MenuItem("Tools/Breakables/Create Breakable Prop Prefabs")]
    public static void CreateBreakablePrefabs()
    {
        List<string> sources = new List<string>();

        foreach (string folder in SourceFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning("[Breakables] missing folder: " + folder);
                continue;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                sources.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        if (sources.Count == 0) { Debug.LogError("[Breakables] no source prefabs found."); return; }

        if (!EditorUtility.DisplayDialog("Create breakable props",
                sources.Count + " prop prefab(s) will get breakable variants in " + OutputFolder +
                ".\n\nThe originals are not modified.",
                "Create", "Cancel"))
            return;

        EnsureFolder();

        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Breakable prop prefabs ===");

        int vehicleFreeLayer = LayerMask.NameToLayer("Crushable");
        int made = 0;

        foreach (string src in sources)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (source == null) continue;

            Preset preset = PresetFor(source.name);

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (inst == null) continue;

            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot,
                InteractionMode.AutomatedAction);

            inst.name = "Breakable_" + source.name;

            string why = Configure(inst, preset, vehicleFreeLayer);

            string outPath = OutputFolder + "/" + inst.name + ".prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, outPath);
            Object.DestroyImmediate(inst);

            if (saved != null)
            {
                made++;
                log.AppendLine(string.Format("  {0,-22} {1,-15} {2}", source.name, preset.Label, why));
            }
        }

        AssetDatabase.SaveAssets();

        log.AppendLine("created: " + made + " breakable prefab(s) in " + OutputFolder);
        log.AppendLine("DONE");

        Debug.Log("[Breakables]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    [MenuItem("Tools/Breakables/Make Selected Breakable")]
    public static void MakeSelectedBreakable()
    {
        GameObject[] selection = Selection.gameObjects;

        if (selection.Length == 0)
        {
            EditorUtility.DisplayDialog("Make breakable",
                "Select the prop objects in the Hierarchy first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Make breakable",
                selection.Length + " selected object(s) will get Rigidbody + collider + " +
                "CrushableObject + CrushOnImpact, and have their Static flags cleared " +
                "(a breakable prop has to be able to move).",
                "Convert", "Cancel"))
            return;

        int layer = LayerMask.NameToLayer("Crushable");
        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Make selected breakable ===");

        Undo.RecordObjects(selection, "Make Breakable");

        int done = 0;
        foreach (GameObject go in selection)
        {
            if (go.GetComponentInChildren<Renderer>(true) == null)
            {
                log.AppendLine("  skipped (no renderer): " + go.name);
                continue;
            }

            Preset preset = PresetFor(go.name);
            string why = Configure(go, preset, layer);

            log.AppendLine(string.Format("  {0,-28} {1,-15} {2}", go.name, preset.Label, why));
            done++;
        }

        log.AppendLine("converted: " + done);
        log.AppendLine("DONE");

        Debug.Log("[Breakables]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }

    // ---------------------------------------------------------------- work

    static string Configure(GameObject go, Preset preset, int crushableLayer)
    {
        // A breakable prop must not be static: static batching bakes the
        // transform, so a flattened or thrown prop would not move.
        GameObjectUtility.SetStaticEditorFlags(go, 0);

        if (crushableLayer >= 0)
        {
            go.layer = crushableLayer;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = crushableLayer;
        }

        Bounds local = LocalBounds(go);

        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            col = box;
        }
        col.isTrigger = false;

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = preset.Mass;
        rb.isKinematic = false;

        // Props stand still until something hits them; sleeping bodies are
        // nearly free, which matters with a lot of them on mobile.
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.1f;

        CrushableObject crush = go.GetComponent<CrushableObject>();
        if (crush == null) crush = go.AddComponent<CrushableObject>();

        crush.displayName = go.name.Replace("Breakable_", "").Replace("sptp_", "").Replace("_", " ");
        crush.canBeDestroyed = true;
        crush.canBeGrabbed = true;
        crush.countsAsPropertyDamage = true;
        crush.propertyValue = preset.Value;
        crush.crushResistance = preset.Resistance;
        crush.structuralIntegrity = preset.Integrity;
        crush.sustainedCrushRate = 0.02f;
        crush.impactCrushRate = 0.004f;
        crush.canExplode = preset.Explodes;
        crush.squashWhenCrushed = true;
        crush.crushedHeightScale = preset.CrushedHeight;
        crush.removeWreckAfter = 0f;

        if (go.GetComponent<CrushOnImpact>() == null)
        {
            CrushOnImpact impact = go.AddComponent<CrushOnImpact>();
            impact.minImpactSpeed = 2.5f;      // props are light, so they break easier
            impact.impactMultiplier = 1f;
        }

        EditorUtility.SetDirty(go);

        return string.Format("res {0:0} / integrity {1:0} / mass {2:0} / size {3}",
            preset.Resistance, preset.Integrity, preset.Mass, local.size.ToString("0.#"));
    }

    static Preset PresetFor(string name)
    {
        string n = name.ToLowerInvariant();

        foreach (Preset p in Presets)
            foreach (string k in p.Keywords)
                if (n.Contains(k)) return p;

        return Fallback;
    }

    static Bounds LocalBounds(GameObject root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

        Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
        bool init = false;
        Bounds result = new Bounds();

        foreach (Renderer r in rends)
        {
            Bounds w = r.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? w.min.x : w.max.x,
                    (i & 2) == 0 ? w.min.y : w.max.y,
                    (i & 4) == 0 ? w.min.z : w.max.z);

                Vector3 p = toLocal.MultiplyPoint3x4(corner);

                if (!init) { result = new Bounds(p, Vector3.zero); init = true; }
                else result.Encapsulate(p);
            }
        }

        return result;
    }

    static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(OutputFolder)) return;

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        AssetDatabase.CreateFolder("Assets/Prefabs", "Breakables");
    }
}
