using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class IKFootSolver : MonoBehaviour
{
    public bool isMovingForward;

    [SerializeField] LayerMask terrainLayer = default;
    [SerializeField] Transform body = default;
    [SerializeField] IKFootSolver otherFoot = default;
    [SerializeField] float speed = 4;
    [SerializeField] float stepDistance = .2f;
    [SerializeField] float stepLength = .2f;
    [SerializeField] float sideStepLength = .1f;

    [SerializeField] float stepHeight = .3f;
    [SerializeField] Vector3 footOffset = default;

    public Vector3 footRotOffset;
    public float footYPosOffset = 0.1f;

    public float rayStartYOffset = 0;
    public float rayLength = 1.5f;

    public Vector3 lateralAxis = Vector3.forward;
    public Vector3 forwardAxis = Vector3.right;
    public float spacingMultiplier = 1.5f;

    public UnityEvent onFootstep;

    float footSpacing;
    Vector3 oldPosition, currentPosition, newPosition;
    Vector3 oldNormal, currentNormal, newNormal;
    float lerp;

    private void Start()
    {
        footSpacing = Vector3.Dot(body.InverseTransformPoint(transform.position), lateralAxis);
        currentPosition = newPosition = oldPosition = transform.position;
        currentNormal = newNormal = oldNormal = transform.up;
        lerp = 1;
    }

    // Update is called once per frame

    void Update()
    {
        transform.position = currentPosition + Vector3.up * footYPosOffset;
        transform.localRotation = Quaternion.Euler(footRotOffset);

        //Ray ray = new Ray(body.position + (body.right * footSpacing) + Vector3.up * rayStartYOffset, Vector3.down);

        //Debug.DrawRay(body.position + (body.right * footSpacing) + Vector3.up * rayStartYOffset, Vector3.down);

        Vector3 rayOrigin = body.TransformPoint(lateralAxis * footSpacing * spacingMultiplier)
                  + Vector3.up * rayStartYOffset;

        Ray ray = new Ray(rayOrigin, Vector3.down);
        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, Color.green);

        if (Physics.Raycast(ray, out RaycastHit info, rayLength, terrainLayer.value))
        {
            if (Vector3.Distance(newPosition, info.point) > stepDistance && !otherFoot.IsMoving() && lerp >= 1)
            {
                lerp = 0;
                Vector3 direction = Vector3.ProjectOnPlane(info.point - currentPosition,Vector3.up).normalized;

                //float angle = Vector3.Angle(body.forward, body.InverseTransformDirection(direction));

                //isMovingForward = angle < 50 || angle > 130;

                //if(isMovingForward)
                //{
                //    newPosition = info.point + direction * stepLength + footOffset;
                //    newNormal = info.normal;
                //}
                //else
                //{
                //    newPosition = info.point + direction * sideStepLength + footOffset;
                //    newNormal = info.normal;
                //}
                Vector3 fwd = Vector3.ProjectOnPlane(body.TransformDirection(forwardAxis), Vector3.up).normalized;
                Vector3 side = Vector3.ProjectOnPlane(body.TransformDirection(lateralAxis), Vector3.up).normalized;

                float angle = Vector3.Angle(fwd, direction);
                isMovingForward = angle < 50 || angle > 130;

                if (isMovingForward)
                {
                    float sign = Mathf.Sign(Vector3.Dot(direction, fwd));
                    newPosition = info.point + fwd * sign * stepLength + footOffset;
                }
                else
                {
                    float sign = Mathf.Sign(Vector3.Dot(direction, side));
                    newPosition = info.point + side * sign * sideStepLength + footOffset;
                }
                newNormal = info.normal;

            }
        }

        if (lerp < 1)
        {
            Vector3 tempPosition = Vector3.Lerp(oldPosition, newPosition, lerp);
            tempPosition.y += Mathf.Sin(lerp * Mathf.PI) * stepHeight;

            currentPosition = tempPosition;
            currentNormal = Vector3.Lerp(oldNormal, newNormal, lerp);
            lerp += Time.deltaTime * speed;

            // Step just finished this frame
            if (lerp >= 1)
            {
                onFootstep?.Invoke();
            }
        }
        else
        {
            oldPosition = newPosition;
            oldNormal = newNormal;
        }
    }

    private void OnDrawGizmos()
    {

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(newPosition, 0.1f);
    }



    public bool IsMoving()
    {
        return lerp < 1;
    }



}
