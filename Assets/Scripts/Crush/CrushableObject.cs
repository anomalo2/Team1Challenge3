using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on anything the mech can pick up, dent, flatten or detonate:
/// cars, lamp posts, dumpsters, people.
///
/// It owns ONE number -- structural integrity -- and two ways to spend it:
///   ApplySustainedCrush(force)  squeezing / standing on it  (force over time)
///   ApplyImpact(impulse)        a stomp or a thrown object   (one hit)
///
/// Nothing here needs the mech to exist. Any script that can produce a force
/// can damage this, which is why the grab and foot components stay so small.
/// </summary>
[DisallowMultipleComponent]
public class CrushableObject : MonoBehaviour
{
    public enum CrushState { Intact, Dented, Destroyed }

    [Header("Identity")]
    [Tooltip("Name used in the damage readout and score log.")]
    public string displayName = "Object";

    [Header("Scoring")]
    [Tooltip("Adds to the property-damage bill when destroyed. Turn OFF for civilians - they are scored as lives, not property.")]
    public bool countsAsPropertyDamage = true;
    [Tooltip("The repair bill for wrecking this.")]
    public float propertyValue = 10f;

    [Header("Durability")]
    [Tooltip("OFF = can NEVER be destroyed. It still reports being overstressed, so a civilian can react to rough handling without ever being harmed. Required for anything alive.")]
    public bool canBeDestroyed = true;

    [Header("Strength")]
    [Tooltip("Force in newtons this object shrugs off completely. Anything below this does NOTHING, so a gentle grip is always safe.")]
    public float crushResistance = 8000f;
    [Tooltip("Total crush work absorbed before it is destroyed.")]
    public float structuralIntegrity = 60f;
    [Tooltip("Integrity lost per second, per newton of force above crushResistance.")]
    public float sustainedCrushRate = 0.003f;
    [Tooltip("Integrity lost per newton-second of impact above crushResistance.")]
    public float impactCrushRate = 0.001f;

    [Header("Catastrophic failure")]
    [Tooltip("A single force spike above explodeForceThreshold detonates this instantly - the squeezed-too-hard case.")]
    public bool canExplode = true;
    public float explodeForceThreshold = 50000f;
    public GameObject explosionEffect;
    public float explosionRadius = 8f;
    public float explosionForce = 12000f;
    [Tooltip("Crush damage dealt to other crushables in the blast. Lets wrecks chain-detonate.")]
    public float explosionDamageToOthers = 30f;

    [Header("Grab")]
    public bool canBeGrabbed = true;

    [Header("Visuals (all optional)")]
    public GameObject intactVisual;
    public GameObject dentedVisual;
    public GameObject wreckVisual;
    [Range(0f, 1f)]
    [Tooltip("Damage fraction at which the dented visual swaps in.")]
    public float dentAtDamageFraction = 0.35f;
    [Tooltip("Squash the object flatter as it is crushed. Cheap stand-in for a crumpled mesh.")]
    public bool squashWhenCrushed = true;
    [Range(0.05f, 1f)] public float crushedHeightScale = 0.25f;
    public GameObject wreckageEffect;

    [Header("Lifetime")]
    [Tooltip("Seconds before the wreck is removed. 0 = leave it lying in the street.")]
    public float removeWreckAfter = 0f;

    [Header("Events")]
    public UnityEvent onDented;
    public UnityEvent onDestroyed;
    public UnityEvent onExploded;
    [Tooltip("Fires when an indestructible object takes force that WOULD have destroyed it. How a civilian reacts to being handled too roughly.")]
    public UnityEvent onOverstressed;

    /// <summary>Raised when a grabber picks this up (true) or lets go (false).</summary>
    public event Action<CrushableObject, bool> HeldChanged;

    /// <summary>Raised once, the moment this is destroyed.</summary>
    public event Action<CrushableObject> WasDestroyed;

    /// <summary>
    /// Raised when an indestructible object takes force that would otherwise
    /// have destroyed it. Fires once per episode; call ClearOverstress to re-arm.
    /// </summary>
    public event Action<CrushableObject> Overstressed;

