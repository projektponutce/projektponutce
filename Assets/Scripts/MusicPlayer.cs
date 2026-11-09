using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

[System.Serializable]
public class DeezerUserPlaylistsResponse
{
    public List<DeezerPlaylistInfo> data;
}

[System.Serializable]
public class DeezerPlaylistInfo
{
    public long id;
    public string title;
}

[System.Serializable]
public class DeezerPlaylistFullResponse
{
    public string title;
    public DeezerTracksWrapper tracks;
}

[System.Serializable]
public class DeezerTracksWrapper
{
    public List<DeezerTrack> data;
}

[System.Serializable]
public class DeezerTrack
{
    public long id;
    public string title;
    public string preview; 
    public DeezerArtist artist;
}

[System.Serializable]
public class DeezerArtist
{
    public string name;
}

public class MusicPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public TMP_InputField guessInputField;
    public TMP_Text feedbackText;
    
    [Header("Deezer User & Playlist Settings")]
    public string userId = "TUTAJ_WPISZ_SWOJ_USER_ID"; 
    public string targetPlaylistName = "Moja Playlista"; 
    public TMP_Text loadingText;

    [Header("Opcjonalne - paski i napis")]
    public Slider playbackBar;
    public Slider unlockedBar;
    public TMP_Text timeBarLabel;

    [Header("Opcjonalne - efekty odpowiedzi")]
    public Image flashOverlay;   
    public Color correctColor = new Color(0.24f, 0.86f, 0.52f); 
    public Color wrongColor = new Color(0.95f, 0.30f, 0.30f);   

    [Header("Opcjonalne - trzęsące się tło")]
    public RectTransform backgroundImage;       
    public float backgroundShakeStrength = 20f;

    private readonly float[] playTimes = { 3f, 5f, 10f, 15f, 30f };
    private int timeIndex = 0;
    private Coroutine playRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine bgShakeRoutine;
    private Vector2 feedbackStartPos;
    private Vector2 backgroundStartPos;
    private bool won = false;
    private bool lost = false;
    private bool isLoaded = false;

    [HideInInspector]
    public string correctAnswer = "";
    private string rawArtistName = "";
    private string rawSongTitle = "";

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

        StartCoroutine(FetchSpecificPlaylistAndPickSong(userId, targetPlaylistName));
    }

    private IEnumerator FetchSpecificPlaylistAndPickSong(string uId, string playlistName)
    {
        if (loadingText != null) loadingText.text = "Szukanie playlisty...";
        if (guessInputField != null) guessInputField.interactable = false;

        string url = $"https://api.deezer.com/user/{uId}/playlists";
        
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Błąd pobierania użytkownika: " + www.error);
                if (loadingText != null) loadingText.text = "Błąd połączenia z Deezer!";
                yield break;
            }

            string jsonResult = www.downloadHandler.text;
            DeezerUserPlaylistsResponse playlistsResponse = JsonUtility.FromJson<DeezerUserPlaylistsResponse>(jsonResult);

            if (playlistsResponse != null && playlistsResponse.data != null && playlistsResponse.data.Count > 0)
            {
                DeezerPlaylistInfo foundPlaylist = null;

                foreach (var pl in playlistsResponse.data)
                {
                    if (pl.title.Trim().ToLower() == playlistName.Trim().ToLower())
                    {
                        foundPlaylist = pl;
                        break;
                    }
                }

                if (foundPlaylist != null)
                {
                    Debug.Log($"Znaleziono playlistę: {foundPlaylist.title} (ID: {foundPlaylist.id})");
                    yield return StartCoroutine(FetchPlaylistTracks(foundPlaylist.id.ToString()));
                }
                else
                {
                    Debug.LogError($"Nie znaleziono publicznej playlisty o nazwie: '{playlistName}'");
                    if (loadingText != null) loadingText.text = "Nie znaleziono takiej playlisty!";
                }
            }
            else
            {
                if (loadingText != null) loadingText.text = "Brak publicznych playlist u użytkownika!";
            }
        }
    }

    private IEnumerator FetchPlaylistTracks(string playlistId)
    {
        string url = $"https://api.deezer.com/playlist/{playlistId}";
        
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                if (loadingText != null) loadingText.text = "Błąd pobierania utworów z playlisty!";
                yield break;
            }

            string jsonResult = www.downloadHandler.text;
            DeezerPlaylistFullResponse playlistFull = JsonUtility.FromJson<DeezerPlaylistFullResponse>(jsonResult);

            if (playlistFull != null && playlistFull.tracks != null && playlistFull.tracks.data != null && playlistFull.tracks.data.Count > 0)
            {
                DeezerTrack randomTrack = playlistFull.tracks.data[Random.Range(0, playlistFull.tracks.data.Count)];
                
                rawArtistName = randomTrack.artist.name;
                rawSongTitle = Regex.Replace(randomTrack.title, @"\s*\(.*?\)", "").Trim();

                correctAnswer = $"{rawArtistName} - {rawSongTitle}";
                Debug.Log($"Wylosowano utwór: {correctAnswer}");

                yield return StartCoroutine(LoadAudioClip(randomTrack.preview));
            }
            else
            {
                if (loadingText != null) loadingText.text = "Wybrana playlista jest pusta!";
            }
        }
    }

    private IEnumerator LoadAudioClip(string url)
    {
        if (loadingText != null) loadingText.text = "Pobieranie próbki audio...";

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                audioSource.clip = clip;
                
                isLoaded = true;
                if (loadingText != null) loadingText.text = "";
                if (guessInputField != null) guessInputField.interactable = true;

                Debug.Log("Utwór gotowy do odtworzenia!");
            }
            else
            {
                Debug.LogError("Błąd pobierania audio: " + www.error);
                if (loadingText != null) loadingText.text = "Błąd pobierania audio!";
            }
        }
    }

    public void CheckAnswer()
    {
        if (!isLoaded || won || lost) return; 

        string playerGuess = RemoveDiacritics(guessInputField.text).ToLower().Trim();
        
        string cleanArtist = RemoveDiacritics(rawArtistName).ToLower().Trim();
        string cleanTitle = RemoveDiacritics(rawSongTitle).ToLower().Trim();
        string fullMatch = $"{cleanArtist} - {cleanTitle}";

        playerGuess = Regex.Replace(playerGuess, @"[\s\-]+", " ");
        string cleanFullMatch = Regex.Replace(fullMatch, @"[\s\-]+", " ");
        string cleanTitleOnly = Regex.Replace(cleanTitle, @"[\s\-]+", " ");
        string cleanArtistOnly = Regex.Replace(cleanArtist, @"[\s\-]+", " ");

        bool isCorrect = (playerGuess == cleanFullMatch) || (playerGuess == cleanTitleOnly) || (playerGuess == cleanArtistOnly) ||
                         (GetLevenshteinDistance(playerGuess, cleanFullMatch) <= 1) ||
                         (GetLevenshteinDistance(playerGuess, cleanTitleOnly) <= 1) ||
                         (GetLevenshteinDistance(playerGuess, cleanArtistOnly) <= 1);

        if (isCorrect)
        {
            won = true;
            ShowFeedback("WYGRANA! Zgadłeś!", correctColor, true);
        }
        else
        {
            if (timeIndex == playTimes.Length - 2)
            {
                timeIndex++;
                guessInputField.text = "";
                SetupBars();
                UpdateProgress(0f);
                ShowFeedback("ŹLE! OSTRZEŻENIE: To Twoja OSTATNIA szansa!", wrongColor, false);
                ShakeBackground();
            }
            else if (timeIndex >= playTimes.Length - 1)
            {
                lost = true;
                guessInputField.text = "";
                ShowFeedback("ŹLE! Przegrałeś. Tytuł: " + correctAnswer, wrongColor, false);
                if (guessInputField != null) guessInputField.interactable = false;
                ShakeBackground();
            }
            else
            {
                timeIndex++;
                guessInputField.text = "";
                SetupBars();
                UpdateProgress(0f);
                ShowFeedback("ŹLE! Odblokowano " + CurrentPlayTime + " sek.", wrongColor, false);
                ShakeBackground();
            }
        }
    }

    private int GetLevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Mathf.Min(
                    Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    public void PlaySnippet()
    {
        if (!isLoaded || won || lost) return; 

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

    private void ShowFeedback(string message, Color color, bool correct)
    {
        if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(FeedbackAnimation(message, color, correct));
    }

    private IEnumerator FeedbackAnimation(string message, Color color, bool correct)
    {
        RectTransform rt = feedbackText.rectTransform;
        feedbackText.text = message;
        feedbackText.color = color;   
        feedbackText.alpha = 1f;      
        rt.anchoredPosition = feedbackStartPos;
        rt.localScale = Vector3.one;

        StartCoroutine(Flash(color));

        if (correct)
        {
            float t = 0f, duration = 0.45f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }
        else
        {
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

            if (!lost)
            {
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
            float fade = 1f - k; 
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