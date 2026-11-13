using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Live damage readout in the cockpit: CityObjective.PropertyDamage, which now
/// climbs as things are dented and crashed, not only when they are destroyed.
///
/// Nothing to wire: after a scene loads that has a CityObjective, this builds a
/// world-space text panel a little in front of and below the pilot's view and
/// parents it to the mech (the object carrying MaintainPlayerPos), so it turns
/// with the cockpit. Put one in the scene yourself to place it by hand; then
/// auto-install leaves it alone and it only drives its own text.
/// </summary>
public class DamageScorePanel : MonoBehaviour
{
    [Tooltip("Leave empty to build a text panel automatically.")]
    public TMP_Text label;
    public string heading = "DAMAGE";

    [Header("Auto placement (relative to the pilot's view)")]
    public float distance = 0.9f;
    public float below = 0.35f;
    public Vector2 size = new Vector2(0.6f, 0.22f);
    [Tooltip("Seconds after startup before placing, so the head pose and height calibration are settled.")]
    public float placeDelay = 1.5f;

    float shown = -1f;

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
        if (FindAnyObjectByType<CityObjective>() == null) return;
        if (FindAnyObjectByType<DamageScorePanel>() != null) return;
        new GameObject("Damage Score Panel").AddComponent<DamageScorePanel>();
    }

    IEnumerator Start()
    {
        if (label != null) yield break; // placed by hand

        label = BuildLabel();
        yield return new WaitForSeconds(placeDelay);
        Place();
    }

    TMP_Text BuildLabel()
    {
        var t = gameObject.AddComponent<TextMeshPro>();
        t.rectTransform.sizeDelta = size;
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.05f;
        t.fontSizeMax = 2f;
        t.color = new Color(1f, 0.55f, 0.2f);
        t.outlineWidth = 0.2f;
        t.outlineColor = Color.black;
        return t;
    }

    void Place()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.0001f ? Vector3.forward : fwd.normalized;

        transform.position = cam.transform.position + fwd * distance + Vector3.down * below;
        // TextMeshPro reads correctly from its -Z side: face away from the pilot.
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);

        MaintainPlayerPos mech = FindAnyObjectByType<MaintainPlayerPos>();
        if (mech != null) transform.SetParent(mech.transform, true);
    }

    void Update()
    {
        if (label == null) return;

        CityObjective board = CityObjective.Instance;
        float damage = board != null ? board.PropertyDamage : 0f;
        if (Mathf.Approximately(damage, shown)) return;

        shown = damage;
        label.text = $"{heading}\n{damage:N0}";
    }
}
