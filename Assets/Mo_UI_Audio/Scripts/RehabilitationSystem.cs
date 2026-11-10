
using System.Collections;
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

    [Header("Audio Volume")]
    [Range(0f, 1f)]
    [SerializeField] private float destructionVolume = 0.35f;

    [Range(0f, 1f)]
    [SerializeField] private float goodDeedVolume = 1f;

    [Header("Failure Warning Effect")]
    [SerializeField] private Image failureFlash;

    [Range(0f, 0.5f)]
    [SerializeField] private float flashOpacity = 0.25f;

    [Header("Warning Animation Timing")]
    [SerializeField] private float fadeInDuration = 2f;
    [SerializeField] private float holdDuration = 2f;
    [SerializeField] private float fadeOutDuration = 2f;

    private int progress = 0;
    private Coroutine flashCoroutine;

    private void Start()
    {
        if (progressBar != null)
        {
            progressBar.minValue = 0;
            progressBar.maxValue = 100;
        }

        SetFlashAlpha(0f);

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
        PlaySound(goodDeedSound, goodDeedVolume);
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
        PlaySound(destructionSound, destructionVolume);

        // Restart the red warning animation
        if (failureFlash != null)
        {
            if (flashCoroutine != null)
                StopCoroutine(flashCoroutine);

            flashCoroutine = StartCoroutine(FlashWarning());
        }
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

    private void PlaySound(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip, volume);
    }

    // Slow red warning animation
    private IEnumerator FlashWarning()
    {
        SetFlashAlpha(0f);

        // Phase 1: Slowly turn red
        yield return FadeFlash(
            flashOpacity, fadeInDuration);

        // Phase 2: Stay red
        yield return new WaitForSeconds(
            Mathf.Max(0f, holdDuration));

        // Phase 3: Slowly return to normal
        yield return FadeFlash(
            0f, fadeOutDuration);

        flashCoroutine = null;
    }

    private IEnumerator FadeFlash(float targetAlpha, float duration)
    {
        if (failureFlash == null)
            yield break;

        float startAlpha = failureFlash.color.a;
        float elapsed = 0f;

        duration = Mathf.Max(0.01f, duration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Lerp(
                startAlpha, targetAlpha, t);

            SetFlashAlpha(alpha);

            yield return null;
        }

        SetFlashAlpha(targetAlpha);
    }

    private void SetFlashAlpha(float alpha)
    {
        if (failureFlash == null)
            return;

        Color color = failureFlash.color;
        color.a = alpha;
        failureFlash.color = color;
    }

    private void OnDisable()
    {
        SetFlashAlpha(0f);
        flashCoroutine = null;
    }

    // Test directly from Unity Inspector
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
