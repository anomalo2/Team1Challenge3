using UnityEngine;

/// <summary>Shrinks a debris chunk away and removes it. Added by PropAssembly.</summary>
public class DebrisLife : MonoBehaviour
{
    public float lifetime = 6f;
    float born;
    Vector3 scale;

    void Start() { born = Time.time; scale = transform.localScale; }

    void Update()
    {
        float left = lifetime - (Time.time - born);
        if (left <= 0f) { Destroy(gameObject); return; }
        if (left < 1f) transform.localScale = scale * left;
    }

    void OnDestroy() { PropAssembly.liveDebris = Mathf.Max(0, PropAssembly.liveDebris - 1); }
}
