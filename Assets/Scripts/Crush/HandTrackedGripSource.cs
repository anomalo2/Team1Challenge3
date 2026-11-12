using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

/// <summary>
/// Drives a MechGrabber from HAND TRACKING, which has no trigger axis and no
/// pressure sensor.
///
/// Why crushing is a WRIST TWIST and not a harder squeeze
/// -----------------------------------------------------
/// It is tempting to map crush force to how hard the fist is clenched. That
/// cannot work. XRFingerShape.FullCurl is computed from joint FLEXION ANGLES,
/// normalized against per-finger degree ranges and then clamped:
///
///     return 1f - math.clamp(totalNormalizedDegrees / divisor, 0f, 1f);
///
/// For the index finger those ranges bottom out at 102/90/120 degrees, and a
/// merely relaxed closed fist already flexes past them. So FullCurl pins at
/// 1.0 the moment the hand shuts, and clenching harder changes NOTHING -- the
/// fingers are physically blocked by the palm, and in that pose they are also
/// occluded from the headset cameras, so the data is low confidence as well.
/// TipCurl clamps the same way, and Pinch works in a 10-45 mm band that is
/// inside tracking noise. No finger-shape value can sense grip strength.
///
/// Wrist roll can. The wrist is the ROOT joint, always visible, the most
/// reliably tracked thing on the hand, and forearm pronation gives a genuinely
/// analog 0-90+ degrees of travel. So:
///
///   CLOSE YOUR HAND -> hold. Always free, can never damage anything.
///   TWIST           -> crush, graded by twist angle. Untwist and it stops.
///   SLAM SHUT FAST  -> a burst of force. Closing SPEED is measured during the
///                      open-to-closed transition while the fingers are still
///                      visible, so it is the one force-like thing curl can
///                      honestly report.
///
/// Crush force is therefore reversible and proportional: you can carry the old
/// lady across a junction indefinitely. Civilians can never be damaged at all
/// (CrushableObject.canBeDestroyed is forced off for them) - twisting too hard
/// just makes them wriggle free, unhurt.
///
/// There are still no haptics, so drive something VISIBLE or AUDIBLE from
/// CrushIntent. That feedback replaces the sense of touch.
///
/// On CONTROLLERS, delete this component and assign MechGrabber.gripAction
/// instead -- a real analog grip axis plus rumble beats all of this.
/// </summary>
public class HandTrackedGripSource : MonoBehaviour
{
    [Header("Wiring")]
    public Handedness handedness = Handedness.Right;
    [Tooltip("The grabber this hand drives.")]
    public MechGrabber grabber;

    [Header("Curl calibration (hold only)")]
    [Tooltip("Raw FullCurl that counts as fully open. Real hands rarely read 0.")]
    [Range(0f, 1f)] public float openCurl = 0.15f;
    [Tooltip("Raw FullCurl that counts as a closed fist. Real hands rarely read 1.")]
    [Range(0f, 1f)] public float closedCurl = 0.8f;
    [Tooltip("Include the thumb in the average. Off by default - it is the least reliable digit for a fist.")]
    public bool includeThumb;
    [Tooltip("Higher = snappier, lower = smoother. Hand tracking is noisy, so some smoothing is mandatory.")]
    public float smoothing = 12f;

    [Header("Hold (hysteresis stops flicker at the boundary)")]
    [Range(0f, 1f)] public float closeAt = 0.55f;
    [Range(0f, 1f)] public float openAt = 0.4f;

    [Header("Crush: wrist twist")]
    [Tooltip("Twist below this many degrees does nothing. Wide enough that carrying something through a turn cannot crush it.")]
    public float twistDeadzoneDegrees = 25f;
    [Tooltip("Twist angle giving full crushing force. Forearm pronation comfortably reaches 80.")]
    public float twistFullCrushDegrees = 80f;
    [Tooltip("Either direction crushes. Off = only one twist direction counts.")]
    public bool twistEitherDirection = true;

    [Header("Crush: slam")]
    public bool slamEnabled = true;
    [Tooltip("Calibrated curl units per second that counts as a deliberate slam.")]
    public float slamCurlSpeed = 3f;
    [Tooltip("Crush intent added instantly by one slam.")]
    [Range(0f, 1f)] public float slamIntentBoost = 0.5f;
    public float slamCooldown = 0.5f;
    [Tooltip("How fast a slam burst bleeds away, per second.")]
    public float slamDecaySpeed = 2f;

    [Header("Tracking loss")]
    [Tooltip("Keep holding when tracking blinks out, instead of dropping what is in hand. Strongly recommended.")]
    public bool keepHoldingWhenTrackingLost = true;
    [Tooltip("Seconds of lost tracking before the hand opens anyway. 0 = hold forever.")]
    public float dropAfterTrackingLost = 1.5f;

