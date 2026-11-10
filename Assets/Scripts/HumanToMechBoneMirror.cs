using UnityEngine;

public class HumanToMechBoneMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;
    [SerializeField] Vector3 rotateOffset;

    Quaternion offset;

    private void Start()
    {
        offset = Quaternion.Euler(rotateOffset);
    }

    void Update()
    {
        transform.rotation = playerBone.rotation * offset;
    }
}
