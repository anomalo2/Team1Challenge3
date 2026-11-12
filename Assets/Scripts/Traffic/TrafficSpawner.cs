using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a lane populated. Spawns car prefabs at the head of a TrafficRoute on
/// a timer, caps how many exist at once, and will not spawn on top of a car
/// that is still sitting at the spawn point (which happens constantly once the
/// player blocks the road).
/// </summary>
public class TrafficSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    [Tooltip("Pick at random. Mix in a bus or a tanker for variety in weight and blast size.")]
    public List<GameObject> carPrefabs = new List<GameObject>();

    [Header("Where")]
    public TrafficRoute route;
    [Tooltip("Waypoint index cars appear at.")]
    public int spawnWaypoint;

    [Header("Rate")]
    public float spawnInterval = 4f;
    [Tooltip("Random slop added to the interval so traffic does not feel metronomic.")]
    public float intervalJitter = 1.5f;
    public int maxAlive = 8;
    [Tooltip("Do not spawn if another car is within this distance of the spawn point.")]
    public float minSpacing = 8f;

    [Header("Startup")]
    [Tooltip("Spawn a car immediately instead of waiting out the first interval.")]
    public bool spawnOnStart = true;
    public bool spawning = true;

    public int AliveCount
    {
        get
        {
            Prune();
            return alive.Count;
        }
    }

    readonly List<GameObject> alive = new List<GameObject>();
    float nextSpawnTime;

    void Start()
    {
        nextSpawnTime = Time.time + (spawnOnStart ? 0f : CurrentInterval());
    }

    void Update()
    {
        if (!spawning || route == null || !route.IsValid) return;
        if (carPrefabs.Count == 0) return;
        if (Time.time < nextSpawnTime) return;

        Prune();

        if (alive.Count < maxAlive && SpawnPointClear()) SpawnOne();

        // Re-check soon even if we skipped, so a cleared road refills promptly.
        nextSpawnTime = Time.time + CurrentInterval();
    }

    float CurrentInterval()
    {
        return Mathf.Max(0.25f, spawnInterval + Random.Range(-intervalJitter, intervalJitter));
    }

    bool SpawnPointClear()
    {
        if (minSpacing <= 0f) return true;

        Vector3 p = route.GetPosition(spawnWaypoint);
        float sqr = minSpacing * minSpacing;

        for (int i = 0; i < alive.Count; i++)
        {
            if (alive[i] == null) continue;
            if ((alive[i].transform.position - p).sqrMagnitude < sqr) return false;
        }

        return true;
    }

    /// <summary>Spawn one car now, ignoring the timer.</summary>
    public GameObject SpawnOne()
    {
        GameObject prefab = carPrefabs[Random.Range(0, carPrefabs.Count)];
        if (prefab == null) return null;

        Vector3 pos = route.GetPosition(spawnWaypoint);

        // Face the next waypoint so it does not start by spinning on the spot.
        int next = route.NextIndex(spawnWaypoint);
        Vector3 look = next >= 0 ? route.GetPosition(next) - pos : route.transform.forward;
        look.y = 0f;

        Quaternion rot = look.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject car = Instantiate(prefab, pos, rot);

        CarController driver = car.GetComponent<CarController>();
        if (driver != null)
        {
            driver.route = route;
            driver.startWaypoint = spawnWaypoint;
        }
        else
        {
            Debug.LogWarning($"[TrafficSpawner] {prefab.name} has no CarController, so it will not drive.", this);
        }

        alive.Add(car);
        return car;
    }

    /// <summary>Drop references to cars that have been destroyed or despawned.</summary>
    void Prune()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
            if (alive[i] == null) alive.RemoveAt(i);
    }

    /// <summary>Clear the lane. Useful when restarting a challenge.</summary>
    public void DespawnAll()
    {
        for (int i = 0; i < alive.Count; i++)
            if (alive[i] != null) Destroy(alive[i]);

        alive.Clear();
    }

    void OnDrawGizmosSelected()
    {
        if (route == null || !route.IsValid) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(route.GetPosition(spawnWaypoint), 1.5f);
    }
}
