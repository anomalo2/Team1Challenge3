using UnityEngine;

/// <summary>
/// Feeds ordinary physics collisions into CrushableObject, so a car that gets
/// thrown into a building takes damage from the landing and detonates if it hit
/// hard enough. Put it on the same object as CrushableObject.
///
/// This only works while the body is dynamic, which is exactly when it matters:
/// the car is kinematic while driving and while held, and dynamic once thrown
/// or wrecked.
/// </summary>
[RequireComponent(typeof(CrushableObject))]
[RequireComponent(typeof(Rigidbody))]
public class CrushOnImpact : MonoBehaviour
{
    [Header("Sensitivity")]
    [Tooltip("Impacts slower than this are ignored, so it does not grind itself apart resting on the road.")]
    public float minImpactSpeed = 4f;
    [Tooltip("Scales the computed impulse. Raise it to make throws more destructive.")]
    public float impactMultiplier = 1f;

    [Header("Filtering")]
    public LayerMask impactLayers = ~0;
    [Tooltip("Shortest gap between two counted impacts, so one landing is not billed many times.")]
    public float impactCooldown = 0.2f;

    CrushableObject crushable;
    Rigidbody body;
    float lastImpactTime = -999f;

    void Awake()
    {
        crushable = GetComponent<CrushableObject>();
        body = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (crushable == null || crushable.IsDestroyed) return;
        if (body.isKinematic) return;
        if (Time.time - lastImpactTime < impactCooldown) return;
        if ((impactLayers.value & (1 << collision.gameObject.layer)) == 0) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minImpactSpeed) return;

        lastImpactTime = Time.time;

        // Dynamic bodies do report a real impulse, so prefer it and fall back
        // to mass * speed if the contact data came through empty.
        float impulse = collision.impulse.magnitude;
        if (impulse <= 0.01f) impulse = body.mass * speed;

        crushable.ApplyImpact(impulse * impactMultiplier);
    }
}
