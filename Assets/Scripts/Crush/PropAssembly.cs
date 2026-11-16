using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a breakable prop a real physics object, and breaks it apart.
///
/// WHY THE PARTS ARE MOVED AT RUNTIME
/// ----------------------------------
/// The city is one imported model (Untitled.glb). Its lamp posts, bins and
/// signs are children of that model, and Unity refuses to reparent the
/// children of a model instance in the editor. The old cluster builder tried
/// anyway: the 490 prop holders got a Rigidbody and a CrushableObject but no
/// mesh and no collider, while the real meshes stayed welded to the city. So
/// nothing the mech touched could ever move or break.
///
/// At runtime that restriction does not exist, so the holder lists its parts
/// and adopts them on Awake. From then on the parts' colliders belong to the
/// holder's Rigidbody and the prop moves, topples and breaks as one body.
///
/// BREAKING
/// --------
/// When the CrushableObject is destroyed the prop flies apart: a multi-part
/// prop (sign on a pole) splits into separate rigid bodies, and every prop
/// throws off a handful of debris chunks in its own material plus an optional
/// dust effect.
/// </summary>
[DefaultExecutionOrder(-200)]   // adopt parts before CrushableObject/SleepableBody look at them
[RequireComponent(typeof(CrushableObject))]
[RequireComponent(typeof(Rigidbody))]
public class PropAssembly : MonoBehaviour
{
    [Tooltip("The meshes (with their colliders) this prop is made of.")]
    public List<Transform> parts = new List<Transform>();

    [Header("Breaking")]
    [Tooltip("Multi-part props come apart into separate rigid bodies when destroyed.")]
    public bool splitWhenDestroyed = true;
    [Tooltip("Small chunks thrown off when destroyed.")]
    public int debrisCount = 6;
    public float debrisLifetime = 6f;
    [Tooltip("Speed the pieces fly apart at, m/s.")]
    public float breakSpeed = 4f;
    [Tooltip("Optional dust / spark effect spawned where it broke.")]
    public GameObject breakEffect;

    [Header("Respawn")]
    [Tooltip("Rebuild the prop where it originally stood after it breaks.")]
    public bool respawn = true;
    [Tooltip("Seconds after breaking before it comes back.")]
    public float respawnDelay = 20f;
    [Tooltip("It will not reappear while the mech, a car or a pedestrian is standing in its spot.")]
    public LayerMask blockRespawnLayers;
    [Tooltip("Seconds it takes to grow back in.")]
    public float popInTime = 0.4f;

    public bool IsBroken => broken;

    /// <summary>Broken, or knocked out of where the level put it.</summary>
    public bool IsDisplaced =>
        broken || (transform.position - homePosition).sqrMagnitude > 0.04f
               || Quaternion.Angle(transform.rotation, homeRotation) > 5f;
    public int TimesRespawned { get; private set; }

    CrushableObject crushable;
    Rigidbody body;
    SleepableBody sleepable;
    bool broken;

    // Where everything started, so a broken prop can be put back exactly.
    Vector3 homePosition;
    Quaternion homeRotation;
    Vector3 homeScale;
    Bounds homeBounds;
    readonly List<Pose> partHome = new List<Pose>();
    readonly List<Vector3> partScale = new List<Vector3>();

    // part -> owning assembly, so the kill box can hand a fallen piece back.
    static readonly Dictionary<Transform, PropAssembly> owners = new Dictionary<Transform, PropAssembly>();
    public static PropAssembly OwnerOf(Transform t)
    {
        if (t == null) return null;
        if (owners.TryGetValue(t, out PropAssembly a) && a != null) return a;
        return t.GetComponentInParent<PropAssembly>();
    }

    // Debris is capped scene-wide so a chain reaction cannot flood physics.
    internal static int liveDebris;
    const int MaxLiveDebris = 120;

    void Awake()
    {
        crushable = GetComponent<CrushableObject>();
        body = GetComponent<Rigidbody>();

        for (int i = 0; i < parts.Count; i++)
        {
            Transform p = parts[i];
            if (p == null) continue;
            if (p.parent != transform) p.SetParent(transform, true);
            owners[p] = this;
        }

        sleepable = GetComponent<SleepableBody>();
        homePosition = transform.position;
        homeRotation = transform.rotation;
        homeScale = transform.localScale;
        homeBounds = WorldBounds();
        foreach (Transform p in parts)
        {
            partHome.Add(p != null ? new Pose(p.localPosition, p.localRotation) : Pose.identity);
            partScale.Add(p != null ? p.localScale : Vector3.one);
        }
        if (blockRespawnLayers.value == 0)
            blockRespawnLayers = LayerMask.GetMask("MechCollider", "Vehicle", "Civilian");

        // Squashing a moving physics body by scale looks wrong and fights the
        // simulation. Props show damage by being knocked about and by breaking.
        crushable.squashWhenCrushed = false;
    }

    void OnEnable() { crushable.WasDestroyed += OnDestroyedProp; }
    void OnDisable() { crushable.WasDestroyed -= OnDestroyedProp; }

    void OnDestroyedProp(CrushableObject c)
    {
        if (broken) return;
        broken = true;

        Bounds b = WorldBounds();
        Vector3 inherited = body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;

        if (breakEffect != null) Instantiate(breakEffect, b.center, Quaternion.identity);

        // Debris first, while the parts' materials are still easy to find.
        Renderer anyRenderer = GetComponentInChildren<Renderer>();
        if (anyRenderer != null) SpawnDebris(b, anyRenderer.sharedMaterial, inherited);

        if (splitWhenDestroyed) Split(b, inherited);

        if (respawn) StartCoroutine(RespawnWhenClear(respawnDelay));
    }

    float displacedRestingSince = -1f;
    float nextDisplacedCheck;

