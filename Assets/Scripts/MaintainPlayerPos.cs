using Unity.XR.CoreUtils;
using UnityEngine;

public class MaintainPlayerPos : MonoBehaviour
{
    [SerializeField] XROrigin xrOrigin;
    [SerializeField] Transform homePos;
    float maxDistance;
    float lookLerpSpeed;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        maxDistance = 0.005f;
        lookLerpSpeed = 1f;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 xrOriginPos = xrOrigin.Camera.transform.position;
        xrOriginPos.y = homePos.position.y;
        if ((xrOriginPos - homePos.position).magnitude > maxDistance)
        {
            Vector3 target = homePos.position;
            target.y = xrOrigin.Camera.transform.position.y;
            xrOrigin.MoveCameraToWorldLocation(target);
        }
    }

    private void LateUpdate()
    {
        Vector3 mechLookDir = xrOrigin.Camera.transform.forward;
        mechLookDir.y = 0;
        Quaternion targetRotation = Quaternion.LookRotation(mechLookDir) * Quaternion.Euler(0f, 90f, 0f);
        //transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, lookLerpSpeed);

        // AI CODE EWW
        float targetYaw = Quaternion.LookRotation(mechLookDir).eulerAngles.y + 90f;
        float diff = Mathf.DeltaAngle(transform.eulerAngles.y, targetYaw);
        float step = diff * (1f - Mathf.Exp(-lookLerpSpeed * Time.deltaTime));

        transform.RotateAround(homePos.position, Vector3.up, step);
    }
}
