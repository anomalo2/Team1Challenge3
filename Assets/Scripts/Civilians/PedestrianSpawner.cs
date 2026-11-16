using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Sends one pedestrian at a time across a set of crossings.
///
/// She walks the first path. When she is safely across she celebrates, steps
/// out of the scene, and the next one appears at the start of the next path.
/// Only ever one on the road, so the player is never juggling two.
///
/// HOW TO USE (no menus, no selecting two things at once)
///   1. Drag Assets/Prefabs/Pedestrians/PedestrianCrossings.prefab into the scene.
///   2. It comes with one path. Press "Add Crossing Path" in its Inspector for
///      more, or drag PedestrianPath.prefab onto it in the Hierarchy.
///   3. Drag each path's green START and red END spheres onto the kerbs.
/// Paths are walked in Hierarchy order (top first).
/// </summary>
public class PedestrianSpawner : MonoBehaviour
{
    [Tooltip("Who crosses. Defaults to the Grandma prefab.")]
    public PedestrianCrosser pedestrianPrefab;

    [Tooltip("Crossings in order. Leave empty to use the child PedestrianPaths in Hierarchy order.")]
    public List<PedestrianPath> paths = new List<PedestrianPath>();

    [Header("Pacing")]
    public float delayBeforeFirst = 1f;
    [Tooltip("Seconds she stays on the far pavement (celebrating) before leaving.")]
    public float lingerAfterSafe = 2.5f;
    [Tooltip("Gap between one pedestrian leaving and the next appearing.")]
    public float delayBetween = 2f;
    [Tooltip("Start again from the first path after the last one.")]
    public bool loop = true;

    [Header("Events")]
    public UnityEvent onPedestrianSpawned;
    public UnityEvent onPedestrianSafe;
    [Tooltip("Every path has been crossed once (fires each lap when looping).")]
    public UnityEvent onAllCrossed;

    public PedestrianCrosser Current { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public int CrossingsCompleted { get; private set; }

    void Reset()
    {
        // Ship with sensible defaults when added by hand.
        delayBeforeFirst = 1f;
    }

    public void CollectPaths()
    {
        if (paths == null) paths = new List<PedestrianPath>();
        paths.RemoveAll(p => p == null);
        if (paths.Count > 0) return;

        foreach (Transform child in transform)
        {
            PedestrianPath p = child.GetComponent<PedestrianPath>();
            if (p != null) paths.Add(p);
        }
    }

    void Start()
    {
        CollectPaths();

        if (pedestrianPrefab == null)
        {
            Debug.LogError("[PedestrianSpawner] No pedestrian prefab assigned.", this);
            return;
        }

        // A pedestrian placed in the scene and used as the template: keep it
        // out of the way so it does not walk around on its own.
        if (pedestrianPrefab.gameObject.scene.IsValid()) pedestrianPrefab.gameObject.SetActive(false);

        StartCoroutine(Run());
    }

    /// <summary>Clear the road and start again from the first crossing.</summary>
    public void Restart()
    {
        StopAllCoroutines();
        if (Current != null) Destroy(Current.gameObject);
        Current = null;
        CurrentIndex = -1;
        CollectPaths();
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        yield return new WaitForSeconds(delayBeforeFirst);

        while (true)
        {
            bool any = false;

            for (int i = 0; i < paths.Count; i++)
            {
                PedestrianPath path = paths[i];
                if (path == null || !path.IsValid) continue;
                any = true;

                CurrentIndex = i;
                Current = Spawn(path);
                onPedestrianSpawned?.Invoke();

                // Wait for her to make it. She cannot be hurt, so she always
                // eventually does: scares only send her back to the kerb.
                while (Current != null && !Current.IsSafe) yield return null;

                if (Current == null) continue;   // removed by something else

                CrossingsCompleted++;
                onPedestrianSafe?.Invoke();

                yield return new WaitForSeconds(lingerAfterSafe);

                if (Current != null) Destroy(Current.gameObject);
                Current = null;

                yield return new WaitForSeconds(delayBetween);
            }

            if (!any)
            {
                Debug.LogWarning("[PedestrianSpawner] No valid paths (each needs a START and an END).", this);
                yield break;
            }

            onAllCrossed?.Invoke();
            if (!loop) yield break;
        }
    }

    PedestrianCrosser Spawn(PedestrianPath path)
    {
        PedestrianCrosser p = Instantiate(pedestrianPrefab, path.StartPosition, path.StartRotation);
        p.gameObject.SetActive(true);
        p.name = pedestrianPrefab.name + " (" + path.name + ")";
        p.AssignPath(path);
        return p;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f);
        Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
    }
}
