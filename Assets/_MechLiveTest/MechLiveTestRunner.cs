#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// TEMPORARY live play-mode test for MechLimbPhysics / MechGrabber / sleep.
// Started by MechLiveTestLauncher in batch mode; writes MechLiveTest.log and exits.
public class MechLiveTestRunner : MonoBehaviour
{
    const string CarPath = "Assets/Prefabs/Traffic/Traffic_MF_Sedan_01.prefab";
    readonly StringBuilder log = new StringBuilder();
    int pass, fail;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (System.Environment.GetEnvironmentVariable("MECH_LIVE_TEST") != "1") return;
        new GameObject("MechLiveTestRunner").AddComponent<MechLiveTestRunner>();
    }

    void Log(string s) { log.AppendLine(s); Debug.Log("[MechLiveTest] " + s); }
    void Check(bool ok, string what)
    {
        if (ok) pass++; else fail++;
        Log((ok ? "PASS  " : "FAIL  ") + what);
    }

    IEnumerator Start()
    {
        Application.logMessageReceived += (c, st, t) =>
        { if (t == LogType.Exception || t == LogType.Error) log.AppendLine("  [unity " + t + "] " + c); };

        float watchdog = Time.realtimeSinceStartup;
        yield return new WaitForSeconds(0.5f);

        IEnumerator[] tests = { Integration(), DrivingCarHitsMechHand(), GrabHoldReleaseSleep(), SwipeMovingHand(), CarryAndThrow(), CrushInFist() };
        foreach (IEnumerator t in tests)
        {
            Log("");
            yield return StartCoroutine(Safe(t));
        }

        Log("");
        Log($"RESULT: {pass} passed, {fail} failed  ({Time.realtimeSinceStartup - watchdog:0.0}s real)");
        System.IO.File.WriteAllText("MechLiveTest.log", log.ToString());
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    IEnumerator Safe(IEnumerator t)
    {
        while (true)
        {
            object cur;
            try { if (!t.MoveNext()) yield break; cur = t.Current; }
            catch (System.Exception e) { Check(false, "exception: " + e); yield break; }
            yield return cur;
        }
    }

    // ------------------------------------------------------------ helpers

    MechLimbPhysics realRight;
    readonly List<GameObject> spawned = new List<GameObject>();

    GameObject SpawnCar(Vector3 centre, Vector3 facing)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
        GameObject car = Instantiate(prefab, centre, Quaternion.LookRotation(facing));
        // Put the collider's centre (not the pivot) on the requested point.
        Bounds b = car.GetComponent<Collider>().bounds;
        car.transform.position += centre - b.center;
        Physics.SyncTransforms();
        spawned.Add(car);
        return car;
    }

    static void DisableHandTracking()
    {
        foreach (var g in FindObjectsByType<HandTrackedGripSource>(FindObjectsSortMode.None)) g.enabled = false;
    }

    IEnumerator Hold(MechGrabber g, float grip, float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) { g.SetGrip(grip); yield return null; }
    }

    IEnumerator WaitUntilOrTimeout(System.Func<bool> cond, float timeout)
    {
        float end = Time.time + timeout;
        while (!cond() && Time.time < end) yield return null;
    }

    // ------------------------------------------------------------ tests

    IEnumerator Integration()
    {
        Log("== 1. Auto-attach on Eli's real mech rig");
        var limbs = FindObjectsByType<RigidbodyFollowBone>(FindObjectsSortMode.None);
        int withPhysics = 0, withGrab = 0;
        foreach (var l in limbs)
        {
            var p = l.GetComponent<MechLimbPhysics>();
            if (p != null) withPhysics++;
            if (p != null && p.Grabber != null)
            {
                withGrab++;
                var s = p.Grabber.GetComponent<SphereCollider>();
                Log($"   {l.name}: grab sphere r={s.radius * s.transform.lossyScale.x:0.00}m at {s.transform.position:F2}, handedness={p.GripSource.handedness}, layer={LayerMask.LayerToName(l.gameObject.layer)}");
                if (l.name == "RightHand") realRight = p;
            }
        }
        Check(limbs.Length == 4 && withPhysics == 4, $"MechLimbPhysics on all mech bodies ({withPhysics}/{limbs.Length})");
        Check(withGrab == 2, $"grab volumes on both hands only ({withGrab})");
        Check(realRight != null && realRight.GripSource.handedness == UnityEngine.XR.Hands.Handedness.Right, "RightHand resolved as Right");

        DisableHandTracking(); // no headset in batch mode; tests drive SetGrip directly

        // Big ground under everything so dropped cars land.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "TestGround";
        ground.transform.position = new Vector3(0, -0.5f, 0);
        ground.transform.localScale = new Vector3(600, 1, 600);
        yield return null;
    }

    IEnumerator DrivingCarHitsMechHand()
    {
        Log("== 2. Driving (kinematic) car meets static mech hand (kinematic)");
        if (realRight == null) { Check(false, "no RightHand"); yield break; }

        Vector3 p = realRight.GetComponent<Rigidbody>().position;
        Bounds hb = default; bool any = false;
        foreach (Collider c in realRight.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) { if (!any) { hb = c.bounds; any = true; } else hb.Encapsulate(c.bounds); }
        p = hb.center;
        Log($"   hand colliders centre {p:F2} size {hb.size:F2}");

        // Route along +X straight through the hand.
        var routeGo = new GameObject("TestRoute");
        routeGo.SetActive(false);
        for (int i = 0; i < 3; i++)
        {
            var w = new GameObject("wp" + i).transform;
            w.SetParent(routeGo.transform);
            w.position = p + Vector3.right * (-25f + 25f * i);
        }
        var route = routeGo.AddComponent<TrafficRoute>();
        route.loop = false;
        routeGo.SetActive(true);

        GameObject car = SpawnCar(p + Vector3.right * -25f, Vector3.right);
        var cc = car.GetComponent<CarController>();
        float pivotToCentre = car.GetComponent<Collider>().bounds.center.y - car.transform.position.y;
        foreach (Transform w in routeGo.transform) w.position -= Vector3.up * pivotToCentre;
        cc.route = route;
        var rb = car.GetComponent<Rigidbody>();
        var sb = car.GetComponent<SleepableBody>();

        yield return WaitUntilOrTimeout(() => car == null || !cc.IsDriving, 6f);
        if (car == null) { Check(false, "car drove through the hand and despawned at route end"); yield break; }

        Check(!cc.IsDriving, $"car stopped being traffic on contact (x={car.transform.position.x - p.x:0.0}m from hand centre)");
        yield return new WaitForFixedUpdate();
        Check(!rb.isKinematic && sb.IsAwake, "car became a dynamic physics body");
        Log($"   velocity just after contact {rb.linearVelocity:F2}");

        Check(PhysicsSleepDirector.Instance != null, "sleep director auto-created in a scene that started with none");

        yield return WaitUntilOrTimeout(() => !sb.IsAwake, 15f);
        Check(!sb.IsAwake && rb.isKinematic, $"car went back to sleep after settling (rest pos y={car.transform.position.y:0.00})");
        Destroy(car);
        Destroy(routeGo);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator GrabHoldReleaseSleep()
    {
        Log("== 3. Grab with the real mech hand, hold 5s, gentle squeeze, release");
        var g = realRight.Grabber;
        Vector3 c = g.transform.position;

        g.SetGrip(0.31f);
        GameObject car = SpawnCar(c, Vector3.right);
        var crush = car.GetComponent<CrushableObject>();
        var rb = car.GetComponent<Rigidbody>();
        var sb = car.GetComponent<SleepableBody>();

        float t0 = Time.time;
        while (g.Held == null && Time.time - t0 < 2f) { g.SetGrip(0.31f); yield return null; }
        Check(g.Held == crush, $"hand grabbed the car ({Time.time - t0:0.00}s)");
        if (g.Held != crush) { Destroy(car); yield break; }

        Vector3 offset = g.transform.InverseTransformPoint(car.transform.position);
        float integrity0 = crush.Integrity;
        bool awakeAtGrab = sb.IsAwake;
        int slept = 0;
        sb.onSlept.AddListener(() => slept++);
        yield return Hold(g, 0.31f, 5f);
        Check(g.Held == crush && rb.isKinematic && crush.IsHeld, "still held after 5s");
        Check(Vector3.Distance(g.transform.InverseTransformPoint(car.transform.position), offset) < 0.05f, "car stays fixed in the hand");
        Check(slept == 0 && sb.IsAwake == awakeAtGrab, $"sleep system left it alone while held (awake={sb.IsAwake}, slept {slept}x)");

        yield return Hold(g, 0.6f, 2f);
        Check(Mathf.Approximately(crush.Integrity, integrity0) && g.Held == crush, $"grip 0.6 ({g.CurrentGripForce:0} N) carries without damage");

        g.SetGrip(0f);
        yield return null; yield return new WaitForFixedUpdate();
        Check(g.Held == null && !rb.isKinematic && !crush.IsHeld, "open hand releases it as a dynamic body");
        float y0 = car.transform.position.y;
        yield return new WaitForSeconds(0.6f);
        Check(car.transform.position.y < y0 - 0.3f, $"released car falls (dropped {y0 - car.transform.position.y:0.00}m in 0.6s)");

        yield return WaitUntilOrTimeout(() => !sb.IsAwake, 15f);
        Check(!sb.IsAwake && rb.isKinematic, "released car settles and sleeps");
        Destroy(car);
        yield return new WaitForSeconds(0.2f);
    }

    // A synthetic limb exactly like Eli's (kinematic RB + RigidbodyFollowBone
    // + child box collider on MechCollider) whose bone we can actually move.
    (Transform bone, MechLimbPhysics limb) MakeMovingHand(Vector3 at)
    {
        var bone = new GameObject("TestBone").transform;
        bone.position = at;
        var hand = new GameObject("TestRightHand");
        hand.layer = LayerMask.NameToLayer("MechCollider");
        hand.transform.position = at;
        var rb = hand.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.mass = 1;
        var col = new GameObject("palm");
        col.layer = hand.layer;
        col.transform.SetParent(hand.transform, false);
        col.AddComponent<BoxCollider>().size = new Vector3(1.6f, 0.8f, 1.6f);
        var follow = hand.AddComponent<RigidbodyFollowBone>();
        typeof(RigidbodyFollowBone).GetField("bone", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(follow, bone);
        var limb = hand.AddComponent<MechLimbPhysics>();
        limb.side = MechLimbPhysics.Side.Right;
        DisableHandTracking();
        spawned.Add(hand); spawned.Add(bone.gameObject);
        return (bone, limb);
    }

    IEnumerator MoveBone(Transform bone, Vector3 velocity, float seconds, MechGrabber g = null, float grip = 0)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            bone.position += velocity * Time.deltaTime;
            if (g != null) g.SetGrip(grip);
            yield return null;
        }
    }

    IEnumerator SwipeMovingHand()
    {
        Log("== 4. Moving mech hand swipes a parked (sleeping) car at 5 m/s");
        Vector3 q = new Vector3(60, 1.2f, 0);
        var (bone, limb) = MakeMovingHand(q);
        GameObject car = SpawnCar(q + Vector3.right * 6f, Vector3.forward);
        var rb = car.GetComponent<Rigidbody>();
        var sb = car.GetComponent<SleepableBody>();
        var crush = car.GetComponent<CrushableObject>();
        yield return new WaitForSeconds(0.3f);
        Check(!sb.IsAwake && rb.isKinematic, "parked car starts asleep (kinematic)");

        float i0 = crush.Integrity;
        float maxV = 0;
        float end = Time.time + 2f;
        while (Time.time < end)
        {
            bone.position += Vector3.right * 5f * Time.deltaTime;
            if (!rb.isKinematic) maxV = Mathf.Max(maxV, rb.linearVelocity.x);
            yield return null;
        }
        Check(sb.IsAwake && !rb.isKinematic, "swipe woke the car");
        Check(maxV > 2f, $"car was shoved along +X (peak {maxV:0.0} m/s, hand 5 m/s)");
        Check(crush.Integrity < i0, $"5 m/s hit dented it (integrity {i0:0} -> {crush.Integrity:0}, state {crush.State})");
        Destroy(car);
        Destroy(limb.gameObject); Destroy(bone.gameObject);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator CarryAndThrow()
    {
        Log("== 5. Grab with a moving hand, carry it, throw it");
        Vector3 q = new Vector3(-60, 3f, 0);
        var (bone, limb) = MakeMovingHand(q);
        yield return null;
        var g = limb.Grabber;
        Check(g != null, "inspector-style MechLimbPhysics built its own grab volume");
        if (g == null) yield break;

        g.SetGrip(0.31f);
        GameObject car = SpawnCar(g.transform.position, Vector3.right);
        var rb = car.GetComponent<Rigidbody>();
        var crush = car.GetComponent<CrushableObject>();
        float t0 = Time.time;
        while (g.Held == null && Time.time - t0 < 2f) { g.SetGrip(0.31f); yield return null; }
        Check(g.Held == crush, "grabbed");
        if (g.Held != crush) yield break;

        Vector3 start = car.transform.position;
        yield return MoveBone(bone, new Vector3(0, 1.5f, 2.5f), 2f, g, 0.31f);
        Vector3 moved = car.transform.position - start;
        Check(moved.y > 2.5f && moved.z > 4.5f, $"car carried with the hand (moved {moved:F2}, expected ~(0,3,5))");

        // Wind up along +X then open the hand mid-swing.
        yield return MoveBone(bone, new Vector3(8f, 0, 0), 0.5f, g, 0.31f);
        g.SetGrip(0f);
        yield return null; yield return new WaitForFixedUpdate();
        Check(g.Held == null && !rb.isKinematic, "released mid-swing");
        Log($"   velocity at release {rb.linearVelocity:F2}");
        Check(rb.linearVelocity.x > 3f, $"car thrown in the swing direction (vx {rb.linearVelocity.x:0.0} m/s, hand 8 m/s)");
        yield return new WaitForSeconds(1f);
        Check(car.transform.position.x > start.x + 5f, $"thrown car travelled (x +{car.transform.position.x - start.x:0.0}m)");
        Destroy(car);
        Destroy(limb.gameObject); Destroy(bone.gameObject);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator CrushInFist()
    {
        Log("== 6. Full grip crushes a held car");
        var g = realRight.Grabber;
        g.SetGrip(0.31f);
        GameObject car = SpawnCar(g.transform.position, Vector3.right);
        var crush = car.GetComponent<CrushableObject>();
        float t0 = Time.time;
        while (g.Held == null && Time.time - t0 < 2f) { g.SetGrip(0.31f); yield return null; }
        Check(g.Held == crush, "grabbed");
        float end = Time.time + 4f;
        while (Time.time < end && car != null && !crush.IsDestroyed) { g.SetGrip(1f); yield return null; }
        Check(car == null || crush.IsDestroyed, $"grip 1.0 ({g.CurrentGripForce:0} N) destroyed it in {Time.time - (end - 4f):0.00}s");
        yield return null;
        Check(g.Held == null, "hand let go of the wreck");
        g.SetGrip(0f);
    }
}
#endif
