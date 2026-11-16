using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor support for pedestrians. Replaces the old "Tools > Mission" steps,
/// which built crossings out of TrafficRoute (so traffic tools put car
/// spawners on them) and needed two objects selected and a menu run.
///
/// The workflow now is the normal Unity one: drag a prefab in, drag its
/// handles. Prefabs live in Assets/Prefabs/Pedestrians:
///   Grandma               the pedestrian. Drop her by a path and she walks it.
///   PedestrianPath        a crossing: drag the green START and red END.
///   PedestrianCrossings   sends Grandma across each child path, one at a time.
/// The same three are under GameObject > Pedestrians (and the Hierarchy's
/// right-click menu).
/// </summary>
public static class PedestrianTools
{
    public const string Folder = "Assets/Prefabs/Pedestrians";
    public const string GrandmaPath = Folder + "/Grandma.prefab";
    public const string PathPrefabPath = Folder + "/PedestrianPath.prefab";
    public const string SpawnerPrefabPath = Folder + "/PedestrianCrossings.prefab";
    public const string EffectPath = Folder + "/CrossingSuccess.prefab";
    public const string DustPath = "Assets/Prefabs/Effects/PropBreakDust.prefab";
    const string ParticleMatPath = "Assets/Prefabs/Effects/EffectParticle.mat";
    const string SuccessClipPath = "Assets/Mo_UI_Audio/Audio/Successful good deed.mp3";
    const string CharacterModel = "Assets/CharacterAssets/OldMan/OldMan_Walk.fbx";

    // ------------------------------------------------------------ GameObject menu

    [MenuItem("GameObject/Pedestrians/Pedestrian Crossings (one Grandma at a time)", false, 10)]
    static void MenuSpawner(MenuCommand cmd) => Place(SpawnerPrefabPath, cmd, false);

    [MenuItem("GameObject/Pedestrians/Pedestrian Path", false, 11)]
    static void MenuPath(MenuCommand cmd) => Place(PathPrefabPath, cmd, true);

    [MenuItem("GameObject/Pedestrians/Grandma", false, 12)]
    static void MenuGrandma(MenuCommand cmd) => Place(GrandmaPath, cmd, false);

