using UnityEngine;
using UnityEngine.XR.Hands;

public class HandIK : MonoBehaviour
{
    public Handedness handedness; // Left or Right
    public Transform ikTarget;    // Assign the IK Target GameObject here

    private XRHandSubsystem handSubsystem;

    void Update()
    {
        if (handSubsystem == null)
        {
            // Locate the running XR Hand Subsystem
            var subsystems = new System.Collections.Generic.List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            if (subsystems.Count > 0) handSubsystem = subsystems[0];
        }
    }

    // Critical to run after standard tracking updates but before rendering
    void LateUpdate()
    {
        if (handSubsystem == null || !handSubsystem.running) return;

        XRHand hand = handedness == Handedness.Left ? handSubsystem.leftHand : handSubsystem.rightHand;

        if (hand.isTracked)
        {
            // Pull tracking data from the specific Wrist joint
            XRHandJoint wristJoint = hand.GetJoint(XRHandJointID.Wrist);

            if (wristJoint.TryGetPose(out Pose jointPose))
            {
                // Synchronize your Animation Rigging IK Target with real-world spatial data
                ikTarget.position = jointPose.position;
                ikTarget.rotation = jointPose.rotation;
            }
        }
    }
}
