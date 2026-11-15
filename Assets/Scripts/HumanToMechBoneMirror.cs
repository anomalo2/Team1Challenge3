using Unity.VisualScripting;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class HumanToMechBoneMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 rotateOffset;
    float mechBoneSpeed;

    Quaternion offset;

    private void Start()
    {
        mechBoneSpeed = 0.06f;
        offset = Quaternion.Euler(rotateOffset);
    }

    private void LateUpdate()
    {
        Quaternion targetRotation = Quaternion.Lerp(transform.rotation, playerBone.rotation * offset, mechBoneSpeed);

        transform.rotation = targetRotation;
    }
}
