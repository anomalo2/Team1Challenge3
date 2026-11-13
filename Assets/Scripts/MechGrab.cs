using UnityEngine;

public class MechGrab : MonoBehaviour
{
    // TODO FIX

    [Header("Triggers")]
    public FingerGrabTrigger thumbTrigger;
    public FingerGrabTrigger indexTrigger;
    public FingerGrabTrigger middleTrigger;
    public FingerGrabTrigger ringTrigger;

    [Header("Hold")]
    [Tooltip("The kinematic Rigidbody on the mech hand.")]
    public Rigidbody handRb;
    [Tooltip("Force at which the object tears free. Infinity = unbreakable.")]
    public float breakForce = Mathf.Infinity;
    public float breakTorque = Mathf.Infinity;

    private bool thumbTriggered, indexTriggered, middleTriggered, ringTriggered;
    private Collider thumbOther, indexOther, middleOther, ringOther;

    private FixedJoint joint;

    public bool IsHolding => joint != null;

    public void GrabObject()
    {
        if (IsHolding || !thumbTriggered) return;

        bool match = (indexTriggered && indexOther == thumbOther) ||
                     (middleTriggered && middleOther == thumbOther) ||
                     (ringTriggered && ringOther == thumbOther);
        if (!match) return;

        Rigidbody rb = thumbOther.attachedRigidbody;
        if (rb == null || rb.isKinematic) return; // a joint can't move a kinematic body

        joint = rb.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = handRb;
        joint.breakForce = breakForce;
        joint.breakTorque = breakTorque;
    }

    public void ReleaseObject()
    {
        if (joint != null) Destroy(joint);
        joint = null;
    }

    private void OnEnable()
    {
        thumbTrigger.OnEnter += Thumb;
        indexTrigger.OnEnter += Index;
        middleTrigger.OnEnter += Middle;
        ringTrigger.OnEnter += Ring;

        thumbTrigger.OnExit += ThumbExit;
        indexTrigger.OnExit += IndexExit;
        middleTrigger.OnExit += MiddleExit;
        ringTrigger.OnExit += RingExit;
    }

    private void OnDisable()
    {
        thumbTrigger.OnEnter -= Thumb;
        indexTrigger.OnEnter -= Index;
        middleTrigger.OnEnter -= Middle;
        ringTrigger.OnEnter -= Ring;

        thumbTrigger.OnExit -= ThumbExit;
        indexTrigger.OnExit -= IndexExit;
        middleTrigger.OnExit -= MiddleExit;
        ringTrigger.OnExit -= RingExit;

        ReleaseObject();
    }

    private void Thumb(Collider o) { if (o.CompareTag("Grab")) { thumbTriggered = true; thumbOther = o; } }
    private void Index(Collider o) { if (o.CompareTag("Grab")) { indexTriggered = true; indexOther = o; } }
    private void Middle(Collider o) { if (o.CompareTag("Grab")) { middleTriggered = true; middleOther = o; } }
    private void Ring(Collider o) { if (o.CompareTag("Grab")) { ringTriggered = true; ringOther = o; } }

    private void ThumbExit(Collider o) { if (o.CompareTag("Grab")) { thumbTriggered = false; thumbOther = null; } }
    private void IndexExit(Collider o) { if (o.CompareTag("Grab")) { indexTriggered = false; indexOther = null; } }
    private void MiddleExit(Collider o) { if (o.CompareTag("Grab")) { middleTriggered = false; middleOther = null; } }
    private void RingExit(Collider o) { if (o.CompareTag("Grab")) { ringTriggered = false; ringOther = null; } }
}