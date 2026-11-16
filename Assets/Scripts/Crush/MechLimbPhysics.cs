using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;

/// <summary>
/// Connects the mech's physics hands (the kinematic RigidbodyFollowBone bodies
/// under PhysicsColliders) to the city's physics objects.
///
/// WHY THIS IS NEEDED
/// ------------------
/// The mech hand bodies are kinematic and so are driving cars (CarController)
/// and parked / settled props (SleepableBody). PhysX generates NO contacts
/// between two kinematic bodies, so without this the hands pass straight
/// through traffic. Every FixedUpdate this overlaps the limb's own solid
/// colliders against Vehicle/Crushable and WAKES anything it touches: a
/// driving car stops being traffic and becomes a normal dynamic body, so from
/// then on the kinematic hand shoves it around for real.
///
/// On a HAND it also builds a GrabVolume child (trigger sphere sized to the
/// hand's colliders) carrying MechGrabber + HandTrackedGripSource, so a
/// closed fist picks a car up and a wrist twist crushes it.
///
/// AUTO-ATTACH
/// -----------
/// Nothing to wire in the scene: after every scene load this is added to each
/// RigidbodyFollowBone that does not have one. Bodies with "hand" in the name
/// get a grab volume, the rest (forearms) only knock things. To configure one
/// by hand, add it in the inspector yourself and auto-attach leaves it alone.
/// Set AutoAttach = false to turn the behaviour off entirely.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class MechLimbPhysics : MonoBehaviour
{
    public enum Side { Auto, Left, Right }

    public static bool AutoAttach = true;

    [Header("Knocking things over")]
    [Tooltip("Layers the limb wakes and shoves. Civilian is deliberately excluded - see the no-harm rule.")]
    public LayerMask pushLayers;
    [Tooltip("Fraction of the limb's velocity handed to an object the moment it is woken.")]
    public float velocityTransfer = 1f;
    [Tooltip("Seconds after the scene loads before limbs can knock anything (the mech settles into its pose).")]
    public float startupGrace = 1f;
    [Tooltip("Movement faster than this (m/s) is a reposition, not a swing.")]
    public float teleportSpeed = 30f;
    [Tooltip("A limb slower than this (m/s) does not wake or shove anything it is touching.")]
    public float minShoveSpeed = 0.3f;
    [Tooltip("Cap on the velocity handed over, m/s, so a tracking glitch cannot launch a car.")]
    public float maxTransferSpeed = 15f;

    [Header("Impact damage (0 mass = no damage, push only)")]
    [Tooltip("Effective mass behind a hit, kg. Impulse = mass x closing speed.")]
    public float impactMass = 3000f;
    [Tooltip("Slower contact than this only shoves; it does not dent.")]
    public float minImpactSpeed = 1.5f;
    [Tooltip("Seconds before the same object can take another impact from this limb.")]
    public float impactCooldown = 0.4f;

    [Header("Grabbing")]
    public bool createGrabVolume = true;
    public Side side = Side.Auto;
    [Tooltip("Grab sphere radius as a multiple of the hand colliders' half-size.")]
    public float grabRadiusScale = 1.25f;
    [Tooltip("Seconds a thrown/dropped object ignores this hand, so it is not shot out of the closed fist.")]
    public float releaseGrace = 0.6f;

    public MechGrabber Grabber { get; private set; }
    public HandTrackedGripSource GripSource { get; private set; }
    public Vector3 Velocity { get; private set; }

    Rigidbody body;
    bool followsBone;
    readonly List<Collider> ownColliders = new List<Collider>();
    readonly Collider[] hits = new Collider[32];
    readonly Dictionary<CrushableObject, float> lastImpact = new Dictionary<CrushableObject, float>();
    readonly List<IgnoredPair> ignored = new List<IgnoredPair>();
    Vector3 lastPosition;
    bool haveLast;
    CrushableObject ignoringHeld;

    struct IgnoredPair
    {
        public CrushableObject obj;
        public Collider[] cols;
        public float until;
    }

    // ----------------------------------------------------------- auto-attach

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => AttachAll();

    static void AttachAll()
    {
        if (!AutoAttach) return;

        foreach (RigidbodyFollowBone limb in FindObjectsByType<RigidbodyFollowBone>(FindObjectsSortMode.None))
        {
            if (limb.GetComponent<MechLimbPhysics>() != null) continue;

            bool isHand = limb.name.ToLowerInvariant().Contains("hand");
            AddTo(limb.gameObject, isHand);
        }
    }

    /// <summary>
    /// Add limb physics to an object, deciding up front whether it grabs.
    /// AddComponent runs Awake before fields can be set, so Awake is told not
    /// to build the grab volume and the decision is made here instead.
    /// </summary>
    public static MechLimbPhysics AddTo(GameObject go, bool grabs)
    {
        autoAdding = true;
        MechLimbPhysics p;
        try { p = go.AddComponent<MechLimbPhysics>(); }
        finally { autoAdding = false; }

        p.createGrabVolume = grabs;
        if (grabs) p.BuildGrabVolume();
        return p;
    }

    static bool autoAdding;

    // ------------------------------------------------------------- lifecycle

    void Reset()
    {
        pushLayers = DefaultPushLayers();
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        followsBone = GetComponent<RigidbodyFollowBone>() != null;
        if (pushLayers.value == 0) pushLayers = DefaultPushLayers();

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) ownColliders.Add(c);

        // An inspector-added component builds here; auto-attach builds it itself.
        if (createGrabVolume && !autoAdding) BuildGrabVolume();
    }

    void OnDestroy()
    {
        if (Grabber != null)
        {
            Grabber.onGrabbed.RemoveListener(OnGrabbed);
            Grabber.onReleased.RemoveListener(OnReleased);
        }
    }

    void FixedUpdate()
    {
        // A body driven by RigidbodyFollowBone.MovePosition reports its pose
        // through the Rigidbody. Anything else (leg hitboxes riding animated
        // bones) is only truthful through its Transform.
        Vector3 pos = body != null && followsBone ? body.position : transform.position;
        bool teleported = false;
        if (haveLast && Time.fixedDeltaTime > 0f)
        {
            Vector3 raw = (pos - lastPosition) / Time.fixedDeltaTime;
            // A jump faster than anything a limb can swing is a reposition
            // (spawn, recentre, respawn), not a blow.
            if (raw.magnitude > teleportSpeed) { Velocity = Vector3.zero; teleported = true; }
            else Velocity = Vector3.Lerp(Velocity, raw, 0.5f);
        }
        lastPosition = pos;
        haveLast = true;

        // The mech snaps into its pose for the first moments after loading;
        // nothing it happens to be touching then should be knocked over.
        if (!teleported && Time.timeSinceLevelLoad >= startupGrace) KnockOverlaps();
        ExpireIgnoredPairs();
    }

    // -------------------------------------------------------------- knocking

    void KnockOverlaps()
    {
        int mask = pushLayers.value;
        if (mask == 0) return;

        // Swap touch sets: impacts only land on NEW contact, so resting a hand
        // on a car (or shoving it along) does not chip away at it every step.
        HashSet<CrushableObject> tmp = touchedLast; touchedLast = touchedNow; touchedNow = tmp;
        touchedNow.Clear();

        for (int i = 0; i < ownColliders.Count; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled || !own.gameObject.activeInHierarchy) continue;

            int n = Overlap(own, mask);
            for (int h = 0; h < n; h++)
            {
                Collider other = hits[h];
                if (other == null || other.attachedRigidbody == body) continue;

                CrushableObject c = CrushableObject.FindOn(other);
                if (c != null && c.IsHeld) continue;

                Rigidbody rb = other.attachedRigidbody;
                if (rb == null) continue;

                // Only a MOVING limb shoves. A limb that merely rests against a
                // lamp post (e.g. the mech's starting pose) must not topple it.
                if (rb.isKinematic && Velocity.magnitude >= minShoveSpeed) WakeAndShove(rb);

                if (c != null && touchedNow.Add(c) && !touchedLast.Contains(c)) TryImpact(c);
            }
        }
    }

    HashSet<CrushableObject> touchedNow = new HashSet<CrushableObject>();
    HashSet<CrushableObject> touchedLast = new HashSet<CrushableObject>();

    int Overlap(Collider own, int mask)
    {
        Transform t = own.transform;
        Vector3 s = t.lossyScale;
        Vector3 abs = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        switch (own)
        {
            case BoxCollider b:
                return Physics.OverlapBoxNonAlloc(t.TransformPoint(b.center),
                    Vector3.Scale(b.size * 0.5f, abs), hits, t.rotation, mask, QueryTriggerInteraction.Ignore);
            case SphereCollider sp:
                return Physics.OverlapSphereNonAlloc(t.TransformPoint(sp.center),
                    sp.radius * Mathf.Max(abs.x, Mathf.Max(abs.y, abs.z)), hits, mask, QueryTriggerInteraction.Ignore);
            default:
                Bounds bb = own.bounds;
                return Physics.OverlapBoxNonAlloc(bb.center, bb.extents, hits, Quaternion.identity, mask,
                    QueryTriggerInteraction.Ignore);
        }
    }

    void WakeAndShove(Rigidbody rb)
    {
        // Driving cars: hand them to physics. SleepableBody.Wake does this
        // itself, and also covers parked/settled props.
        SleepableBody sleepable = rb.GetComponent<SleepableBody>();
        if (sleepable != null) sleepable.Wake();

        CarController car = rb.GetComponent<CarController>();
        if (car != null && car.IsDriving) car.StopDriving();

        // Still kinematic = no traffic/sleep owner (scenery we were not asked
        // to move) or parked by the sleep director as out of range. Leave it.
        if (rb.isKinematic) return;

        Vector3 v = Velocity * velocityTransfer;
        if (v.magnitude > maxTransferSpeed) v = v.normalized * maxTransferSpeed;
        rb.linearVelocity = v;
    }

    void TryImpact(CrushableObject c)
    {
        if (impactMass <= 0f || c.IsDestroyed) return;

        float speed = Velocity.magnitude;
        if (speed < minImpactSpeed) return;

        Vector3 to = c.transform.position - transform.position;
        float closing = to.sqrMagnitude > 0.0001f ? Mathf.Max(0f, Vector3.Dot(Velocity, to.normalized)) : speed;
        if (closing < minImpactSpeed) return;

        if (lastImpact.TryGetValue(c, out float t) && Time.time - t < impactCooldown) return;
        lastImpact[c] = Time.time;

        c.ApplyImpact(impactMass * closing);
    }

    // -------------------------------------------------------------- grabbing

    void BuildGrabVolume()
    {
        if (Grabber != null) return;

        Transform existing = transform.Find("GrabVolume");
        GameObject vol = existing != null ? existing.gameObject : new GameObject("GrabVolume");
        vol.layer = gameObject.layer;
        vol.transform.SetParent(transform, false);

        // Size the sphere to the hand's own colliders.
        Bounds b = default;
        bool any = false;
        foreach (Collider c in ownColliders)
        {
            if (c == null) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (!any) b = new Bounds(transform.position, Vector3.one * 0.5f);

        vol.transform.position = b.center;
        vol.transform.rotation = transform.rotation;

        SphereCollider sphere = vol.GetComponent<SphereCollider>();
        if (sphere == null) sphere = vol.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.center = Vector3.zero;
        Vector3 ls = vol.transform.lossyScale;
        float worldRadius = b.extents.magnitude * grabRadiusScale;
        sphere.radius = worldRadius / Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z))));

        Grabber = vol.GetComponent<MechGrabber>();
        if (Grabber == null)
        {
            Grabber = vol.AddComponent<MechGrabber>();
            Grabber.grabLayers = LayerMask.GetMask("Vehicle", "Civilian", "Crushable");
        }
        Grabber.onGrabbed.AddListener(OnGrabbed);
        Grabber.onReleased.AddListener(OnReleased);

        GripSource = vol.GetComponent<HandTrackedGripSource>();
        if (GripSource == null) GripSource = vol.AddComponent<HandTrackedGripSource>();
        GripSource.grabber = Grabber;
        GripSource.handedness = ResolveHandedness();
    }

    Handedness ResolveHandedness()
    {
        if (side == Side.Left) return Handedness.Left;
        if (side == Side.Right) return Handedness.Right;

        string n = name.ToLowerInvariant();
        if (n.Contains("left") || n.EndsWith(".l") || n.EndsWith("_l")) return Handedness.Left;
        return Handedness.Right;
    }

    // While held (and briefly after), the object must not collide with the
    // fist that is wrapped around it, or it is shot out on release.
    void OnGrabbed()
    {
        ignoringHeld = Grabber.Held;
        if (ignoringHeld != null) SetIgnore(ignoringHeld, true, float.MaxValue);
    }

    void OnReleased()
    {
        if (ignoringHeld == null) return;
        for (int i = 0; i < ignored.Count; i++)
        {
            if (ignored[i].obj != ignoringHeld) continue;
            IgnoredPair p = ignored[i];
            p.until = Time.time + releaseGrace;
            ignored[i] = p;
        }
        ignoringHeld = null;
    }

    void SetIgnore(CrushableObject obj, bool ignore, float until)
    {
        Collider[] cols = obj.GetComponentsInChildren<Collider>();
        foreach (Collider mine in ownColliders)
            foreach (Collider theirs in cols)
                if (mine != null && theirs != null) Physics.IgnoreCollision(mine, theirs, ignore);

        if (ignore) ignored.Add(new IgnoredPair { obj = obj, cols = cols, until = until });
    }

    void ExpireIgnoredPairs()
    {
        for (int i = ignored.Count - 1; i >= 0; i--)
        {
            IgnoredPair p = ignored[i];
            if (p.obj != null && Time.time < p.until) continue;

            if (p.cols != null)
                foreach (Collider mine in ownColliders)
                    foreach (Collider theirs in p.cols)
                        if (mine != null && theirs != null) Physics.IgnoreCollision(mine, theirs, false);

            ignored.RemoveAt(i);
        }
    }

    static LayerMask DefaultPushLayers() => LayerMask.GetMask("Vehicle", "Crushable");
}
