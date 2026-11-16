using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The old lady. She walks a PedestrianPath from one kerb to the other and has no
/// idea how much danger she is in.
///
/// NO DEATH, NO INJURY -- this is a hard rule, enforced in code
/// -----------------------------------------------------------
/// She cannot be killed or hurt by anything. There is no harm state and no
/// code path that produces one. The failure state is that she gets
/// FRIGHTENED: she hurries back to the kerb she started from and tries again.
/// That costs the player time and score, nothing else.
///
/// Three things enforce it, not one:
///   1. her CrushableObject has canBeDestroyed OFF, so crush force can never
///      destroy her -- it reports Overstressed instead
///   2. squeezing her makes her squirm free of the grip, which also ends the
///      squeeze
///   3. a car that reaches her performs an emergency stop; a near miss
///      frightens her and nothing more
///
/// Two ways to get her across, both intended:
///   1. Block the road. Cars brake for the mech, and she only treats a MOVING
///      car as a threat, so parking in the lane opens a gap.
///   2. Carry her. Hold her gently and set her down on the far pavement.
///      Handle her roughly and she wriggles out and retreats -- unharmed.
/// </summary>
public class PedestrianCrosser : MonoBehaviour
{
    public enum CrossState { WaitingAtCurb, Crossing, Frozen, Carried, Retreating, KnockedDown, Safe }

    [Header("Identity")]
    public string displayName = "Elderly pedestrian";

    [Header("Path")]
    [Tooltip("Route from the starting kerb to the far pavement. Leave empty and she uses the PedestrianPath whose START is nearest to where you placed her (within Auto Path Radius). A PedestrianSpawner sets this for you.")]
    public PedestrianPath path;
    [Tooltip("When Path is empty, look for a path starting within this many metres of her.")]
    public float autoPathRadius = 12f;
    public float walkSpeed = 0.9f;
    [Tooltip("She hurries when retreating, so a scare is a real setback.")]
    public float retreatSpeed = 1.6f;
    public float turnSpeed = 180f;
    [Tooltip("How close to a waypoint counts as reaching it.")]
    public float waypointTolerance = 0.4f;
    [Tooltip("Distance from the last waypoint that counts as safely across.")]
    public float arriveRadius = 1.5f;

    [Header("Nerve")]
    [Tooltip("She waits for a gap in traffic before stepping out.")]
    public bool waitsForGap = true;
    [Tooltip("No moving car within this distance means the road looks clear to her.")]
    public float safeGapDistance = 18f;
    [Tooltip("Seconds before she gives up waiting and steps out anyway. This is the pressure on the player.")]
    public float patience = 12f;

    [Header("Panic")]
    [Tooltip("She freezes mid-road when a car gets close, instead of walking on.")]
    public bool freezesWhenCarNear = true;
    public float panicDistance = 8f;
    public float freezeDuration = 1.5f;

    [Header("Animation")]
    [Tooltip("Float parameter on the Animator driven by how fast she is actually moving. Blank to skip.")]
    public string speedParameter = "Speed";

    [Header("Knockdown")]
    [Tooltip("A vehicle at or above this speed knocks her down instead of merely startling her.")]
    public float knockdownSpeed = 4f;
    [Tooltip("Seconds spent on the ground before she blinks back to the start.")]
    public float knockdownDuration = 2.5f;
    [Tooltip("Shove given to the ragdoll, so she is thrown rather than simply dropped.")]
    public float knockdownImpulse = 6f;
    [Tooltip("Use the ragdoll bones if the character has them. Without a rig she tumbles as a single body instead.")]
    public bool useRagdoll = true;

    [Header("Traffic awareness")]
    public LayerMask vehicleLayers;