    static GameObject Place(string prefabPath, MenuCommand cmd, bool parentToSpawner)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Pedestrians",
                prefabPath + " is missing. Run Tools > Pedestrians > Rebuild Pedestrian Prefabs.", "OK");
            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Create " + prefab.name);

        GameObject context = cmd != null ? cmd.context as GameObject : Selection.activeGameObject;
        if (parentToSpawner && context != null && context.GetComponentInParent<PedestrianSpawner>() != null)
            Undo.SetTransformParent(go.transform, context.GetComponentInParent<PedestrianSpawner>().transform, "Parent path");

        go.transform.position = GroundAt(SceneView.lastActiveSceneView != null
            ? SceneView.lastActiveSceneView.pivot : Vector3.zero);

        Selection.activeGameObject = go;
        return go;
    }

    /// <summary>
    /// The walkable surface at a point's X/Z, choosing the one nearest the
    /// point's current height - so a point under a flyover stays on the street
    /// instead of jumping onto the bridge deck. The point itself if nothing is there.
    /// </summary>
    public static Vector3 GroundAt(Vector3 p)
    {
        Physics.SyncTransforms();
        Vector3 best = p;
        float bestGap = float.MaxValue;
        foreach (RaycastHit hit in SortedHits(p))
        {
            // Skip characters, cars, props and the mech: stand on the world.
            if (hit.collider.GetComponentInParent<PedestrianCrosser>() != null) continue;
            if (hit.collider.GetComponentInParent<CrushableObject>() != null) continue;
            if (hit.collider.isTrigger) continue;
            int l = hit.collider.gameObject.layer;
            if (l == LayerMask.NameToLayer("Crushable") || l == LayerMask.NameToLayer("Vehicle")
                || l == LayerMask.NameToLayer("MechCollider") || l == LayerMask.NameToLayer("Civilian")) continue;
            float gap = Mathf.Abs(hit.point.y - p.y);
            if (gap < bestGap) { bestGap = gap; best = hit.point; }
        }
        return best;
    }

    static RaycastHit[] SortedHits(Vector3 p)
    {
        RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    // ------------------------------------------------------------ prefab builder

    [MenuItem("Tools/Pedestrians/Rebuild Pedestrian Prefabs")]
    public static void RebuildPrefabs()
    {
        EnsureFolder("Assets/Prefabs", "Pedestrians");
        EnsureFolder("Assets/Prefabs", "Effects");

        Material particleMat = ParticleMaterial();
        GameObject dust = BuildDust(particleMat);
        GameObject effect = BuildSuccessEffect(particleMat);
        GameObject grandma = BuildGrandma(effect);
        BuildPathPrefab();
        BuildSpawnerPrefab(grandma);

        PropClusterBuilder.BreakEffectPrefab = dust;
        AssetDatabase.SaveAssets();
        Debug.Log("[Pedestrians] prefabs rebuilt in " + Folder);
    }

    static Material ParticleMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(ParticleMatPath);
        if (m != null) return m;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        m = new Material(sh);
        m.name = "EffectParticle";
        AssetDatabase.CreateAsset(m, ParticleMatPath);
        return m;
    }

    static ParticleSystem NewSystem(Transform parent, string name, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 0f;
        return ps;
    }

    static GameObject BuildDust(Material mat)
    {
        GameObject root = new GameObject("PropBreakDust");
        ParticleSystem ps = NewSystem(root.transform, "Dust", mat);
        var main = ps.main;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.5f, 0.45f), new Color(0.8f, 0.76f, 0.7f));
        main.gravityModifier = 0.3f;
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.6f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        root.AddComponent<AutoDestroy>().seconds = 3f;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, DustPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    static GameObject BuildSuccessEffect(Material mat)
    {
        GameObject root = new GameObject("CrossingSuccess");

        // Confetti: a burst of bright squares that tumble down.
        ParticleSystem confetti = NewSystem(root.transform, "Confetti", mat);
        confetti.transform.localPosition = Vector3.up * 2f;
        var cm = confetti.main;
        cm.duration = 1f;
        cm.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
        cm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
        cm.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        cm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        cm.gravityModifier = 0.6f;
        Gradient rainbow = new Gradient();
        rainbow.SetKeys(new[] {
            new GradientColorKey(new Color(1f, 0.25f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.1f), 0.25f),
            new GradientColorKey(new Color(0.2f, 0.9f, 0.3f), 0.5f), new GradientColorKey(new Color(0.2f, 0.6f, 1f), 0.75f),
            new GradientColorKey(new Color(0.85f, 0.3f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        cm.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };
        var ce = confetti.emission;
        ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });
        var cs = confetti.shape;
        cs.shapeType = ParticleSystemShapeType.Cone;
        cs.angle = 35f;
        cs.radius = 0.3f;
        confetti.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // cone points up
        var crot = confetti.rotationOverLifetime;
        crot.enabled = true;
        crot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        // A green ring of sparks rising around her.
        ParticleSystem ring = NewSystem(root.transform, "Ring", mat);
        var rm = ring.main;
        rm.duration = 1.5f;
        rm.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
        rm.startSpeed = 0f;
        rm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        rm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 1f, 0.45f), new Color(0.8f, 1f, 0.5f));
        var re = ring.emission;
        re.SetBursts(new[] { new ParticleSystem.Burst(0f, 80), new ParticleSystem.Burst(0.35f, 60) });
        var rs = ring.shape;
        rs.shapeType = ParticleSystemShapeType.Circle;
        rs.radius = 1.4f;
        rs.radiusThickness = 0f;
        ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ring.transform.localPosition = Vector3.up * 0.2f;
        var rv = ring.velocityOverLifetime;
        rv.enabled = true;
        rv.space = ParticleSystemSimulationSpace.World;
        rv.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        rv.y = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        rv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var rsize = ring.sizeOverLifetime;
        rsize.enabled = true;
        rsize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        // Floating text.
        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);
        textGo.transform.localPosition = Vector3.up * 3f;
        TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
        tmp.text = "SAFE!";
        tmp.fontSize = 14f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.35f, 1f, 0.45f);
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = new Color32(0, 60, 10, 255);
        tmp.rectTransform.sizeDelta = new Vector2(8f, 4f);

        // The chime Mo made for exactly this.
        AudioSource audio = root.AddComponent<AudioSource>();
        audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SuccessClipPath);
        audio.playOnAwake = true;
        audio.spatialBlend = 0.5f;
        audio.minDistance = 8f;
        audio.volume = 1f;
        if (audio.clip == null) Debug.LogWarning("[Pedestrians] success sound not found at " + SuccessClipPath);

        CrossingSuccessEffect fx = root.AddComponent<CrossingSuccessEffect>();
        fx.label = tmp;
        fx.lifetime = 4f;

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, EffectPath);
        Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// Grandma prefab from the configured character in the open scene if there
    /// is one (it already has her animator set up), else from the model.
    /// </summary>
    static GameObject BuildGrandma(GameObject effect)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GrandmaPath);

        GameObject source = null;
        foreach (PedestrianCrosser p in Object.FindObjectsByType<PedestrianCrosser>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!PrefabUtility.IsPartOfPrefabAsset(p) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject) != GrandmaPath)
            { source = p.gameObject; break; }

        GameObject inst;
        if (source != null) inst = Object.Instantiate(source);
        else if (existing != null) inst = (GameObject)PrefabUtility.InstantiatePrefab(existing);
        else
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModel);
            if (model == null) { Debug.LogError("[Pedestrians] no character model at " + CharacterModel); return null; }
            inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        }

        inst.name = "Grandma";
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Configure(inst, effect);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, GrandmaPath);
        Object.DestroyImmediate(inst);
        return saved;
    }

    /// <summary>Everything a pedestrian needs. Safe to run twice.</summary>
    public static void Configure(GameObject character, GameObject effect)
    {
        int civilian = LayerMask.NameToLayer("Civilian");
        int vehicle = LayerMask.NameToLayer("Vehicle");

        if (civilian >= 0)
            foreach (Transform t in character.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = civilian;

        Animator anim = character.GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;

        Rigidbody rb = character.GetComponent<Rigidbody>();
        if (rb == null) rb = character.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.isKinematic = true;

        if (character.GetComponent<Collider>() == null)
        {
            CapsuleCollider cap = character.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = 0.3f;
        }

        CrushableObject crush = character.GetComponent<CrushableObject>();
        if (crush == null) crush = character.AddComponent<CrushableObject>();
        crush.displayName = "Elderly pedestrian";
        crush.canBeGrabbed = true;
        crush.canBeDestroyed = false;
        crush.canExplode = false;
        crush.countsAsPropertyDamage = false;
        crush.crushResistance = 400f;
        crush.structuralIntegrity = 8f;
        crush.sustainedCrushRate = 0.02f;
        crush.squashWhenCrushed = false;

        PedestrianCrosser c = character.GetComponent<PedestrianCrosser>();
        if (c == null) c = character.AddComponent<PedestrianCrosser>();
        c.path = null;                   // set by the spawner, or found by proximity
        c.walkSpeed = 0.9f;
        c.retreatSpeed = 1.6f;
        c.waitsForGap = true;
        c.safeGapDistance = 18f;
        c.patience = 12f;
        c.freezesWhenCarNear = true;
        c.panicDistance = 8f;
        c.knockdownSpeed = 4f;
        c.knockdownDuration = 2.5f;
        c.useRagdoll = true;
        c.successEffect = effect;
        if (vehicle >= 0) c.vehicleLayers = 1 << vehicle;
    }

    static void BuildPathPrefab()
    {
        GameObject root = new GameObject("PedestrianPath");
        PedestrianPath path = root.AddComponent<PedestrianPath>();
        GameObject a = new GameObject("Start");
        a.transform.SetParent(root.transform, false);
        GameObject b = new GameObject("End");
        b.transform.SetParent(root.transform, false);
        b.transform.localPosition = new Vector3(0f, 0f, 12f);
        path.points = new List<Transform> { a.transform, b.transform };

        PrefabUtility.SaveAsPrefabAsset(root, PathPrefabPath);
        Object.DestroyImmediate(root);
    }

    static void BuildSpawnerPrefab(GameObject grandma)
    {
        GameObject root = new GameObject("PedestrianCrossings");
        PedestrianSpawner sp = root.AddComponent<PedestrianSpawner>();
        sp.pedestrianPrefab = grandma != null ? grandma.GetComponent<PedestrianCrosser>() : null;

        // Comes with one crossing, so it works the moment it is dropped in.
        GameObject pathPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathPrefabPath);
        GameObject first = (GameObject)PrefabUtility.InstantiatePrefab(pathPrefab, root.transform);
        first.name = "Crossing 1";
        first.transform.localPosition = new Vector3(3f, 0f, 0f);

        PrefabUtility.SaveAsPrefabAsset(root, SpawnerPrefabPath);
        Object.DestroyImmediate(root);
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>Adds a crossing under a spawner, across the view, snapped to the ground.</summary>
    public static PedestrianPath AddPathTo(PedestrianSpawner spawner, Vector3 near)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathPrefabPath);
        GameObject go = prefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, spawner.transform)
            : new GameObject("PedestrianPath", typeof(PedestrianPath));
        Undo.RegisterCreatedObjectUndo(go, "Add Crossing Path");
        go.transform.SetParent(spawner.transform, true);
        go.name = "Crossing " + (spawner.GetComponentsInChildren<PedestrianPath>().Length);
        go.transform.position = GroundAt(near);

        PedestrianPath p = go.GetComponent<PedestrianPath>();
        p.Refresh();
        foreach (Transform t in p.points) t.position = GroundAt(t.position);

        Undo.RecordObject(spawner, "Add Crossing Path");
        spawner.paths.RemoveAll(x => x == null);
        if (spawner.paths.Count > 0) spawner.paths.Add(p);   // explicit list: keep it in step
        EditorUtility.SetDirty(spawner);
        return p;
    }
}

