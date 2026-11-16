using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Walks the mech with the LEFT thumbstick, in the direction the pilot is
/// looking (flattened, so looking down does not walk you into the ground).
///
/// It moves this transform - put it on the mech's root ("Mech") - and the XR
/// Origin by the same amount, so the pilot stays in the seat (see
/// PlayAreaBounds.MoveMech). The hand/arm colliders come along because
/// PhysicsColliders is a child of the mech. Turning is already handled: the mech faces where you look.
///
/// The mech is kinematic, so walking into traffic crashes cars through
/// CarController's overlap check. It does not stop at buildings yet.
///
/// In the editor without a headset, WASD / arrow keys also walk.
/// </summary>
[DisallowMultipleComponent]
public class MechLocomotion : MonoBehaviour
{
    [Tooltip("Top walking speed, m/s.")]
    public float maxSpeed = 5f;
    [Tooltip("How fast it gets up to speed, m/s².")]
    public float acceleration = 6f;
    [Tooltip("How fast it stops when you let go, m/s².")]
    public float deceleration = 10f;
    [Tooltip("Stick deflection below this is ignored (drift).")]
    [Range(0f, 0.5f)] public float deadzone = 0.15f;

    [Tooltip("Whose view sets 'forward'. Empty = the main camera.")]
    public Transform head;

    /// <summary>Current walking velocity, m/s (horizontal).</summary>
    public Vector3 Velocity { get; private set; }

    InputAction move;

    void OnEnable()
    {
        if (move == null)
        {
            move = new InputAction("Mech Walk", InputActionType.Value, expectedControlType: "Vector2");
            // Quest Touch (OpenXR) and any XR controller with a primary stick.
            move.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
            move.AddBinding("<XRController>{LeftHand}/thumbstick");
            // Editor testing without a headset.
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        }
        move.Enable();
    }

    void OnDisable()
    {
        move?.Disable();
    }

    void OnDestroy()
    {
        move?.Dispose();
    }

    void Update()
    {
        Vector2 stick = move.ReadValue<Vector2>();
        if (stick.magnitude < deadzone) stick = Vector2.zero;
        stick = Vector2.ClampMagnitude(stick, 1f);

        Transform view = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
        Vector3 desired = Vector3.zero;

        if (view != null && stick != Vector2.zero)
        {
            Vector3 fwd = view.forward; fwd.y = 0f;
            Vector3 right = view.right; right.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f) fwd.Normalize();
            if (right.sqrMagnitude > 0.0001f) right.Normalize();

            desired = (fwd * stick.y + right * stick.x) * maxSpeed;
        }

        float rate = desired.sqrMagnitude > Velocity.sqrMagnitude ? acceleration : deceleration;
        Velocity = Vector3.MoveTowards(Velocity, desired, rate * Time.deltaTime);

        // Carry the pilot along. MaintainPlayerPos moves the mech toward the
        // pilot's head; if the stick moved only the mech, the head would be
        // left behind the seat, read as "the pilot stepped backward", and the
        // mech would be dragged back - the slingshot after letting go.
        if (Velocity.sqrMagnitude > 0.000001f)
            PlayAreaBounds.MoveMech(transform, Velocity * Time.deltaTime);
    }
}
