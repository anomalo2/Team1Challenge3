using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The scoreboard for "help people without breaking things".
///
/// One of these in the scene. Everything reports into it:
///   CrushableObject   -> property damage when it is wrecked
///   PedestrianCrosser -> a life saved or lost
///
/// Deliberately has no UI dependency. Hook onStateChanged up to whatever
/// readout you build (a canvas in the cockpit, a dashboard light, audio).
/// </summary>
public class CityObjective : MonoBehaviour
{
    public static CityObjective Instance { get; private set; }

    [Header("Score weighting")]
    [Tooltip("Points earned per civilian who gets where they were going.")]
    public float pointsPerCivilianSaved = 100f;
    [Tooltip("Points lost each time a civilian is frightened back to the kerb. No civilian can ever be hurt - a scare is the worst that happens to them.")]
    public float penaltyPerCivilianFrightened = 150f;
    [Tooltip("Points lost per unit of property value destroyed.")]
    public float penaltyPerPropertyValue = 1f;

    [Header("Failure limits (0 = no limit)")]
    [Tooltip("Mission fails once the repair bill passes this.")]
    public float maxPropertyDamage = 0f;
    [Tooltip("Mission fails after this many civilian scares.")]
    public int maxCiviliansFrightened = 0;

    [Header("Logging")]
    [Tooltip("Print each incident to the Console. Very handy before any UI exists.")]
    public bool logIncidents = true;

    [Header("Events")]
    public UnityEvent onStateChanged;
    public UnityEvent onMissionFailed;

    public float PropertyDamage { get; private set; }
    public int CiviliansSaved { get; private set; }

    /// <summary>
    /// Times a civilian has been startled. There is deliberately no casualty
    /// count, because nothing in the game can hurt them.
    /// </summary>
    public int CiviliansFrightened { get; private set; }
    public bool MissionFailed { get; private set; }

    public float Score =>
        CiviliansSaved * pointsPerCivilianSaved
        - CiviliansFrightened * penaltyPerCivilianFrightened
        - PropertyDamage * penaltyPerPropertyValue;

    /// <summary>Fired for every incident, for feeds and subtitles. (message, isGood)</summary>
    public event Action<string, bool> Incident;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void ReportPropertyDestroyed(float value, string what)
    {
        PropertyDamage += Mathf.Max(0f, value);
        Report($"Destroyed: {what} (+{value:0} damage)", false);
        CheckFailure();
    }

    public void ReportCivilianSafe(string who)
    {
        CiviliansSaved++;
        Report($"Safe: {who}", true);
    }

    /// <summary>A civilian got a fright and retreated. They are unharmed.</summary>
    public void ReportCivilianFrightened(string who, string cause)
    {
        CiviliansFrightened++;
        Report($"Frightened: {who} ({cause})", false);
        CheckFailure();
    }

    void Report(string message, bool good)
    {
        if (logIncidents) Debug.Log($"[Objective] {message} | score {Score:0}");

        Incident?.Invoke(message, good);
        onStateChanged?.Invoke();
    }

    void CheckFailure()
    {
        if (MissionFailed) return;

        bool overDamage = maxPropertyDamage > 0f && PropertyDamage >= maxPropertyDamage;
        bool overScares = maxCiviliansFrightened > 0
                          && CiviliansFrightened >= maxCiviliansFrightened;

        if (!overDamage && !overScares) return;

        MissionFailed = true;
        if (logIncidents) Debug.LogWarning("[Objective] Mission failed.");
        onMissionFailed?.Invoke();
    }

    /// <summary>Wipe the board, for retrying without reloading the scene.</summary>
    public void ResetObjective()
    {
        PropertyDamage = 0f;
        CiviliansSaved = 0;
        CiviliansFrightened = 0;
        MissionFailed = false;
        onStateChanged?.Invoke();
    }
}