    [Header("Obstacles")]
    [Tooltip("She stops for the mech and for anything physical in her way (cars, knocked-over props, wrecks, debris) instead of walking through it.")]
    public bool blockedByObstacles = true;
    [Tooltip("How far ahead she checks, beyond this frame's step.")]
    public float obstacleLookahead = 0.45f;
    public float bodyRadius = 0.3f;
    [Tooltip("Blocked this long by something that is NOT the mech, she steps around it. The mech she always waits for.")]
    public float detourAfter = 2.5f;

    /// <summary>What is in her way right now, or null.</summary>
    public Collider Blocker { get; private set; }
    public bool BlockedByMech { get; private set; }

    [Header("Feedback")]
    [Tooltip("Spawned where she reaches the far pavement: the 'you did well' effect.")]
    public GameObject successEffect;

    [Header("Events")]
    public UnityEvent onStartedCrossing;
    public UnityEvent onSaved;
    [Tooltip("She got a fright and is retreating. The failure cue - she is never hurt.")]
    public UnityEvent onFrightened;
    public UnityEvent onPickedUp;
    [Tooltip("Knocked off her feet. Never an injury - she gets up at the start again.")]
    public UnityEvent onKnockedDown;
    [Tooltip("Blinked back to the start, ready to try again.")]
    public UnityEvent onReturnedToStart;

    public CrossState State { get; private set; } = CrossState.WaitingAtCurb;
    public bool IsSafe => State == CrossState.Safe;

    /// <summary>How many times she has been scared back to the kerb.</summary>
    public int ScareCount { get; private set; }

    Rigidbody body;
    CrushableObject crushable;
    Animator animator;
    Rigidbody[] ragdollBodies;
    Collider[] ragdollColliders;
    float knockdownUntil;
    Vector3 lastAnimPosition;
    float animSpeed;
    int targetIndex;
    float waitingSince;
    float frozenUntil;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        crushable = GetComponent<CrushableObject>();
        animator = GetComponentInChildren<Animator>();

        CacheRagdoll();
        lastAnimPosition = transform.position;

        if (body != null) body.isKinematic = true;

        EnforceNonLethalSetup();

