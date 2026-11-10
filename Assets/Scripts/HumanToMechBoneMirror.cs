using UnityEngine;

public class HumanToMechBoneMirror : MonoBehaviour
{
    [SerializeField] Transform playerBone;

    void Update()
    {
        transform.rotation = playerBone.rotation;
    }
}
