using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives every SleepableBody from one place.
///
/// Hundreds of props each running their own Update costs more than the physics
/// being saved, so bodies register here and are ticked in slices: a fraction of
/// the list each frame. With 500 props and 8 slices that is ~62 distance checks
/// a frame instead of 500 Update calls.
///
/// The player reference is whatever the mech is riding on. It falls back to the
/// main camera, then to an object named Center Point, so it works in a test
/// scene with no rig present.
/// </summary>
[DefaultExecutionOrder(-50)]
public class PhysicsSleepDirector : MonoBehaviour
{
    public static PhysicsSleepDirector Instance { get; private set; }

    [Header("Range")]
    [Tooltip("Bodies further than this from the player are parked kinematic. Keep it inside the radius that actually has collision, or woken objects fall through the world.")]
    public float wakeRadius = 90f;
    [Tooltip("Extra distance before a woken body is parked again, so one on the boundary does not flicker.")]
    public float hysteresis = 10f;

    [Header("Cost")]
    [Tooltip("The list is split into this many slices, one checked per frame.")]
    [Range(1, 16)] public int slices = 8;

    [Header("Player")]
    [Tooltip("Leave empty to use the main camera, or an object named Center Point.")]
    public Transform player;

    public int TrackedCount => bodies.Count;
    public int AwakeCount { get; private set; }

    static readonly List<SleepableBody> bodies = new List<SleepableBody>();
    int cursor;

    public static void Register(SleepableBody b)
    {
        if (b != null && !bodies.Contains(b)) bodies.Add(b);

        // A scene that starts with no sleepable bodies (e.g. the mech test
        // scenes) gets none from EnsureExists. Without a director nothing ever
        // goes back to sleep, so create one the first time a body shows up -
        // a spawned car, or a prop dropped in at runtime.
        // Only after the first scene finished loading (EnsureExists covers the
        // load itself), and only if no director is waiting to Awake - otherwise
        // a body enabling before the scene's own director would replace it and
        // its tuned radius/player.
        if (Instance == null && Application.isPlaying && !quitting && firstLoadDone
            && FindAnyObjectByType<PhysicsSleepDirector>(FindObjectsInactive.Include) == null)
        {
            GameObject go = new GameObject("PhysicsSleepDirector");
            go.AddComponent<PhysicsSleepDirector>();
        }
    }

    static bool quitting;
    static bool firstLoadDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Domain reload may be off in the editor; never carry state between plays.
        quitting = false;
        firstLoadDone = false;
        bodies.Clear();
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    static void OnQuitting() => quitting = true;

    public static void Unregister(SleepableBody b)
    {
        bodies.Remove(b);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Creates a director on demand, so a scene need not remember one.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        firstLoadDone = true;
        if (Instance != null) return;
        if (bodies.Count == 0) return;   // nothing to manage

        GameObject go = new GameObject("PhysicsSleepDirector");
        go.AddComponent<PhysicsSleepDirector>();
    }

    void Update()
    {
        Transform reference = ResolvePlayer();
        if (reference == null) return;

        Vector3 origin = reference.position;
        float now = Time.time;

        int count = bodies.Count;
        if (count == 0) return;

        int perSlice = Mathf.Max(1, Mathf.CeilToInt(count / (float)slices));
        int examined = 0;
        int awake = 0;

        for (int i = 0; i < count && examined < perSlice; i++)
        {
            int index = (cursor + i) % count;
            SleepableBody b = bodies[index];

            if (b == null) continue;

            // Awake bodies get a longer leash, so one sitting on the boundary
            // does not sleep and wake every other frame.
            float limit = b.IsAwake ? wakeRadius + hysteresis : wakeRadius;
            bool inRange = (b.transform.position - origin).sqrMagnitude <= limit * limit;

            b.Tick(now, inRange);
            examined++;

            if (b.IsAwake) awake++;
        }

        cursor = count > 0 ? (cursor + examined) % count : 0;
        AwakeCount = awake;
    }

    Transform ResolvePlayer()
    {
        if (player != null) return player;

        if (Camera.main != null) { player = Camera.main.transform; return player; }

        GameObject centre = GameObject.Find("Center Point");
        if (centre == null) centre = GameObject.Find("center point");

        if (centre != null) player = centre.transform;

        return player;
    }

    void OnDrawGizmosSelected()
    {
        Transform reference = player != null ? player : transform;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(reference.position, wakeRadius);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(reference.position, wakeRadius + hysteresis);
    }
}
