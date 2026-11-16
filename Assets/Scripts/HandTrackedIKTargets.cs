using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

/// <summary>
/// Lets the pilot's body (EvilModel) - and so the mech, which mirrors it -
/// follow TRACKED HANDS, not only controllers.
///
/// In rigs where the arm IK targets (LeftTarget / RightTarget) are children
/// of the controllers, putting the controllers down froze the targets: the
/// body's arms hung, the mech's arms (copied from the body) hung below the
/// cockpit out of view, and nothing tracked. While a hand is tracked this
/// puts its target on the tracked wrist, with the same wrist-to-target
/// rotation the team's Cool XR Origin uses. When the hand is lost (controllers
/// picked up) the target goes back to its controller.
///
/// Nothing to wire: added after scene load to any XROrigin whose targets sit
/// under a controller. Rigs that already drive targets from the wrist
/// (Cool XR Origin + HandDriver) are left alone.
/// </summary>
[DefaultExecutionOrder(-50)]   // before the Animator evaluates the IK
public class HandTrackedIKTargets : MonoBehaviour
{
    public Transform leftTarget;
    public Transform rightTarget;
    [Tooltip("Target rotation relative to the tracked wrist (matches Cool XR Origin's LeftTarget/RightTarget).")]
    public Vector3 wristToTargetEuler = new Vector3(90f, 0f, 0f);

    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }

    XROrigin origin;
    XRHandSubsystem subsystem;
    Pose leftRest, rightRest;
    static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Install();

    static void Install()
    {
        foreach (XROrigin o in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
        {
            if (o.GetComponent<HandTrackedIKTargets>() != null) continue;

            Transform l = FindUnderController(o.transform, "LeftTarget");
            Transform r = FindUnderController(o.transform, "RightTarget");
            if (l == null && r == null) continue;

            HandTrackedIKTargets h = o.gameObject.AddComponent<HandTrackedIKTargets>();
            h.leftTarget = l;
            h.rightTarget = r;
        }
    }

    static Transform FindUnderController(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name && t.parent != null && t.parent.name.ToLowerInvariant().Contains("controller"))
                return t;
        return null;
    }

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        if (leftTarget != null) leftRest = new Pose(leftTarget.localPosition, leftTarget.localRotation);
        if (rightTarget != null) rightRest = new Pose(rightTarget.localPosition, rightTarget.localRotation);
    }

    void Update()
    {
        if (subsystem == null || !subsystem.running)
        {
            subsystem = null;
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (XRHandSubsystem s in s_Subsystems)
                if (s.running) { subsystem = s; break; }
        }

        LeftTracked = Drive(subsystem != null ? subsystem.leftHand : default, leftTarget, leftRest, LeftTracked);
        RightTracked = Drive(subsystem != null ? subsystem.rightHand : default, rightTarget, rightRest, RightTracked);
    }

    bool Drive(XRHand hand, Transform target, Pose rest, bool wasTracked)
    {
        if (target == null) return false;

        if (subsystem != null && hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))
        {
            // Hand joint poses are in tracking space, which is the XR Origin's
            // camera floor offset object (the parent of the camera and hands).
            Transform space = origin != null && origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform : transform;

            target.SetPositionAndRotation(
                space.TransformPoint(wrist.position),
                space.rotation * wrist.rotation * Quaternion.Euler(wristToTargetEuler));
            return true;
        }

        // Hand lost: give the target back to its controller.
        if (wasTracked)
        {
            target.localPosition = rest.position;
            target.localRotation = rest.rotation;
        }
        return false;
    }
}
