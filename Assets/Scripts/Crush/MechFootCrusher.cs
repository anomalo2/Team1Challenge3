using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drop this on an empty GameObject with a TRIGGER collider roughly the size of
/// a mech foot, then parent it to the foot bone when the mech exists.
///
/// Why triggers and not collisions: the mech is posed by IK and by
/// ModelDisplacement writing transform.localPosition, so it is not simulated.
/// Collision.impulse and Rigidbody.linearVelocity both read zero on an animated
/// limb. So we difference the foot transform ourselves and multiply by a
/// configured mech mass to get a believable stomp impulse.
///
/// Two ways a foot ruins a car:
///   stepping ON it   -> one impact, scaled by how fast the foot came down
///   standing ON it   -> sustained weight, flattens it over a second or two
/// </summary>
public class MechFootCrusher : MonoBehaviour
{
    [Header("Mech weight")]
    [Tooltip("Effective mass driven through this foot, in kg. Stomp impulse = this x downward foot speed. At 12000, a 3 m/s stomp dents a car and a 4.2 m/s slam detonates it.")]
    public float effectiveMass = 12000f;
    [Tooltip("Force in newtons pressed continuously into whatever is underfoot. Keep this BELOW a car explodeForceThreshold (50000) or simply standing on a car detonates it instead of flattening it.")]
    public float standingForce = 45000f;

    [Header("Stomp")]
    [Tooltip("Foot must be descending at least this fast (m/s) to register as a stomp.")]
    public float minStompSpeed = 0.75f;
    [Tooltip("Only count downward motion. Off = any direction of travel counts.")]
    public bool downwardOnly = true;

    [Header("Filtering")]
    public LayerMask crushLayers = ~0;

    [Header("Events")]
    [Tooltip("Fired on a registered stomp. Good hook for GroundCracks.SpawnCrack and audio.")]
    public UnityEvent onStomp;

    /// <summary>Foot speed this frame, m/s. Tracked by hand because the limb is animated.</summary>
    public float FootSpeed { get; private set; }
    public Vector3 FootVelocity => trackedVelocity;

    Vector3 lastPos;
    Vector3 trackedVelocity;

    void Awake()
    {
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Vector3 v = (transform.position - lastPos) / dt;
            trackedVelocity = Vector3.Lerp(trackedVelocity, v, 0.5f);
            FootSpeed = trackedVelocity.magnitude;
        }

        lastPos = transform.position;
    }

    float CurrentStompSpeed()
    {
        if (!downwardOnly) return trackedVelocity.magnitude;

        // Only the component travelling down counts.
        float down = Vector3.Dot(trackedVelocity, Vector3.down);
        return Mathf.Max(0f, down);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!PassesFilter(other)) return;

        CrushableObject c = CrushableObject.FindOn(other);
        if (c == null || c.IsDestroyed) return;

        float speed = CurrentStompSpeed();
        if (speed < minStompSpeed) return;

        c.ApplyImpact(effectiveMass * speed);
        onStomp?.Invoke();
    }

    void OnTriggerStay(Collider other)
    {
        if (!PassesFilter(other)) return;

        CrushableObject c = CrushableObject.FindOn(other);
        if (c == null || c.IsDestroyed) return;

        // Dead weight. This is what turns a car into a pancake if you just stand there.
        c.ApplySustainedCrush(standingForce);
    }

    bool PassesFilter(Collider other)
    {
        return other != null && (crushLayers.value & (1 << other.gameObject.layer)) != 0;
    }
}