    public bool IsOverstressed { get; private set; }

    public CrushState State { get; private set; } = CrushState.Intact;
    public bool IsDestroyed => State == CrushState.Destroyed;
    public bool IsHeld { get; private set; }
    public bool IsGrabbable => canBeGrabbed && !IsDestroyed;
    public float Integrity => currentIntegrity;

    /// <summary>0 = pristine, 1 = wrecked.</summary>
    public float DamageFraction =>
        structuralIntegrity <= 0f ? 1f : 1f - Mathf.Clamp01(currentIntegrity / structuralIntegrity);

    float currentIntegrity;
    Rigidbody body;
    SleepableBody sleepable;
    Vector3 baseScale;
    bool exploding;

    void Awake()
    {
        currentIntegrity = structuralIntegrity;
        baseScale = transform.localScale;
        body = GetComponent<Rigidbody>();
        sleepable = GetComponent<SleepableBody>();
        SetVisual(intactVisual);
    }

    /// <summary>Finds the crushable that owns a collider, including compound/child colliders.</summary>
    public static CrushableObject FindOn(Collider col)
    {
        return col == null ? null : col.GetComponentInParent<CrushableObject>();
    }

    /// <summary>
    /// Continuous pressure: a closing hand, or a foot resting on the roof.
    /// Call every frame while contact lasts. Force is in newtons.
    /// </summary>
    public void ApplySustainedCrush(float force)
    {
        if (IsDestroyed) return;

        // Indestructible objects skip the shortcut and fall through, so the
        // damage path can report them as overstressed instead.
        if (canExplode && canBeDestroyed && force >= explodeForceThreshold)
        {
            currentIntegrity = 0f;
            DestroyNow(true);
            return;
        }

        float excess = force - crushResistance;
        if (excess <= 0f) return;

        ApplyDamage(excess * sustainedCrushRate * Time.deltaTime, force);
    }

    /// <summary>A single blow. Impulse is in newton-seconds (mass * speed).</summary>
    public void ApplyImpact(float impulse)
    {
        if (IsDestroyed) return;

        if (canExplode && canBeDestroyed && impulse >= explodeForceThreshold)
        {
            currentIntegrity = 0f;
            DestroyNow(true);
            return;
        }

        float excess = impulse - crushResistance;
        if (excess <= 0f) return;

        ApplyDamage(excess * impactCrushRate, impulse);
    }

    /// <summary>Raw integrity damage, ignoring resistance. Used by blasts.</summary>
    public void ApplyDirectDamage(float amount)
    {
        if (IsDestroyed || amount <= 0f) return;
        ApplyDamage(amount, 0f);
    }

    void ApplyDamage(float amount, float causingForce)
    {
        // Something is genuinely acting on this, so bring it into the
        // simulation rather than leaving it parked.
        if (sleepable != null) sleepable.Wake();

        CrushState before = State;
        currentIntegrity -= amount;

        if (currentIntegrity <= 0f)
        {
            // Cannot be broken: report rough handling and leave it intact.
            if (!canBeDestroyed)
            {
                Overstress();
                return;
            }

            currentIntegrity = 0f;
            // Finished off by slow pressure: it crumples.
            // Finished off by a hard spike: it detonates.
            DestroyNow(canExplode && causingForce >= explodeForceThreshold);
            return;
        }

        if (DamageFraction >= dentAtDamageFraction)
        {
            State = CrushState.Dented;
            if (before == CrushState.Intact)
            {
                SetVisual(dentedVisual);
                onDented?.Invoke();
            }
        }

        UpdateSquash();
    }

    /// <summary>
    /// Took force that would have destroyed it, but it cannot be destroyed.
    /// Integrity is floored and the object reports the rough handling instead.
    /// Fires once per episode; ClearOverstress re-arms it.
    /// </summary>
    void Overstress()
    {
        // Floor it above zero so nothing can ever read this as destroyed.
        currentIntegrity = Mathf.Max(currentIntegrity, structuralIntegrity * 0.05f);

        if (IsOverstressed) return;

        IsOverstressed = true;
        onOverstressed?.Invoke();
        Overstressed?.Invoke(this);
    }

