using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the two downloaded Old Man FBXs into a usable walking pedestrian.
///
/// Mixamo exports one file per animation, each carrying its own copy of the
/// skinned mesh. So the Walk file is used as the character and the Idle file is
/// stripped to animation only -- otherwise the same 50 MB mesh is imported
/// twice and shipped twice.
///
/// Both rigs are set to Humanoid so the clips retarget onto one avatar, then a
/// small controller blends Idle and Walk on a Speed parameter that
/// PedestrianCrosser drives from how fast she is actually moving.
/// </summary>
public static class OldManSetup
{
    const string Folder = "Assets/CharacterAssets/OldMan";
    const string WalkPath = Folder + "/OldMan_Walk.fbx";
    const string IdlePath = Folder + "/OldMan_Idle.fbx";
    const string ControllerPath = Folder + "/OldMan.controller";
    const string LogPath = "OldManSetup.log";

    [MenuItem("Tools/Mission/0b - Import Old Man Character")]
    public static void Setup() { Run(); }
    public static void SetupBatch() { Run(); }

    static void Run()
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Old Man character setup ===");

        ModelImporter walk = AssetImporter.GetAtPath(WalkPath) as ModelImporter;
        ModelImporter idle = AssetImporter.GetAtPath(IdlePath) as ModelImporter;

        if (walk == null || idle == null)
        {
            log.AppendLine("ERROR: expected both " + WalkPath + " and " + IdlePath);
            Finish(log);
            return;
        }

        // ---- the Walk file is the character: mesh, rig and avatar
        walk.animationType = ModelImporterAnimationType.Human;
        walk.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        walk.importAnimation = true;
        walk.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        walk.SaveAndReimport();
        log.AppendLine("walk FBX: Humanoid, avatar created from this model");

        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(WalkPath))
        {
            Avatar a = o as Avatar;
            if (a != null) { avatar = a; break; }
        }

        if (avatar == null) log.AppendLine("WARNING: no avatar produced by the walk FBX");
        else log.AppendLine("avatar: " + avatar.name);

        // ---- the Idle file is animation only, retargeted onto that avatar
        idle.animationType = ModelImporterAnimationType.Human;

        if (avatar != null)
        {
            idle.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            idle.sourceAvatar = avatar;
        }

        idle.importAnimation = true;
        idle.materialImportMode = ModelImporterMaterialImportMode.None;   // duplicates of the walk file
        idle.SaveAndReimport();
        log.AppendLine("idle FBX: Humanoid, retargeted onto the walk avatar, materials off");

        // ---- clips, looped
        AnimationClip walkClip = FindClip(WalkPath, log, "walk");
        AnimationClip idleClip = FindClip(IdlePath, log, "idle");

        MakeLooping(walk, walkClip, log);
        MakeLooping(idle, idleClip, log);

        // ---- controller: Idle <-> Walk on Speed
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idleState = sm.AddState("Idle");
        idleState.motion = idleClip;

        AnimatorState walkState = sm.AddState("Walk");
        walkState.motion = walkClip;

        sm.defaultState = idleState;

        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        toWalk.duration = 0.15f;
        toWalk.hasExitTime = false;

        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        toIdle.duration = 0.2f;
        toIdle.hasExitTime = false;

        AssetDatabase.SaveAssets();
        log.AppendLine("controller: " + ControllerPath + "  (Idle <-> Walk on Speed)");

        // ---- place her in the scene
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(WalkPath);
        if (source == null) { log.AppendLine("ERROR: could not load the walk model"); Finish(log); return; }

        GameObject existing = GameObject.Find("Grandma");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
            log.AppendLine("replaced the previous Grandma object");
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source);
        go.name = "Grandma";

        Animator animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;      // PedestrianCrosser drives movement

        // ---- stand her on the crossing path if one exists
        TrafficRoute path = FindCrossingPath();

        if (path != null && path.IsValid)
        {
            go.transform.position = path.GetPosition(0);

            Vector3 facing = path.GetPosition(1) - path.GetPosition(0);
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

            log.AppendLine("placed at the start of '" + path.gameObject.name + "'");
        }
        else log.AppendLine("no crossing path found - she is at the origin, move her onto the path");

        Undo.RegisterCreatedObjectUndo(go, "Create Old Man");
        Selection.activeGameObject = go;

        EditorUtility.SetDirty(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        log.AppendLine("DONE - now select her AND the path, then run '2 - Set Up Grandma Mission'");
        Finish(log);
    }

    static TrafficRoute FindCrossingPath()
    {
        TrafficRoute best = null;

        foreach (TrafficRoute r in Object.FindObjectsByType<TrafficRoute>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("crossing") || n.Contains("pedestrian")) return r;

            if (best == null) best = r;
        }

        return null;   // do not guess a traffic lane as her crossing
    }

    static AnimationClip FindClip(string assetPath, StringBuilder log, string label)
    {
        AnimationClip found = null;

        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            AnimationClip c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview")) continue;

            found = c;
            break;
        }

        log.AppendLine(label + " clip: " + (found != null ? found.name + " (" + found.length.ToString("0.##") + "s)" : "NOT FOUND"));
        return found;
    }

    /// <summary>Mixamo clips arrive unlooped, which makes walking stutter.</summary>
    static void MakeLooping(ModelImporter importer, AnimationClip clip, StringBuilder log)
    {
        if (importer == null || clip == null) return;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = true;
            clips[i].lockRootHeightY = true;   // stops the walk sinking into the road
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        log.AppendLine("  looped " + clips.Length + " clip(s) on " + System.IO.Path.GetFileName(importer.assetPath));
    }

    static void Finish(StringBuilder log)
    {
        Debug.Log("[OldMan]" + System.Environment.NewLine + log);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
