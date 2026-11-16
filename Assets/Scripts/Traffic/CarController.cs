using UnityEngine;

/// <summary>
/// City traffic. Follows a TrafficRoute, brakes for things in the road, and
/// hands itself over to physics the moment it is grabbed or wrecked.
///
/// The car is KINEMATIC while driving. That keeps lanes tidy and stops the mech
/// from bulldozing traffic around by brushing it. It flips to a normal dynamic
/// body when grabbed, crushed or destroyed, so it can be thrown and tumble.
/// CrushableObject does not care either way -- crush force is plain arithmetic.
///
/// Note on the crossing challenge: by default cars DO NOT yield to pedestrians.
/// That is the whole danger. They do brake for whatever is on obstacleLayers,
/// so the intended solution is to put the mech in the road as a roadblock.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Route")]
    public TrafficRoute route;
    public int startWaypoint;
    [Tooltip("How close counts as reaching a waypoint. Also counts as reached once the car has DRIVEN PAST it, so a wide corner cannot leave it orbiting.")]
    public float waypointTolerance = 1.5f;

    [Header("Corners")]
    [Tooltip("Slow down for bends so the car can actually steer round them instead of swinging wide.")]
    public bool slowForCorners = true;
    [Tooltip("Start slowing this far before the corner.")]
    public float cornerLookahead = 22f;
    [Tooltip("Fraction of maxSpeed taken into a 90 degree bend.")]
    [Range(0.1f, 1f)] public float cornerSpeedFactor = 0.3f;

    [Header("Stuck recovery")]
    [Tooltip("Seconds of going nowhere before the car gives up on its waypoint. Stops one confused car jamming a whole lane.")]
    public float stuckTimeout = 6f;
    [Tooltip("Speed below which the car counts as not moving.")]
    public float stuckSpeed = 0.4f;

    [Header("Driving")]
    public float maxSpeed = 11f;
    public float acceleration = 6f;
    public float braking = 18f;
    [Tooltip("Degrees per second of steering toward the next waypoint.")]
    public float turnSpeed = 120f;

    [Header("Slopes and ramps")]
    [Tooltip("Pitch the car to match the slope it is driving. Cosmetic only - it never changes road speed.")]
    public bool alignToSlope = true;
    [Tooltip("Steepest gradient the car will follow, as rise over run. 1 = 45 degrees. Stops a bad waypoint launching a car into the air.")]
    public float maxSlope = 1f;

    [Header("Obstacle sensing")]
    [Tooltip("What makes a car stop: the mech, other cars, barriers.")]
    public LayerMask obstacleLayers;
    [Tooltip("How far ahead it looks.")]
    public float sensorLength = 12f;
    public float sensorRadius = 1.2f;
    [Tooltip("Height above the pivot to cast from, so it does not hit the road.")]
    public float sensorHeight = 0.6f;
    [Tooltip("Comes to a full stop at this distance from an obstacle.")]
    public float stopDistance = 3.5f;

    [Header("Pedestrians")]
    [Tooltip("ON = a careful driver who brakes for people. OFF = the hazard the crossing challenge needs.")]
    public bool yieldToPedestrians;
    public LayerMask pedestrianLayers;
    [Tooltip("How far ahead a pedestrian triggers an emergency stop.")]
    public float pedestrianStopDistance = 2.5f;
    [Tooltip("Seconds the car stays stopped after an emergency stop.")]
    public float emergencyStopDuration = 2f;

    [Header("End of route")]
    [Tooltip("Despawn on reaching the end of a non-looping route.")]
    public bool despawnAtEnd = true;

    public float CurrentSpeed { get; private set; }
    public bool IsEmergencyStopped => Time.time < emergencyStopUntil;

    /// <summary>Going nowhere for a while, for diagnostics.</summary>
    public bool IsStuck { get; private set; }
    public bool IsDriving { get; private set; } = true;
    public bool IsBraking { get; private set; }

    [Header("Crashes")]
    [Tooltip("Log what each car crashed into. Handy for spotting props that block lanes.")]
    public bool logCrashes = true;

    Rigidbody body;
    BoxCollider crashBox;
    readonly Collider[] crashHits = new Collider[16];
    CrushableObject crushable;
    int targetIndex;
    float emergencyStopUntil = -1f;
    float stuckSince = -1f;
    int stuckSkips;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        crushable = GetComponent<CrushableObject>();

        // Broken cars clear themselves away after a while.
        if (GetComponent<WreckDespawner>() == null) gameObject.AddComponent<WreckDespawner>();

        foreach (BoxCollider b in GetComponentsInChildren<BoxCollider>(true))
            if (!b.isTrigger) { crashBox = b; break; }

        body.isKinematic = true;
        targetIndex = startWaypoint;
    }

    void OnEnable()
    {
        if (crushable == null) return;

        crushable.HeldChanged += OnHeldChanged;
        crushable.WasDestroyed += OnWasDestroyed;
    }

    void OnDisable()
    {
        if (crushable == null) return;

        crushable.HeldChanged -= OnHeldChanged;
        crushable.WasDestroyed -= OnWasDestroyed;
    }

    void OnHeldChanged(CrushableObject obj, bool held)
    {
        // Picked up: stop being traffic. Put down intact: it is debris, not traffic.
        if (held) StopDriving();
    }

    void OnWasDestroyed(CrushableObject obj)
    {
        StopDriving();
    }

    /// <summary>Hand the car over to physics for good.</summary>
    public void StopDriving()
    {
        if (!IsDriving) return;

        IsDriving = false;
        CurrentSpeed = 0f;

        // Only hand it to physics if nothing else owns it right now.
        if (crushable != null && crushable.IsHeld) return;

        SleepableBody sleepable = GetComponent<SleepableBody>();
        if (sleepable != null) sleepable.Wake();
        else body.isKinematic = false;
    }

    void Update()
    {
        if (!IsDriving || route == null || !route.IsValid) return;

        CheckForPedestrians();

        // An emergency stop overrides everything else. A car never drives
        // through a person: it stops dead, and the fright is the consequence.
        if (IsEmergencyStopped)
        {
            CurrentSpeed = 0f;
            IsBraking = true;
            return;
        }

        float desired = maxSpeed;

        float obstacle = DistanceToObstacle();
        if (obstacle >= 0f)
        {
            // Ramp down to a stop as the gap closes.
            float t = Mathf.InverseLerp(stopDistance, sensorLength, obstacle);
            desired = maxSpeed * Mathf.Clamp01(t);
        }

        if (slowForCorners) desired = Mathf.Min(desired, CornerSpeedLimit());

        IsBraking = desired < CurrentSpeed;

        CurrentSpeed = Mathf.MoveTowards(
            CurrentSpeed, desired,
            (IsBraking ? braking : acceleration) * Time.deltaTime);

        TrackStuck();

        Steer();
        Advance();

        if (IsDriving) CheckForCrash();
    }

    /// <summary>
    /// Kinematic bodies get no collisions with each other, so a driving car
    /// would pass through the mech, parked props and other traffic. Check the
    /// car's own box instead: touching a physics body - the mech's hands, feet
    /// or body, a wreck, a woken prop - is a crash, and the car becomes a full
    /// rigidbody simulation carrying its speed. Never a crash: static scenery
    /// (road, kerbs, buildings: no rigidbody), parked props still asleep, and
    /// other cars that are still driving.
    /// Dynamic things that hit a driving car are already handled by
    /// SleepableBody waking it on collision.
    /// </summary>
    void CheckForCrash()
    {
        if (crashBox == null) return;

        Transform t = crashBox.transform;
        Vector3 s = t.lossyScale;
        Vector3 half = Vector3.Scale(crashBox.size * 0.5f, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));

        int n = Physics.OverlapBoxNonAlloc(t.TransformPoint(crashBox.center), half, crashHits,
            t.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
        {
            Collider other = crashHits[i];
            if (other == null || other.transform.IsChildOf(transform)) continue;

            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || rb == body) continue;

            // Chunks thrown off a broken prop are gravel, not an accident:
            // driving over them must not turn the car into a wreck.
            if (rb.GetComponent<DebrisLife>() != null) continue;

            CarController otherCar = rb.GetComponent<CarController>();

            // Two cars that are both still traffic never crash each other. The
            // sensor misses a car it already overlaps, so queues and crossing
            // lanes touch constantly; the traffic logic owns that. A car that
            // has crashed is no longer driving, so running into a wreck still
            // causes a pile-up.
            if (otherCar != null && otherCar.IsDriving) continue;

            // Parked scenery that is asleep (lamp posts, bins, kerbside props)
            // is part of the street, not an accident: cars drove past it before
            // and still do. Once something wakes it, it counts.
            if (otherCar == null)
            {
                SleepableBody prop = rb.GetComponent<SleepableBody>();
                if (prop != null && !prop.IsAwake) continue;
            }

            if (logCrashes)
                Debug.Log($"[Car] {name} crashed into {other.name} (body {rb.name}, kinematic={rb.isKinematic}) at {transform.position:F1}", this);

            Crash();
            return;
        }
    }

    /// <summary>Stop being traffic and become a physics wreck, keeping its momentum.</summary>
    public void Crash()
    {
        if (!IsDriving) return;

        Vector3 momentum = FlatForward() * CurrentSpeed;
        StopDriving();

        if (!body.isKinematic) body.linearVelocity = momentum;
    }

    void Steer()
    {
        Vector3 target = route.GetPosition(targetIndex);
        Vector3 toTarget = target - transform.position;

        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        if (flat.sqrMagnitude < 0.0001f) return;

        // Pitch is cosmetic: it makes a car look like it is climbing the ramp.
        // Movement is driven by FlatForward() + slope below, so adding pitch
        // here cannot change how fast the car travels.
        Vector3 lookDir = flat;

        if (alignToSlope)
        {
            float horizontal = flat.magnitude;
            float rise = Mathf.Clamp(toTarget.y, -horizontal * maxSlope, horizontal * maxSlope);
            lookDir = new Vector3(toTarget.x, rise, toTarget.z);
        }

        Quaternion want = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, want, turnSpeed * Time.deltaTime);
    }

    /// <summary>Facing with any pitch removed, so slopes never change road speed.</summary>
    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;

        return f.sqrMagnitude < 0.000001f ? Vector3.forward : f.normalized;
    }

    void Advance()
    {
        Vector3 target = route.GetPosition(targetIndex);

        float stepLength = CurrentSpeed * Time.deltaTime;
        Vector3 position = transform.position + FlatForward() * stepLength;

        // Follow the lane's height. Lanes can climb a ramp onto another level,
        // so the car has to gain height as it goes. Without this it drives at
        // its spawn height forever and sails straight through the ramp.
        Vector3 toTargetFull = target - transform.position;
        float horizontalDistance = new Vector2(toTargetFull.x, toTargetFull.z).magnitude;

        if (horizontalDistance > 0.01f)
        {
            float slope = Mathf.Clamp(toTargetFull.y / horizontalDistance, -maxSlope, maxSlope);
            position.y += slope * stepLength;
        }
        else
        {
            position.y = target.y;
        }

        transform.position = position;

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        // A waypoint counts as reached if we are near it OR have driven past
        // it. Distance alone is not enough: on a sharp bend the car swings wide,
        // never enters the tolerance circle, and then orbits the waypoint
        // forever -- which jams every car behind it.
        float tolerance = Mathf.Max(waypointTolerance, CurrentSpeed * Time.deltaTime * 2f);

        bool near = toTarget.magnitude <= tolerance;
        bool passed = Vector3.Dot(toTarget, FlatForward()) < 0f
                      && toTarget.magnitude < Mathf.Max(tolerance * 4f, 8f);

        if (!near && !passed) return;

        int next = route.NextIndex(targetIndex);
        if (next < 0)
        {
            if (despawnAtEnd) Destroy(gameObject);
            else StopDriving();
            return;
        }

        targetIndex = next;

        // Progress made: forget any stuck history.
        stuckSince = -1f;
        stuckSkips = 0;
        IsStuck = false;
    }

    /// <summary>
    /// Slowest speed the upcoming bend can be taken at. Without this a car
    /// enters a 90 degree corner at full speed, cannot turn fast enough, and
    /// leaves the road.
    /// </summary>
    float CornerSpeedLimit()
    {
        int next = route.NextIndex(targetIndex);
        if (next < 0) return maxSpeed;

        Vector3 target = route.GetPosition(targetIndex);

        Vector3 incoming = target - transform.position;
        Vector3 outgoing = route.GetPosition(next) - target;
        incoming.y = 0f;
        outgoing.y = 0f;

        if (incoming.sqrMagnitude < 0.01f || outgoing.sqrMagnitude < 0.01f) return maxSpeed;

        float distance = incoming.magnitude;
        if (distance > cornerLookahead) return maxSpeed;

        float angle = Vector3.Angle(incoming, outgoing);
        float sharpness = Mathf.Clamp01(angle / 90f);

        // Full speed far out, cornering speed right at the bend.
        float atCorner = Mathf.Lerp(maxSpeed, maxSpeed * cornerSpeedFactor, sharpness);
        float closeness = 1f - Mathf.Clamp01(distance / cornerLookahead);

        return Mathf.Lerp(maxSpeed, atCorner, closeness);
    }

    /// <summary>
    /// Notices a car that is going nowhere and gets it moving again, so one
    /// confused car cannot block a lane indefinitely.
    /// </summary>
    void TrackStuck()
    {
        // Stopping for an obstacle or a pedestrian is legitimate, not stuck.
        if (IsEmergencyStopped || IsBraking || CurrentSpeed >= stuckSpeed)
        {
            stuckSince = -1f;
            IsStuck = false;
            return;
        }

        if (stuckSince < 0f) { stuckSince = Time.time; return; }
        if (Time.time - stuckSince < stuckTimeout) return;

        IsStuck = true;
        stuckSince = Time.time;
        stuckSkips++;

        // First try skipping the waypoint it cannot reach; if that keeps
        // happening the lane is unusable for this car, so remove it.
        int next = route.NextIndex(targetIndex);

        if (stuckSkips > 3 || next < 0)
        {
            if (despawnAtEnd) Destroy(gameObject);
            else StopDriving();
            return;
        }

        targetIndex = next;
    }

    /// <summary>Distance to the nearest thing blocking the lane, or -1 if clear.</summary>
    float DistanceToObstacle()
    {
        Vector3 origin = transform.position + Vector3.up * sensorHeight;

        int mask = obstacleLayers.value;
        if (yieldToPedestrians) mask |= pedestrianLayers.value;

        if (mask == 0) return -1f;

        if (Physics.SphereCast(origin, sensorRadius, transform.forward,
                out RaycastHit hit, sensorLength, mask, QueryTriggerInteraction.Ignore))
        {
            // Never brake for ourselves or for our own wreckage.
            if (hit.collider.transform.IsChildOf(transform)) return -1f;
            return hit.distance;
        }

        return -1f;
    }

    /// <summary>
    /// Looks for someone in the road and performs an EMERGENCY STOP. Nobody is
    /// ever run over: a near miss startles them and that is the entire
    /// consequence. Done by cast rather than collision because the car is
    /// kinematic while driving, so collision callbacks are not dependable.
    /// </summary>
    void CheckForPedestrians()
    {
        if (pedestrianLayers.value == 0 || CurrentSpeed < 0.5f) return;

        Vector3 origin = transform.position + Vector3.up * sensorHeight;

        if (!Physics.SphereCast(origin, sensorRadius, transform.forward,
                out RaycastHit hit, pedestrianStopDistance, pedestrianLayers.value,
                QueryTriggerInteraction.Collide))
            return;

        // Stop first, then startle. Order matters: by the time she reacts, the
        // car is already stationary.
        CurrentSpeed = 0f;
        emergencyStopUntil = Time.time + emergencyStopDuration;

        PedestrianCrosser person = hit.collider.GetComponentInParent<PedestrianCrosser>();
        if (person != null) person.NearMiss(this);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * sensorHeight;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + transform.forward * sensorLength);
        Gizmos.DrawWireSphere(origin + transform.forward * sensorLength, sensorRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(origin + transform.forward * stopDistance, sensorRadius * 0.9f);
    }
}
