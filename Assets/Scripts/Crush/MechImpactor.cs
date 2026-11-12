using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Makes ANY part of the mech break things it hits: shins, knees, shoulders,
/// the chest, a swinging arm. Put it on an empty child with a TRIGGER collider
/// and parent it to the bone you want to be dangerous.
///
/// MechFootCrusher is the specialised version for feet (it adds standing
/// weight). This is the general one: it only cares how fast the part was
/// travelling when it touched something.
///
/// Why velocity is tracked by hand: the mech is posed by IK and by
/// ModelDisplacement writing transform.localPosition, so it is animated rather
/// than simulated. Collision.impulse and Rigidbody.linearVelocity both read
/// zero on an animated limb, so we difference our own transform instead.
/// </summary>
public class MechImpactor : MonoBehaviour
{
    [Header("Impact")]
    [Tooltip("Effective mass behind this part, in kg. Impulse = this x closing speed. A shin carries far more than a fingertip.")]
    public float effectiveMass = 6000f;
    [Tooltip("Minimum speed (m/s) for contact to count as a hit. Stops a slow brush from flattening things.")]
    public float minImpactSpeed = 0.6f;

    [Header("Continuous pressure")]
    [Tooltip("Keep pressing while in contact, so leaning on a lamp post eventually bends it. 0 = impacts only.")]
    public float sustainedForce = 0f;

    [Header("Filtering")]
    public LayerMask crushLayers = ~0;

    [Header("Events")]
    public UnityEvent onImpact;

    /// <summary>Speed of this part in m/s, tracked manually because the limb is animated.</summary>
    public float Speed { get; private set; }
    public Vector3 Velocity => trackedVelocity;

    Vector3 lastPosition;
    Vector3 trackedVelocity;

    void Awake()
    {
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        // After IK has posed the bone for this frame.
        float dt = Time.deltaTime;

        if (dt > 0f)
        {
            Vector3 v = (transform.position - lastPosition) / dt;
            trackedVelocity = Vector3.Lerp(trackedVelocity, v, 0.5f);
            Speed = trackedVelocity.magnitude;
        }

        lastPosition = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Passes(other)) return;

        CrushableObject c = CrushableObject.FindOn(other);
        if (c == null || c.IsDestroyed) return;

        if (Speed < minImpactSpeed) return;

        // Only the part of our motion heading into the object counts, so
        // sliding along something is not the same as driving into it.
        Vector3 toTarget = c.transform.position - transform.position;
        float closing = toTarget.sqrMagnitude > 0.0001f
            ? Mathf.Max(0f, Vector3.Dot(trackedVelocity, toTarget.normalized))
            : Speed;

        if (closing < minImpactSpeed) return;

        c.ApplyImpact(effectiveMass * closing);
        onImpact?.Invoke();
    }

    void OnTriggerStay(Collider other)
    {
        if (sustainedForce <= 0f || !Passes(other)) return;

        CrushableObject c = CrushableObject.FindOn(other);
        if (c == null || c.IsDestroyed) return;

        c.ApplySustainedCrush(sustainedForce);
    }

    bool Passes(Collider other)
    {
        return other != null && (crushLayers.value & (1 << other.gameObject.layer)) != 0;
    }
}
