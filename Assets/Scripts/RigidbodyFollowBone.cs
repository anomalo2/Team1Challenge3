using UnityEngine;

public class RigidbodyFollowBone : MonoBehaviour
{
    [SerializeField] Transform bone;
    Rigidbody rb;
    Vector3 localPos;
    Quaternion localRot;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // AI CODE EWWW
        localPos = bone.InverseTransformPoint(transform.position);
        localRot = Quaternion.Inverse(bone.rotation) * transform.rotation;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        //rb.MoveRotation(bone.rotation);
        //rb.MovePosition(bone.position);

        // AI CODE EWWW
        rb.MovePosition(bone.TransformPoint(localPos));
        rb.MoveRotation(bone.rotation * localRot);
    }
}
