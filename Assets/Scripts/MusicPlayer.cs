using System.Collections;
using UnityEngine;
using TMPro; // Obsługa TextMeshPro

public class MusicPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public TMP_InputField guessInputField;
    public TMP_Text feedbackText; // NOWE: Odniesienie do tekstu na ekranie
    public string correctAnswer = "Mami"; // Twoja odpowiedź

    private float currentPlayTime = 1f;

    public void PlaySnippet()
    {
        StartCoroutine(PlayForSeconds());
    }

    private IEnumerator PlayForSeconds()
    {
        audioSource.Play();
        yield return new WaitForSeconds(currentPlayTime);
        audioSource.Stop();
    }

    public void CheckAnswer()
    {
        string playerGuess = guessInputField.text.ToLower().Trim();
        string actualAnswer = correctAnswer.ToLower().Trim();

        if (playerGuess == actualAnswer)
        {
            feedbackText.text = "WYGRANA! Zgadłeś!";
            feedbackText.color = Color.green; // Zmienia kolor tekstu na zielony
        }
        else
        {
            currentPlayTime += 1f;
            feedbackText.text = "ŹLE! Czas wydłużony do " + currentPlayTime + " sek.";
            feedbackText.color = Color.red; // Zmienia kolor tekstu na czerwony
            guessInputField.text = ""; // Czyści pole wpisywania
        }
    }
}