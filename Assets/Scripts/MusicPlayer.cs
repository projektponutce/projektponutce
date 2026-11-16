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
    public DeezerAlbum album;
}

[System.Serializable]
public class DeezerArtist
{
    public string name;
}

[System.Serializable]
public class DeezerAlbum
{
    public string cover_medium;
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

    [Header("Okładka Albumu")]
    public RawImage albumCover;

    [Header("Okienko Tytułu Piosenki")]
    public TMP_Text songTitleDisplay;

    [Header("Punktacja i Combo")]
    public TMP_Text scoreText;
    public TMP_Text streakText;
    public TMP_Text highScoreText;

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

    // Czasy odtwarzania fragmentów
    private readonly float[] playTimes = { 0.1f, 1f, 3f, 5f, 10f };
    private int timeIndex = 0;
    private Coroutine playRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine titleAnimRoutine;
    private Coroutine bgShakeRoutine;
    private Vector2 feedbackStartPos;
    private Vector2 backgroundStartPos;
    private bool won = false;
    private bool lost = false;
    private bool isLoaded = false;

    private int currentScore = 0;
    private int currentStreak = 0;
    private int highScore = 0;

    // Pamięć podręczna utworów z playlisty i historia zagraných utworów
    private List<DeezerTrack> cachedValidTracks = new List<DeezerTrack>();
    private HashSet<long> playedTrackIds = new HashSet<long>();

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

        if (songTitleDisplay != null)
        {
            songTitleDisplay.text = "";
            songTitleDisplay.alpha = 0f;
        }

        if (backgroundImage != null)
            backgroundStartPos = backgroundImage.anchoredPosition;

        if (flashOverlay != null)
        {
            Color c = flashOverlay.color; c.a = 0f; flashOverlay.color = c;
        }

        SetupBars();
        UpdateProgress(0f);

