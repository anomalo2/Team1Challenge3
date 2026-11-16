using UnityEditor;
using UnityEditor.SceneManagement;

// TEMPORARY. Batch-mode entry point: opens Eli's mech scene and enters play
// mode; MechLiveTestRunner does the rest and exits the editor.
public static class MechLiveTestLauncher
{
    public static void Run()
    {
        System.Environment.SetEnvironmentVariable("MECH_LIVE_TEST", "1");
        EditorSceneManager.OpenScene("Assets/Scenes/EliTestScene.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
}
