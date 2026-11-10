
using UnityEngine;

public class MechFootstepAudio : MonoBehaviour
{
    [Header("Audio Source")]
    [SerializeField] private AudioSource footstepSource;

    [Header("Footstep Sounds")]
    [SerializeField] private AudioClip leftFootSound;
    [SerializeField] private AudioClip rightFootSound;

    [Header("Volume Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float footstepVolume = 0.6f;

    // Called when the mech's left foot lands
    public void PlayLeftFootstep()
    {
        PlayFootstep(leftFootSound);
    }

    // Called when the mech's right foot lands
    public void PlayRightFootstep()
    {
        PlayFootstep(rightFootSound);
    }

    private void PlayFootstep(AudioClip clip)
    {
        if (footstepSource != null && clip != null)
        {
            footstepSource.PlayOneShot(clip, footstepVolume);
        }
    }

    // Manual testing from Unity Inspector
    [ContextMenu("TEST - Left Footstep")]
    private void TestLeftFootstep()
    {
        if (Application.isPlaying)
            PlayLeftFootstep();
    }

    [ContextMenu("TEST - Right Footstep")]
    private void TestRightFootstep()
    {
        if (Application.isPlaying)
            PlayRightFootstep();
    }
}
