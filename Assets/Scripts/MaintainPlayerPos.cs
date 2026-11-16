using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UIElements;

public class MaintainPlayerPos : MonoBehaviour
{
    [SerializeField] XROrigin xrOrigin;
    [SerializeField] Transform homePos;
    float maxDistance;
    float lookLerpSpeed;
    float headLerpSpeed;
    float mechMoveSpeed;


    Vector3 smoothMove;
    float moveSmooth;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        maxDistance = 0.08f;
        lookLerpSpeed = 2f;
        headLerpSpeed = 10f;
        mechMoveSpeed = 8f;
        moveSmooth = 10f;
        Vector3 xrStartPos = homePos.position;
        xrStartPos.y = xrOrigin.Camera.transform.position.y;
        xrOrigin.MoveCameraToWorldLocation(xrStartPos);
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

    /*
    private void maintainPlayerPos()
    {
        Vector3 xrOriginPos = xrOrigin.Camera.transform.position;
        xrOriginPos.y = homePos.position.y;
        Vector3 xrVsHomeDiff = xrOriginPos - homePos.position;
        if (xrVsHomeDiff.magnitude > maxDistance)
        {
            float t = 1f - Mathf.Exp(-headLerpSpeed * Time.deltaTime);
            transform.position += xrVsHomeDiff * t * mechMoveSpeed;
            Vector3 target = homePos.position;
            target.y = xrOrigin.Camera.transform.position.y;
            Vector3 targetLerped = Vector3.Lerp(xrOrigin.Camera.transform.position, target, t);
            xrOrigin.MoveCameraToWorldLocation(targetLerped);

            float t = 1f - Mathf.Exp(-headLerpSpeed * Time.deltaTime);
            transform.position += xrVsHomeDiff * t * mechMoveSpeed;

            xrOrigin.MoveCameraToWorldLocation(xrOrigin.Camera.transform.position + xrVsHomeDiff * t * (mechMoveSpeed - 1f));
        }
    }
    */
    private void maintainPlayerPos()
    {
        Vector3 xrOriginPos = xrOrigin.Camera.transform.position;
        xrOriginPos.y = homePos.position.y;
        Vector3 xrVsHomeDiff = xrOriginPos - homePos.position;

        float t = 1f - Mathf.Exp(-headLerpSpeed * Time.deltaTime);

        // only the part beyond the dead zone, so it ramps in instead of jumping
        Vector3 move = Vector3.zero;
        if (xrVsHomeDiff.magnitude > maxDistance)
            move = xrVsHomeDiff.normalized * (xrVsHomeDiff.magnitude - maxDistance) * t;

        // ease the movement itself
        smoothMove = Vector3.Lerp(smoothMove, move, 1f - Mathf.Exp(-moveSmooth * Time.deltaTime));

        transform.position += smoothMove * mechMoveSpeed;
        xrOrigin.MoveCameraToWorldLocation(xrOrigin.Camera.transform.position + smoothMove * (mechMoveSpeed - 1f));
    }
}