        highScore = PlayerPrefs.GetInt("HighScore", 0);
        UpdateScoreUI();

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
                if (loadingText != null) loadingText.text = "Błąd połączenia z Deezer!";
                if (guessInputField != null) guessInputField.interactable = true;
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
                    yield return StartCoroutine(FetchPlaylistTracks(foundPlaylist.id.ToString()));
                else
                {
                    if (loadingText != null) loadingText.text = "Nie znaleziono takiej playlisty!";
                    if (guessInputField != null) guessInputField.interactable = true;
                }
            }
            else
            {
                if (loadingText != null) loadingText.text = "Brak playlist użytkownika!";
                if (guessInputField != null) guessInputField.interactable = true;
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
                if (loadingText != null) loadingText.text = "Błąd pobierania playlisty!";
                if (guessInputField != null) guessInputField.interactable = true;
                yield break;
            }

            string jsonResult = www.downloadHandler.text;
            DeezerPlaylistFullResponse playlistFull = JsonUtility.FromJson<DeezerPlaylistFullResponse>(jsonResult);

            if (playlistFull != null && playlistFull.tracks != null && playlistFull.tracks.data != null && playlistFull.tracks.data.Count > 0)
            {
                // Zapisujemy przefiltrowaną listę utworów do pamięci podręcznej jednorazowo
                cachedValidTracks = playlistFull.tracks.data.FindAll(t => 
                    t != null && 
                    t.preview != null && 
                    t.preview.Trim() != ""
                );

                if (cachedValidTracks.Count == 0)
                {
                    if (loadingText != null) loadingText.text = "Brak utworów z działającym audio na playliście!";
                    Debug.LogError("Żaden utwór na tej playliście nie posiada działającej próbki audio w bazie Deezer!");
                    if (guessInputField != null) guessInputField.interactable = true;
                    yield break;
                }

                // Rozpoczynamy losowanie pierwszego utworu
                yield return StartCoroutine(PickAndLoadRandomTrack());
            }
            else
            {
                if (loadingText != null) loadingText.text = "Playlista jest pusta!";
                if (guessInputField != null) guessInputField.interactable = true;
            }
        }
    }

    // Nowa metoda losująca utwór lokalnie z pamięci podręcznej (bez zapytań HTTP do playlisty)
    private IEnumerator PickAndLoadRandomTrack()
    {
        List<DeezerTrack> availableTracks = cachedValidTracks.FindAll(t => !playedTrackIds.Contains(t.id));

        if (availableTracks.Count == 0)
        {
            playedTrackIds.Clear();
            availableTracks = new List<DeezerTrack>(cachedValidTracks);

            if (availableTracks.Count == 0)
            {
                if (loadingText != null) loadingText.text = "Brak dostępnych utworów!";
                if (guessInputField != null) guessInputField.interactable = true;
                yield break;
            }
        }

        DeezerTrack randomTrack = availableTracks[Random.Range(0, availableTracks.Count)];
        playedTrackIds.Add(randomTrack.id);

        rawArtistName = randomTrack.artist.name;
        rawSongTitle = Regex.Replace(randomTrack.title, @"\s*\(.*?\)", "").Trim();
        correctAnswer = $"{rawArtistName} - {rawSongTitle}";

        Debug.Log($"Próbuję wczytać utwór: {correctAnswer} (ID: {randomTrack.id})");

        if (randomTrack.album != null && !string.IsNullOrEmpty(randomTrack.album.cover_medium))
            StartCoroutine(DownloadAndSetCover(randomTrack.album.cover_medium));

        yield return StartCoroutine(LoadAudioClip(randomTrack.preview));
    }

    private IEnumerator DownloadAndSetCover(string url)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(www);
                if (albumCover != null)
                {
                    albumCover.texture = tex;
                    albumCover.color = Color.black;
                    albumCover.gameObject.SetActive(true);
                }
            }
        }
    }

    private void RevealAnswerAndCover(bool isSuccess)
    {
        if (songTitleDisplay != null)
        {
            songTitleDisplay.text = correctAnswer;
            songTitleDisplay.color = isSuccess ? correctColor : wrongColor;

            if (titleAnimRoutine != null) StopCoroutine(titleAnimRoutine);
            titleAnimRoutine = StartCoroutine(AnimateTitlePopup());
        }

        if (albumCover != null) 
            StartCoroutine(FadeInCover());
    }

    private IEnumerator AnimateTitlePopup()
    {
        RectTransform rt = songTitleDisplay.rectTransform;
        float t = 0f;
        float duration = 0.5f;

        songTitleDisplay.alpha = 0f;
        rt.localScale = Vector3.zero;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(0f, 1f, EaseOutBack(k));
            songTitleDisplay.alpha = k;
            yield return null;
        }

        rt.localScale = Vector3.one;
        songTitleDisplay.alpha = 1f;
    }

    private IEnumerator FadeInCover()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 2f;
            albumCover.color = Color.Lerp(Color.black, Color.white, t);
            yield return null;
        }
    }

    private IEnumerator LoadAudioClip(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[Pominięto] Pusty link audio. Losuję kolejny utwór lokalnie...");
            yield return StartCoroutine(PickAndLoadRandomTrack());
            yield break;
        }

        if (loadingText != null) loadingText.text = "Pobieranie próbki audio...";
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip != null && clip.length > 0)
                {
                    audioSource.clip = clip;
                    isLoaded = true;
                    if (loadingText != null) loadingText.text = "";
                    if (guessInputField != null) guessInputField.interactable = true;
                }
                else
                {
                    Debug.LogWarning("[Pominięto automatycznie] Uszkodzony klip audio. Losuję następny...");
                    yield return StartCoroutine(PickAndLoadRandomTrack());
                }
            }
            else
            {
                Debug.LogWarning("[Pominięto] Błąd sieci przy pobieraniu audio. Losuję następny...");
                yield return StartCoroutine(PickAndLoadRandomTrack());
            }
        }
    }

    public void CheckAnswer()
    {
        if (!isLoaded || won || lost) return;

        string playerGuess = RemoveDiacritics(guessInputField.text).ToLower().Trim();

        string cleanArtist = RemoveDiacritics(rawArtistName).ToLower().Trim();
        string cleanTitle = RemoveDiacritics(rawSongTitle).ToLower().Trim();
        string fullMatch = $"{cleanArtist} {cleanTitle}";

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

            int basePoints = 0;
            if (timeIndex == 0) basePoints = 1000;
            else if (timeIndex == 1) basePoints = 800;
            else if (timeIndex == 2) basePoints = 600;
            else if (timeIndex == 3) basePoints = 400;
            else basePoints = 200;

            int streakBonus = currentStreak * 100;
            int pointsEarned = basePoints + streakBonus;

            currentScore += pointsEarned;
            currentStreak++;

            if (currentScore > highScore)
            {
                highScore = currentScore;
                PlayerPrefs.SetInt("HighScore", highScore);
                PlayerPrefs.Save();
            }

            UpdateScoreUI();
            ShowFeedback($"WYGRANA! +{pointsEarned} pkt!", correctColor, true);
            RevealAnswerAndCover(true);
            
            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayFullSnippetOnEnd());
        }
        else
        {
            currentStreak = 0;
            currentScore = 0;
            UpdateScoreUI();

            if (timeIndex == playTimes.Length - 2)
            {
                timeIndex++;
                guessInputField.text = "";
                SetupBars();
                UpdateProgress(0f);
                ShowFeedback("ŹLE! OSTRZEŻENIE: To Twoja OSTATNIA szansa! Straciłeś punkty!", wrongColor, false);
                ShakeBackground();
            }
            else if (timeIndex >= playTimes.Length - 1)
            {
                lost = true;
                guessInputField.text = "";
                ShowFeedback("ŹLE! Przegrałeś.", wrongColor, false);
                if (guessInputField != null) guessInputField.interactable = false;
                ShakeBackground();
                RevealAnswerAndCover(false);

                if (playRoutine != null) StopCoroutine(playRoutine);
                playRoutine = StartCoroutine(PlayFullSnippetOnEnd());
            }
            else
            {
                timeIndex++;
                guessInputField.text = "";
                SetupBars();
                UpdateProgress(0f);
                ShowFeedback("ŹLE! Straciłeś punkty! Odblokowano " + CurrentPlayTime + " sek.", wrongColor, false);
                ShakeBackground();
            }
        }
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null) scoreText.text = $"Punkty: {currentScore}";
        if (streakText != null) streakText.text = $"Seria: {currentStreak}";
        if (highScoreText != null) highScoreText.text = $"Rekord: {highScore}";
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
                d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
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
        audioSource.volume = 1f;
        audioSource.Play();
        float elapsed = 0f;
        while (elapsed < CurrentPlayTime)
        {
            elapsed += Time.deltaTime;
            UpdateProgress(Mathf.Min(elapsed, CurrentPlayTime));
            yield return null;
        }

        yield return StartCoroutine(FadeOutAudio());

        UpdateProgress(0f);
        playRoutine = null;
    }

    private IEnumerator PlayFullSnippetOnEnd()
    {
        if (audioSource.clip == null) yield break;

        float duration = Mathf.Min(30f, audioSource.clip.length);

        if (playbackBar != null)
        {
            playbackBar.maxValue = duration;
        }

        audioSource.volume = 1f;
        audioSource.time = 0f;
        audioSource.Play();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (playbackBar != null) playbackBar.value = elapsed;
            if (timeBarLabel != null) timeBarLabel.text = elapsed.ToString("0.0") + " / " + duration.ToString("0.0") + " s";
            yield return null;
        }

        yield return StartCoroutine(FadeOutAudio());
        UpdateProgress(0f);
        playRoutine = null;
    }

    private IEnumerator FadeOutAudio()
    {
        float startVolume = audioSource.volume;
        float fadeDuration = 0.5f;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, t / fadeDuration);
            yield return null;
        }

        audioSource.Stop();
        audioSource.volume = startVolume;
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
            playbackBar.interactable = false;
        }
        if (unlockedBar != null)
        {
            unlockedBar.minValue = 0f;
            unlockedBar.maxValue = MaxPlayTime;
            unlockedBar.value = CurrentPlayTime;
            unlockedBar.interactable = false;
        }
    }

    private void UpdateProgress(float elapsed)
    {
        if (playbackBar != null) playbackBar.value = elapsed;
        if (timeBarLabel != null) timeBarLabel.text = elapsed.ToString("0.0") + " / " + CurrentPlayTime + " s";
    }

    public void StartNewRound()
    {
        if (lost)
        {
            currentScore = 0;
            currentStreak = 0;
            UpdateScoreUI();
        }

        won = false;
        lost = false;
        isLoaded = false;
        timeIndex = 0;

        if (songTitleDisplay != null)
        {
            songTitleDisplay.text = "";
            songTitleDisplay.alpha = 0f;
        }

        if (guessInputField != null)
        {
            guessInputField.text = "";
            guessInputField.interactable = false;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        feedbackText.alpha = 0f;
        SetupBars();
        UpdateProgress(0f);

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (albumCover != null)
        {
            albumCover.texture = null;
            albumCover.gameObject.SetActive(false);
        }

        // Jeśli mamy już pobraną listę utworów w pamięci podręcznej, losujemy od razu lokalnie
        if (cachedValidTracks != null && cachedValidTracks.Count > 0)
        {
            StartCoroutine(PickAndLoadRandomTrack());
        }
        else
        {
            StartCoroutine(FetchSpecificPlaylistAndPickSong(userId, targetPlaylistName));
        }
    }
}