    // Knocked over but not broken: once it has come to rest, it goes back to
    // its spot after the same delay. Without this a toppled lamp post lies
    // across a crossing for ever.
    void Update()
    {
        if (!respawn || broken || Time.time < nextDisplacedCheck) return;
        nextDisplacedCheck = Time.time + 1f;

        bool resting = body == null || body.isKinematic || body.linearVelocity.sqrMagnitude < 0.04f;
        if (!IsDisplaced || !resting || crushable.IsHeld) { displacedRestingSince = -1f; return; }

        if (displacedRestingSince < 0f) { displacedRestingSince = Time.time; return; }
        if (Time.time - displacedRestingSince < respawnDelay || SpotOccupied()) return;

        displacedRestingSince = -1f;
        Restore();
    }

    System.Collections.IEnumerator RespawnWhenClear(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Never pop back inside the mech's leg, a car, a pedestrian, or a loose
        // physics object (debris, another prop's pieces) lying in the spot.
        while (SpotOccupied()) yield return new WaitForSeconds(1f);

        Restore();
    }

    readonly Collider[] spotHits = new Collider[32];

    bool SpotOccupied()
    {
        int n = Physics.OverlapBoxNonAlloc(homeBounds.center, homeBounds.extents + Vector3.one * 0.3f, spotHits,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = spotHits[i];
            if (c == null || c.transform.IsChildOf(transform) || parts.Contains(c.transform)) continue;
            if ((blockRespawnLayers.value & (1 << c.gameObject.layer)) != 0) return true;
            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic && OwnerOf(rb.transform) != this) return true;
        }
        return false;
    }

    /// <summary>Put the prop back where it started, whole and at rest.</summary>
    public void Restore()
    {
        StopAllCoroutines();

        for (int i = 0; i < parts.Count; i++)
        {
            Transform p = parts[i];
            if (p == null) continue;

            // A split piece carries its own Rigidbody; it must not stay nested
            // under the holder's.
            Rigidbody own = p.GetComponent<Rigidbody>();
            if (own != null) { own.isKinematic = true; Destroy(own); }

            p.gameObject.SetActive(true);
            p.SetParent(transform, false);
            p.localPosition = partHome[i].position;
            p.localRotation = partHome[i].rotation;
            p.localScale = partScale[i];
        }

        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
        }
        transform.SetPositionAndRotation(homePosition, homeRotation);
        if (body != null) { body.position = homePosition; body.rotation = homeRotation; }

        if (sleepable != null) sleepable.Sleep();
        crushable.Restore();
        crushable.squashWhenCrushed = false;
        broken = false;
        TimesRespawned++;

        StartCoroutine(PopIn());
    }

    System.Collections.IEnumerator PopIn()
    {
        for (float t = 0f; t < popInTime; t += Time.deltaTime)
        {
            float k = t / popInTime;
            // ease-out with a touch of overshoot
            float s = 1f + 0.12f * Mathf.Sin(k * Mathf.PI) - (1f - k) * (1f - k);
            transform.localScale = homeScale * Mathf.Max(0.01f, s);
            yield return null;
        }
        transform.localScale = homeScale;
    }

    /// <summary>
    /// A piece of this prop fell out of the world (kill box). Hide it until
    /// the prop respawns; if the whole prop fell, rebuild it now.
    /// </summary>
    public void Reclaim(Transform piece)
    {
        if (piece == transform) { Restore(); return; }

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
        piece.gameObject.SetActive(false);

        if (!broken) Restore();   // knocked off without breaking: put it back
    }

    /// <summary>Every part after the first becomes its own rigid body.</summary>
    void Split(Bounds b, Vector3 inherited)
    {
        List<Transform> live = new List<Transform>();
        foreach (Transform p in parts) if (p != null && p.parent == transform) live.Add(p);
        if (live.Count < 2) return;

        float pieceMass = Mathf.Max(5f, (body != null ? body.mass : 50f) / live.Count);

        for (int i = 1; i < live.Count; i++)
        {
            Transform p = live[i];
            p.SetParent(null, true);

            Rigidbody rb = p.gameObject.AddComponent<Rigidbody>();
            rb.mass = pieceMass;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = inherited + Scatter(p.position, b.center) * breakSpeed;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
        }

        // The base part stays on the holder, which is already dynamic now.
        if (body != null && !body.isKinematic)
            body.linearVelocity = inherited + Vector3.up * breakSpeed * 0.3f;
    }

    void SpawnDebris(Bounds b, Material mat, Vector3 inherited)
    {
        int count = Mathf.Min(debrisCount, MaxLiveDebris - liveDebris);
        if (count <= 0 || mat == null) return;

        float size = Mathf.Clamp(b.size.magnitude * 0.12f, 0.08f, 0.6f);

        for (int i = 0; i < count; i++)
        {
            GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "Debris";
            chunk.transform.position = b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.6f);
            chunk.transform.rotation = Random.rotation;
            chunk.transform.localScale = Vector3.one * size * Random.Range(0.6f, 1.3f);
            chunk.GetComponent<Renderer>().sharedMaterial = mat;

            Rigidbody rb = chunk.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.linearVelocity = inherited + Scatter(chunk.transform.position, b.center) * breakSpeed * Random.Range(0.8f, 1.6f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;

            chunk.AddComponent<DebrisLife>().lifetime = debrisLifetime * Random.Range(0.8f, 1.2f);
            liveDebris++;
        }
    }

    static Vector3 Scatter(Vector3 from, Vector3 centre)
    {
        Vector3 d = from - centre;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) d = Random.insideUnitSphere;
        d.y = 0f;
        return (d.normalized + Vector3.up * Random.Range(0.6f, 1.2f)).normalized;
    }

    Bounds WorldBounds()
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(transform.position, Vector3.one);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }
}