        waitingSince = Time.time;
    }

    /// <summary>
    /// The no-death rule is not left to inspector discipline. If someone
    /// configures her as destructible, this corrects it and says so loudly.
    /// </summary>
    void EnforceNonLethalSetup()
    {
        if (crushable == null) return;

        if (crushable.canBeDestroyed)
        {
            Debug.LogWarning(
                $"[PedestrianCrosser] {name}: CrushableObject.canBeDestroyed was ON for a civilian. " +
                "Forcing it OFF - civilians can never be destroyed.", this);
            crushable.canBeDestroyed = false;
        }

        if (crushable.canExplode)
        {
            Debug.LogWarning(
                $"[PedestrianCrosser] {name}: CrushableObject.canExplode was ON for a civilian. Forcing it OFF.",
                this);
            crushable.canExplode = false;
        }

        if (crushable.countsAsPropertyDamage)
        {
            // Otherwise she would be billed as a broken thing as well.
            Debug.LogWarning(
                $"[PedestrianCrosser] {name}: CrushableObject.countsAsPropertyDamage was ON for a civilian. Forcing it OFF.",
                this);
            crushable.countsAsPropertyDamage = false;
        }
    }

    void OnEnable()
    {
        if (crushable == null) return;

        crushable.HeldChanged += OnHeldChanged;
        crushable.Overstressed += OnHandledRoughly;
    }

    void OnDisable()
    {
        if (crushable == null) return;

        crushable.HeldChanged -= OnHeldChanged;
        crushable.Overstressed -= OnHandledRoughly;
    }

    void Start()
    {
        // Dropped into the scene on her own: walk the nearest path.
        if (path == null)
        {
            path = PedestrianPath.FindNearestStart(transform.position, autoPathRadius);
            if (path == null)
                Debug.LogWarning($"[PedestrianCrosser] {name}: no PedestrianPath starts within {autoPathRadius}m, " +
                                 "so she will stand still. Drag a PedestrianPath prefab next to her.", this);
            else
                PlaceAtStart();
        }

        if (path != null && path.IsValid)
            targetIndex = path.NearestIndex(transform.position);
    }

    /// <summary>Give her a path and stand her on its first point, facing across.</summary>
    public void AssignPath(PedestrianPath newPath)
    {
        path = newPath;
        detour = null;
        PlaceAtStart();
        targetIndex = 0;
        waitingSince = Time.time;
        State = CrossState.WaitingAtCurb;
    }

    void PlaceAtStart()
    {
        if (path == null || !path.IsValid) return;
        transform.SetPositionAndRotation(path.StartPosition, path.StartRotation);
        lastAnimPosition = transform.position;
    }

    void Update()
    {
        if (State == CrossState.KnockedDown)
        {
            // Lying on the ground. Let the ragdoll land, then put her back at
            // the start to try again.
            if (Time.time >= knockdownUntil) ReturnToStart();
            return;
        }

        if (State == CrossState.Safe || State == CrossState.Carried) return;
        if (path == null || !path.IsValid) return;

        switch (State)
        {
            case CrossState.WaitingAtCurb:
                TickWaiting();
                break;

            case CrossState.Frozen:
                if (Time.time >= frozenUntil) State = CrossState.Crossing;
                break;

            case CrossState.Crossing:
                TickCrossing();
                break;

            case CrossState.Retreating:
                TickRetreating();
                break;
        }

        UpdateAnimation();
    }

    void TickWaiting()
    {
        bool outOfPatience = Time.time - waitingSince >= patience;

        if (!waitsForGap || outOfPatience || RoadLooksClear()) StartCrossing();
    }

    void StartCrossing()
    {
        State = CrossState.Crossing;
        onStartedCrossing?.Invoke();
    }

    void TickCrossing()
    {
        if (freezesWhenCarNear && NearestVehicleDistance() < panicDistance)
        {
            State = CrossState.Frozen;
            frozenUntil = Time.time + freezeDuration;
            return;
        }

        Vector3 target = path.GetPosition(targetIndex);
        float distance = StepToward(target, walkSpeed);

        if (distance > waypointTolerance) return;

        int next = path.NextIndex(targetIndex);

        // End of the route, or close enough to the far pavement.
        if ((next < 0 || targetIndex >= path.Count - 1)
            && Vector3.Distance(transform.position, path.EndPosition) <= arriveRadius)
        {
            ReachSafety();
            return;
        }

        if (next >= 0) targetIndex = next;
    }

    void TickRetreating()
    {
        Vector3 start = path.GetPosition(0);
        float distance = StepToward(start, retreatSpeed);

        if (distance > waypointTolerance) return;

        // Back on the kerb, shaken but fine. She will try again.
        targetIndex = 0;
        waitingSince = Time.time;
        State = CrossState.WaitingAtCurb;

        if (crushable != null) crushable.ClearOverstress();
    }

    /// <summary>
    /// Feeds the Animator from distance actually covered, not intended speed,
    /// so she stands still in the animation whenever she is blocked or frozen
    /// rather than moonwalking on the spot.
    /// </summary>
    void UpdateAnimation()
    {
        if (animator == null || !animator.enabled || string.IsNullOrEmpty(speedParameter)) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float moved = Vector3.Distance(transform.position, lastAnimPosition) / dt;
        lastAnimPosition = transform.position;

        animSpeed = Mathf.Lerp(animSpeed, moved, 1f - Mathf.Exp(-10f * dt));

        animator.SetFloat(speedParameter, animSpeed);
    }

    /// <summary>Walk toward a point. Returns the pre-step flat distance to it.</summary>
    float StepToward(Vector3 target, float speed)
    {
        // Going round something? Head for the side-step point first.
        if (detour.HasValue)
        {
            Vector3 d = detour.Value - transform.position; d.y = 0f;
            if (d.magnitude <= waypointTolerance) detour = null;
        }
        Vector3 aim = detour ?? target;

        Vector3 flat = target - transform.position;
        flat.y = 0f;
        Vector3 aimFlat = aim - transform.position;
        aimFlat.y = 0f;

        if (aimFlat.sqrMagnitude > 0.0001f)
        {
            Quaternion want = Quaternion.LookRotation(aimFlat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, want, turnSpeed * Time.deltaTime);
        }

        float step = speed * Time.deltaTime;

        if (blockedByObstacles && aimFlat.sqrMagnitude > 0.0001f && IsBlocked(transform.position, aimFlat.normalized, step))
        {
            blockedFor += Time.deltaTime;
            if (!BlockedByMech && blockedFor >= detourAfter && !detour.HasValue) PlanDetour(aimFlat.normalized);
            return flat.magnitude;   // stand still; animation reads zero speed
        }

        blockedFor = 0f;
        Blocker = null;
        BlockedByMech = false;

        Vector3 next = Vector3.MoveTowards(transform.position, aim, step);
        // Keep her feet at the path's height, not the detour point's.
        if (detour.HasValue) next.y = Mathf.MoveTowards(transform.position.y, target.y, step);
        transform.position = next;

        return flat.magnitude;
    }

    Vector3? detour;
    float blockedFor;
    readonly RaycastHit[] obstacleHits = new RaycastHit[16];

    /// <summary>
    /// Is something solid that should stop her within reach in this direction?
    /// Counts: the mech; any non-kinematic rigid body; cars; props that have
    /// been knocked out of place or broken. Ignores the ground, buildings,
    /// props still standing where the level put them, and herself.
    /// </summary>
    bool IsBlocked(Vector3 from, Vector3 dir, float step)
    {
        Vector3 bottom = from + Vector3.up * (bodyRadius + 0.25f);
        Vector3 top = from + Vector3.up * 1.5f;
        int n = Physics.CapsuleCastNonAlloc(bottom, top, bodyRadius, dir, obstacleHits,
            step + obstacleLookahead, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
        {
            Collider c = obstacleHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            // Already overlapping at the start of the cast: report only if it is
            // genuinely in front of her, so she can always walk out of a touch.
            if (obstacleHits[i].distance <= 0f)
            {
                Vector3 to = c.ClosestPoint(from + Vector3.up) - from; to.y = 0f;
                if (to.sqrMagnitude > 0.0001f && Vector3.Dot(to.normalized, dir) < 0.3f) continue;
            }

            bool mech = c.GetComponentInParent<MechLocomotion>() != null
                        || c.gameObject.layer == LayerMask.NameToLayer("MechCollider");
            if (mech) { Blocker = c; BlockedByMech = true; return true; }

            Rigidbody rb = c.attachedRigidbody;
            if (rb == null) continue;   // static world: ground, kerbs, buildings
            if (rb.GetComponent<DebrisLife>() != null) continue;   // small chunks, gone in seconds

            if (rb.GetComponentInParent<CarController>() != null) { Blocker = c; BlockedByMech = false; return true; }

            PropAssembly prop = rb.GetComponent<PropAssembly>();
            if (prop != null && !prop.IsDisplaced) continue;   // standing where the level put it

            if (!rb.isKinematic || prop != null || PropAssembly.OwnerOf(rb.transform) != null)
            { Blocker = c; BlockedByMech = false; return true; }
        }
        return false;
    }

    /// <summary>Find a clear point to the side and step around the obstacle.</summary>
    void PlanDetour(Vector3 dir)
    {
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        // Near first; far enough to get round a fallen lamp post.
        float[] offsets = { 1f, -1f, 1.8f, -1.8f, 2.6f, -2.6f, 3.5f, -3.5f, 4.5f, -4.5f, 6f, -6f };
        foreach (float o in offsets)
        {
            Vector3 p = transform.position + side * o + dir * 1.2f;
            Vector3 to = p - transform.position; to.y = 0f;
            if (IsBlocked(transform.position, to.normalized, to.magnitude)) continue;
            Vector3 onward = dir; // from the side point, the way ahead must be clear too
            if (IsBlocked(p, onward, 1.5f)) continue;
            detour = p;
            blockedFor = 0f;
            Blocker = null;
            return;
        }
        blockedFor = 0f;   // nowhere to go yet; wait and try again
    }

    bool RoadLooksClear()
    {
        return NearestVehicleDistance() >= safeGapDistance;
    }

    /// <summary>Distance to the closest MOVING vehicle, or +infinity if none is near.</summary>
    float NearestVehicleDistance()
    {
        if (vehicleLayers.value == 0) return float.PositiveInfinity;

        Collider[] hits = Physics.OverlapSphere(
            transform.position, Mathf.Max(safeGapDistance, panicDistance),
            vehicleLayers.value, QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            CarController car = hits[i].GetComponentInParent<CarController>();

            // A stopped or wrecked car is not a threat, which is what makes
            // body-blocking the lane a real solution.
            if (car == null || !car.IsDriving || car.CurrentSpeed < 0.5f) continue;

            float d = Vector3.Distance(transform.position, car.transform.position);
            if (d < best) best = d;
        }

        return best;
    }

    /// <summary>
    /// Called by CarController when a car gets dangerously close. The car has
    /// already emergency-stopped by this point. She is startled, not hurt.
    /// </summary>
    public void NearMiss(CarController car)
    {
        // A car that actually reached her at speed knocks her over. One that
        // stopped in time only gives her a fright.
        if (car != null && car.CurrentSpeed >= knockdownSpeed)
        {
            KnockDown(car.transform.forward, "clipped by a vehicle");
            return;
        }

        Frighten("a car stopped right in front of her");
    }

    /// <summary>Handled too roughly by the mech. She wriggles free, unhurt.</summary>
    void OnHandledRoughly(CrushableObject obj)
    {
        // The mech squeezed or struck her. Nothing can injure her, so she is
        // knocked off her feet and reset rather than harmed.
        Vector3 away = transform.position - obj.transform.position;
        away.y = 0f;

        KnockDown(away.sqrMagnitude > 0.01f ? away.normalized : transform.forward,
                  "knocked over by the mech");
    }

    void OnHeldChanged(CrushableObject obj, bool held)
    {
        if (State == CrossState.Safe) return;

        if (held)
        {
            State = CrossState.Carried;
            if (body != null) body.isKinematic = true;
            onPickedUp?.Invoke();
            return;
        }

        bool haveRoute = path != null && path.IsValid;

        // Set down on the far pavement? She is home. Anywhere else and she
        // carries on. Guarding on haveRoute matters: without it a missing
        // route measures her distance to herself, which is 0, and every drop
        // would score as a rescue.
        if (haveRoute
            && Vector3.Distance(transform.position, path.EndPosition) <= arriveRadius)
        {
            ReachSafety();
            return;
        }

        if (body != null) body.isKinematic = true;

        // If she was dropped because she squirmed free, she is already
        // retreating - do not overwrite that.
        if (State == CrossState.Retreating) return;

        if (haveRoute) targetIndex = path.NearestIndex(transform.position);
        State = CrossState.Crossing;
    }

    void ReachSafety()
    {
        if (State == CrossState.Safe) return;

        State = CrossState.Safe;

        if (CityObjective.Instance != null)
            CityObjective.Instance.ReportCivilianSafe(displayName);

        // Tell the player they did well.
        if (successEffect != null)
            Instantiate(successEffect, transform.position, Quaternion.identity);

        // Settle into an idle pose instead of walking on the spot.
        if (animator != null && animator.enabled && !string.IsNullOrEmpty(speedParameter))
            animator.SetFloat(speedParameter, 0f);

        onSaved?.Invoke();
    }

    /// <summary>
    /// The only failure this civilian has. She retreats to the kerb and tries
    /// again. There is deliberately no path from here to injury or death.
    /// </summary>
    public void Frighten(string cause)
    {
        if (State == CrossState.Safe || State == CrossState.Retreating) return;

        ScareCount++;

        if (CityObjective.Instance != null)
            CityObjective.Instance.ReportCivilianFrightened(displayName, cause);

        if (body != null) body.isKinematic = true;

        State = CrossState.Retreating;
        onFrightened?.Invoke();
    }

    /// <summary>
    /// Knocked off her feet: ragdoll, land, then blink back to the start.
    /// Deliberately not an injury - she is only ever delayed.
    /// </summary>
    public void KnockDown(Vector3 direction, string cause)
    {
        if (State == CrossState.Safe || State == CrossState.KnockedDown) return;

        ScareCount++;

        if (CityObjective.Instance != null)
            CityObjective.Instance.ReportCivilianFrightened(displayName, cause);

        State = CrossState.KnockedDown;
        knockdownUntil = Time.time + knockdownDuration;

        SetRagdoll(true);

        Vector3 push = direction;
        push.y = 0.35f;                       // a little lift reads better than a slide
        push = push.normalized * knockdownImpulse;

        if (ragdollBodies != null && ragdollBodies.Length > 0)
        {
            foreach (Rigidbody rb in ragdollBodies)
                if (rb != null) rb.AddForce(push, ForceMode.VelocityChange);
        }
        else if (body != null && !body.isKinematic)
        {
            body.AddForce(push, ForceMode.VelocityChange);
        }

        onKnockedDown?.Invoke();
    }

    /// <summary>Stand her up at the first waypoint so she can try again.</summary>
    public void ReturnToStart()
    {
        detour = null;
        blockedFor = 0f;
        SetRagdoll(false);

        if (path != null && path.IsValid)
        {
            transform.position = path.GetPosition(0);

            Vector3 facing = path.GetPosition(1) - path.GetPosition(0);
            facing.y = 0f;

            if (facing.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        }

        if (body != null)
        {
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (crushable != null) crushable.ClearOverstress();

        targetIndex = 0;
        waitingSince = Time.time;
        State = CrossState.WaitingAtCurb;

        // Without this the teleport reads as enormous movement for one frame
        // and she sprints on the spot after respawning.
        lastAnimPosition = transform.position;
        animSpeed = 0f;

        onReturnedToStart?.Invoke();
    }

    /// <summary>
    /// Bones carrying rigidbodies are a ragdoll rig. A character without one
    /// still gets knocked over, just as a single tumbling body.
    /// </summary>
    void CacheRagdoll()
    {
        if (!useRagdoll) return;

        List<Rigidbody> bones = new List<Rigidbody>();
        List<Collider> cols = new List<Collider>();

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == body) continue;          // the root is not part of the ragdoll
            bones.Add(rb);
        }

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            if (col.gameObject == gameObject) continue;
            cols.Add(col);
        }

        ragdollBodies = bones.ToArray();
        ragdollColliders = cols.ToArray();

        if (ragdollBodies.Length > 0) SetRagdoll(false);
    }

    void SetRagdoll(bool on)
    {
        if (animator != null) animator.enabled = !on;

        if (ragdollBodies != null)
            foreach (Rigidbody rb in ragdollBodies)
                if (rb != null) rb.isKinematic = !on;

        if (ragdollColliders != null)
            foreach (Collider col in ragdollColliders)
                if (col != null && !col.isTrigger) col.enabled = on;

        // No ragdoll rig present: tumble the whole character as one body.
        if ((ragdollBodies == null || ragdollBodies.Length == 0) && body != null)
            body.isKinematic = !on;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, safeGapDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, panicDistance);

        if (path != null && path.IsValid)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(path.EndPosition, arriveRadius);
        }
    }
}
