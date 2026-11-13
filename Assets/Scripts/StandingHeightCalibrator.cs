using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Makes a seated player see the world as if standing.
///
/// On the Quest's recenter (hold the Meta button) and once at startup, the
/// XR Origin's Camera Offset is raised or lowered so the headset sits at the
/// pilot avatar's standing eye height, whatever the player's real head height
/// is. Sitting down and recentering puts your view back in the pilot's head
/// instead of in its chest.
///
/// Nothing to wire: it adds itself to every XR Origin after a scene loads, and
/// measures the avatar (the "mixamorig:Head" bone under the XR Origin) at
/// startup. Add it yourself to override the fields; auto-install skips origins
/// that already have one.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class StandingHeightCalibrator : MonoBehaviour
{
    [Tooltip("Bone whose height is the avatar's standing head height.")]
    public string headBoneName = "mixamorig:Head";
    [Tooltip("Eyes sit this far above the head bone.")]
    public float eyeAboveHeadBone = 0.1f;
    [Tooltip("Used if the head bone cannot be found or measures implausibly.")]
    public float fallbackEyeHeight = 1.65f;
    [Tooltip("Calibrate once at startup, a moment after tracking starts.")]
    public bool calibrateOnStart = true;
    [Tooltip("Seconds after startup before the first calibration, so tracking has a real pose.")]
    public float startDelay = 1f;

    [Header("Recenter fallback")]
    [Tooltip("Also treat a sudden jump in head pose as a recenter, in case the runtime does not report one.")]
    public bool detectPoseJumps = true;
    public float jumpDistance = 0.35f;
    public float jumpAngle = 45f;

    /// <summary>Standing eye height above the XR Origin, measured from the avatar.</summary>
    public float StandingEyeHeight { get; private set; }

    XROrigin origin;
    Transform offset;
    Transform cam;
    Vector3 lastCamLocalPos;
    float lastCamYaw;
    bool haveLast;
    readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        InstallAll();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => InstallAll();

    static void InstallAll()
    {
        foreach (XROrigin o in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
            if (o.GetComponent<StandingHeightCalibrator>() == null)
                o.gameObject.AddComponent<StandingHeightCalibrator>();
    }

    void Awake()
    {
        origin = GetComponent<XROrigin>();
        StandingEyeHeight = MeasureAvatarEyeHeight();
        Debug.Log($"[HeightCal] standing eye height {StandingEyeHeight:0.00} m");
    }

    void OnEnable()
    {
        SubsystemManager.GetSubsystems(subsystems);
        foreach (XRInputSubsystem s in subsystems)
        {
            s.trackingOriginUpdated -= OnTrackingOriginUpdated;
            s.trackingOriginUpdated += OnTrackingOriginUpdated;
        }
    }

    void OnDisable()
    {
        foreach (XRInputSubsystem s in subsystems)
            if (s != null) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
    }

    IEnumerator Start()
    {
        if (!calibrateOnStart) yield break;
        yield return new WaitForSeconds(startDelay);
        Calibrate("startup");
    }

    float MeasureAvatarEyeHeight()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name != headBoneName) continue;
            float h = t.position.y - transform.position.y + eyeAboveHeadBone;
            if (h > 0.5f && h < 3f) return h;
        }
        return fallbackEyeHeight;
    }

    void OnTrackingOriginUpdated(XRInputSubsystem s)
    {
        // XROrigin resets the Camera Offset in this same event, and the new
        // head pose arrives next frame: calibrate after both.
        StartCoroutine(CalibrateNextFrame("recenter"));
    }

    IEnumerator CalibrateNextFrame(string why)
    {
        yield return null;
        yield return null;
        Calibrate(why);
    }

    void LateUpdate()
    {
        if (!detectPoseJumps || !Resolve()) return;

        Vector3 p = cam.localPosition;
        float yaw = cam.localEulerAngles.y;
        if (haveLast)
        {
            bool jumped = (p - lastCamLocalPos).magnitude > jumpDistance
                          || Mathf.Abs(Mathf.DeltaAngle(yaw, lastCamYaw)) > jumpAngle;
            if (jumped) StartCoroutine(CalibrateNextFrame("pose jump"));
        }
        lastCamLocalPos = p;
        lastCamYaw = yaw;
        haveLast = true;
    }

    bool Resolve()
    {
        if (origin == null || origin.Camera == null) return false;
        cam = origin.Camera.transform;
        offset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : cam.parent;
        return offset != null;
    }

    /// <summary>Put the headset at standing eye height above the origin, right now.</summary>
    public void Calibrate(string why = "manual")
    {
        if (!Resolve()) return;

        // Real head height above the tracking floor, as the headset reports it.
        float trackedHeight = cam.localPosition.y;
        if (trackedHeight < 0.2f) return; // no real pose yet

        Vector3 lp = offset.localPosition;
        lp.y = StandingEyeHeight - trackedHeight;
        offset.localPosition = lp;

        // The adjustment itself moves nothing in camera-local space, so it
        // cannot trip the jump detector; just refresh the reference.
        lastCamLocalPos = cam.localPosition;
        haveLast = true;

        Debug.Log($"[HeightCal] {why}: head {trackedHeight:0.00} m -> view at {StandingEyeHeight:0.00} m (offset {lp.y:+0.00;-0.00})");
    }
}
