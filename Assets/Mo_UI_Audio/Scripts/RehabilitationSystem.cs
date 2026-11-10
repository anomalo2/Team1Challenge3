
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RehabilitationSystem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private TMP_Text statusMessage;

    [Header("Rehabilitation Settings")]
    [SerializeField] private int pointsPerGoodDeed = 25;

    [Header("Optional Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip goodDeedSound;
    [SerializeField] private AudioClip destructionSound;

    private int progress = 0;

    private void Start()
    {
        if (progressBar != null)
        {
            progressBar.minValue = 0;
            progressBar.maxValue = 100;
        }

        UpdateUI("WELCOME TO REDEMPTION OS. " +
                 "YOUR REPUTATION IS BEYOND REPAIR.");
    }

    // Called when the player completes a good deed
    public void GoodDeedCompleted()
    {
        progress = Mathf.Clamp(
            progress + pointsPerGoodDeed, 0, 100);

        string message = progress == 100
            ? "REHABILITATED. THIS IS STATISTICALLY UNEXPECTED."
            : "GOOD DEED RECORDED. PUBLIC REMAINS SUSPICIOUS.";

        UpdateUI(message);
        PlaySound(goodDeedSound);
    }

    // Called whenever destruction occurs
    public void DestructionOccurred()
    {
        // Absolutely no forgiveness!
        progress = 0;

        string[] warnings =
        {
            "REHABILITATION REVOKED. WE HAD ONE RULE.",
            "YOUR THERAPIST HAS BEEN NOTIFIED.",
            "ALL PROGRESS ERASED. IMPRESSIVELY DISAPPOINTING.",
            "THE CITY REGRETS GIVING YOU ANOTHER CHANCE.",
            "ONE MAILBOX. YOU COULDN'T SPARE ONE MAILBOX?"
        };

        string warning = warnings[
            Random.Range(0, warnings.Length)];

        UpdateUI(warning);
        PlaySound(destructionSound);
    }

    private void UpdateUI(string message)
    {
        if (progressBar != null)
            progressBar.value = progress;

        if (percentageText != null)
            percentageText.text = progress + "%";

        if (statusMessage != null)
            statusMessage.text = message;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // Test directly from the Unity Inspector
    [ContextMenu("TEST - Complete Good Deed")]
    private void TestGoodDeed()
    {
        if (Application.isPlaying)
            GoodDeedCompleted();
    }

    [ContextMenu("TEST - Cause Destruction")]
    private void TestDestruction()
    {
        if (Application.isPlaying)
            DestructionOccurred();
    }
}
