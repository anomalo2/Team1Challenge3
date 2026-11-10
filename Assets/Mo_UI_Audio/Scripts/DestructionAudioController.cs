
using UnityEngine;

public class DestructionAudioController : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip metalCrashSound;

    [Range(0f, 1f)]
    [SerializeField] private float crashVolume = 0.6f;

    [Header("Rehabilitation System")]
    [SerializeField] private RehabilitationSystem rehabilitationSystem;

    [Header("Settings")]
    [SerializeField] private float cooldown = 0.75f;

    private float lastDestructionTime = -999f;

    // Called when the mech destroys something
    public void ReportDestruction()
    {
        if (Time.time - lastDestructionTime < cooldown)
            return;

        lastDestructionTime = Time.time;

        // Play the metal crash
        if (audioSource != null && metalCrashSound != null)
        {
            audioSource.PlayOneShot(
                metalCrashSound, crashVolume);
        }

        // Reset rehabilitation progress
        if (rehabilitationSystem != null)
        {
            rehabilitationSystem.DestructionOccurred();
        }
    }

    // Test from the Unity Inspector
    [ContextMenu("TEST - Destroy Object")]
    private void TestDestruction()
    {
        if (Application.isPlaying)
            ReportDestruction();
    }
}
