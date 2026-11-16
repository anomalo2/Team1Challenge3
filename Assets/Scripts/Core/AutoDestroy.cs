using UnityEngine;

/// <summary>Removes a one-shot effect after a while.</summary>
public class AutoDestroy : MonoBehaviour
{
    public float seconds = 3f;
    void Start() { Destroy(gameObject, seconds); }
}