    [Header("Events")]
    [Tooltip("Crush force has started being applied. The warning cue that replaces haptics.")]
    public UnityEvent onCrushStarted;
    public UnityEvent onCrushEnded;

    /// <summary>Calibrated, smoothed fist closure, 0..1. Hold only - never force.</summary>
    public float Curl { get; private set; }

    /// <summary>Signed wrist roll from the neutral captured when the hand closed, in degrees.</summary>
    public float TwistDegrees { get; private set; }

    /// <summary>How much crushing force is being applied, 0..1. Drive a visible gauge from this.</summary>
    public float CrushIntent { get; private set; }

    public bool IsTracked { get; private set; }
    public bool IsClosed { get; private set; }
    public bool IsCrushing { get; private set; }

    static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    XRHandSubsystem subsystem;
    Quaternion neutralWristRotation = Quaternion.identity;
    bool haveNeutral;
    float externalIntent;
    float slamIntent;
    float curlVelocity;
    float lastCalibratedCurl;
    float lastSlamTime = -999f;
    float trackingLostAt = -1f;
    bool haveLastCurl;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (subsystem == null || !subsystem.running) AcquireSubsystem();

        if (subsystem == null || !subsystem.running)
        {
            HandleNoTracking();
            return;
        }

        XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;

        if (!hand.isTracked || !TryGetRawCurl(hand, out float rawCurl))
        {
            HandleNoTracking();
            return;
        }

        IsTracked = true;
        trackingLostAt = -1f;

        float calibrated = Mathf.Clamp01(Mathf.InverseLerp(openCurl, closedCurl, rawCurl));

        // Exponential smoothing, frame-rate independent.
        Curl = Mathf.Lerp(Curl, calibrated, 1f - Mathf.Exp(-smoothing * dt));

        float newVelocity = haveLastCurl ? (calibrated - lastCalibratedCurl) / dt : 0f;
        curlVelocity = Mathf.Lerp(curlVelocity, newVelocity, 0.5f);
        lastCalibratedCurl = calibrated;
        haveLastCurl = true;

