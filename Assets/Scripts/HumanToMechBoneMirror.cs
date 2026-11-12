using Unity.VisualScripting;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class HumanToMechBoneMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 rotateOffset;
    Rigidbody rb;
    //bool isRigidbody;
    float mechBoneSpeed;

    Quaternion offset;

    private void Start()
    {
        mechBoneSpeed = 0.03f;
        //isRigidbody = false;
        offset = Quaternion.Euler(rotateOffset);
        //if (GetComponent<Rigidbody>())
        //{
        //    rb = GetComponent<Rigidbody>();
        //    isRigidbody = true;
        //}
    }

    private void LateUpdate()
    {
        Quaternion targetRotation = Quaternion.Lerp(transform.rotation, playerBone.rotation * offset, mechBoneSpeed);
        //Quaternion targetRotation = playerBone.rotation * offset;

        transform.rotation = targetRotation;
    }

    //void FixedUpdate()
    //{
     //   if (isRigidbody)
    //    {
    //        rb.MoveRotation(transform.rotation);
    //        rb.MovePosition(transform.position);
    //    }
    //}
}
