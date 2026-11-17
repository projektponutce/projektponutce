using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using System.IO;
using UnityEngine.TextCore.LowLevel;

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
    public Color partialColor = new Color(1.00f, 0.82f, 0.28f);
    public Color wrongColor = new Color(0.95f, 0.30f, 0.30f);

    [Header("Opcjonalne - trzęsące się tło")]
    public RectTransform backgroundImage;
    public float backgroundShakeStrength = 20f;

    [Header("Czcionka i Wygląd UI")]
    public TMP_FontAsset customFontAsset;
    public int maxVisibleSuggestions = 4;
    public int maxSearchResults = 25;
    public float itemHeight = 36f;
    public float scrollSensitivity = 7f;
    public bool styleSceneControls = true;

    [Header("Szafa Grająca (Historia Piosenek)")]
    public int maxHistoryItems = 3;
    public Vector2 jukeboxOffset = new Vector2(0f, -22f); // Odstęp bezpośrednio pod napisem REKORD
    public float jukeboxScale = 1.0f;

    [Header("Kolory Pasków Czasu")]
    public Color sliderTrackBgColor = new Color(0.08f, 0.05f, 0.18f, 0.96f);
    public Color unlockedBarColor = new Color(0.68f, 0.48f, 1.00f, 0.42f);
    public Color playbackBarColor = new Color(0.72f, 0.22f, 1.00f, 1.00f);
    public Color sliderOutlineColor = new Color(0.62f, 0.42f, 0.98f, 0.95f);

    private readonly float[] playTimes = { 0.1f, 1f, 3f, 5f, 10f };
    private int timeIndex = 0;
    private Coroutine playRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine titleAnimRoutine;
    private Coroutine bgShakeRoutine;
    private Coroutine loadTrackRoutine;
    private Coroutine coverRoutine;
    private Vector2 feedbackStartPos;
    private Vector2 backgroundStartPos;
    private bool won = false;
    private bool lost = false;
    private bool isLoaded = false;
    private bool artistGuessedInThisRound = false;

    private int currentScore = 0;
    private int currentStreak = 0;
    private int highScore = 0;

    private List<DeezerTrack> cachedValidTracks = new List<DeezerTrack>();

    private RectTransform customBarContainer;
    private RectTransform customUnlockedFillRt;
    private RectTransform customPlaybackFillRt;
    private float currentBarMaxDuration = 10f;

    private Button guessActionBtn;
    private TMP_Text guessActionBtnTmp;
    private Text guessActionBtnLegacyText;
    private Image guessActionBtnImg;

    private Dictionary<TMP_Text, RectTransform> statBadges = new Dictionary<TMP_Text, RectTransform>();

    private Texture2D currentFullCoverTex;
    private Texture2D currentPixelatedCoverTex;
    private GameObject coverFrameObj;
    private TMP_Text coverMysteryQuestionText;
    private TMP_FontAsset runtimeCrispFont;

    private long currentTrackId = 0;

    private class PlayedSongRecord
    {
        public long trackId;
        public string artist;
        public string title;
        public bool wasWon;
        public float guessedInSeconds;
        public int pointsEarned;
        public Texture2D coverTexture;
    }

    private class HistorySlotUI
    {
        public GameObject rootObj;
        public RawImage coverImg;
        public TMP_Text infoText;
        public Outline borderOutline;
        public Button openUrlBtn;
    }

    private List<PlayedSongRecord> songHistory = new List<PlayedSongRecord>();
    private List<HistorySlotUI> historySlots = new List<HistorySlotUI>();
    private GameObject jukeboxPanelObj;

    private class SongSuggestion
    {
        public string artist;
        public string title;
        public string displayText;
        public string normalizedSearch;
        public string normalizedArtist;
    }

    private List<SongSuggestion> allSuggestions = new List<SongSuggestion>();
    private List<SongSuggestion> currentMatches = new List<SongSuggestion>();
    private GameObject dropdownPanel;
    private ScrollRect dropdownScrollRect;
    private RectTransform contentRect;
    private List<Button> suggestionButtons = new List<Button>();
    private List<TMP_Text> suggestionLabels = new List<TMP_Text>();
    private bool ignoreInputCallback = false;

    [HideInInspector]
    public string correctAnswer = "";
    private string rawArtistName = "";
    private string rawSongTitle = "";

    public float CurrentPlayTime => playTimes[timeIndex];
    public float MaxPlayTime => playTimes[playTimes.Length - 1];

    void Start()
    {
        Debug.Log("<color=cyan>[START] Skrypt MusicPlayer został uruchomiony.</color>");

        if (audioSource == null) Debug.LogError("[BŁĄD] Brak przypisanego AudioSource w inspektorze!");
        if (guessInputField == null) Debug.LogError("[BŁĄD] Brak przypisanego TMP_InputField w inspektorze!");
        if (feedbackText == null) Debug.LogError("[BŁĄD] Brak przypisanego FeedbackText w inspektorze!");

        SetupCrispModernFont();

        if (flashOverlay != null)
        {
            flashOverlay.raycastTarget = false;
            Color c = flashOverlay.color;
            c.a = 0f;
            flashOverlay.color = c;
        }

        if (feedbackText != null)
        {
            feedbackText.raycastTarget = false;
            feedbackStartPos = feedbackText.rectTransform.anchoredPosition;
            feedbackText.alpha = 0f;
        }

        if (songTitleDisplay != null)
        {
            songTitleDisplay.raycastTarget = false;
            songTitleDisplay.text = "";
            songTitleDisplay.alpha = 0f;
        }

        if (loadingText != null)
        {
            loadingText.raycastTarget = false;
        }

        if (albumCover != null)
        {
            albumCover.raycastTarget = false;
        }

        if (backgroundImage != null)
        {
            backgroundStartPos = backgroundImage.anchoredPosition;
            Image bgImg = backgroundImage.GetComponent<Image>();
            if (bgImg != null) bgImg.raycastTarget = false;
        }

        if (styleSceneControls)
        {
            ApplyPurpleThemeToUI();
        }

        SetupBars();
        UpdateProgress(0f);

        if (guessInputField != null)
        {
            guessInputField.interactable = true;
            CreateScrollableAutoCompleteUI();
            guessInputField.onValueChanged.AddListener(OnGuessInputChanged);
            guessInputField.onSubmit.AddListener(OnGuessSubmitted);
        }

        highScore = PlayerPrefs.GetInt("HighScore", 0);
        UpdateScoreUI();

        CreateJukeboxHistoryUI();
        ApplyCrispFontToAllSceneTexts();
        UpdateGuessButtonLabel();

        StartCoroutine(FetchSpecificPlaylistAndPickSong(userId, targetPlaylistName));
    }

    void Update()
    {
        if (coverMysteryQuestionText != null && coverMysteryQuestionText.gameObject.activeSelf && !won && !lost)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.08f;
            coverMysteryQuestionText.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
        }
    }

    private void SetupCrispModernFont()
    {
        if (customFontAsset != null)
        {
            runtimeCrispFont = customFontAsset;
            return;
        }

        string[] preferredOSFonts = new string[]
        {
            "Avenir Next",
            "Avenir Next Demi Bold",
            "Montserrat",
            "Poppins",
            "Helvetica Neue",
            "Trebuchet MS",
            "Segoe UI",
            "Verdana",
            "Arial"
        };

        try
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(preferredOSFonts, 64);
            if (osFont != null)
            {
                runtimeCrispFont = TMP_FontAsset.CreateFontAsset(osFont, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic);
                if (runtimeCrispFont != null && runtimeCrispFont.material != null)
                {
                    runtimeCrispFont.TryAddCharacters("AĄBCĆDEĘFGHIJKLŁMNŃOÓPQRSŚTUVWXYZŹŻaąbcćdeęfghijklłmnńoópqrsśtuvwxyzźż0123456789!@#$%^&*()-_=+[]{};:'\",.<>/?♪—•");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[FONT] Nie udało się wygenerować czcionki systemowej HD: {ex.Message}");
        }
    }

    private void ApplyCrispFontToAllSceneTexts()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null && guessInputField != null)
            rootCanvas = guessInputField.GetComponentInParent<Canvas>();

        if (rootCanvas != null)
        {
            rootCanvas.pixelPerfect = false;

            CanvasScaler scaler = rootCanvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                }
                scaler.dynamicPixelsPerUnit = 4f;
                scaler.referencePixelsPerUnit = 100f;
            }

            TMP_Text[] allTexts = rootCanvas.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text t in allTexts)
            {
                SharpenSingleTMP(t);
            }
        }
    }

    private void SharpenSingleTMP(TMP_Text t)
    {
        if (t == null) return;

        Outline[] badOutlines = t.GetComponents<Outline>();
        foreach (var o in badOutlines) Destroy(o);

        Shadow[] badShadows = t.GetComponents<Shadow>();
        foreach (var s in badShadows) Destroy(s);

        if (runtimeCrispFont != null)
        {
            t.font = runtimeCrispFont;
        }

        t.extraPadding = true;

        if (t.fontSharedMaterial != null && t.fontSharedMaterial.mainTexture != null)
        {
            t.fontSharedMaterial.mainTexture.filterMode = FilterMode.Bilinear;
        }

        t.ForceMeshUpdate();
    }

    // Buduje dużą, czytelną Szafę Grającą bezpośrednio pod napisem REKORD po lewej stronie ekranu!
    private void CreateJukeboxHistoryUI()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null && guessInputField != null)
            rootCanvas = guessInputField.GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        // Szukamy najniższego napisu punktacji z lewej strony (zazwyczaj highScoreText -> "REKORD")
        TMP_Text anchorStatText = highScoreText != null ? highScoreText : (streakText != null ? streakText : scoreText);
        Transform parentToUse = anchorStatText != null ? anchorStatText.transform.parent : rootCanvas.transform;

        jukeboxPanelObj = new GameObject("JukeboxHistoryPanel", typeof(RectTransform), typeof(VerticalLayoutGroup));
        jukeboxPanelObj.transform.SetParent(parentToUse, false);

        float cardWidth = 310f;
        float cardHeight = 66f;
        float totalHeight = 28f + maxHistoryItems * (cardHeight + 8f);

        RectTransform panelRt = jukeboxPanelObj.GetComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(cardWidth, totalHeight);
        panelRt.localScale = Vector3.one * jukeboxScale;

        PositionJukeboxUnderStats();

        VerticalLayoutGroup vlg = jukeboxPanelObj.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Nagłówek sekcji (większy i wyraźniejszy)
        GameObject headerObj = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
        headerObj.transform.SetParent(jukeboxPanelObj.transform, false);
        RectTransform headerRt = headerObj.GetComponent<RectTransform>();
        headerRt.sizeDelta = new Vector2(cardWidth, 24f);

        TextMeshProUGUI headerTmp = headerObj.GetComponent<TextMeshProUGUI>();
        if (runtimeCrispFont != null) headerTmp.font = runtimeCrispFont;
        headerTmp.text = "<color=#D4C2FF>♪ OSTATNIE UTWORY</color> <size=82%><color=#9A84D8>(kliknij, by odsłuchać)</color></size>";
        headerTmp.fontSize = 14f;
        headerTmp.fontStyle = FontStyles.Bold;
        headerTmp.alignment = TextAlignmentOptions.BottomLeft;
        headerTmp.textWrappingMode = TextWrappingModes.NoWrap;
        headerTmp.raycastTarget = false;
        SharpenSingleTMP(headerTmp);

        Color shadowCol = new Color(0f, 0f, 0f, 0.60f);

        for (int i = 0; i < maxHistoryItems; i++)
        {
            int slotIndex = i;
            GameObject cardObj = new GameObject($"HistoryCard_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline), typeof(Shadow));
            cardObj.transform.SetParent(jukeboxPanelObj.transform, false);

            RectTransform cardRt = cardObj.GetComponent<RectTransform>();
            cardRt.sizeDelta = new Vector2(cardWidth, cardHeight);

            Image cardBg = cardObj.GetComponent<Image>();
            cardBg.color = new Color(0.12f, 0.08f, 0.26f, 0.96f);

            Outline cardOutline = cardObj.GetComponent<Outline>();
            cardOutline.effectColor = new Color(0.55f, 0.38f, 0.92f, 0.90f);
            cardOutline.effectDistance = new Vector2(1.5f, -1.5f);

            Shadow cardShadow = cardObj.GetComponent<Shadow>();
            cardShadow.effectColor = shadowCol;
            cardShadow.effectDistance = new Vector2(0f, -3f);

            Button cardBtn = cardObj.GetComponent<Button>();
            ColorBlock cb = cardBtn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.30f, 1.20f, 1.50f, 1f);
            cb.pressedColor = new Color(0.85f, 0.75f, 1f, 1f);
            cb.fadeDuration = 0.08f;
            cardBtn.colors = cb;

            cardBtn.onClick.AddListener(() => OnHistoryCardClicked(slotIndex));

            // Większa miniaturka okładki (52x52 px)
            GameObject coverObj = new GameObject("MiniCover", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(Outline));
            coverObj.transform.SetParent(cardObj.transform, false);

            RectTransform coverRt = coverObj.GetComponent<RectTransform>();
            coverRt.anchorMin = new Vector2(0f, 0.5f);
            coverRt.anchorMax = new Vector2(0f, 0.5f);
            coverRt.pivot = new Vector2(0f, 0.5f);
            coverRt.anchoredPosition = new Vector2(8f, 0f);
            coverRt.sizeDelta = new Vector2(52f, 52f);

            RawImage miniRaw = coverObj.GetComponent<RawImage>();
            miniRaw.color = Color.white;
            miniRaw.raycastTarget = false;

            Outline miniOutline = coverObj.GetComponent<Outline>();
            miniOutline.effectColor = new Color(0.45f, 0.30f, 0.80f, 0.85f);
            miniOutline.effectDistance = new Vector2(1.2f, -1.2f);

            // Większy i czytelniejszy tekst po prawej stronie okładki
            GameObject textObj = new GameObject("InfoText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(cardObj.transform, false);

            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(68f, 4f);
            textRt.offsetMax = new Vector2(-10f, -4f);

            TextMeshProUGUI infoTmp = textObj.GetComponent<TextMeshProUGUI>();
            if (runtimeCrispFont != null) infoTmp.font = runtimeCrispFont;
            infoTmp.fontSize = 14.5f;
            infoTmp.color = Color.white;
            infoTmp.alignment = TextAlignmentOptions.MidlineLeft;
            infoTmp.textWrappingMode = TextWrappingModes.NoWrap;
            infoTmp.overflowMode = TextOverflowModes.Ellipsis;
            infoTmp.richText = true;
            infoTmp.raycastTarget = false;
            SharpenSingleTMP(infoTmp);

            HistorySlotUI slot = new HistorySlotUI
            {
                rootObj = cardObj,
                coverImg = miniRaw,
                infoText = infoTmp,
                borderOutline = cardOutline,
                openUrlBtn = cardBtn
            };

            cardObj.SetActive(false);
            historySlots.Add(slot);
        }

        jukeboxPanelObj.SetActive(false);
    }

    // Ustawia Szafę Grającą dokładnie pod kafelkiem REKORD po lewej stronie ekranu!
    private void PositionJukeboxUnderStats()
    {
        if (jukeboxPanelObj == null) return;

        RectTransform panelRt = jukeboxPanelObj.GetComponent<RectTransform>();
        TMP_Text anchorStatText = highScoreText != null ? highScoreText : (streakText != null ? streakText : scoreText);

        if (anchorStatText != null)
        {
            // Jeśli mamy kafelek tła pod napisem REKORD, bierzemy jego lewy dolny róg
            if (statBadges.TryGetValue(anchorStatText, out RectTransform badgeRt) && badgeRt != null)
            {
                panelRt.anchorMin = badgeRt.anchorMin;
                panelRt.anchorMax = badgeRt.anchorMax;
                panelRt.pivot = new Vector2(0f, 1f); // Lewy górny róg Szafy Grającej

                float bottomY = badgeRt.localPosition.y - (badgeRt.sizeDelta.y * 0.5f);
                panelRt.localPosition = new Vector3(badgeRt.localPosition.x + jukeboxOffset.x, bottomY + jukeboxOffset.y, 0f);
                return;
            }

            RectTransform txtRt = anchorStatText.rectTransform;
            Vector3[] corners = new Vector3[4];
            txtRt.GetLocalCorners(corners);
            panelRt.anchorMin = txtRt.anchorMin;
            panelRt.anchorMax = txtRt.anchorMax;
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.localPosition = txtRt.localPosition + new Vector3(corners[0].x + jukeboxOffset.x, corners[0].y + jukeboxOffset.y, 0f);
        }
    }

    private void RecordCurrentSongToHistory(bool wasWon, int pointsEarned)
    {
        if (string.IsNullOrEmpty(rawSongTitle) && string.IsNullOrEmpty(rawArtistName))
            return;

        PlayedSongRecord record = new PlayedSongRecord
        {
            trackId = currentTrackId,
            artist = rawArtistName,
            title = rawSongTitle,
            wasWon = wasWon,
            guessedInSeconds = CurrentPlayTime,
            pointsEarned = pointsEarned,
            coverTexture = currentFullCoverTex
        };

        songHistory.Insert(0, record);

        while (songHistory.Count > maxHistoryItems)
        {
            PlayedSongRecord oldest = songHistory[songHistory.Count - 1];
            songHistory.RemoveAt(songHistory.Count - 1);

            if (oldest.coverTexture != null && oldest.coverTexture != currentFullCoverTex)
            {
                bool stillUsed = songHistory.Exists(r => r.coverTexture == oldest.coverTexture);
                if (!stillUsed)
                {
                    Destroy(oldest.coverTexture);
                }
            }
        }

        RefreshJukeboxHistoryUI();
    }

    private void RefreshJukeboxHistoryUI()
    {
        if (jukeboxPanelObj == null || historySlots.Count == 0) return;

        if (songHistory.Count == 0)
        {
            jukeboxPanelObj.SetActive(false);
            return;
        }

        PositionJukeboxUnderStats();
        jukeboxPanelObj.SetActive(true);

        for (int i = 0; i < historySlots.Count; i++)
        {
            if (i < songHistory.Count)
            {
                PlayedSongRecord rec = songHistory[i];
                HistorySlotUI slot = historySlots[i];

                slot.rootObj.SetActive(true);

                if (rec.coverTexture != null)
                {
                    slot.coverImg.texture = rec.coverTexture;
                    slot.coverImg.color = Color.white;
                }
                else
                {
                    slot.coverImg.texture = null;
                    slot.coverImg.color = new Color(0.22f, 0.14f, 0.42f, 1f);
                }

                string statusLine = rec.wasWon
                    ? $"<color=#3DDB84>Odgadnięto w {rec.guessedInSeconds:0.#}s (+{rec.pointsEarned} pkt)</color>"
                    : "<color=#FF5C5C>Nie odgadnięto</color>";

                slot.infoText.text = $"<size=105%><b>{rec.title}</b></size>\n<size=90%><color=#D0BEFF>♪ {rec.artist}</color></size>\n<size=82%>{statusLine}</size>";
                slot.borderOutline.effectColor = rec.wasWon
                    ? new Color(0.24f, 0.86f, 0.52f, 0.85f)
                    : new Color(0.95f, 0.30f, 0.30f, 0.80f);
            }
            else
            {
                historySlots[i].rootObj.SetActive(false);
            }
        }
    }

    private void OnHistoryCardClicked(int index)
    {
        if (index < 0 || index >= songHistory.Count) return;

        PlayedSongRecord rec = songHistory[index];
        string url;

        if (rec.trackId > 0)
        {
            url = $"https://www.deezer.com/track/{rec.trackId}";
        }
        else
        {
            string q = UnityWebRequest.EscapeURL($"{rec.artist} {rec.title}");
            url = $"https://www.deezer.com/search/{q}";
        }

        Debug.Log($"<color=cyan>[SZAFA GRAJĄCA] Otwieram utwór w przeglądarce: {url}</color>");
        Application.OpenURL(url);
    }

    private bool IsTextureUsedInHistory(Texture2D tex)
    {
        if (tex == null) return false;
        for (int i = 0; i < songHistory.Count; i++)
        {
            if (songHistory[i].coverTexture == tex) return true;
        }
        return false;
    }

    private void ApplyPurpleThemeToUI()
    {
        Color darkBg = new Color(0.12f, 0.08f, 0.25f, 0.96f);
        Color buttonBg = new Color(0.24f, 0.14f, 0.46f, 0.98f);
        Color neonBorder = new Color(0.62f, 0.42f, 0.98f, 0.90f);
        Color shadowCol = new Color(0f, 0f, 0f, 0.55f);

        if (guessInputField != null)
        {
            Image inputBg = guessInputField.GetComponent<Image>();
            if (inputBg != null)
            {
                inputBg.color = darkBg;
            }

            AddOrUpdateOutlineAndShadow(guessInputField.gameObject, neonBorder, shadowCol);

            guessInputField.customCaretColor = true;
            guessInputField.caretColor = new Color(1f, 0.88f, 0.40f, 1f);
            guessInputField.selectionColor = new Color(0.55f, 0.35f, 0.90f, 0.45f);

            if (guessInputField.textComponent != null)
            {
                guessInputField.textComponent.color = Color.white;
                SharpenSingleTMP(guessInputField.textComponent);
            }

            UpdateInputPlaceholderText();
        }

        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null && guessInputField != null)
            rootCanvas = guessInputField.GetComponentInParent<Canvas>();

        if (rootCanvas != null)
        {
            Button[] allButtons = rootCanvas.GetComponentsInChildren<Button>(true);
            foreach (Button btn in allButtons)
            {
                TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>(true);
                Text legacyText = btn.GetComponentInChildren<Text>(true);
                string rawLabel = btnText != null ? btnText.text : (legacyText != null ? legacyText.text : "");
                bool hasText = !string.IsNullOrWhiteSpace(rawLabel);

                if (!hasText || btn.name.ToLower().Contains("play"))
                    continue;

                Image btnImg = btn.GetComponent<Image>();
                if (btnImg != null)
                {
                    btnImg.color = buttonBg;
                }

                if (btn.name.ToLower().Contains("guess") || rawLabel.ToUpper().Contains("ZGADNIJ") || rawLabel.ToUpper().Contains("POMI"))
                {
                    guessActionBtn = btn;
                    guessActionBtnTmp = btnText;
                    guessActionBtnLegacyText = legacyText;
                    guessActionBtnImg = btnImg;
                }

                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.30f, 1.20f, 1.50f, 1f);
                cb.pressedColor = new Color(0.80f, 0.70f, 0.95f, 1f);
                cb.selectedColor = Color.white;
                cb.fadeDuration = 0.1f;
                btn.colors = cb;

                AddOrUpdateOutlineAndShadow(btn.gameObject, neonBorder, shadowCol);

                if (btnText != null)
                {
                    btnText.color = Color.white;
                    btnText.fontStyle = FontStyles.Bold;
                    SharpenSingleTMP(btnText);
                }
                if (legacyText != null)
                {
                    legacyText.color = Color.white;
                    legacyText.fontStyle = FontStyle.Bold;
                }
            }
        }

        BuildPerfectProgressBar(shadowCol);
        StyleScorePanel(neonBorder, shadowCol);
        StyleBottomAndTitleTexts();
        SetupAlbumCoverPresentation(neonBorder, shadowCol);
    }

    private void UpdateInputPlaceholderText()
    {
        if (guessInputField == null || !(guessInputField.placeholder is TMP_Text placeholderTmp))
            return;

        SharpenSingleTMP(placeholderTmp);

        if (artistGuessedInThisRound && !string.IsNullOrEmpty(rawArtistName))
        {
            placeholderTmp.color = new Color(1f, 0.85f, 0.35f, 0.85f);
            placeholderTmp.text = $"♪ {rawArtistName} — wpisz tytuł...";
        }
        else
        {
            placeholderTmp.color = new Color(0.72f, 0.64f, 0.92f, 0.60f);
            placeholderTmp.text = "Wpisz wykonawcę lub tytuł...";
        }
    }

    private void UpdateGuessButtonLabel()
    {
        if (guessActionBtnTmp == null && guessActionBtnLegacyText == null) return;

        bool hasInput = guessInputField != null && !string.IsNullOrWhiteSpace(guessInputField.text);
        string targetText;

        if (hasInput)
        {
            targetText = "ZGADNIJ!";
            if (guessActionBtnImg != null)
                guessActionBtnImg.color = new Color(0.32f, 0.18f, 0.62f, 0.98f);
        }
        else
        {
            targetText = "POMIŃ RUNDĘ";
            if (guessActionBtnImg != null)
                guessActionBtnImg.color = new Color(0.20f, 0.12f, 0.38f, 0.98f);
        }

        if (guessActionBtnTmp != null)
        {
            guessActionBtnTmp.text = targetText;
            guessActionBtnTmp.enableAutoSizing = true;
            guessActionBtnTmp.fontSizeMin = 11f;
            guessActionBtnTmp.fontSizeMax = 20f;
        }

        if (guessActionBtnLegacyText != null)
        {
            guessActionBtnLegacyText.text = targetText;
        }
    }

    private void BuildPerfectProgressBar(Color shadowCol)
    {
        if (playbackBar == null && unlockedBar == null) return;

        Slider refSlider = playbackBar != null ? playbackBar : unlockedBar;
        RectTransform refRt = refSlider.GetComponent<RectTransform>();

        if (playbackBar != null)
        {
            foreach (Graphic g in playbackBar.GetComponentsInChildren<Graphic>(true))
                g.enabled = false;
        }
        if (unlockedBar != null)
        {
            foreach (Graphic g in unlockedBar.GetComponentsInChildren<Graphic>(true))
                g.enabled = false;
        }

        if (customBarContainer != null) Destroy(customBarContainer.gameObject);

        GameObject containerObj = new GameObject("CustomProgressBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow));
        containerObj.transform.SetParent(refRt.parent, false);
        containerObj.transform.SetSiblingIndex(refRt.GetSiblingIndex());

        customBarContainer = containerObj.GetComponent<RectTransform>();
        customBarContainer.anchorMin = refRt.anchorMin;
        customBarContainer.anchorMax = refRt.anchorMax;
        customBarContainer.pivot = refRt.pivot;
        customBarContainer.anchoredPosition = refRt.anchoredPosition;

        float barWidth = refRt.rect.width > 50f ? refRt.sizeDelta.x : 440f;
        float barHeight = Mathf.Clamp(refRt.rect.height, 18f, 24f);
        customBarContainer.sizeDelta = new Vector2(barWidth, barHeight);
        customBarContainer.localScale = refRt.localScale;

        Image bgImg = containerObj.GetComponent<Image>();
        bgImg.color = sliderTrackBgColor;
        bgImg.raycastTarget = false;

        AddOrUpdateOutlineAndShadow(containerObj, sliderOutlineColor, shadowCol);

        GameObject innerArea = new GameObject("InnerMaskArea", typeof(RectTransform), typeof(RectMask2D));
        innerArea.transform.SetParent(customBarContainer, false);
        RectTransform innerRt = innerArea.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(2f, 2f);
        innerRt.offsetMax = new Vector2(-2f, -2f);

        GameObject unlockedFillObj = new GameObject("UnlockedFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        unlockedFillObj.transform.SetParent(innerRt, false);
        customUnlockedFillRt = unlockedFillObj.GetComponent<RectTransform>();
        customUnlockedFillRt.anchorMin = new Vector2(0f, 0f);
        customUnlockedFillRt.anchorMax = new Vector2(0f, 1f);
        customUnlockedFillRt.offsetMin = Vector2.zero;
        customUnlockedFillRt.offsetMax = Vector2.zero;

        Image unlockedFillImg = unlockedFillObj.GetComponent<Image>();
        unlockedFillImg.color = unlockedBarColor;
        unlockedFillImg.raycastTarget = false;

        GameObject playFillObj = new GameObject("PlaybackFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        playFillObj.transform.SetParent(innerRt, false);
        customPlaybackFillRt = playFillObj.GetComponent<RectTransform>();
        customPlaybackFillRt.anchorMin = new Vector2(0f, 0f);
        customPlaybackFillRt.anchorMax = new Vector2(0f, 1f);
        customPlaybackFillRt.offsetMin = Vector2.zero;
        customPlaybackFillRt.offsetMax = Vector2.zero;

        Image playFillImg = playFillObj.GetComponent<Image>();
        playFillImg.color = playbackBarColor;
        playFillImg.raycastTarget = false;
    }

    private void StyleScorePanel(Color borderCol, Color shadowCol)
    {
        // Sprawdzamy, czy napisy z lewej nie są ucięte przez lewą krawędź ekranu, i w razie potrzeby lekko przesuwamy je w prawo
        KeepLeftStatsInsideScreen();

        StyleSingleStatBadge(scoreText, new Color(0.13f, 0.08f, 0.28f, 0.92f), borderCol, shadowCol);
        StyleSingleStatBadge(streakText, new Color(0.16f, 0.09f, 0.32f, 0.92f), borderCol, shadowCol);
        StyleSingleStatBadge(highScoreText, new Color(0.19f, 0.11f, 0.36f, 0.94f), new Color(0.95f, 0.78f, 0.30f, 0.88f), shadowCol);
    }

    private void KeepLeftStatsInsideScreen()
    {
        TMP_Text[] stats = { scoreText, streakText, highScoreText };
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) return;

        RectTransform canvasRt = rootCanvas.GetComponent<RectTransform>();
        Vector3[] canvasCorners = new Vector3[4];
        canvasRt.GetWorldCorners(canvasCorners);
        float canvasLeftWorldX = canvasCorners[0].x;

        foreach (TMP_Text t in stats)
        {
            if (t == null) continue;
            Vector3[] textCorners = new Vector3[4];
            t.rectTransform.GetWorldCorners(textCorners);
            float textLeftWorldX = textCorners[0].x;

            // Jeśli lewy brzeg tekstu jest zbyt blisko (lub poza) lewą krawędzią ekranu, przesuwamy go do bezpiecznego marginesu
            float minMarginWorld = 28f * rootCanvas.scaleFactor;
            if (textLeftWorldX < canvasLeftWorldX + minMarginWorld)
            {
                float diff = (canvasLeftWorldX + minMarginWorld) - textLeftWorldX;
                t.rectTransform.position += new Vector3(diff, 0f, 0f);
            }
        }
    }

    private void StyleSingleStatBadge(TMP_Text txt, Color bgCol, Color borderCol, Color shadowCol)
    {
        if (txt == null) return;

        txt.richText = true;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Overflow;
        SharpenSingleTMP(txt);

        if (!statBadges.ContainsKey(txt) || statBadges[txt] == null)
        {
            GameObject badgeObj = new GameObject($"StatBadge_{txt.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow));
            badgeObj.transform.SetParent(txt.transform.parent, false);
            badgeObj.transform.SetSiblingIndex(txt.transform.GetSiblingIndex());

            Image bgImg = badgeObj.GetComponent<Image>();
            bgImg.color = bgCol;
            bgImg.raycastTarget = false;

            AddOrUpdateOutlineAndShadow(badgeObj, borderCol, shadowCol);
            statBadges[txt] = badgeObj.GetComponent<RectTransform>();
        }

        RefreshStatBadgeSize(txt);
    }

    private void RefreshStatBadgeSize(TMP_Text txt)
    {
        if (txt == null || !statBadges.TryGetValue(txt, out RectTransform badgeRt) || badgeRt == null)
            return;

        txt.ForceMeshUpdate();
        RectTransform txtRt = txt.rectTransform;

        Vector3[] corners = new Vector3[4];
        txtRt.GetLocalCorners(corners);
        float leftEdgeLocalX = corners[0].x;
        float centerLocalY = (corners[0].y + corners[1].y) * 0.5f;

        float textWidth = Mathf.Max(txt.preferredWidth, 85f);
        float textHeight = Mathf.Max(txt.preferredHeight, 22f);

        float padLeft = 12f;
        float padRight = 16f;
        float padVert = 10f;

        badgeRt.anchorMin = txtRt.anchorMin;
        badgeRt.anchorMax = txtRt.anchorMax;
        badgeRt.pivot = new Vector2(0f, 0.5f);
        badgeRt.localPosition = txtRt.localPosition + new Vector3(leftEdgeLocalX - padLeft, centerLocalY, 0f);
        badgeRt.sizeDelta = new Vector2(textWidth + padLeft + padRight, textHeight + padVert);
    }

    private void StyleBottomAndTitleTexts()
    {
        if (timeBarLabel != null)
        {
            timeBarLabel.richText = true;
            timeBarLabel.color = new Color(0.92f, 0.85f, 1f, 0.98f);
            timeBarLabel.fontStyle = FontStyles.Bold;
            SharpenSingleTMP(timeBarLabel);
        }

        if (feedbackText != null)
        {
            feedbackText.richText = true;
            feedbackText.fontStyle = FontStyles.Bold;
            feedbackText.alignment = TextAlignmentOptions.Center;
            SharpenSingleTMP(feedbackText);
        }

        if (songTitleDisplay != null)
        {
            songTitleDisplay.richText = true;
            songTitleDisplay.alignment = TextAlignmentOptions.Top;
            SharpenSingleTMP(songTitleDisplay);
        }

        if (loadingText != null)
        {
            loadingText.richText = true;
            loadingText.color = new Color(0.85f, 0.75f, 1f, 0.95f);
            loadingText.fontStyle = FontStyles.Italic;
            SharpenSingleTMP(loadingText);
        }
    }

    private void SetupAlbumCoverPresentation(Color borderCol, Color shadowCol)
    {
        if (albumCover == null) return;

        RectTransform coverRt = albumCover.rectTransform;

        if (coverFrameObj == null)
        {
            // Tworzymy ramkę jako osobną, nadrzędną strukturę
            coverFrameObj = new GameObject("AlbumCoverFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow));
            coverFrameObj.transform.SetParent(coverRt.parent, false);
            coverFrameObj.transform.SetSiblingIndex(coverRt.GetSiblingIndex());

            RectTransform frameRt = coverFrameObj.GetComponent<RectTransform>();
            frameRt.anchorMin = coverRt.anchorMin;
            frameRt.anchorMax = coverRt.anchorMax;
            frameRt.pivot = coverRt.pivot;
            frameRt.anchoredPosition = coverRt.anchoredPosition;
            frameRt.localScale = coverRt.localScale;

            // Ramy ramki będą teraz AUTOMATYCZNIE o 14 pikseli większe z każdej strony niż okładka!
            frameRt.sizeDelta = coverRt.sizeDelta + new Vector2(14f, 14f);

            Image frameImg = coverFrameObj.GetComponent<Image>();
            frameImg.color = new Color(0.12f, 0.07f, 0.26f, 0.96f);
            frameImg.raycastTarget = false;

            AddOrUpdateOutlineAndShadow(coverFrameObj, borderCol, shadowCol);

            // Wrzucamy okładkę do środka ramki, żeby była idealnie wyśśrodkowana
            coverRt.SetParent(frameRt, true);
            coverRt.anchorMin = new Vector2(0.5f, 0.5f);
            coverRt.anchorMax = new Vector2(0.5f, 0.5f);
            coverRt.pivot = new Vector2(0.5f, 0.5f);
            coverRt.anchoredPosition = Vector2.zero;
            coverRt.localScale = Vector3.one;

            // Pytajnik "?" na środku okładki
            GameObject qObj = new GameObject("MysteryQuestionMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            qObj.transform.SetParent(coverRt, false);

            RectTransform qRt = qObj.GetComponent<RectTransform>();
            qRt.anchorMin = Vector2.zero;
            qRt.anchorMax = Vector2.one;
            qRt.offsetMin = Vector2.zero;
            qRt.offsetMax = Vector2.zero;

            coverMysteryQuestionText = qObj.GetComponent<TextMeshProUGUI>();
            coverMysteryQuestionText.text = "?";
            coverMysteryQuestionText.fontSize = Mathf.Clamp(coverRt.rect.height * 0.55f, 36f, 78f);
            coverMysteryQuestionText.fontStyle = FontStyles.Bold;
            coverMysteryQuestionText.color = new Color(0.92f, 0.84f, 1f, 0.95f);
            coverMysteryQuestionText.alignment = TextAlignmentOptions.Center;
            coverMysteryQuestionText.raycastTarget = false;
            SharpenSingleTMP(coverMysteryQuestionText);

            // PRZYPIĘCIE NAPISU POD RAMKĄ: Napis jest teraz dzieckiem ramki i zawsze trzyma się 14 pikseli pod nią!
            if (songTitleDisplay != null)
            {
                RectTransform titleRt = songTitleDisplay.rectTransform;
                titleRt.SetParent(frameRt, false);
                titleRt.anchorMin = new Vector2(0.5f, 0f); // Kotwica na dolnej krawędzi ramki
                titleRt.anchorMax = new Vector2(0.5f, 0f);
                titleRt.pivot = new Vector2(0.5f, 1f);    // Pivot na górnej krawędzi napisu
                titleRt.anchoredPosition = new Vector2(0f, -14f); // Zawsze dokładnie 14 px odstępu pod ramką
                titleRt.sizeDelta = new Vector2(Mathf.Max(frameRt.rect.width + 100f, 300f), 90f);
                songTitleDisplay.alignment = TextAlignmentOptions.Top;
            }
        }
        else
        {
            // Jeśli ramka już istnieje, aktualizujemy jej rozmiar na wypadek, gdyby okładka zmieniła się w trakcie gry
            RectTransform frameRt = coverFrameObj.GetComponent<RectTransform>();
            frameRt.sizeDelta = coverRt.sizeDelta + new Vector2(14f, 14f);
        }

        coverFrameObj.SetActive(true);
        albumCover.gameObject.SetActive(true);
    }

    private void UpdateCoverMysteryState()
    {
        if (albumCover == null) return;

        if (coverFrameObj != null) coverFrameObj.SetActive(true);
        albumCover.gameObject.SetActive(true);

        if (currentFullCoverTex == null)
        {
            albumCover.texture = null;
            albumCover.color = new Color(0.16f, 0.10f, 0.32f, 1f);
            if (coverMysteryQuestionText != null) coverMysteryQuestionText.gameObject.SetActive(true);
            return;
        }

        int[] pixelSteps = { 8, 12, 18, 26, 38 };
        int res = pixelSteps[Mathf.Clamp(timeIndex, 0, pixelSteps.Length - 1)];

        if (currentPixelatedCoverTex != null) Destroy(currentPixelatedCoverTex);
        currentPixelatedCoverTex = CreatePixelatedTexture(currentFullCoverTex, res);

        albumCover.texture = currentPixelatedCoverTex;
        albumCover.color = new Color(0.65f, 0.55f, 0.85f, 1f);

        if (coverMysteryQuestionText != null)
        {
            coverMysteryQuestionText.gameObject.SetActive(true);
        }
    }

    private Texture2D CreatePixelatedTexture(Texture2D source, int resolution)
    {
        Texture2D small = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        small.filterMode = FilterMode.Point;
        small.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution;
                float v = (y + 0.5f) / resolution;
                small.SetPixel(x, y, source.GetPixelBilinear(u, v));
            }
        }
        small.Apply();
        return small;
    }

    private void AddOrUpdateOutlineAndShadow(GameObject target, Color outlineCol, Color shadowCol)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = outlineCol;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Shadow shadow = target.GetComponent<Shadow>();
        if (shadow == null) shadow = target.AddComponent<Shadow>();
        shadow.effectColor = shadowCol;
        shadow.effectDistance = new Vector2(0f, -3f);
    }

    private void OnGuessSubmitted(string text)
    {
        if (!isLoaded || won || lost) return;
        if (!string.IsNullOrWhiteSpace(text))
        {
            HideSuggestions();
            CheckAnswer();
        }
    }

    private void CreateScrollableAutoCompleteUI()
    {
        RectTransform inputRt = guessInputField.GetComponent<RectTransform>();

        dropdownPanel = new GameObject("AutoCompleteScrollPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow));
        dropdownPanel.transform.SetParent(inputRt, false);
        dropdownPanel.transform.SetAsLastSibling();

        RectTransform panelRt = dropdownPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(1f, 0f);
        panelRt.pivot = new Vector2(0.5f, 1f);
        panelRt.anchoredPosition = new Vector2(0f, -4f);
        panelRt.sizeDelta = new Vector2(0f, maxVisibleSuggestions * itemHeight + 8f);

        Image panelImg = dropdownPanel.GetComponent<Image>();
        panelImg.color = new Color(0.11f, 0.07f, 0.23f, 0.98f);

        Outline outline = dropdownPanel.GetComponent<Outline>();
        outline.effectColor = new Color(0.62f, 0.42f, 0.98f, 0.90f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Shadow shadow = dropdownPanel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(0f, -4f);

        dropdownScrollRect = dropdownPanel.AddComponent<ScrollRect>();
        dropdownScrollRect.horizontal = false;
        dropdownScrollRect.vertical = true;
        dropdownScrollRect.movementType = ScrollRect.MovementType.Clamped;
        dropdownScrollRect.scrollSensitivity = scrollSensitivity;
        dropdownScrollRect.inertia = true;
        dropdownScrollRect.decelerationRate = 0.08f;

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportObj.transform.SetParent(dropdownPanel.transform, false);

        RectTransform viewportRt = viewportObj.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(4f, 4f);
        viewportRt.offsetMax = new Vector2(-12f, -4f);

        Image vpImg = viewportObj.GetComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.01f);

        GameObject scrollbarObj = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        scrollbarObj.transform.SetParent(dropdownPanel.transform, false);
        RectTransform sbRt = scrollbarObj.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1f, 0f);
        sbRt.anchorMax = new Vector2(1f, 1f);
        sbRt.pivot = new Vector2(1f, 1f);
        sbRt.offsetMin = new Vector2(-9f, 4f);
        sbRt.offsetMax = new Vector2(-3f, -4f);

        Image sbBg = scrollbarObj.GetComponent<Image>();
        sbBg.color = new Color(0.05f, 0.03f, 0.12f, 0.85f);

        GameObject slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
        slidingArea.transform.SetParent(scrollbarObj.transform, false);
        RectTransform saRt = slidingArea.GetComponent<RectTransform>();
        saRt.anchorMin = Vector2.zero;
        saRt.anchorMax = Vector2.one;
        saRt.offsetMin = Vector2.zero;
        saRt.offsetMax = Vector2.zero;

        GameObject handleObj = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handleObj.transform.SetParent(slidingArea.transform, false);
        RectTransform handleRt = handleObj.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;

        Image handleImg = handleObj.GetComponent<Image>();
        handleImg.color = new Color(0.62f, 0.42f, 0.98f, 0.95f);

        Scrollbar scrollbar = scrollbarObj.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handleRt;
        scrollbar.targetGraphic = handleImg;

        GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(viewportObj.transform, false);

        contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vlg = contentObj.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 2f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contentObj.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        dropdownScrollRect.viewport = viewportRt;
        dropdownScrollRect.content = contentRect;
        dropdownScrollRect.verticalScrollbar = scrollbar;
        dropdownScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        TMP_FontAsset fontToUse = runtimeCrispFont != null
            ? runtimeCrispFont
            : (guessInputField.textComponent != null ? guessInputField.textComponent.font : null);

        for (int i = 0; i < maxSearchResults; i++)
        {
            int index = i;
            GameObject itemObj = new GameObject($"SuggestionItem_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            itemObj.transform.SetParent(contentObj.transform, false);

            LayoutElement le = itemObj.GetComponent<LayoutElement>();
            le.minHeight = itemHeight;
            le.preferredHeight = itemHeight;

            Image itemBg = itemObj.GetComponent<Image>();
            itemBg.color = (i % 2 == 0)
                ? new Color(0.19f, 0.12f, 0.36f, 0.96f)
                : new Color(0.15f, 0.10f, 0.30f, 0.96f);

            Button btn = itemObj.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.40f, 1.30f, 1.65f, 1f);
            cb.pressedColor = new Color(0.85f, 0.75f, 1f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.85f, 0.85f, 0.85f, 0.7f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            btn.onClick.AddListener(() =>
            {
                if (index < currentMatches.Count)
                {
                    ApplySuggestion(currentMatches[index].displayText);
                }
            });

            GameObject textObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(itemObj.transform, false);

            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 2f);
            textRt.offsetMax = new Vector2(-10f, -2f);

            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            if (fontToUse != null) tmp.font = fontToUse;
            tmp.fontSize = 15f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.richText = true;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;

            suggestionButtons.Add(btn);
            suggestionLabels.Add(tmp);
        }

        dropdownPanel.SetActive(false);
    }

    private void BuildSuggestionsList(List<DeezerTrack> tracks)
    {
        allSuggestions.Clear();
        HashSet<string> seen = new HashSet<string>();

        foreach (var t in tracks)
        {
            if (t == null) continue;
            string artist = t.artist != null && !string.IsNullOrEmpty(t.artist.name) ? t.artist.name.Trim() : "Nieznany";
            string title = !string.IsNullOrEmpty(t.title) ? Regex.Replace(t.title, @"\s*\(.*?\)", "").Trim() : "Nieznany";

            string display = $"{artist} - {title}";
            string norm = NormalizeForComparison(display);
            string normArtist = NormalizeForComparison(artist);

            if (!seen.Contains(norm))
            {
                seen.Add(norm);
                allSuggestions.Add(new SongSuggestion
                {
                    artist = artist,
                    title = title,
                    displayText = display,
                    normalizedSearch = norm,
                    normalizedArtist = normArtist
                });
            }
        }

        Debug.Log($"<color=cyan>[AUTOCOMPLETE] Zbudowano bazę {allSuggestions.Count} podpowiedzi.</color>");
    }

    private string FormatPrettySuggestion(SongSuggestion item, string rawInput)
    {
        string cleanInput = rawInput != null ? rawInput.Trim() : "";
        string artistPart = item.artist;
        string titlePart = item.title;

        if (cleanInput.Length >= 1)
        {
            string pattern = Regex.Escape(cleanInput);
            if (!artistGuessedInThisRound)
                artistPart = Regex.Replace(artistPart, pattern, "<color=#FFE066><b>$0</b></color>", RegexOptions.IgnoreCase);
            titlePart = Regex.Replace(titlePart, pattern, "<color=#FFE066><b>$0</b></color>", RegexOptions.IgnoreCase);
        }

        if (artistGuessedInThisRound)
        {
            return $"<color=#FFE066>♪ {artistPart}</color>  <color=#7A64B0>—</color>  <color=#FFFFFF><b>{titlePart}</b></color>";
        }

        return $"<color=#C4B2F0>♪ {artistPart}</color>  <color=#7A64B0>—</color>  <color=#FFFFFF><b>{titlePart}</b></color>";
    }

    private void OnGuessInputChanged(string input)
    {
        UpdateGuessButtonLabel();

        if (ignoreInputCallback || !isLoaded || won || lost)
        {
            HideSuggestions();
            return;
        }

        string query = NormalizeForComparison(input);

        int minChars = artistGuessedInThisRound ? 1 : 2;
        if (string.IsNullOrEmpty(query) || query.Length < minChars)
        {
            HideSuggestions();
            return;
        }

        currentMatches.Clear();
        string[] queryWords = query.Split(' ');
        string targetArtistNorm = NormalizeForComparison(rawArtistName);

        foreach (var item in allSuggestions)
        {
            if (artistGuessedInThisRound && item.normalizedArtist != targetArtistNorm)
            {
                continue;
            }

            bool matchesAllWords = true;
            foreach (var w in queryWords)
            {
                if (string.IsNullOrEmpty(w)) continue;
                if (!item.normalizedSearch.Contains(w))
                {
                    matchesAllWords = false;
                    break;
                }
            }

            if (matchesAllWords)
            {
                currentMatches.Add(item);
                if (currentMatches.Count >= maxSearchResults) break;
            }
        }

        RectTransform panelRt = dropdownPanel.GetComponent<RectTransform>();

        if (currentMatches.Count == 0)
        {
            float emptyPanelHeight = itemHeight + 8f;
            panelRt.sizeDelta = new Vector2(0f, emptyPanelHeight);
            panelRt.anchoredPosition = new Vector2(0f, -4f);

            guessInputField.transform.SetAsLastSibling();
            dropdownPanel.transform.SetAsLastSibling();
            dropdownPanel.SetActive(true);

            suggestionButtons[0].gameObject.SetActive(true);
            suggestionButtons[0].interactable = false;

            if (artistGuessedInThisRound)
                suggestionLabels[0].text = $"<color=#FF8899><i>Brak utworów {rawArtistName} pasujących do wpisu...</i></color>";
            else
                suggestionLabels[0].text = "<color=#FF8899><i>Brak pasujących utworów na playliście...</i></color>";

            for (int i = 1; i < maxSearchResults; i++)
            {
                suggestionButtons[i].gameObject.SetActive(false);
            }

            if (contentRect != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
                contentRect.anchoredPosition = Vector2.zero;
            }
            return;
        }

        int visibleCount = Mathf.Min(currentMatches.Count, maxVisibleSuggestions);
        float panelHeight = visibleCount * itemHeight + (visibleCount - 1) * 2f + 8f;

        panelRt.sizeDelta = new Vector2(0f, panelHeight);
        panelRt.anchoredPosition = new Vector2(0f, -4f);

        guessInputField.transform.SetAsLastSibling();
        dropdownPanel.transform.SetAsLastSibling();
        dropdownPanel.SetActive(true);

        if (dropdownScrollRect != null)
        {
            dropdownScrollRect.scrollSensitivity = scrollSensitivity;
            dropdownScrollRect.StopMovement();
        }

        for (int i = 0; i < maxSearchResults; i++)
        {
            if (i < currentMatches.Count)
            {
                suggestionButtons[i].gameObject.SetActive(true);
                suggestionButtons[i].interactable = true;
                suggestionLabels[i].text = FormatPrettySuggestion(currentMatches[i], input);
            }
            else
            {
                suggestionButtons[i].gameObject.SetActive(false);
            }
        }

        if (contentRect != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            contentRect.anchoredPosition = Vector2.zero;
        }
        if (dropdownScrollRect != null)
        {
            dropdownScrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void ApplySuggestion(string suggestionText)
    {
        ignoreInputCallback = true;
        guessInputField.text = suggestionText;
        guessInputField.caretPosition = suggestionText.Length;
        ignoreInputCallback = false;

        UpdateGuessButtonLabel();
        HideSuggestions();
        guessInputField.ActivateInputField();
    }

    private void HideSuggestions()
    {
        currentMatches.Clear();
        if (dropdownPanel != null) dropdownPanel.SetActive(false);
    }

    private IEnumerator FetchSpecificPlaylistAndPickSong(string uId, string playlistName)
    {
        Debug.Log($"<color=cyan>[DEEZER] Rozpoczynam wyszukiwanie playlisty dla User ID: {uId}, Playlist: {playlistName}</color>");
        if (loadingText != null) loadingText.text = "♪ Szukanie playlisty...";

        string url = $"https://api.deezer.com/user/{uId}/playlists";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 5;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[BŁĄD API] Błąd połączenia z API Deezer: {www.error}</color>");
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
                    Debug.Log($"<color=green>[DEEZER] Znaleziono playlistę! ID: {foundPlaylist.id}</color>");
                    yield return StartCoroutine(FetchPlaylistTracks(foundPlaylist.id.ToString()));
                }
                else
                {
                    Debug.LogWarning("<color=orange>[DEEZER] Nie znaleziono wskazanej playlisty o takiej nazwie!</color>");
                    if (loadingText != null) loadingText.text = "Nie znaleziono playlisty!";
                }
            }
            else
            {
                Debug.LogWarning("<color=orange>[DEEZER] Brak playlist dla podanego użytkownika lub zły User ID!</color>");
                if (loadingText != null) loadingText.text = "Brak playlist użytkownika!";
            }
        }
    }

    private IEnumerator FetchPlaylistTracks(string playlistId)
    {
        Debug.Log($"<color=cyan>[DEEZER] Pobieranie utworów z playlisty ID: {playlistId}</color>");
        string url = $"https://api.deezer.com/playlist/{playlistId}";
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.timeout = 5;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"<color=red>[BŁĄD API] Nie udało się pobrać utworów: {www.error}</color>");
                if (loadingText != null) loadingText.text = "Błąd pobierania utworów!";
                yield break;
            }

            string jsonResult = www.downloadHandler.text;
            DeezerPlaylistFullResponse playlistFull = JsonUtility.FromJson<DeezerPlaylistFullResponse>(jsonResult);

            if (playlistFull != null && playlistFull.tracks != null && playlistFull.tracks.data != null)
            {
                cachedValidTracks = playlistFull.tracks.data.FindAll(t =>
                    t != null &&
                    !string.IsNullOrEmpty(t.preview) &&
                    t.preview.Trim() != ""
                );

                BuildSuggestionsList(playlistFull.tracks.data);

                Debug.Log($"<color=green>[DEEZER] Pobrano pomyślnie. Liczba utworów z linkiem preview: {cachedValidTracks.Count}</color>");
                LoadNextTrack();
            }
            else
            {
                Debug.LogWarning("<color=orange>[DEEZER] Playlista jest pusta lub struktura JSON jest niepoprawna.</color>");
                if (loadingText != null) loadingText.text = "Pusta playlista!";
            }
        }
    }

    public void LoadNextTrack()
    {
        if (loadTrackRoutine != null) StopCoroutine(loadTrackRoutine);
        loadTrackRoutine = StartCoroutine(SafeLoadTrackRoutine());
    }

    private bool IsValidAudioData(byte[] data, out AudioType detectedType, out string fileExt)
    {
        detectedType = AudioType.MPEG;
        fileExt = ".mp3";

        if (data == null || data.Length < 16384)
            return false;

        if (data[0] == (byte)'<' || data[0] == (byte)'{')
            return false;

        if (data.Length > 8 && data[4] == (byte)'f' && data[5] == (byte)'t' && data[6] == (byte)'y' && data[7] == (byte)'p')
        {
            detectedType = AudioType.ACC;
            fileExt = ".m4a";
            return true;
        }

        bool isId3 = (data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3');
        bool isMpegSync = (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0);

        if (isId3 || isMpegSync)
        {
            detectedType = AudioType.MPEG;
            fileExt = ".mp3";
            return true;
        }

        return false;
    }

    private IEnumerator SafeLoadTrackRoutine()
    {
        Debug.Log("<color=cyan>[ŁADOWANIE] Uruchomiono SafeLoadTrackRoutine...</color>");
        if (loadingText != null) loadingText.text = "♪ Szukanie utworu...";
        if (guessInputField != null) guessInputField.interactable = true;
        HideSuggestions();
        isLoaded = false;
        artistGuessedInThisRound = false;
        UpdateInputPlaceholderText();
        UpdateGuessButtonLabel();

        UpdateCoverMysteryState();

        int safetyCounter = 0;

        while (safetyCounter < 40 && cachedValidTracks.Count > 0)
        {
            safetyCounter++;
            int randomIndex = Random.Range(0, cachedValidTracks.Count);
            DeezerTrack randomTrack = cachedValidTracks[randomIndex];

            string previewUrlToUse = randomTrack.preview;
            string coverUrlToUse = randomTrack.album != null ? randomTrack.album.cover_medium : "";

            if (randomTrack.id > 0)
            {
                using (UnityWebRequest trackInfoReq = UnityWebRequest.Get($"https://api.deezer.com/track/{randomTrack.id}"))
                {
                    trackInfoReq.timeout = 4;
                    yield return trackInfoReq.SendWebRequest();

                    if (trackInfoReq.result == UnityWebRequest.Result.Success)
                    {
                        DeezerTrack refreshedTrack = JsonUtility.FromJson<DeezerTrack>(trackInfoReq.downloadHandler.text);
                        if (refreshedTrack != null && !string.IsNullOrEmpty(refreshedTrack.preview))
                        {
                            previewUrlToUse = refreshedTrack.preview;
                            if (refreshedTrack.album != null && !string.IsNullOrEmpty(refreshedTrack.album.cover_medium))
                            {
                                coverUrlToUse = refreshedTrack.album.cover_medium;
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"<color=yellow>[POMINIĘTO] Brak aktywnego preview w API dla: {randomTrack.title}. Losuję kolejny...</color>");
                            cachedValidTracks.RemoveAt(randomIndex);
                            yield return null;
                            continue;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(previewUrlToUse))
            {
                cachedValidTracks.RemoveAt(randomIndex);
                yield return null;
                continue;
            }

            currentTrackId = randomTrack.id;
            rawArtistName = randomTrack.artist != null ? randomTrack.artist.name : "Nieznany";
            rawSongTitle = randomTrack.title != null ? Regex.Replace(randomTrack.title, @"\s*\(.*?\)", "").Trim() : "Nieznany";
            correctAnswer = $"<size=110%><b>{rawSongTitle}</b></size>\n<size=85%><color=#D8C8FF>♪ {rawArtistName}</color></size>";

            Debug.Log($"<color=cyan>[ŁADOWANIE] Próba #{safetyCounter}: {rawArtistName} - {rawSongTitle}</color>");

            byte[] rawAudioBytes = null;
            using (UnityWebRequest byteReq = UnityWebRequest.Get(previewUrlToUse))
            {
                byteReq.timeout = 5;
                yield return byteReq.SendWebRequest();

                if (byteReq.result == UnityWebRequest.Result.Success && byteReq.responseCode == 200)
                {
                    rawAudioBytes = byteReq.downloadHandler.data;
                }
            }

            if (!IsValidAudioData(rawAudioBytes, out AudioType audioType, out string ext))
            {
                Debug.LogWarning($"<color=yellow>[POMINIĘTO] Plik z Deezera dla '{rawSongTitle}' jest pusty lub ma nieobsługiwany format. Losuję kolejny...</color>");
                cachedValidTracks.RemoveAt(randomIndex);
                yield return null;
                continue;
            }

            string tempFilePath = Path.Combine(Application.temporaryCachePath, "temp_preview" + ext);
            bool fileSaved = false;
            try
            {
                File.WriteAllBytes(tempFilePath, rawAudioBytes);
                fileSaved = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"<color=orange>[PLIK] Nie udało się zapisać pliku tymczasowego: {ex.Message}</color>");
            }

            if (!fileSaved)
            {
                cachedValidTracks.RemoveAt(randomIndex);
                yield return null;
                continue;
            }

            string localUrl = "file://" + tempFilePath;
            using (UnityWebRequest localClipReq = UnityWebRequestMultimedia.GetAudioClip(localUrl, audioType))
            {
                yield return localClipReq.SendWebRequest();

                if (localClipReq.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(localClipReq);
                    if (clip != null && clip.length > 0.5f)
                    {
                        audioSource.clip = clip;
                        isLoaded = true;

                        if (!string.IsNullOrEmpty(coverUrlToUse))
                        {
                            if (coverRoutine != null) StopCoroutine(coverRoutine);
                            coverRoutine = StartCoroutine(DownloadAndSetCover(coverUrlToUse));
                        }

                        if (loadingText != null) loadingText.text = "";
                        if (guessInputField != null)
                        {
                            guessInputField.interactable = true;
                            guessInputField.ActivateInputField();
                        }

                        UpdateGuessButtonLabel();
                        Debug.Log($"<color=green>[SUKCES] Pomyślnie załadowano utwór: {rawArtistName} - {rawSongTitle}</color>");
                        loadTrackRoutine = null;
                        yield break;
                    }
                }
            }

            Debug.LogWarning($"<color=yellow>[POMINIĘTO] Nie udało się zdekodować utworu: {rawSongTitle}. Losuję kolejny...</color>");
            cachedValidTracks.RemoveAt(randomIndex);
            yield return null;
        }

        loadTrackRoutine = null;
        Debug.LogError("<color=red>[ALARM] Nie udało się załadować żadnego utworu! Ponawiam pobieranie playlisty...</color>");
        if (loadingText != null) loadingText.text = "Odświeżanie playlisty...";
        yield return new WaitForSeconds(1f);
        StartCoroutine(FetchSpecificPlaylistAndPickSong(userId, targetPlaylistName));
    }

    private IEnumerator DownloadAndSetCover(string url)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            www.timeout = 5;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(www);
                if (albumCover != null && tex != null)
                {
                    if (currentFullCoverTex != null && !IsTextureUsedInHistory(currentFullCoverTex))
                    {
                        Destroy(currentFullCoverTex);
                    }
                    currentFullCoverTex = tex;

                    if ((won || lost) && songHistory.Count > 0 && songHistory[0].trackId == currentTrackId)
                    {
                        songHistory[0].coverTexture = currentFullCoverTex;
                        RefreshJukeboxHistoryUI();
                    }

                    if (won || lost)
                    {
                        albumCover.texture = currentFullCoverTex;
                        albumCover.color = Color.white;
                        if (coverMysteryQuestionText != null) coverMysteryQuestionText.gameObject.SetActive(false);
                    }
                    else
                    {
                        UpdateCoverMysteryState();
                    }
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
        {
            StartCoroutine(FlipAndRevealCover(isSuccess));
        }
    }

    private void RevealPartialArtistUnderCover()
    {
        if (songTitleDisplay != null)
        {
            songTitleDisplay.text = $"<size=80%><color=#FFE066>WYKONAWCA TRAFIONY:</color></size>\n<size=105%><color=#FFFFFF><b>♪ {rawArtistName}</b></color></size>";
            songTitleDisplay.color = partialColor;

            if (titleAnimRoutine != null) StopCoroutine(titleAnimRoutine);
            titleAnimRoutine = StartCoroutine(AnimateTitlePopup());
        }

        if (coverFrameObj != null)
        {
            Outline frameOutline = coverFrameObj.GetComponent<Outline>();
            if (frameOutline != null)
                frameOutline.effectColor = partialColor;
        }
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

    private IEnumerator FlipAndRevealCover(bool isSuccess)
    {
        Transform targetToFlip = coverFrameObj != null ? coverFrameObj.transform : albumCover.transform;

        if (coverFrameObj != null)
        {
            Outline frameOutline = coverFrameObj.GetComponent<Outline>();
            if (frameOutline != null)
                frameOutline.effectColor = isSuccess ? correctColor : wrongColor;
        }

        float halfDuration = 0.18f;
        float t = 0f;
        while (t < halfDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / halfDuration);
            float scaleX = Mathf.Lerp(1f, 0f, k);
            targetToFlip.localScale = new Vector3(scaleX, 1.05f, 1f);
            yield return null;
        }

        if (coverMysteryQuestionText != null)
            coverMysteryQuestionText.gameObject.SetActive(false);

        if (currentFullCoverTex != null)
        {
            albumCover.texture = currentFullCoverTex;
        }
        albumCover.color = Color.white;

        t = 0f;
        float secondDuration = 0.28f;
        while (t < secondDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / secondDuration);
            float scaleX = Mathf.LerpUnclamped(0f, 1f, EaseOutBack(k));
            float scaleY = Mathf.Lerp(1.08f, 1f, k);
            targetToFlip.localScale = new Vector3(scaleX, scaleY, 1f);
            yield return null;
        }

        targetToFlip.localScale = Vector3.one;
    }

    private string NormalizeForComparison(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string clean = RemoveDiacritics(text).ToLower().Trim();
        clean = Regex.Replace(clean, @"[\s\-\–\—_.,!?'""()]+", " ").Trim();
        return clean;
    }

    private void SkipCurrentAttempt()
    {
        if (timeIndex < playTimes.Length - 1)
        {
            timeIndex++;
            SetupBars();
            UpdateProgress(0f);
            UpdateCoverMysteryState();
            UpdateGuessButtonLabel();

            if (timeIndex == playTimes.Length - 1)
            {
                ShowFeedback($"POMINIĘTO! Ostatnia szansa ({CurrentPlayTime} sek.)", new Color(0.95f, 0.72f, 0.30f), false);
            }
            else
            {
                ShowFeedback($"POMINIĘTO! Odblokowano {CurrentPlayTime} sek. nagrania!", new Color(0.78f, 0.62f, 1f), false);
            }
        }
        else
        {
            lost = true;
            currentStreak = 0;
            currentScore = 0;
            UpdateScoreUI();
            UpdateGuessButtonLabel();

            ShowFeedback("KONIEC SZANS! Nie udało się odgadnąć.", wrongColor, false);
            ShakeBackground();
            RevealAnswerAndCover(false);
            RecordCurrentSongToHistory(false, 0);

            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayFullSnippetOnEnd());
        }
    }

    public void CheckAnswer()
    {
        if (!isLoaded || won || lost) return;

        HideSuggestions();

        string rawInput = guessInputField != null ? guessInputField.text : "";
        string playerGuess = NormalizeForComparison(rawInput);

        if (string.IsNullOrWhiteSpace(rawInput) || string.IsNullOrEmpty(playerGuess))
        {
            SkipCurrentAttempt();
            return;
        }

        string cleanArtist = NormalizeForComparison(rawArtistName);
        string cleanTitle = NormalizeForComparison(rawSongTitle);
        string cleanFullMatch = $"{cleanArtist} {cleanTitle}";
        string cleanReverseMatch = $"{cleanTitle} {cleanArtist}";

        bool isFullWin = (playerGuess == cleanFullMatch) ||
                         (playerGuess == cleanReverseMatch) ||
                         (playerGuess == cleanTitle) ||
                         (GetLevenshteinDistance(playerGuess, cleanFullMatch) <= 2) ||
                         (GetLevenshteinDistance(playerGuess, cleanReverseMatch) <= 2) ||
                         (GetLevenshteinDistance(playerGuess, cleanTitle) <= 1);

        bool isArtistOnlyMatch = !isFullWin && (
            (playerGuess == cleanArtist) ||
            (GetLevenshteinDistance(playerGuess, cleanArtist) <= 1) ||
            (cleanArtist.Length >= 3 && playerGuess.StartsWith(cleanArtist + " "))
        );

        if (isFullWin)
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
            ShowFeedback($"BRAWO! +{pointsEarned} PKT!", correctColor, true);
            RevealAnswerAndCover(true);
            RecordCurrentSongToHistory(true, pointsEarned);

            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayFullSnippetOnEnd());
        }
        else if (isArtistOnlyMatch)
        {
            ignoreInputCallback = true;
            guessInputField.text = "";
            ignoreInputCallback = false;
            HideSuggestions();

            if (!artistGuessedInThisRound)
            {
                artistGuessedInThisRound = true;
                int artistBonus = 150;
                currentScore += artistBonus;

                if (currentScore > highScore)
                {
                    highScore = currentScore;
                    PlayerPrefs.SetInt("HighScore", highScore);
                    PlayerPrefs.Save();
                }

                UpdateScoreUI();
                RevealPartialArtistUnderCover();
                UpdateInputPlaceholderText();
                ShowFeedback($"DOBRY WYKONAWCA: {rawArtistName}! (+{artistBonus} PKT) Wpisz tytuł!", partialColor, true);
            }
            else
            {
                ShowFeedback($"Wykonawca to {rawArtistName} — wpisz właściwy tytuł!", partialColor, true);
            }

            UpdateGuessButtonLabel();
            guessInputField.ActivateInputField();
        }
        else
        {
            if (timeIndex == playTimes.Length - 2)
            {
                timeIndex++;
                ignoreInputCallback = true;
                guessInputField.text = "";
                ignoreInputCallback = false;
                UpdateGuessButtonLabel();
                SetupBars();
                UpdateProgress(0f);
                UpdateCoverMysteryState();

                string msg = artistGuessedInThisRound
                    ? $"ZŁY TYTUŁ ({rawArtistName})! OSTATNIA SZANSA ({CurrentPlayTime} s)!"
                    : $"ŹLE! OSTATNIA SZANSA! (Odblokowano {CurrentPlayTime} s)";
                ShowFeedback(msg, wrongColor, false);
                ShakeBackground();
            }
            else if (timeIndex >= playTimes.Length - 1)
            {
                lost = true;
                currentStreak = 0;
                currentScore = 0;
                UpdateScoreUI();

                ignoreInputCallback = true;
                guessInputField.text = "";
                ignoreInputCallback = false;
                UpdateGuessButtonLabel();
                ShowFeedback("KONIEC SZANS! Nie udało się odgadnąć.", wrongColor, false);
                ShakeBackground();
                RevealAnswerAndCover(false);
                RecordCurrentSongToHistory(false, 0);

                if (playRoutine != null) StopCoroutine(playRoutine);
                playRoutine = StartCoroutine(PlayFullSnippetOnEnd());
            }
            else
            {
                timeIndex++;
                ignoreInputCallback = true;
                guessInputField.text = "";
                ignoreInputCallback = false;
                UpdateGuessButtonLabel();
                SetupBars();
                UpdateProgress(0f);
                UpdateCoverMysteryState();

                string msg = artistGuessedInThisRound
                    ? $"ZŁY TYTUŁ! Wykonawca: {rawArtistName} (Odblokowano {CurrentPlayTime} s)"
                    : $"PUDŁO! Odblokowano {CurrentPlayTime} sek. nagrania!";
                ShowFeedback(msg, wrongColor, false);
                ShakeBackground();
            }
        }
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = $"<color=#B8A6E8>PUNKTY:</color> <color=#FFFFFF><b>{currentScore}</b></color>";
            RefreshStatBadgeSize(scoreText);
        }
        if (streakText != null)
        {
            streakText.text = $"<color=#B8A6E8>SERIA:</color> <color=#FFAE42><b>{currentStreak}x</b></color>";
            RefreshStatBadgeSize(streakText);
        }
        if (highScoreText != null)
        {
            highScoreText.text = $"<color=#FFE066>REKORD:</color> <color=#FFE066><b>{highScore}</b></color>";
            RefreshStatBadgeSize(highScoreText);
        }

        PositionJukeboxUnderStats();
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
                if (c == 'ł') stringBuilder.Append('l');
                else if (c == 'Ł') stringBuilder.Append('L');
                else stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    public void PlaySnippet()
    {
        if (!isLoaded || won || lost)
        {
            Debug.LogWarning($"[PLAY] Nie można teraz odtworzyć (isLoaded={isLoaded}, won={won}, lost={lost})");
            return;
        }

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            audioSource.Stop();
        }
        playRoutine = StartCoroutine(PlayForSeconds());
    }

    private IEnumerator PlayForSeconds()
    {
        if (audioSource == null || audioSource.clip == null) yield break;

        currentBarMaxDuration = MaxPlayTime;
        audioSource.Stop();
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
        if (audioSource == null || audioSource.clip == null) yield break;

        float duration = Mathf.Min(30f, audioSource.clip.length);
        currentBarMaxDuration = duration;

        if (customUnlockedFillRt != null)
        {
            customUnlockedFillRt.anchorMax = new Vector2(1f, 1f);
        }

        audioSource.Stop();
        audioSource.volume = 1f;
        audioSource.Play();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float clamped = Mathf.Min(elapsed, duration);

            if (customPlaybackFillRt != null)
            {
                float ratio = duration > 0.001f ? Mathf.Clamp01(clamped / duration) : 0f;
                customPlaybackFillRt.anchorMax = new Vector2(ratio, 1f);
            }

            if (timeBarLabel != null)
            {
                timeBarLabel.text = $"<color=#C4B2F0>♪</color> <b>{clamped:0.0}</b> <color=#8A74C8>/</color> {duration:0.0} s";
            }
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

    private void ShowFeedback(string message, Color color, bool keepVisibleOnScreen)
    {
        if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(FeedbackAnimation(message, color, keepVisibleOnScreen));
    }

    private IEnumerator FeedbackAnimation(string message, Color color, bool keepVisibleOnScreen)
    {
        RectTransform rt = feedbackText.rectTransform;
        feedbackText.text = message;
        feedbackText.color = color;
        feedbackText.alpha = 1f;
        rt.anchoredPosition = feedbackStartPos;
        rt.localScale = Vector3.one;
        StartCoroutine(Flash(color));
        if (keepVisibleOnScreen)
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
                yield return new WaitForSeconds(1.8f);
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
        currentBarMaxDuration = MaxPlayTime;

        if (customUnlockedFillRt != null)
        {
            float unlockedRatio = MaxPlayTime > 0.001f ? Mathf.Clamp01(CurrentPlayTime / MaxPlayTime) : 0f;
            customUnlockedFillRt.anchorMax = new Vector2(unlockedRatio, 1f);
        }

        if (customPlaybackFillRt != null)
        {
            customPlaybackFillRt.anchorMax = new Vector2(0f, 1f);
        }
    }

    private void UpdateProgress(float elapsed)
    {
        if (customPlaybackFillRt != null)
        {
            float ratio = currentBarMaxDuration > 0.001f ? Mathf.Clamp01(elapsed / currentBarMaxDuration) : 0f;
            customPlaybackFillRt.anchorMax = new Vector2(ratio, 1f);
        }

        if (timeBarLabel != null)
        {
            timeBarLabel.text = $"<color=#C4B2F0>♪</color> <b>{elapsed:0.0}</b> <color=#8A74C8>/</color> {CurrentPlayTime:0.0} s";
        }
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
        artistGuessedInThisRound = false;
        timeIndex = 0;

        HideSuggestions();

        if (songTitleDisplay != null)
        {
            songTitleDisplay.text = "";
            songTitleDisplay.alpha = 0f;
        }

        if (guessInputField != null)
        {
            ignoreInputCallback = true;
            guessInputField.text = "";
            ignoreInputCallback = false;
            guessInputField.interactable = true;
        }

        UpdateInputPlaceholderText();
        UpdateGuessButtonLabel();

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (coverRoutine != null)
        {
            StopCoroutine(coverRoutine);
            coverRoutine = null;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        if (feedbackText != null)
        {
            feedbackText.alpha = 0f;
        }

        SetupBars();
        UpdateProgress(0f);

        if (currentFullCoverTex != null && !IsTextureUsedInHistory(currentFullCoverTex))
        {
            Destroy(currentFullCoverTex);
        }
        currentFullCoverTex = null;

        if (currentPixelatedCoverTex != null)
        {
            Destroy(currentPixelatedCoverTex);
            currentPixelatedCoverTex = null;
        }
        if (coverFrameObj != null)
        {
            coverFrameObj.transform.localScale = Vector3.one;
            Outline frameOutline = coverFrameObj.GetComponent<Outline>();
            if (frameOutline != null) frameOutline.effectColor = sliderOutlineColor;
        }
        UpdateCoverMysteryState();

        if (cachedValidTracks != null && cachedValidTracks.Count > 0)
        {
            LoadNextTrack();
        }
        else
        {
            StartCoroutine(FetchSpecificPlaylistAndPickSong(userId, targetPlaylistName));
        }
    }
}