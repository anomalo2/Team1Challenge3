using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

[DefaultExecutionOrder(100)]
public class MechFingerMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 rotateOffset;
    float mechBoneSpeed;

    Quaternion offset;
    Quaternion humanRest;
    Quaternion mechRest;


    private void Start()
    {
        mechBoneSpeed = 0.06f;
        offset = Quaternion.Euler(rotateOffset);
        humanRest = playerBone.localRotation;
        mechRest = transform.localRotation;
    }

    private void LateUpdate()
    {
        Quaternion delta = Quaternion.Inverse(humanRest) * playerBone.localRotation;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        float curl = angle * Vector3.Dot(axis, Vector3.right);

        Quaternion targetRotation = Quaternion.Lerp(transform.localRotation, mechRest * Quaternion.AngleAxis(curl, Vector3.right), mechBoneSpeed);

        transform.localRotation = targetRotation;
    }
}
