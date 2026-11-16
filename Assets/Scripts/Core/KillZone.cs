using UnityEngine;

/// <summary>
/// The kill box under the map. Anything that falls out of the world ends up
/// here and is dealt with:
///
///   pedestrian   -> stood back up at the start of her crossing (never harmed)
///   traffic car  -> removed (the spawner refills the lane)
///   prop         -> rebuilt where it originally stood (PropAssembly)
///   debris       -> removed
///   mech / rig   -> put back where it started
///
/// Physics objects are caught by the trigger. The mech is moved by its
/// transform rather than physics, so it is also checked by height every
/// frame: below the top of this box counts as fallen.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class KillZone : MonoBehaviour
{
    [Tooltip("Log every catch to the Console.")]
    public bool logCatches = true;

    BoxCollider box;
    MechLocomotion[] mechs;
    Vector3[] mechStarts;

    public int Caught { get; private set; }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;

        mechs = FindObjectsByType<MechLocomotion>(FindObjectsSortMode.None);
        mechStarts = new Vector3[mechs.Length];
        for (int i = 0; i < mechs.Length; i++) mechStarts[i] = mechs[i].transform.position;
    }

    float TopY => box.bounds.max.y;

    void Update()
    {
        for (int i = 0; i < mechs.Length; i++)
        {
            if (mechs[i] == null) continue;
            if (mechs[i].transform.position.y < TopY)
            {
                // Move the pilot with it, or MaintainPlayerPos drags the mech
                // straight back toward the camera.
                PlayAreaBounds.MoveMech(mechs[i].transform, mechStarts[i] - mechs[i].transform.position);
                Report(mechs[i].name, "respawned at start");
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        GameObject root = rb != null ? rb.gameObject : other.gameObject;

        PedestrianCrosser ped = root.GetComponentInParent<PedestrianCrosser>();
        if (ped != null)
        {
            ped.ReturnToStart();
            Report(ped.name, "returned to the start of her crossing");
            return;
        }

        // Never delete the mech, the rig, or the level's own static geometry.
        if (root.GetComponentInParent<MechLocomotion>() != null) return;
        if (root.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) return;
        if (rb == null) return;

        // Props are never lost: hand the piece back to its prop, which
        // rebuilds where it originally stood.
        PropAssembly prop = PropAssembly.OwnerOf(root.transform);
        if (prop != null)
        {
            prop.Reclaim(root.transform);   // the whole prop, or one split-off piece
            Report(root.name, "returned to its prop for respawn");
            return;
        }

        CarController car = root.GetComponentInParent<CarController>();
        GameObject doomed = car != null ? car.gameObject : root;

        Report(doomed.name, "removed");
        Destroy(doomed);
    }

    void Report(string what, string action)
    {
        Caught++;
        if (logCatches) Debug.Log("[KillZone] " + what + " fell out of the world - " + action);
    }

    void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
