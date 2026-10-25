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
    public Slider playbackBar;      // wypełnia się, gdy leci muzyka
    public Slider unlockedBar;      // pokazuje odblokowany czas (leży pod playbackBar)
    public TMP_Text timeBarLabel;   // np. "2.4 / 10 s"

    private readonly float[] playTimes = { 3f, 5f, 10f, 15f, 30f, 45f };
    private int timeIndex = 0;
    private Coroutine playRoutine;

    public float CurrentPlayTime => playTimes[timeIndex];
    public float MaxPlayTime => playTimes[playTimes.Length - 1];

    void Start()
    {
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
            yield return null; // czeka jedną klatkę
        }

        audioSource.Stop();
        UpdateProgress(0f); // pasek wraca na początek
        playRoutine = null;
    }

    public void CheckAnswer()
    {
        string playerGuess = guessInputField.text.ToLower().Trim();
        string actualAnswer = correctAnswer.ToLower().Trim();

        if (playerGuess == actualAnswer)
        {
            feedbackText.text = "WYGRANA! Zgadłeś!";
            feedbackText.color = Color.green;
        }
        else
        {
            if (timeIndex < playTimes.Length - 1)
            {
                timeIndex++;
                feedbackText.text = "ŹLE! Czas wydłużony do " + CurrentPlayTime + " sek.";
            }
            else
            {
                feedbackText.text = "ŹLE! Maksymalny czas (" + CurrentPlayTime + " sek.) już osiągnięty.";
            }

            feedbackText.color = Color.red;
            guessInputField.text = "";
            SetupBars();
            UpdateProgress(0f);
        }
    }

    // Ustawia zakres pasków i odblokowany czas
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

    // Aktualizuje wypełnienie paska i napis
    private void UpdateProgress(float elapsed)
    {
        if (playbackBar != null)
        {
            playbackBar.value = elapsed;
        }

        if (timeBarLabel != null)
        {
            timeBarLabel.text = elapsed.ToString("0.0") + " / " + CurrentPlayTime + " s";
        }
    }
}