    /// <summary>Re-arm overstress reporting, and heal back to full.</summary>
    public void ClearOverstress()
    {
        IsOverstressed = false;
        currentIntegrity = structuralIntegrity;
        State = CrushState.Intact;
        UpdateSquash();
    }

    void DestroyNow(bool detonate)
    {
        // Belt and braces: nothing indestructible ever reaches a destroyed state.
        if (IsDestroyed || !canBeDestroyed) return;

        State = CrushState.Destroyed;
        currentIntegrity = 0f;

        // Make the hand let go before we turn into debris.
        if (IsHeld) SetHeld(false);

        SetVisual(wreckVisual);
        UpdateSquash();

        if (sleepable != null) sleepable.Wake();
        else if (body != null) body.isKinematic = false;

        if (countsAsPropertyDamage && CityObjective.Instance != null)
            CityObjective.Instance.ReportPropertyDestroyed(propertyValue, displayName);

        if (detonate) Explode();
        else if (wreckageEffect != null) Instantiate(wreckageEffect, transform.position, transform.rotation);

        onDestroyed?.Invoke();
        WasDestroyed?.Invoke(this);

        if (removeWreckAfter > 0f) Destroy(gameObject, removeWreckAfter);
    }

    void Explode()
    {
        if (exploding) return;
        exploding = true;

        if (explosionEffect != null)
            Instantiate(explosionEffect, transform.position, Quaternion.identity);

        Vector3 origin = transform.position;
        Collider[] hits = Physics.OverlapSphere(origin, explosionRadius);

        // Shove loose physics objects...
        for (int i = 0; i < hits.Length; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb == null || rb == body) continue;

            // Wake first: a parked body ignores forces entirely.
            SleepableBody sb = rb.GetComponent<SleepableBody>();
            if (sb != null) sb.Wake();

            if (!rb.isKinematic)
                rb.AddExplosionForce(explosionForce, origin, explosionRadius, 1.5f);
        }

        // ...then damage other crushables, so a blast can set off the car next to it.
        if (explosionDamageToOthers > 0f)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                CrushableObject other = FindOn(hits[i]);
                if (other == null || other == this || other.IsDestroyed) continue;

                float falloff = 1f - Mathf.Clamp01(
                    Vector3.Distance(origin, other.transform.position) / Mathf.Max(0.01f, explosionRadius));

                other.ApplyDirectDamage(explosionDamageToOthers * falloff);
            }
        }

        onExploded?.Invoke();
    }

    /// <summary>Called by MechGrabber. Not meant for gameplay code.</summary>
    public void SetHeld(bool held)
    {
        if (IsHeld == held) return;

        IsHeld = held;

        // Being handled counts as interaction: a prop put down should fall and
        // settle, not freeze in mid-air.
        if (!held && sleepable != null) sleepable.Wake();

        HeldChanged?.Invoke(this, held);
    }

    void UpdateSquash()
    {
        if (!squashWhenCrushed) return;

        float d = DamageFraction;
        float spread = Mathf.Lerp(1f, 1.08f, d); // bulges outward as it flattens

        transform.localScale = new Vector3(
            baseScale.x * spread,
            baseScale.y * Mathf.Lerp(1f, crushedHeightScale, d),
            baseScale.z * spread);
    }

    void SetVisual(GameObject target)
    {
        // Nothing assigned for this state? Leave whatever is showing alone.
        if (target == null) return;

        if (intactVisual != null) intactVisual.SetActive(intactVisual == target);
        if (dentedVisual != null) dentedVisual.SetActive(dentedVisual == target);
        if (wreckVisual != null) wreckVisual.SetActive(wreckVisual == target);
    }

    void OnDrawGizmosSelected()
    {
        if (!canExplode) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
