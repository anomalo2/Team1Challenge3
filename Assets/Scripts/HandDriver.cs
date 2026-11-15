using UnityEngine;
using UnityEngine.XR.Hands;

public class HandDriver : MonoBehaviour
{
    // kill me
    public XRHandTrackingEvents events;
    public Transform xrOrigin;       // the transform the armature/empties sit under
    public Transform wristJoint;     // LeftWristJoint / RightWristJoint empty

    // Index = XRHandJointID.X.ToIndex(). Leave unused entries null
    // (wrist, palm, metacarpals, tips). Do NOT assign the hand bone.
    public Transform[] bones = new Transform[XRHandJointID.EndMarker.ToIndex()];
    public Vector3[] angleOffsets = new Vector3[26];
    Quaternion[] offsets;
    bool calibrate;

    void Awake()
    {
        offsets = new Quaternion[26];
        for (int i = 0; i < offsets.Length; i++) offsets[i] = Quaternion.Euler(angleOffsets[i]);
    }

    void OnEnable() => events.jointsUpdated.AddListener(OnJoints);
    void OnDisable() => events.jointsUpdated.RemoveListener(OnJoints);

    [ContextMenu("Calibrate (hold hand flat, open)")]
    public void Calibrate() => calibrate = true;

    void OnJoints(XRHandJointsUpdatedEventArgs args)
    {
        if (args.hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var w))
            wristJoint.SetPositionAndRotation(
                xrOrigin.TransformPoint(w.position),
                xrOrigin.rotation * w.rotation);

        for (int i = XRHandJointID.Palm.ToIndex() + 1; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            if (!args.hand.GetJoint(XRHandJointIDUtility.FromIndex(i)).TryGetPose(out var p)) continue;

            var tracked = xrOrigin.rotation * p.rotation;
            if (calibrate) offsets[i] = Quaternion.Inverse(tracked) * bones[i].rotation;
            else bones[i].rotation = tracked * offsets[i];
        }
        calibrate = false;
    }
}