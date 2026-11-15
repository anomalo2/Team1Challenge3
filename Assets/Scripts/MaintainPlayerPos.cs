using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UIElements;

public class MaintainPlayerPos : MonoBehaviour
{
    [SerializeField] XROrigin xrOrigin;
    [SerializeField] Transform homePos;
    float maxDistance;
    float lookLerpSpeed;
    float mechMoveSpeed;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        maxDistance = 0.016f;
        lookLerpSpeed = 2f;
        mechMoveSpeed = 0.2f;
    }

    // Update is called once per frame
    void Update()
    {
        maintainPlayerPos();
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

    private void maintainPlayerPos()
    {
        Vector3 xrOriginPos = xrOrigin.Camera.transform.position;
        xrOriginPos.y = homePos.position.y;
        Vector3 xrVsHomeDiff = xrOriginPos - homePos.position;
        if (xrVsHomeDiff.magnitude > maxDistance)
        {
            transform.position = Vector3.Lerp(transform.position, transform.position + xrVsHomeDiff.normalized * mechMoveSpeed, mechMoveSpeed);
            //transform.position += (xrVsHomeDiff.normalized * mechMoveSpeed);
            Vector3 target = homePos.position;
            target.y = xrOrigin.Camera.transform.position.y;
            xrOrigin.MoveCameraToWorldLocation(target);
        }
    }
}
