using TMPro;
using UnityEngine;

/// <summary>
/// The "you did well" moment when a pedestrian reaches the far pavement.
///
/// Lives on the CrossingSuccess effect prefab (built by
/// Tools > Pedestrians > Rebuild Pedestrian Prefabs). The prefab carries the
/// particle bursts and the chime; this script floats the "SAFE!" text up,
/// keeps it facing the pilot, fades it, and cleans the whole thing up.
/// </summary>
public class CrossingSuccessEffect : MonoBehaviour
{
    public TMP_Text label;
    [Tooltip("Points shown under the headline. Read from CityObjective when there is one.")]
    public bool showPoints = true;
    public float lifetime = 4f;
    public float riseSpeed = 1.2f;
    [Tooltip("Text grows from nothing to full size over this many seconds.")]
    public float popTime = 0.25f;

    float born;
    Vector3 labelBaseScale = Vector3.one;
    Color labelColor = Color.white;

    void Start()
    {
        born = Time.time;

        if (label != null)
        {
            labelBaseScale = label.transform.localScale;
            labelColor = label.color;

            if (showPoints && CityObjective.Instance != null)
                label.text = "SAFE!\n<size=55%>+" + CityObjective.Instance.pointsPerCivilianSaved.ToString("0") + "</size>";
        }

        Destroy(gameObject, lifetime);
    }

    void LateUpdate()
    {
        float age = Time.time - born;
        if (label == null) return;

        Transform t = label.transform;
        t.position += Vector3.up * riseSpeed * Time.deltaTime;

        // Face the pilot, upright.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 away = t.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        // Pop in with a little overshoot, then fade out over the last second.
        float pop = popTime > 0f ? Mathf.Clamp01(age / popTime) : 1f;
        float overshoot = 1f + Mathf.Sin(pop * Mathf.PI) * 0.25f;
        t.localScale = labelBaseScale * pop * overshoot;

        float fade = Mathf.Clamp01(lifetime - age);
        label.color = new Color(labelColor.r, labelColor.g, labelColor.b, labelColor.a * fade);
    }
}
