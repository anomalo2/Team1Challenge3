using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// A grab volume. Drop it on an empty GameObject with a TRIGGER collider,
/// then parent that object to a mech hand bone whenever the mech is ready.
/// It needs nothing from the mech but a transform to hang off.
///
/// How grip maps to force (this is the whole game in one curve):
///   grip below grabThreshold      -> open hand, nothing held
///   grip just above the threshold -> object held, almost no squeeze: safe to carry a person
///   grip mid-range                -> enough to crumple sheet metal
///   grip at full                  -> past explodeForceThreshold, the car goes off
///
/// The curve is cubic, so the bottom of the trigger pull is very forgiving.
/// That is what makes "pick up gently or crush" a skill instead of a coin flip.
///
/// Driving it: either assign gripAction, or have your mech script call
/// SetGrip(0..1) every frame. Both work; SetGrip wins if you call it.
/// </summary>
public class MechGrabber : MonoBehaviour
{
    [Header("Hold point")]
    [Tooltip("Where held objects sit. Leave empty to use this transform.")]
    public Transform holdAnchor;

    [Header("Grip")]
    [Tooltip("Grip value at which the hand closes on an object.")]
    [Range(0.05f, 0.95f)] public float grabThreshold = 0.3f;
    [Tooltip("Squeeze force in newtons at full grip. Must exceed a car explodeForceThreshold for full grip to detonate it.")]
    public float maxGripForce = 80000f;
    [Tooltip("Higher = more of the trigger travel is gentle. 3 gives a very forgiving low end.")]
    [Range(1f, 5f)] public float gripForceCurve = 3f;

    [Header("What can be grabbed")]
    public LayerMask grabLayers = ~0;
    [Tooltip("Ignore objects heavier than this. 0 = no limit.")]
    public float maxGrabMass = 0f;

    [Header("Throwing")]
    [Tooltip("Multiplies the hand velocity handed to the object on release.")]
    public float throwVelocityScale = 1f;
    public float maxThrowSpeed = 40f;

    [Header("Input (optional)")]
    [Tooltip("Leave empty if a mech script drives this with SetGrip().")]
    public InputActionProperty gripAction;

    [Header("Events")]
    // Initialized here so they are never null when this component is added at
    // runtime (MechLimbPhysics.AttachAll does that). In a player build nothing
    // serializes a runtime-added component, so a bare field stays null and
    // AddListener throws.
    public UnityEvent onGrabbed = new UnityEvent();
    public UnityEvent onReleased = new UnityEvent();
    [Tooltip("Fires while squeezing hard enough to actually damage what is held.")]
    public UnityEvent onCrushing = new UnityEvent();

    /// <summary>Current grip, 0..1.</summary>
    public float Grip { get; private set; }
    public CrushableObject Held { get; private set; }
    public bool HasSomething => Held != null;

    /// <summary>Squeeze force in newtons being applied right now.</summary>
    public float CurrentGripForce => ComputeGripForce(Grip);

    Transform Anchor => holdAnchor != null ? holdAnchor : transform;

    // Tracked per-collider, not per-object. A car prefab usually has several
    // colliders, and removing the object on the first OnTriggerExit would drop
    // it while the rest of it is still inside the hand.
    readonly List<Collider> overlaps = new List<Collider>();

    Vector3 heldLocalPos;
    Quaternion heldLocalRot;

    Vector3 lastAnchorPos;
    Quaternion lastAnchorRot;
    Vector3 trackedVelocity;
    Vector3 trackedAngularVelocity;

    bool weEnabledAction;

    void Awake()
    {
        lastAnchorPos = Anchor.position;
        lastAnchorRot = Anchor.rotation;
    }

    void OnEnable()
    {
        InputAction a = gripAction.action;
        if (a != null && !a.enabled)
        {
            a.Enable();
            weEnabledAction = true;
        }
    }

    void OnDisable()
    {
        if (weEnabledAction)
        {
            InputAction a = gripAction.action;
            if (a != null && a.enabled) a.Disable();
            weEnabledAction = false;
        }

        if (Held != null) Release();
    }

    /// <summary>Call this from your mech controller with the trigger value, 0..1.</summary>
    public void SetGrip(float value)
    {
        Grip = Mathf.Clamp01(value);
    }

    void Update()
    {
        InputAction a = gripAction.action;
        if (a != null) Grip = Mathf.Clamp01(a.ReadValue<float>());

        PruneOverlaps();

        if (Held == null)
        {
            if (Grip >= grabThreshold) TryGrabClosest();
        }
        else
        {
            // Dropped below the threshold, what we held got wrecked, or
            // something indestructible wriggled out of a too-tight grip.
            if (Grip < grabThreshold || Held.IsDestroyed || Held.IsOverstressed)
            {
                Release();
            }
            else
            {
                float force = CurrentGripForce;
                Held.ApplySustainedCrush(force);

                if (force > Held.crushResistance) onCrushing?.Invoke();
            }
        }
    }

