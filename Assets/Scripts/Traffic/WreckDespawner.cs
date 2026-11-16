using UnityEngine;

/// <summary>
/// Clears away broken cars. Once a car stops being traffic - crashed,
/// wrecked, or picked up and thrown - it lies where it ended up for
/// despawnAfter seconds, then shrinks away and is removed. Its spawner then
/// refills the lane, so the street never silts up with wrecks.
///
/// The timer only runs while nobody is holding it, so a car in the mech's
/// hand never vanishes. CarController adds this automatically; put one on a
/// car prefab yourself to change the timing.
/// </summary>
[DisallowMultipleComponent]
public class WreckDespawner : MonoBehaviour
{
    [Tooltip("Seconds a broken car stays before it is removed.")]
    public float despawnAfter = 20f;
    [Tooltip("Seconds it takes to shrink away at the end.")]
    public float shrinkTime = 1f;

    CarController car;
    CrushableObject crushable;
    float brokenFor;
    Vector3 baseScale;

    public bool IsBroken => car != null ? !car.IsDriving : (crushable != null && crushable.IsDestroyed);

    void Awake()
    {
        car = GetComponent<CarController>();
        crushable = GetComponent<CrushableObject>();
    }

    void Update()
    {
        if (!IsBroken || (crushable != null && crushable.IsHeld))
        {
            brokenFor = 0f;
            return;
        }

        brokenFor += Time.deltaTime;

        float left = despawnAfter - brokenFor;
        if (left <= 0f) { Destroy(gameObject); return; }

        if (left < shrinkTime)
        {
            // Crushing squashes the car via scale, so shrink from wherever it is now.
            if (left + Time.deltaTime >= shrinkTime) baseScale = transform.localScale;
            transform.localScale = baseScale * Mathf.Max(0.01f, left / shrinkTime);
        }
    }
}
