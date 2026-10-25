using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MusicPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public TMP_InputField guessInputField;
    public TMP_Text feedbackText;
    public string correctAnswer = "Mami";

    [Header("Opcjonalne - paski i napis")]
    public Slider playbackBar;
    public Slider unlockedBar;
    public TMP_Text timeBarLabel;

    [Header("Opcjonalne - efekty odpowiedzi")]
    public Image flashOverlay;   // pełnoekranowy Image, alfa 0, Raycast Target wyłączony
    public Color correctColor = new Color(0.24f, 0.86f, 0.52f); // zielony
    public Color wrongColor = new Color(0.95f, 0.30f, 0.30f);   // czerwony

    [Header("Opcjonalne - trzęsące się tło")]
    public RectTransform backgroundImage;       // obiekt Image z tłem
    public float backgroundShakeStrength = 20f;

    private readonly float[] playTimes = { 3f, 5f, 10f, 15f, 30f, 45f };
    private int timeIndex = 0;
    private Coroutine playRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine bgShakeRoutine;
    private Vector2 feedbackStartPos;
    private Vector2 backgroundStartPos;
    private bool won = false;

    public float CurrentPlayTime => playTimes[timeIndex];
    public float MaxPlayTime => playTimes[playTimes.Length - 1];

    void Start()
    {
        feedbackStartPos = feedbackText.rectTransform.anchoredPosition;
        feedbackText.alpha = 0f;

        if (backgroundImage != null)
            backgroundStartPos = backgroundImage.anchoredPosition;

        if (flashOverlay != null)
        {
            Color c = flashOverlay.color; c.a = 0f; flashOverlay.color = c;
        }
        SetupBars();
        UpdateProgress(0f);
    }

    public void PlaySnippet()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            audioSource.Stop();
        }
        playRoutine = StartCoroutine(PlayForSeconds());
    }

    private IEnumerator PlayForSeconds()
    {
        audioSource.Play();
        float elapsed = 0f;
        while (elapsed < CurrentPlayTime)
        {
            elapsed += Time.deltaTime;
            UpdateProgress(Mathf.Min(elapsed, CurrentPlayTime));
            yield return null;
        }
        audioSource.Stop();
        UpdateProgress(0f);
        playRoutine = null;
    }

    public void CheckAnswer()
    {
        if (won) return; // po wygranej nie można już zgadywać

        string playerGuess = guessInputField.text.ToLower().Trim();
        string actualAnswer = correctAnswer.ToLower().Trim();

        if (playerGuess == actualAnswer)
        {
            won = true;
            ShowFeedback("WYGRANA! Zgadłeś!", correctColor, true);
        }
        else
        {
            string msg;
            if (timeIndex < playTimes.Length - 1)
            {
                timeIndex++;
                msg = "ŹLE! Odblokowano " + CurrentPlayTime + " sek.";
            }
            else
            {
                msg = "ŹLE! Maksymalny czas już odblokowany.";
            }

            guessInputField.text = "";
            SetupBars();
            UpdateProgress(0f);
            ShowFeedback(msg, wrongColor, false);
            ShakeBackground();   // NOWE: trzęsące się tło
        }
    }

    // ---------- EFEKTY ----------

    private void ShowFeedback(string message, Color color, bool correct)
    {
        if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(FeedbackAnimation(message, color, correct));
    }

    private IEnumerator FeedbackAnimation(string message, Color color, bool correct)
    {
        RectTransform rt = feedbackText.rectTransform;
        feedbackText.text = message;
        feedbackText.color = color;   // najpierw kolor...
        feedbackText.alpha = 1f;      // ...potem alfa
        rt.anchoredPosition = feedbackStartPos;
        rt.localScale = Vector3.one;

        StartCoroutine(Flash(color));

        if (correct)
        {
            // "Wyskok" z odbiciem
            float t = 0f, duration = 0.45f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k));
                yield return null;
            }
            rt.localScale = Vector3.one;
            // tekst zostaje na ekranie
        }
        else
        {
            // Trzęsienie tekstu
            float t = 0f, duration = 0.4f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                float offset = Mathf.Sin(k * 40f) * (1f - k) * 15f;
                rt.anchoredPosition = feedbackStartPos + new Vector2(offset, 0f);
                yield return null;
            }
            rt.anchoredPosition = feedbackStartPos;

            // Chwila czytania i zanikanie
            yield return new WaitForSeconds(1.5f);
            float fade = 0.5f; t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                feedbackText.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }
            feedbackText.alpha = 0f;
        }
        feedbackRoutine = null;
    }

    private void ShakeBackground()
    {
        if (backgroundImage == null) return;
        if (bgShakeRoutine != null) StopCoroutine(bgShakeRoutine);
        bgShakeRoutine = StartCoroutine(BackgroundShakeAnimation());
    }

    private IEnumerator BackgroundShakeAnimation()
    {
        float t = 0f, duration = 0.4f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = t / duration;
            float fade = 1f - k; // trzęsienie słabnie z czasem
            float x = Mathf.Sin(k * 45f) * backgroundShakeStrength * fade;
            float y = Mathf.Cos(k * 38f) * backgroundShakeStrength * 0.6f * fade;
            backgroundImage.anchoredPosition = backgroundStartPos + new Vector2(x, y);
            yield return null;
        }
        backgroundImage.anchoredPosition = backgroundStartPos;
        bgShakeRoutine = null;
    }

    private IEnumerator Flash(Color color)
    {
        if (flashOverlay == null) yield break;

        float t = 0f, duration = 0.4f, startAlpha = 0.35f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Color c = color;
            c.a = Mathf.Lerp(startAlpha, 0f, t / duration);
            flashOverlay.color = c;
            yield return null;
        }
        Color end = color; end.a = 0f;
        flashOverlay.color = end;
    }

    private float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ---------- PASKI ----------

    private void SetupBars()
    {
        if (playbackBar != null)
        {
            playbackBar.minValue = 0f;
            playbackBar.maxValue = MaxPlayTime;
        }
        if (unlockedBar != null)
        {
            unlockedBar.minValue = 0f;
            unlockedBar.maxValue = MaxPlayTime;
            unlockedBar.value = CurrentPlayTime;
        }
    }

    private void UpdateProgress(float elapsed)
    {
        if (playbackBar != null) playbackBar.value = elapsed;
        if (timeBarLabel != null)
            timeBarLabel.text = elapsed.ToString("0.0") + " / " + CurrentPlayTime + " s";
    }
}