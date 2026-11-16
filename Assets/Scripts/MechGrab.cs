using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MechGrab : MonoBehaviour
{
    // TODO FIX

    //[Header("Triggers")]
    //public FingerGrabTrigger thumbTrigger;
    //public FingerGrabTrigger indexTrigger;
    //public FingerGrabTrigger middleTrigger;
    //public FingerGrabTrigger ringTrigger;

    //[Header("Hold")]
    //public Rigidbody handRb;
    //public float breakForce = Mathf.Infinity;
    //public float breakTorque = Mathf.Infinity;

    //private bool thumbTriggered, indexTriggered, middleTriggered, ringTriggered;
    //private Collider thumbOther, indexOther, middleOther, ringOther;

    //private FixedJoint joint;

    //public bool IsHolding => joint != null;

    //public void GrabObject()
    //{
    //    if (IsHolding || !thumbTriggered) return;

    //    bool match = (indexTriggered && indexOther == thumbOther) ||
    //                 (middleTriggered && middleOther == thumbOther) ||
    //                 (ringTriggered && ringOther == thumbOther);
    //    if (!match) return;

    //    Rigidbody rb = thumbOther.attachedRigidbody;
    //    if (rb == null || rb.isKinematic) return; // a joint can't move a kinematic body

    //    joint = rb.gameObject.AddComponent<FixedJoint>();
    //    joint.connectedBody = handRb;
    //    joint.breakForce = breakForce;
    //    joint.breakTorque = breakTorque;
    //}

    //public void ReleaseObject()
    //{
    //    if (joint != null) Destroy(joint);
    //    joint = null;
    //}

    //private void OnEnable()
    //{
    //    thumbTrigger.OnEnter += Thumb;
    //    indexTrigger.OnEnter += Index;
    //    middleTrigger.OnEnter += Middle;
    //    ringTrigger.OnEnter += Ring;

    //    thumbTrigger.OnExit += ThumbExit;
    //    indexTrigger.OnExit += IndexExit;
    //    middleTrigger.OnExit += MiddleExit;
    //    ringTrigger.OnExit += RingExit;
    //}

    //private void OnDisable()
    //{
    //    thumbTrigger.OnEnter -= Thumb;
    //    indexTrigger.OnEnter -= Index;
    //    middleTrigger.OnEnter -= Middle;
    //    ringTrigger.OnEnter -= Ring;

    //    thumbTrigger.OnExit -= ThumbExit;
    //    indexTrigger.OnExit -= IndexExit;
    //    middleTrigger.OnExit -= MiddleExit;
    //    ringTrigger.OnExit -= RingExit;

    //    ReleaseObject();
    //}

    //private void Thumb(Collider o) { if (o.CompareTag("Grab")) { thumbTriggered = true; thumbOther = o; } }
    //private void Index(Collider o) { if (o.CompareTag("Grab")) { indexTriggered = true; indexOther = o; } }
    //private void Middle(Collider o) { if (o.CompareTag("Grab")) { middleTriggered = true; middleOther = o; } }
    //private void Ring(Collider o) { if (o.CompareTag("Grab")) { ringTriggered = true; ringOther = o; } }

    //private void ThumbExit(Collider o) { if (o.CompareTag("Grab")) { thumbTriggered = false; thumbOther = null; } }
    //private void IndexExit(Collider o) { if (o.CompareTag("Grab")) { indexTriggered = false; indexOther = null; } }
    //private void MiddleExit(Collider o) { if (o.CompareTag("Grab")) { middleTriggered = false; middleOther = null; } }
    //private void RingExit(Collider o) { if (o.CompareTag("Grab")) { ringTriggered = false; ringOther = null; } }

    [Header("Triggers")]
    public FingerGrabTrigger thumbTrigger;
    public FingerGrabTrigger indexTrigger;
    public FingerGrabTrigger middleTrigger;
    public FingerGrabTrigger ringTrigger;

    [Header("Hold")]
    public Rigidbody handRb;
    public float breakForce = Mathf.Infinity;
    public float breakTorque = Mathf.Infinity;

    [Tooltip("Grab automatically when the thumb and another finger touch the same body, and release when they no longer do.")]
    public bool autoGrab = true;

    private readonly Dictionary<Rigidbody, int> thumbSet = new();
    private readonly Dictionary<Rigidbody, int> indexSet = new();
    private readonly Dictionary<Rigidbody, int> middleSet = new();
    private readonly Dictionary<Rigidbody, int> ringSet = new();

    private FixedJoint joint;
    private Rigidbody heldRb;
    public bool IsHolding => joint != null;

    private void Update()
    {
        if (!autoGrab) return;

        if (!IsHolding) GrabObject();
        else if (!IsPinched(heldRb)) ReleaseObject();
    }

    private bool IsPinched(Rigidbody rb)
    {
        return thumbSet.ContainsKey(rb) &&
               (indexSet.ContainsKey(rb) || middleSet.ContainsKey(rb) || ringSet.ContainsKey(rb));
    }

    public void GrabObject()
    {
        if (IsHolding) return;

        if (handRb == null)
        {
            Debug.LogError("handRb is not assigned on MechGrab!", this);
            return;
        }

        foreach (var rb in thumbSet.Keys)
        {
            if (!IsPinched(rb) || rb.isKinematic) continue;

            joint = rb.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = handRb;
            joint.breakForce = breakForce;
            joint.breakTorque = breakTorque;
            heldRb = rb;
            Debug.Log($"Grabbed {rb.name}", this);
            return;
        }
    }

    public void ReleaseObject()
    {
        if (joint != null) Destroy(joint);
        joint = null;
        heldRb = null;
    }

    private void OnEnable()
    {
        Debug.Log("MechGrab enabled", this);

        thumbTrigger.OnEnter += ThumbEnter;
        indexTrigger.OnEnter += IndexEnter;
        middleTrigger.OnEnter += MiddleEnter;
        ringTrigger.OnEnter += RingEnter;

        thumbTrigger.OnExit += ThumbExit;
        indexTrigger.OnExit += IndexExit;
        middleTrigger.OnExit += MiddleExit;
        ringTrigger.OnExit += RingExit;
    }

    private void OnDisable()
    {
        thumbTrigger.OnEnter -= ThumbEnter;
        indexTrigger.OnEnter -= IndexEnter;
        middleTrigger.OnEnter -= MiddleEnter;
        ringTrigger.OnEnter -= RingEnter;

        thumbTrigger.OnExit -= ThumbExit;
        indexTrigger.OnExit -= IndexExit;
        middleTrigger.OnExit -= MiddleExit;
        ringTrigger.OnExit -= RingExit;

        ReleaseObject();
        thumbSet.Clear(); indexSet.Clear(); middleSet.Clear(); ringSet.Clear();
    }

    private void ThumbEnter(Collider c) => Add(thumbSet, c, "thumb");
    private void IndexEnter(Collider c) => Add(indexSet, c, "index");
    private void MiddleEnter(Collider c) => Add(middleSet, c, "middle");
    private void RingEnter(Collider c) => Add(ringSet, c, "ring");

    private void ThumbExit(Collider c) => Remove(thumbSet, c);
    private void IndexExit(Collider c) => Remove(indexSet, c);
    private void MiddleExit(Collider c) => Remove(middleSet, c);
    private void RingExit(Collider c) => Remove(ringSet, c);

    private static void Add(Dictionary<Rigidbody, int> set, Collider c, string finger)
    {
        Debug.Log($"{finger} trigger enter: {c.name}, tag:{c.tag}, rb:{c.attachedRigidbody}");
        if (!c.CompareTag("Grab")) return;
        Rigidbody rb = c.attachedRigidbody;
        if (rb == null) return;
        set.TryGetValue(rb, out int n);
        set[rb] = n + 1;
    }

    private static void Remove(Dictionary<Rigidbody, int> set, Collider c)
    {
        if (!c.CompareTag("Grab")) return;
        Rigidbody rb = c.attachedRigidbody;
        if (rb == null || !set.TryGetValue(rb, out int n)) return;
        if (n <= 1) set.Remove(rb); else set[rb] = n - 1;
    }
}