// ===================================================================== inspectors

[CustomEditor(typeof(PedestrianPath))]
public class PedestrianPathEditor : Editor
{
    PedestrianPath Path => (PedestrianPath)target;

    public override void OnInspectorGUI()
    {
        PedestrianPath p = Path;
        p.Refresh();

        PedestrianSpawner owner = p.GetComponentInParent<PedestrianSpawner>();
        EditorGUILayout.HelpBox(
            "Pedestrian path (not traffic: no cars ever use this).\n\n" +
            "In the Scene view drag the GREEN sphere (START, her kerb) and the RED sphere (END, the far pavement). " +
            "Points snap to the ground.\n\n" +
            (owner != null
                ? "Walked by '" + owner.name + "' as crossing #" + (owner.GetComponentsInChildren<PedestrianPath>().Length > 0 ? IndexIn(owner, p) : 0) + "."
                : "Not under a Pedestrian Crossings object: a Grandma placed near the START will walk it."),
            MessageType.Info);

        if (!p.IsValid) EditorGUILayout.HelpBox("Needs at least a START and an END point.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Middle Point")) AddMiddle(p);
            GUI.enabled = p.Count > 2;
            if (GUILayout.Button("Remove Middle Point")) RemoveMiddle(p);
            GUI.enabled = true;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reverse Direction")) Reverse(p);
            if (GUILayout.Button("Snap To Ground")) SnapAll(p);
        }

        EditorGUILayout.LabelField(string.Format("{0} points, {1:0.#} m long", p.Count, p.Length), EditorStyles.miniLabel);
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("color"));
        serializedObject.ApplyModifiedProperties();
    }

    static int IndexIn(PedestrianSpawner s, PedestrianPath p)
    {
        int i = 1;
        foreach (Transform c in s.transform)
        {
            if (c == p.transform) return i;
            if (c.GetComponent<PedestrianPath>() != null) i++;
        }
        return 0;
    }

    void OnSceneGUI()
    {
        PedestrianPath p = Path;
        p.Refresh();

        for (int i = 0; i < p.points.Count; i++)
        {
            Transform t = p.points[i];
            float size = HandleUtility.GetHandleSize(t.position);
            bool first = i == 0, last = i == p.points.Count - 1;
            Handles.color = first ? Color.green : (last ? Color.red : Color.yellow);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(t.position, size * 0.15f, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Move Path Point");
                t.position = PedestrianTools.GroundAt(moved);
            }

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Handles.color } };
            Handles.Label(t.position + Vector3.up * size * 0.4f, first ? "START" : (last ? "END" : "point " + i), style);
        }
    }

    static void AddMiddle(PedestrianPath p)
    {
        int n = p.points.Count;
        if (n < 2) return;
        Vector3 pos = (p.points[n - 2].position + p.points[n - 1].position) * 0.5f;
        GameObject go = new GameObject("Point");
        Undo.RegisterCreatedObjectUndo(go, "Add Middle Point");
        go.transform.SetParent(p.transform, true);
        go.transform.position = PedestrianTools.GroundAt(pos);
        go.transform.SetSiblingIndex(p.points[n - 1].GetSiblingIndex());
        Undo.RecordObject(p, "Add Middle Point");
        p.points.Insert(n - 1, go.transform);
        EditorUtility.SetDirty(p);
    }

    static void RemoveMiddle(PedestrianPath p)
    {
        int n = p.points.Count;
        if (n <= 2) return;
        Transform doomed = p.points[n - 2];
        Undo.RecordObject(p, "Remove Middle Point");
        p.points.RemoveAt(n - 2);
        Undo.DestroyObjectImmediate(doomed.gameObject);
        EditorUtility.SetDirty(p);
    }

    static void Reverse(PedestrianPath p)
    {
        Undo.RecordObject(p, "Reverse Path");
        p.points.Reverse();
        for (int i = 0; i < p.points.Count; i++)
        {
            Undo.SetSiblingIndex(p.points[i], i, "Reverse Path");
            p.points[i].name = i == 0 ? "Start" : (i == p.points.Count - 1 ? "End" : "Point");
        }
        EditorUtility.SetDirty(p);
    }

    static void SnapAll(PedestrianPath p)
    {
        foreach (Transform t in p.points)
        {
            Undo.RecordObject(t, "Snap To Ground");
            t.position = PedestrianTools.GroundAt(t.position);
        }
    }
}

