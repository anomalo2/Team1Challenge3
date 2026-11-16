using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the game inside the part of the city that matters: the four-way
/// intersection.
///
/// Two layers, because two kinds of thing move here:
///   INVISIBLE WALLS  four tall box colliders around the area, built on Awake.
///                    They stop physics objects: thrown cars, knocked-over
///                    props, debris, a ragdolled pedestrian.
///   CLAMP            the mech is moved by setting its transform (stick
///                    locomotion and room-scale walking), so colliders cannot
///                    stop it. Its root is clamped back inside every frame.
///
/// Traffic still drives in and out: driving cars are kinematic and pass
/// through the walls, and only become solid once they are wrecked.
///
/// Move / resize the area with the transform position and Size; the box is
/// drawn in the Scene view whenever the object is in the scene.
/// </summary>
[DefaultExecutionOrder(1000)]   // after locomotion and MaintainPlayerPos
public class PlayAreaBounds : MonoBehaviour
{
    [Tooltip("Width (X), height (Y) and depth (Z) of the playable box, centred on this object.")]
    public Vector3 size = new Vector3(70f, 60f, 70f);
    public float wallThickness = 2f;
    public bool buildWalls = true;

    [Header("Mech / player clamp")]
    [Tooltip("Roots kept inside. Empty = every MechLocomotion in the scene.")]
    public List<Transform> keepInside = new List<Transform>();
    [Tooltip("How far inside the walls the mech's root is held, so its body does not poke through.")]
    public float margin = 4f;

    readonly List<BoxCollider> walls = new List<BoxCollider>();
    public IReadOnlyList<BoxCollider> Walls => walls;

    void Awake()
    {
        if (keepInside.Count == 0)
            foreach (MechLocomotion m in FindObjectsByType<MechLocomotion>(FindObjectsSortMode.None))
                keepInside.Add(m.transform);

        if (buildWalls) BuildWalls();
    }

    void BuildWalls()
    {
        Vector3 c = transform.position;
        float hx = size.x * 0.5f, hz = size.z * 0.5f, t = wallThickness;
        float y = c.y + size.y * 0.5f - 10f;   // dips 10 m below the centre so nothing slips under

        Add("Wall_North", new Vector3(c.x, y, c.z + hz + t * 0.5f), new Vector3(size.x + 2f * t, size.y, t));
        Add("Wall_South", new Vector3(c.x, y, c.z - hz - t * 0.5f), new Vector3(size.x + 2f * t, size.y, t));
        Add("Wall_East", new Vector3(c.x + hx + t * 0.5f, y, c.z), new Vector3(t, size.y, size.z));
        Add("Wall_West", new Vector3(c.x - hx - t * 0.5f, y, c.z), new Vector3(t, size.y, size.z));
    }

    void Add(string n, Vector3 worldCentre, Vector3 worldSize)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.position = worldCentre;
        go.transform.rotation = Quaternion.identity;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = worldSize;
        walls.Add(bc);
    }

    void LateUpdate()
    {
        for (int i = 0; i < keepInside.Count; i++)
        {
            Transform t = keepInside[i];
            if (t == null) continue;

            Vector3 p = t.position;
            Vector3 clamped = Clamp(p);
            if ((clamped - p).sqrMagnitude > 0.000001f) t.position = clamped;
        }
    }

    /// <summary>Nearest point to p that is inside the area (minus margin), horizontally.</summary>
    public Vector3 Clamp(Vector3 p)
    {
        Vector3 c = transform.position;
        float hx = Mathf.Max(0f, size.x * 0.5f - margin);
        float hz = Mathf.Max(0f, size.z * 0.5f - margin);
        p.x = Mathf.Clamp(p.x, c.x - hx, c.x + hx);
        p.z = Mathf.Clamp(p.z, c.z - hz, c.z + hz);
        return p;
    }

    public bool Contains(Vector3 p)
    {
        Vector3 c = transform.position;
        return Mathf.Abs(p.x - c.x) <= size.x * 0.5f && Mathf.Abs(p.z - c.z) <= size.z * 0.5f;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Vector3 c = transform.position + Vector3.up * (size.y * 0.5f - 10f);
        Gizmos.DrawWireCube(c, size);
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.08f);
        Gizmos.DrawCube(c, size);
    }
}
