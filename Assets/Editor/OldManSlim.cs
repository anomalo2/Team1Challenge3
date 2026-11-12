using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Halves what the Old Man character costs the repository.
///
/// Mixamo exports one FBX per animation, each carrying its own full copy of the
/// skinned mesh. OldMan_Idle.fbx is 53 MB of which only the animation curves are
/// ever used -- the mesh in it is a byte-for-byte second copy of the one in
/// OldMan_Walk.fbx. Import settings cannot help: the bytes are in the file, so
/// the file has to go.
///
/// So: lift the idle clip out into a standalone .anim asset, repoint the
/// Animator controller at it, then delete the FBX. Same behaviour on screen,
/// roughly half the Git LFS footprint.
/// </summary>
public static class OldManSlim
{
    const string Folder = "Assets/CharacterAssets/OldMan";
    const string IdleFbx = Folder + "/OldMan_Idle.fbx";
    const string IdleClip = Folder + "/OldMan_Idle.anim";
    const string ControllerPath = Folder + "/OldMan.controller";
    const string LogPath = "OldManSlim.log";

    [MenuItem("Tools/Mission/0c - Slim Old Man Import")]
    public static void Slim() { Run(); }
    public static void SlimBatch() { Run(); }

    static void Run()
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine("=== Slim Old Man import ===");

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (controller == null)
        {
            log.AppendLine("ERROR: no controller at " + ControllerPath);
            Finish(log);
            return;
        }

        // Already done on an earlier run? Then there is nothing to do.
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip) != null
            && AssetImporter.GetAtPath(IdleFbx) == null)
        {
            log.AppendLine("already slimmed: .anim present, FBX gone");
            Finish(log);
            return;
        }

        // ---- 1. find the idle clip inside the FBX
        AnimationClip source = null;

        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(IdleFbx))
        {
            AnimationClip c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview")) continue;

            source = c;
            break;
        }

        if (source == null)
        {
            log.AppendLine("ERROR: no animation clip inside " + IdleFbx);
            Finish(log);
            return;
        }

        log.AppendLine("source clip: " + source.name
                       + "  (" + source.length.ToString("0.##") + "s, humanoid="
                       + source.isHumanMotion + ")");

        // ---- 2. copy it out as its own asset
        AnimationClip copy = Object.Instantiate(source);
        copy.name = "OldMan_Idle";

        AssetDatabase.CreateAsset(copy, IdleClip);

        // Mixamo clips arrive unlooped, which makes standing still twitch.
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);

        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssets();

        log.AppendLine("extracted to " + IdleClip + "  (looping)");

        // ---- 3. point every state that used the FBX clip at the copy
        int repointed = 0;

        foreach (AnimatorControllerLayer layer in controller.layers)
            repointed += Repoint(layer.stateMachine, source, copy, log);

        if (repointed == 0)
            log.AppendLine("WARNING: no controller state referenced the FBX clip");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // ---- 4. the FBX itself
        long bytes = 0;
        try { bytes = new System.IO.FileInfo(IdleFbx).Length; } catch { }

        if (AssetDatabase.DeleteAsset(IdleFbx))
            log.AppendLine("deleted " + IdleFbx
                           + "  (" + (bytes / 1048576f).ToString("0.#") + " MB reclaimed)");
        else
            log.AppendLine("ERROR: could not delete " + IdleFbx);

        AssetDatabase.Refresh();

        // ---- 5. prove the controller still resolves
        foreach (AnimatorControllerLayer layer in controller.layers)
            Verify(layer.stateMachine, log);

        Finish(log);
    }

    static int Repoint(AnimatorStateMachine sm, AnimationClip from, AnimationClip to, StringBuilder log)
    {
        int n = 0;

        foreach (ChildAnimatorState child in sm.states)
        {
            if (child.state == null) continue;

            if (child.state.motion == from)
            {
                child.state.motion = to;
                log.AppendLine("repointed state '" + child.state.name + "' at the .anim");
                n++;
            }
        }

        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            if (child.stateMachine != null)
                n += Repoint(child.stateMachine, from, to, log);

        return n;
    }

    static void Verify(AnimatorStateMachine sm, StringBuilder log)
    {
        foreach (ChildAnimatorState child in sm.states)
        {
            if (child.state == null) continue;

            Motion m = child.state.motion;
            log.AppendLine("  state '" + child.state.name + "' -> "
                           + (m == null ? "MISSING" : m.name + " @ " + AssetDatabase.GetAssetPath(m)));
        }

        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            if (child.stateMachine != null) Verify(child.stateMachine, log);
    }

    static void Finish(StringBuilder log)
    {
        log.AppendLine("DONE");
        Debug.Log("[OldManSlim]" + System.Environment.NewLine + log);

        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
}