        bool wristValid = hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wristPose);

        UpdateHold(wristValid, wristPose.rotation);
        UpdateCrushIntent(dt, wristValid, wristPose.rotation);
        PushToGrabber();
    }

    void AcquireSubsystem()
    {
        SubsystemManager.GetSubsystems(s_Subsystems);

        for (int i = 0; i < s_Subsystems.Count; i++)
        {
            if (s_Subsystems[i] != null && s_Subsystems[i].running)
            {
                subsystem = s_Subsystems[i];
                return;
            }
        }

        if (s_Subsystems.Count > 0) subsystem = s_Subsystems[0];
    }

    /// <summary>Mean FullCurl across the fingers. 0 = flat hand, 1 = closed fist.</summary>
    bool TryGetRawCurl(XRHand hand, out float curl)
    {
        int first = includeThumb ? (int)XRHandFingerID.Thumb : (int)XRHandFingerID.Index;
        int last = (int)XRHandFingerID.Little;

        float sum = 0f;
        int count = 0;

        for (int f = first; f <= last; f++)
        {
            XRFingerShape shape = hand.CalculateFingerShape(
                (XRHandFingerID)f, XRFingerShapeTypes.FullCurl);

            if (!shape.TryGetFullCurl(out float value)) continue;

            sum += value;
            count++;
        }

        if (count == 0)
        {
            curl = 0f;
            return false;
        }

        curl = sum / count;
        return true;
    }

    void UpdateHold(bool wristValid, Quaternion wristRotation)
    {
        bool wasClosed = IsClosed;

        // Hysteresis: a single threshold would chatter on tracking noise.
        if (IsClosed)
        {
            if (Curl <= openAt) IsClosed = false;
        }
        else
        {
            if (Curl >= closeAt) IsClosed = true;
        }

        if (IsClosed && !wasClosed)
        {
            // Capture the twist neutral at the moment of grabbing, so the
            // gesture is relative and does not care how the arm is oriented.
            if (wristValid)
            {
                neutralWristRotation = wristRotation;
                haveNeutral = true;
            }
        }
        else if (!IsClosed && wasClosed)
        {
            haveNeutral = false;
            TwistDegrees = 0f;
        }
    }

    void UpdateCrushIntent(float dt, bool wristValid, Quaternion wristRotation)
    {
        // Slam burst bleeds away on its own.
        slamIntent = Mathf.Max(0f, slamIntent - slamDecaySpeed * dt);

        if (slamEnabled
            && IsClosed
            && curlVelocity >= slamCurlSpeed
            && Time.time - lastSlamTime >= slamCooldown)
        {
            slamIntent = Mathf.Clamp01(slamIntent + slamIntentBoost);
            lastSlamTime = Time.time;
        }

        // Twist maps straight to force, so it is reversible: untwist and the
        // crushing stops.
        float twistIntent = 0f;

        if (IsClosed && wristValid && haveNeutral)
        {
            Quaternion delta = Quaternion.Inverse(neutralWristRotation) * wristRotation;

            // Decompose about the neutral frame's +Z, which XR Hands documents
            // as pointing along the wrist/forearm. Swing-twist isolates roll,
            // so merely bending the wrist does not read as a twist.
            TwistDegrees = SignedTwistDegrees(delta, Vector3.forward);

            float magnitude = twistEitherDirection
                ? Mathf.Abs(TwistDegrees)
                : Mathf.Max(0f, TwistDegrees);

            twistIntent = Mathf.Clamp01(
                Mathf.InverseLerp(twistDeadzoneDegrees, twistFullCrushDegrees, magnitude));
        }
        else
        {
            TwistDegrees = 0f;
        }

        CrushIntent = Mathf.Clamp01(Mathf.Max(twistIntent, slamIntent) + externalIntent);
        externalIntent = 0f;

        bool crushingNow = CrushIntent > 0.01f;

        if (crushingNow != IsCrushing)
        {
            IsCrushing = crushingNow;

            if (crushingNow) onCrushStarted?.Invoke();
            else onCrushEnded?.Invoke();
        }
    }

    /// <summary>
    /// Swing-twist decomposition: the component of <paramref name="delta"/>
    /// that is rotation about <paramref name="axis"/>, in signed degrees.
    /// </summary>
    static float SignedTwistDegrees(Quaternion delta, Vector3 axis)
    {
        Vector3 rotationAxis = new Vector3(delta.x, delta.y, delta.z);
        Vector3 projected = Vector3.Project(rotationAxis, axis);

        Quaternion twist = new Quaternion(projected.x, projected.y, projected.z, delta.w);

        float sqrLength = twist.x * twist.x + twist.y * twist.y
                          + twist.z * twist.z + twist.w * twist.w;

        if (sqrLength < 1e-8f) return 0f;

        twist.Normalize();
        twist.ToAngleAxis(out float angle, out Vector3 resultAxis);

        if (angle > 180f) angle -= 360f;

        if (resultAxis.sqrMagnitude < 1e-8f
            || float.IsNaN(resultAxis.x) || float.IsInfinity(resultAxis.x))
            return 0f;

        return angle * Mathf.Sign(Vector3.Dot(resultAxis, axis));
    }

    void PushToGrabber()
    {
        if (grabber == null) return;

        if (!IsClosed)
        {
            grabber.SetGrip(0f);
            return;
        }

        // A closed hand sits exactly at the grab threshold, where the cubic
        // force curve evaluates to ~0 newtons. Holding is free; only crush
        // intent spends force.
        float floor = Mathf.Min(grabber.grabThreshold + 0.001f, 1f);
        grabber.SetGrip(Mathf.Lerp(floor, 1f, CrushIntent));
    }

    void HandleNoTracking()
    {
        IsTracked = false;
        haveLastCurl = false;
        curlVelocity = 0f;

        // Crush force goes to zero while blind. Letting a garbage wrist pose
        // drive it is how you detonate a car because tracking blinked. The
        // HOLD is what we try to preserve, not the crush.
        TwistDegrees = 0f;
        slamIntent = 0f;
        externalIntent = 0f;
        CrushIntent = 0f;

        if (IsCrushing)
        {
            IsCrushing = false;
            onCrushEnded?.Invoke();
        }

        if (trackingLostAt < 0f) trackingLostAt = Time.time;

        bool giveUp = !keepHoldingWhenTrackingLost
                      || (dropAfterTrackingLost > 0f
                          && Time.time - trackingLostAt >= dropAfterTrackingLost);

        if (giveUp)
        {
            IsClosed = false;
            haveNeutral = false;
            if (grabber != null) grabber.SetGrip(0f);
            return;
        }

        // Still holding, just not crushing.
        PushToGrabber();
    }

    /// <summary>
    /// Add crush intent from some other reliable signal (a second hand, a
    /// controller axis). Consumed each Update, so call it every frame.
    /// </summary>
    public void AddCrushIntent(float amountThisFrame)
    {
        externalIntent += amountThisFrame;
    }

    /// <summary>Re-zero the twist neutral to the current wrist pose.</summary>
    public void RecenterTwist()
    {
        if (subsystem == null || !subsystem.running) return;

        XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;

        if (hand.isTracked && hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose pose))
        {
            neutralWristRotation = pose.rotation;
            haveNeutral = true;
        }
    }
}
