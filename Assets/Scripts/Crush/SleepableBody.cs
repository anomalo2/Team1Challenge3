using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Physics level-of-detail for a single object.
///
/// Scenery sits still almost all of the time, so simulating it is wasted work.
/// An object with this component is KINEMATIC until something actually happens
/// to it, becomes fully simulated when disturbed, and returns to kinematic once
/// it has settled or the player has gone away.
///
/// Three ways it goes back to sleep:
///   out of range   - past PhysicsSleepDirector's radius it cannot be reached
///   settled        - moving slower than settleSpeed for settleTime
///   never woken    - it simply starts asleep
///
/// Three ways it wakes:
///   collision      - something hit it, including a thrown prop or car
///   Wake()         - called by the crush, grab and impact systems
///   force applied  - WakeAndPush for explosions
///
/// Sleeping out of range matters beyond tidiness: collision only exists within
/// a radius of the player, so a body that went dynamic out there would fall
/// through the world. Keeping it kinematic holds it in place.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SleepableBody : MonoBehaviour
{
    [Header("Settling")]
    [Tooltip("Below this speed the object counts as at rest.")]
    public float settleSpeed = 0.15f;
    [Tooltip("Seconds at rest before it goes back to kinematic. 0 = never settle.")]
    public float settleTime = 3f;

    [Header("Waking")]
    [Tooltip("Wake when something collides with this. Needs a non-trigger collider.")]
    public bool wakeOnCollision = true;
    [Tooltip("Ignore gentle taps: collisions slower than this do not wake it.")]
    public float wakeImpactSpeed = 0.8f;

    [Header("State")]
    [Tooltip("Start kinematic. Almost always right for scenery.")]
    public bool startAsleep = true;

    [Header("Events")]
    public UnityEvent onWoke;
    public UnityEvent onSlept;

    public bool IsAwake { get; private set; }

    /// <summary>True when the director has parked this because it is out of range.</summary>
    public bool IsOutOfRange { get; private set; }

    Rigidbody body;
    CarController car;
    CrushableObject crushable;
    float restingSince = -1f;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        car = GetComponentInParent<CarController>();
        crushable = GetComponentInParent<CrushableObject>();
        followsBone = GetComponent<RigidbodyFollowBone>() != null || GetComponentInParent<MechLocomotion>() != null;
    }

    bool followsBone;

    void OnEnable()
    {
        PhysicsSleepDirector.Register(this);

        if (startAsleep && !IsAwake) ApplySleep();
    }

    void OnDisable()
    {
        PhysicsSleepDirector.Unregister(this);
    }

    /// <summary>Bring this object into the simulation.</summary>
    public void Wake()
    {
        restingSince = -1f;

        if (IsAwake) return;

        // A body driven along a bone (the mech's physics hands) must stay
        // kinematic, or it stops following the bone and flails under physics.
        if (followsBone) return;

        // Out of range there is no ground to land on, so staying kinematic is
        // the only safe answer; remember the request and honour it on return.
        if (IsOutOfRange) return;

        IsAwake = true;

        if (body != null)
        {
            body.isKinematic = false;
            body.WakeUp();
        }

        // A car being shoved is no longer traffic: hand it to physics.
        if (car != null && car.IsDriving) car.StopDriving();

        onWoke?.Invoke();
    }

    /// <summary>Wake, then apply an impulse. Used by blasts and throws.</summary>
    public void WakeAndPush(Vector3 impulse, Vector3 atPoint)
    {
        Wake();

        if (body != null && !body.isKinematic)
            body.AddForceAtPosition(impulse, atPoint, ForceMode.Impulse);
    }

    /// <summary>Drop out of the simulation and hold position.</summary>
    public void Sleep()
    {
        if (!IsAwake) return;

        ApplySleep();
        onSlept?.Invoke();
    }

    void ApplySleep()
    {
        IsAwake = false;
        restingSince = -1f;

        if (body == null) return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
    }

    /// <summary>
    /// Driven by PhysicsSleepDirector rather than Update, so hundreds of these
    /// cost one sliced pass instead of hundreds of per-frame callbacks.
    /// </summary>
    public void Tick(float now, bool inRange)
    {
        IsOutOfRange = !inRange;

        if (!inRange)
        {
            if (IsAwake) Sleep();
            return;
        }

        if (!IsAwake || settleTime <= 0f || body == null) return;

        // A held object is kinematic and motionless relative to physics, so
        // it would read as "settled" and be put to sleep in the mech's hand.
        // Being held is being acted on: never settle while held.
        if (crushable != null && crushable.IsHeld) { restingSince = -1f; return; }

        bool moving = body.linearVelocity.sqrMagnitude > settleSpeed * settleSpeed
                      || body.angularVelocity.sqrMagnitude > settleSpeed * settleSpeed;

        if (moving) { restingSince = -1f; return; }

        if (restingSince < 0f) { restingSince = now; return; }

        if (now - restingSince >= settleTime) Sleep();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!wakeOnCollision || IsAwake) return;
        if (collision.relativeVelocity.magnitude < wakeImpactSpeed) return;

        Wake();
    }
}
