using UnityEngine;

public class GazeCheck : MonoBehaviour
{
    public Transform MainCam;

    public Transform lookTarget;

    [Range(10f, 90f)]
    [Tooltip("Half-angle of cone")]
    public float fovHalfAngle = 35f;

    [Tooltip("Occluding layers (probably dont need):")]
    public LayerMask occluders;

    public bool IsSpeakerInView()
    {
        Vector3 toSpeaker = lookTarget.position - MainCam.position;

        float angle = Vector3.Angle(MainCam.forward, toSpeaker);
        if (angle > fovHalfAngle) return false;

        if (occluders.value != 0 &&
            Physics.Raycast(MainCam.position, toSpeaker.normalized,
                            out RaycastHit hit, toSpeaker.magnitude, occluders))
        {
            return false;
        }

        return true;
    }
}