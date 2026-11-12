using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Wraps one "help her across" objective: arms it when the player gets close,
/// watches the pedestrian, and reports success or failure once.
///
/// Keep the pedestrian disabled-ish until the player is nearby by leaving
/// waitsForGap on with a long patience, or just let her stand at the curb.
/// This component only decides when the challenge is live and how it ended.
/// </summary>
public class CrosswalkChallenge : MonoBehaviour
{
    [Header("Who")]
    public PedestrianCrosser pedestrian;

    [Header("Arming")]
    [Tooltip("Usually the mech root, or the XR Origin before the mech exists.")]
    public Transform player;
    [Tooltip("Challenge arms when the player comes within this distance. 0 = armed immediately.")]
    public float armRadius = 30f;

    [Header("Traffic")]
    [Tooltip("Spawners switched on while this challenge is live. Leave empty to let traffic run on its own.")]
    public List<TrafficSpawner> trafficDuringChallenge = new List<TrafficSpawner>();
    [Tooltip("Stop spawning new cars once she is safe, so the street calms down.")]
    public bool stopTrafficOnSuccess;

    [Header("Failure (she can never be hurt, so these are the only ways to fail)")]
    [Tooltip("Seconds to get her across before it counts as a failure. 0 = no limit.")]
    public float timeLimit = 0f;
    [Tooltip("Scares allowed before the challenge fails. 0 = unlimited retries.")]
    public int maxScares = 3;

    [Header("Events")]
    public UnityEvent onArmed;
    public UnityEvent onSuccess;
    public UnityEvent onFailure;

    public bool IsArmed { get; private set; }
    public bool IsFinished { get; private set; }
    public bool Succeeded { get; private set; }

    /// <summary>Seconds left, or -1 when there is no time limit.</summary>
    public float TimeRemaining =>
        timeLimit <= 0f || !IsArmed ? -1f : Mathf.Max(0f, (armedAt + timeLimit) - Time.time);

    float armedAt;

    void Start()
    {
        SetTraffic(false);

        if (armRadius <= 0f) Arm();
    }

    void Update()
    {
        if (IsFinished) return;

        if (!IsArmed)
        {
            if (player != null && Vector3.Distance(player.position, transform.position) <= armRadius)
                Arm();

            return;
        }

        if (pedestrian == null) return;

        if (pedestrian.IsSafe)
        {
            Finish(true);
            return;
        }

        if (maxScares > 0 && pedestrian.ScareCount >= maxScares)
        {
            Finish(false);
            return;
        }

        if (timeLimit > 0f && Time.time >= armedAt + timeLimit) Finish(false);
    }

    /// <summary>Start the challenge now, ignoring the arm radius.</summary>
    public void Arm()
    {
        if (IsArmed || IsFinished) return;

        IsArmed = true;
        armedAt = Time.time;

        SetTraffic(true);
        onArmed?.Invoke();
    }

    void Finish(bool success)
    {
        if (IsFinished) return;

        IsFinished = true;
        Succeeded = success;

        if (success && stopTrafficOnSuccess) SetTraffic(false);

        if (success) onSuccess?.Invoke();
        else onFailure?.Invoke();

        Debug.Log($"[CrosswalkChallenge] {(success ? "SUCCESS" : "FAILED")} - {name}");
    }

    void SetTraffic(bool on)
    {
        for (int i = 0; i < trafficDuringChallenge.Count; i++)
            if (trafficDuringChallenge[i] != null) trafficDuringChallenge[i].spawning = on;
    }

    void OnDrawGizmosSelected()
    {
        if (armRadius <= 0f) return;

        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, armRadius);
    }
}