    void LateUpdate()
    {
        // Follow the hand AFTER animation/IK has posed it this frame.
        if (Held != null && !Held.IsDestroyed)
        {
            Transform anchor = Anchor;
            Held.transform.SetPositionAndRotation(
                anchor.TransformPoint(heldLocalPos),
                anchor.rotation * heldLocalRot);
        }

        TrackHandMotion();
    }

    /// <summary>
    /// The mech is animated, not simulated, so its Rigidbody velocity reads zero.
    /// We difference the anchor transform ourselves to get a throw velocity.
    /// </summary>
    void TrackHandMotion()
    {
        Transform anchor = Anchor;
        float dt = Time.deltaTime;

        if (dt > 0f)
        {
            Vector3 v = (anchor.position - lastAnchorPos) / dt;
            trackedVelocity = Vector3.Lerp(trackedVelocity, v, 0.5f);

            Quaternion delta = anchor.rotation * Quaternion.Inverse(lastAnchorRot);
            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);

            if (angleDeg > 180f) angleDeg -= 360f;

            if (axis.sqrMagnitude > 0.0001f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
            {
                Vector3 w = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);
                trackedAngularVelocity = Vector3.Lerp(trackedAngularVelocity, w, 0.5f);
            }
        }

        lastAnchorPos = anchor.position;
        lastAnchorRot = anchor.rotation;
    }

    float ComputeGripForce(float grip)
    {
        if (grip < grabThreshold) return 0f;

        float t = Mathf.InverseLerp(grabThreshold, 1f, grip);
        return maxGripForce * Mathf.Pow(t, gripForceCurve);
    }

    void TryGrabClosest()
    {
        CrushableObject best = null;
        float bestDist = float.MaxValue;
        Vector3 p = Anchor.position;

        // Duplicates are harmless here: several colliders of the same car just
        // resolve to the same object, and we only keep the nearest.
        for (int i = 0; i < overlaps.Count; i++)
        {
            CrushableObject c = CrushableObject.FindOn(overlaps[i]);
            if (c == null || !c.IsGrabbable || c.IsHeld) continue;

            if (maxGrabMass > 0f)
            {
                Rigidbody rb = c.GetComponent<Rigidbody>();
                if (rb != null && rb.mass > maxGrabMass) continue;
            }

            float d = (c.transform.position - p).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (best != null) Grab(best);
    }

    /// <summary>Grab a specific object, ignoring the trigger volume.</summary>
    public void Grab(CrushableObject target)
    {
        if (target == null || !target.IsGrabbable || Held != null) return;

        Held = target;
        target.SetHeld(true);

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Keep the object where it was caught rather than snapping it into the palm.
        // We follow by transform instead of reparenting, so a scaled mech hand
        // cannot stretch the car.
        Transform anchor = Anchor;
        heldLocalPos = anchor.InverseTransformPoint(target.transform.position);
        heldLocalRot = Quaternion.Inverse(anchor.rotation) * target.transform.rotation;

        onGrabbed?.Invoke();
    }

    /// <summary>Let go, handing over the hand velocity so the object can be thrown.</summary>
    public void Release()
    {
        if (Held == null) return;

        CrushableObject released = Held;
        Held = null;

        released.SetHeld(false);

        // Wake before throwing, or a parked body swallows the velocity.
        SleepableBody sleepable = released.GetComponent<SleepableBody>();
        if (sleepable != null) sleepable.Wake();

        Rigidbody rb = released.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;

            Vector3 v = trackedVelocity * throwVelocityScale;
            if (v.magnitude > maxThrowSpeed) v = v.normalized * maxThrowSpeed;

            rb.linearVelocity = v;
            rb.angularVelocity = trackedAngularVelocity * throwVelocityScale;
        }

        onReleased?.Invoke();
    }

    void PruneOverlaps()
    {
        for (int i = overlaps.Count - 1; i >= 0; i--)
        {
            Collider col = overlaps[i];
            if (col == null || !col.gameObject.activeInHierarchy)
            {
                overlaps.RemoveAt(i);
                continue;
            }

            CrushableObject c = CrushableObject.FindOn(col);
            if (c == null || c.IsDestroyed) overlaps.RemoveAt(i);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if ((grabLayers.value & (1 << other.gameObject.layer)) == 0) return;
        if (CrushableObject.FindOn(other) == null) return;

        if (!overlaps.Contains(other)) overlaps.Add(other);
    }

    void OnTriggerExit(Collider other)
    {
        overlaps.Remove(other);
    }
}