[CustomEditor(typeof(PedestrianSpawner))]
public class PedestrianSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        PedestrianSpawner s = (PedestrianSpawner)target;

        EditorGUILayout.HelpBox(
            "Sends one Grandma at a time across each crossing below, top to bottom. " +
            "When she is safe she celebrates, leaves, and the next one starts.\n\n" +
            "Add Crossing Path, then drag its green START and red END in the Scene view.",
            MessageType.Info);

        if (s.pedestrianPrefab == null)
            EditorGUILayout.HelpBox("No pedestrian prefab. Drag Assets/Prefabs/Pedestrians/Grandma into the field below.", MessageType.Error);

        List<PedestrianPath> list = new List<PedestrianPath>();
        if (s.paths != null && s.paths.Exists(x => x != null)) list.AddRange(s.paths.FindAll(x => x != null));
        else foreach (Transform c in s.transform) { PedestrianPath p = c.GetComponent<PedestrianPath>(); if (p != null) list.Add(p); }

        EditorGUILayout.LabelField("Crossings (" + list.Count + ")", EditorStyles.boldLabel);
        for (int i = 0; i < list.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                PedestrianPath p = list[i];
                EditorGUILayout.LabelField((i + 1) + ". " + p.name, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(p.IsValid ? p.Length.ToString("0.#") + " m" : "INVALID", GUILayout.Width(60));
                if (GUILayout.Button("Select", GUILayout.Width(60))) Selection.activeGameObject = p.gameObject;
            }
        }
        if (list.Count == 0) EditorGUILayout.HelpBox("No crossings yet.", MessageType.Warning);

        if (GUILayout.Button("Add Crossing Path", GUILayout.Height(28)))
        {
            Vector3 near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : s.transform.position;
            Selection.activeGameObject = PedestrianTools.AddPathTo(s, near).gameObject;
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
