using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives the mech's legs solid hitboxes so walking into a lamp post knocks it
/// over and breaks it.
///
/// The mech model's foot BoxColliders sit on the skinned-mesh objects, which
/// never move with the animated skeleton, so the legs passed straight through
/// scenery. This builds a capsule on each thigh, shin and foot BONE instead;
/// they ride the animation, and MechLimbPhysics on each one wakes, shoves and
/// damages whatever it touches (Vehicle and Crushable only: civilians are
/// never in the mask).
///
/// Nothing to wire: after a scene loads, every object with MechLocomotion
/// gets one of these. Add it yourself to tune it; auto-install leaves an
/// existing one alone.
/// </summary>
[DisallowMultipleComponent]
public class MechLegHitboxes : MonoBehaviour
{
    [Tooltip("Bone names to cover, parent first. Each bone's capsule runs to its first child bone.")]
    public string[] bones = { "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
    [Tooltip("Capsule radius as a fraction of bone length.")]
    public float radiusFraction = 0.28f;
    [Tooltip("Effective mass behind a leg hit, kg.")]
    public float impactMass = 6000f;

    public static bool AutoInstall = true;

    readonly List<GameObject> built = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Install();

    static void Install()
    {
        if (!AutoInstall) return;
        foreach (MechLocomotion mech in FindObjectsByType<MechLocomotion>(FindObjectsSortMode.None))
            if (mech.GetComponent<MechLegHitboxes>() == null) mech.gameObject.AddComponent<MechLegHitboxes>();
    }

    public IReadOnlyList<GameObject> Hitboxes => built;

    void Start()
    {
        int layer = LayerMask.NameToLayer("MechCollider");
        if (layer < 0) layer = gameObject.layer;

        foreach (string boneName in bones)
        {
            Transform bone = FindBone(transform, boneName);
            if (bone == null) { Debug.LogWarning("[MechLegHitboxes] bone not found: " + boneName, this); continue; }

            Transform tip = FirstChildBone(bone);
            Vector3 tipLocal = tip != null ? bone.InverseTransformPoint(tip.position) : Vector3.up * 0.5f;
            float length = tipLocal.magnitude;
            if (length < 0.01f) continue;

            GameObject hb = new GameObject("LegHitbox_" + boneName);
            hb.layer = layer;
            hb.transform.SetParent(bone, false);

            // Capsule along the bone, from joint to the next joint.
            CapsuleCollider cap = hb.AddComponent<CapsuleCollider>();
            Vector3 dir = tipLocal / length;
            hb.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            cap.direction = 1;
            cap.height = length;
            cap.center = new Vector3(0f, length * 0.5f, 0f);
            cap.radius = length * radiusFraction;

            // Kinematic, so it is a proper moving collider and shoves dynamic
            // bodies (props it has already woken) for real.
            Rigidbody rb = hb.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            MechLimbPhysics limb = MechLimbPhysics.AddTo(hb, false);
            limb.impactMass = impactMass;

            built.Add(hb);
        }
    }

    /// <summary>A deform bone: the transform carries no renderer (the model also has meshes named like bones).</summary>
    static Transform FindBone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name && t.GetComponent<Renderer>() == null && t.parent != null && t.parent.GetComponent<Renderer>() == null)
                return t;
        return null;
    }

    static Transform FirstChildBone(Transform bone)
    {
        // A foot runs to its toe, not back to its heel.
        foreach (Transform c in bone)
            if (c.name.ToLowerInvariant().StartsWith("toe") && c.GetComponent<Renderer>() == null) return c;

        foreach (Transform c in bone)
            if (c.GetComponent<Renderer>() == null && !c.name.StartsWith("LegHitbox_")) return c;
        return null;
    }
}
