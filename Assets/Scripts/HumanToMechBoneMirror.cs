using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

[DefaultExecutionOrder(100)]
public class HumanToMechBoneMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 rotateOffset;
    [SerializeField] GazeCheck gaze;
    [SerializeField] bool lockedRot;
    float mechBoneSpeed;

    Quaternion offset;
    private Quaternion rotCache;

    private void Start()
    {
        mechBoneSpeed = 0.06f;
        offset = Quaternion.Euler(rotateOffset);
        rotCache = transform.localRotation;
    }

    private void LateUpdate()
    {
        if (gaze.IsSpeakerInView())   //check gaze
        {
            if (!lockedRot)
            {
                BoneMirror();
            }
            else
            {
                FingerMirror();
            }
        }
        //Quaternion targetRotation = Quaternion.Lerp(transform.rotation, playerBone.rotation * offset, mechBoneSpeed);

        //transform.rotation = targetRotation;
    }

    private void BoneMirror()
    {
        Quaternion targetRotation = Quaternion.Lerp(transform.rotation, playerBone.rotation * offset, mechBoneSpeed);

        transform.rotation = targetRotation;

    }

    private void FingerMirror()
    {
        //Quaternion target = Quaternion.Euler(playerBone.eulerAngles.x, 0f, 0f);
        //transform.rotation = Quaternion.Slerp(transform.rotation, target, mechBoneSpeed * Time.deltaTime);
        Vector3 rotOffset = new Vector3(0f, 0f, 1f);

        //transform.rotation = Quaternion.AngleAxis(, rotOffset) * rotCache;

        Vector3 flat = Vector3.ProjectOnPlane(playerBone.forward, Vector3.right);
        float angleX = Vector3.SignedAngle(Vector3.forward, flat, Vector3.right);

        Quaternion target = Quaternion.AngleAxis(angleX, Vector3.right) * rotCache;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, mechBoneSpeed); //TODO add time delta time

    }
}
