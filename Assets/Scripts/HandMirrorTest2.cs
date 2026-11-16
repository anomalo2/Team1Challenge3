using UnityEngine;

[DefaultExecutionOrder(100)]
public class HandMirrorTest2 : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 openRotation;     // mech bone local rotation, hand open
    [SerializeField] Vector3 closedRotation;   // mech bone local rotation, hand closed
    float humanMaxAngle = 90f; // how far the human bone bends when fully closed
    float mechBoneSpeed = 0.06f;

    Quaternion humanRest, openRot, closedRot;

    private void Start()
    {
        humanRest = playerBone.localRotation;
        openRot = Quaternion.Euler(openRotation);
        closedRot = Quaternion.Euler(closedRotation);
    }

    private void LateUpdate()
    {
        Quaternion delta = Quaternion.Inverse(humanRest) * playerBone.localRotation;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        float curl = Mathf.Clamp01(Mathf.Abs(angle) / humanMaxAngle);

        Quaternion target = Quaternion.Slerp(openRot, closedRot, curl);
        transform.localRotation = Quaternion.Lerp(transform.localRotation, target, mechBoneSpeed);
    }
}