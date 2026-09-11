using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PullTheWorld
{
    /// <summary>
    /// The one owner of screen flow: menu -> level -> complete -> next level, with settings as an
    /// overlay on top of any of them.
    ///
    /// Every panel is a dumb UiPanel that only knows how to fade; all the "what shows when" logic
    /// lives here, in one readable state machine. That is worth the indirection because in a
    /// hyper-casual game the flow changes constantly and the panels do not.
    ///
    /// Two things here are about correctness rather than flow:
    ///  * Opening settings during play REALLY pauses (Time.timeScale = 0). The first build only
    ///    disabled rotation input, so the ball kept rolling - and dying - behind the overlay.
    ///  * "Restart all levels" wipes saved progress, so it is two taps: the first arms it and
    ///    changes the label, the second does it, and it disarms itself after a few seconds. A
    ///    single tap on a destructive button next to CLOSE is a support ticket waiting to happen.
    /// </summary>
    public class UiRoot : MonoBehaviour
    {
        public enum Screen { MainMenu, Playing, LevelComplete }

        [Header("Panels")]
        [SerializeField] UiPanel mainMenu;
        [SerializeField] UiPanel hud;
        [SerializeField] UiPanel levelComplete;
        [SerializeField] UiPanel settings;

        [Header("Main menu widgets")]
        [SerializeField] Button playButton;
        [SerializeField] Button menuSettingsButton;
        [SerializeField] TMP_Text progressLabel;

        [Header("HUD widgets")]
        [SerializeField] Button restartButton;
        [SerializeField] Button pauseButton;
        [SerializeField] TMP_Text levelLabel;
        [SerializeField] TMP_Text levelTitle;
        [SerializeField] TMP_Text keyLabel;
        [SerializeField] GameObject keyGroup;

        [Header("Level complete widgets")]
        [SerializeField] Button continueButton;
        [SerializeField] TMP_Text completeTitle;
        [SerializeField] TMP_Text completeSubtitle;
        [SerializeField] ParticleSystem celebrationVfx;

        [Header("Settings widgets")]
        [SerializeField] Button closeSettingsButton;
        [SerializeField] Button restartAllButton;
        [SerializeField] TMP_Text restartAllLabel;
        [SerializeField] Toggle soundToggle;
        [SerializeField] Toggle musicToggle;
        [SerializeField] Toggle hapticsToggle;
        [Tooltip("Seconds the armed 'tap again' state lasts before it quietly disarms.")]
        [SerializeField] float restartArmSeconds = 3.5f;

        [Header("Level select")]
        [Tooltip("Development convenience in Settings: unlocks every level so a build can be tested " +
                 "from any point. Turn off for release.")]
        [SerializeField] bool showDevUnlock = true;
        [SerializeField] Button unlockAllButton;
        [SerializeField] Button levelsButton;
        [SerializeField] UiPanel levelSelect;
        [SerializeField] Transform levelGrid;
        [Tooltip("One inactive tile; cloned per level when the picker opens.")]
        [SerializeField] Button levelTileTemplate;
        [SerializeField] Button closeLevelsButton;

        [Header("Ads")]
        [Tooltip("Rewarded skip, shown in the HUD only after several failures on one level.")]
        [SerializeField] Button skipButton;

        [Header("Chapter card")]
        [Tooltip("Fades in over the first level of each chapter: 'CHAPTER II' and the sky's name.")]
        [SerializeField] CanvasGroup chapterCard;
        [SerializeField] TMP_Text chapterNumber;
        [SerializeField] TMP_Text chapterName;
        [SerializeField] float chapterCardSeconds = 2.4f;

        [Header("Gem flight")]
        [Tooltip("A gem icon that flies from where a gem was picked up to the HUD counter.")]
        [SerializeField] Image flyIcon;
        [SerializeField] float flySeconds = 0.45f;

        [Header("Screen flash")]
        [SerializeField] Image flashImage;
        [SerializeField] Color winFlash = new Color(1f, 0.95f, 0.75f, 0.45f);
        [SerializeField] Color failFlash = new Color(1f, 0.35f, 0.25f, 0.42f);
        [SerializeField] float flashFade = 2.6f;

        [Header("Refs")]
        [SerializeField] LevelManager levels;
        [SerializeField] OnboardingHint onboarding;

        const string RestartAllIdle = "RESTART ALL LEVELS";
        const string RestartAllArmed = "TAP AGAIN TO CONFIRM";

        Screen screen = Screen.MainMenu;
        Color flash = Color.clear;
        bool suppressToggleCallbacks;
        bool restartArmed;
        Coroutine disarm;
        bool paused;

        public static UiRoot Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            if (!levels) levels = LevelManager.Instance
                ? LevelManager.Instance
                : FindFirstObjectByType<LevelManager>();

            if (playButton) playButton.onClick.AddListener(OnPlay);
            if (menuSettingsButton) menuSettingsButton.onClick.AddListener(OpenSettings);
            if (restartButton) restartButton.onClick.AddListener(OnRestart);
            if (pauseButton) pauseButton.onClick.AddListener(OpenSettings);
            if (continueButton) continueButton.onClick.AddListener(OnContinue);
            if (closeSettingsButton) closeSettingsButton.onClick.AddListener(CloseSettings);
            if (restartAllButton) restartAllButton.onClick.AddListener(OnRestartAll);
            if (levelsButton) levelsButton.onClick.AddListener(OpenLevelSelect);
            if (unlockAllButton)
            {
                unlockAllButton.onClick.AddListener(OnUnlockAll);
                unlockAllButton.gameObject.SetActive(showDevUnlock);
            }
            if (closeLevelsButton) closeLevelsButton.onClick.AddListener(CloseLevelSelect);
            if (skipButton) { skipButton.onClick.AddListener(OnSkipLevel); skipButton.gameObject.SetActive(false); }

            if (soundToggle) soundToggle.onValueChanged.AddListener(v => SetSetting(() => GameProgress.SoundOn = v));
            if (musicToggle) musicToggle.onValueChanged.AddListener(v => SetSetting(() => GameProgress.MusicOn = v));
            if (hapticsToggle) hapticsToggle.onValueChanged.AddListener(v => SetSetting(() => GameProgress.HapticsOn = v));
        }

        void OnEnable()
        {
            if (levels != null)
            {
                levels.OnLevelLoaded += HandleLevelLoaded;
                levels.OnLevelWon += HandleWon;
                levels.OnLevelFailed += HandleFailed;
                levels.OnKeysChanged += HandleKeysChanged;
            }
        }

        void OnDisable()
        {
            if (levels != null)
            {
                levels.OnLevelLoaded -= HandleLevelLoaded;
                levels.OnLevelWon -= HandleWon;
                levels.OnLevelFailed -= HandleFailed;
                levels.OnKeysChanged -= HandleKeysChanged;
            }
            SetPaused(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            PullSettingsIntoToggles();
            SetRestartArmed(false);
            GoTo(Screen.MainMenu, immediate: true);
        }

        // ----------------------------------------------------------------------- flow -------
        void GoTo(Screen s, bool immediate = false)
        {
            screen = s;
            SetPaused(false);   // a screen change never leaves the clock stopped

            bool menu = s == Screen.MainMenu;
            bool playing = s == Screen.Playing;
            bool complete = s == Screen.LevelComplete;
            if (menu) shownChapter = -1;            // coming back from the menu re-introduces the chapter

            if (immediate)
            {
                if (mainMenu) mainMenu.SetImmediate(menu);
                if (hud) hud.SetImmediate(playing);
                if (levelComplete) levelComplete.SetImmediate(complete);
                if (settings) settings.SetImmediate(false);
                if (levelSelect) levelSelect.SetImmediate(false);
            }
            else
            {
                if (mainMenu) mainMenu.SetVisible(menu);
                if (hud) hud.SetVisible(playing);
                if (levelComplete) levelComplete.SetVisible(complete);
                if (levelSelect) levelSelect.SetVisible(false);
            }

            if (menu) RefreshProgressLabel();
        }

        void OnPlay()
        {
            Click();
            // Resume where they left off, clamped in case the level list shrank since last run.
            int start = Mathf.Clamp(GameProgress.UnlockedIndex, 0,
                                    Mathf.Max(0, (levels ? levels.LevelCount : 1) - 1));
            GoTo(Screen.Playing);
            if (levels) levels.LoadLevel(start);
        }

        void OnRestart()
        {
            Click();
            if (levels) levels.Restart();
        }

        void OnContinue()
        {
            Click();
            // The only place an interstitial can ever appear: after a win, on the way to the next
            // level, and only when AdsManager's cadence says so. Never on a death.
            var ads = PullTheWorld.Ads.AdsManager.Instance;
            int idx = levels ? levels.CurrentIndex : 0;
            System.Action advance = () => { GoTo(Screen.Playing); if (levels) levels.Next(); };
            if (ads) ads.TryShowInterstitial(idx, advance); else advance();
        }

        // ---------------------------------------------------------------------- ads ----------
        /// <summary>True while the HUD is showing the rewarded skip. Read by the tests.</summary>
        public bool SkipOffered => skipButton && skipButton.gameObject.activeSelf;

        void RefreshSkipOffer()
        {
            if (!skipButton) return;
            var ads = PullTheWorld.Ads.AdsManager.Instance;
            bool offer = screen == Screen.Playing && levels && ads && ads.CanOfferSkip(levels.FailsOnLevel);
            if (offer && !skipButton.gameObject.activeSelf) PunchOn(skipButton.transform, 1f);
            skipButton.gameObject.SetActive(offer);
        }

        void OnSkipLevel()
        {
            Click();
            var ads = PullTheWorld.Ads.AdsManager.Instance;
            if (!ads || !levels) return;
            ads.ShowRewarded(PullTheWorld.Ads.AdPlacement.SkipLevel,
                onReward: () => { skipButton.gameObject.SetActive(false); levels.Next(); });
        }

        // -------------------------------------------------------------- level select ---------
        void OpenLevelSelect()
        {
            Click();
            if (!levelSelect || !levelGrid || !levelTileTemplate || !levels) return;

            // Rebuild the grid each time: it is cheap and it always reflects current progress.
            for (int i = levelGrid.childCount - 1; i >= 0; i--)
            {
                var child = levelGrid.GetChild(i);
                if (child != levelTileTemplate.transform) Destroy(child.gameObject);
            }

            int unlocked = GameProgress.UnlockedIndex;
            for (int i = 0; i < levels.LevelCount; i++)
            {
                var tile = Instantiate(levelTileTemplate, levelGrid);
                tile.gameObject.SetActive(true);
                tile.name = $"Level {i + 1}";
                bool open = i <= unlocked;
                var label = tile.GetComponentInChildren<TMP_Text>();
                if (label) label.text = (i + 1).ToString();
                var img = tile.GetComponent<Image>();
                if (img) img.color = open ? img.color : new Color(img.color.r, img.color.g, img.color.b, 0.28f);
                if (label && !open) label.color = new Color(1f, 1f, 1f, 0.35f);
                tile.interactable = open;
                int index = i;
                tile.onClick.AddListener(() =>
                {
                    Click();
                    GoTo(Screen.Playing);
                    levels.LoadLevel(index);
                });
            }
            levelSelect.SetVisible(true);
        }

        void CloseLevelSelect()
        {
            Click();
            if (levelSelect) levelSelect.SetVisible(false);
        }

        void OnUnlockAll()
        {
            Click();
            if (!levels) return;
            GameProgress.UnlockedIndex = Mathf.Max(0, levels.LevelCount - 1);
            RefreshProgressLabel();
            var label = unlockAllButton ? unlockAllButton.GetComponentInChildren<TMP_Text>() : null;
            if (label) label.text = $"ALL {levels.LevelCount} UNLOCKED";
            PunchOn(unlockAllButton ? unlockAllButton.transform : null, 0.8f);
        }

        // ------------------------------------------------------------------- settings -------
        void OpenSettings()
        {
            Click();
            PullSettingsIntoToggles();
            SetRestartArmed(false);
            if (settings) settings.Show();

            // Rotation input has to stop or a drag behind the overlay still turns the world - and
            // the clock has to stop or the ball keeps rolling into hazards while the player is
            // busy toggling haptics.
            if (WorldRotator.Instance) WorldRotator.Instance.CancelDrive();
            SetRotationInput(false);
            if (screen == Screen.Playing) SetPaused(true);
        }

        void CloseSettings()
        {
            Click();
            SetRestartArmed(false);
            if (settings) settings.Hide();
            SetPaused(false);
            SetRotationInput(screen == Screen.Playing);
        }

        void SetPaused(bool on)
        {
            if (paused == on) return;
            paused = on;
            Time.timeScale = on ? 0f : 1f;
            PtwMusic.SetDucked(on);
        }

        void SetRotationInput(bool on)
        {
            var input = FindFirstObjectByType<RotateInput>();
            if (input) input.InputEnabled = on;
        }

        /// <summary>
        /// Two-tap wipe of saved progress. First tap arms and relabels, second tap does it.
        /// Runs on realtime because settings can be open while the game is paused.
        /// </summary>
        void OnRestartAll()
        {
            Click();

            if (!restartArmed)
            {
                SetRestartArmed(true);
                if (disarm != null) StopCoroutine(disarm);
                disarm = StartCoroutine(DisarmAfter(restartArmSeconds));
                return;
            }

            if (disarm != null) { StopCoroutine(disarm); disarm = null; }
            SetRestartArmed(false);

            GameProgress.ResetProgress();
            Haptics.Play(HapticKind.Break);

            if (settings) settings.Hide();
            SetPaused(false);

            // From the menu, resetting is enough - the next PLAY starts at level 1. Mid-game,
            // actually go there now; leaving them on level 14 with the counter reading 0 would
            // look like the button did nothing.
            if (screen == Screen.Playing || screen == Screen.LevelComplete)
            {
                GoTo(Screen.Playing);
                if (levels) levels.LoadLevel(0);
            }
            else
            {
                RefreshProgressLabel();
            }
        }

        IEnumerator DisarmAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            disarm = null;
            SetRestartArmed(false);
        }

        void SetRestartArmed(bool armed)
        {
            restartArmed = armed;
            if (restartAllLabel) restartAllLabel.text = armed ? RestartAllArmed : RestartAllIdle;
        }

        void SetSetting(System.Action apply)
        {
            if (suppressToggleCallbacks) return;
            apply();
            Click();
        }

        void PullSettingsIntoToggles()
        {
            suppressToggleCallbacks = true;
            if (soundToggle) soundToggle.isOn = GameProgress.SoundOn;
            if (musicToggle) musicToggle.isOn = GameProgress.MusicOn;
            if (hapticsToggle) hapticsToggle.isOn = GameProgress.HapticsOn;
            suppressToggleCallbacks = false;
        }

        // -------------------------------------------------------------- level manager hooks --
        void HandleLevelLoaded(LevelDefinition def)
        {
            if (screen != Screen.Playing) GoTo(Screen.Playing);

            if (levelLabel) levelLabel.text = def ? $"LEVEL {def.number}" : "";
            if (levelTitle) levelTitle.text = def ? def.title.ToUpperInvariant() : "";
            PunchOn(levelLabel, 0.6f);
            SetRotationInput(true);
            if (onboarding) onboarding.Begin(def);
            RefreshSkipOffer();

            // First level of a chapter (or first level after the menu): a title card.
            int chapter = ChapterOf(levels ? levels.CurrentIndex : 0);
            if (chapter != shownChapter)
            {
                shownChapter = chapter;
                ShowChapterCard(chapter);
            }
        }

        // ------------------------------------------------------------------ chapter card ----
        int shownChapter = -1;
        SkyTheme sky;
        Coroutine cardRoutine;

        int ChapterOf(int levelIndex)
        {
            if (!sky) sky = FindFirstObjectByType<SkyTheme>();
            return sky && levels ? sky.ChapterFor(levelIndex, levels.LevelCount) : 0;
        }

        static string Roman(int n) => n switch
        {
            1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", _ => n.ToString(),
        };

        void ShowChapterCard(int chapter)
        {
            if (!chapterCard) return;
            if (chapterNumber) chapterNumber.text = $"CHAPTER {Roman(chapter + 1)}";
            if (chapterName) chapterName.text = sky ? sky.ChapterName(chapter).ToUpperInvariant() : "";
            if (cardRoutine != null) StopCoroutine(cardRoutine);
            cardRoutine = StartCoroutine(ChapterCardRoutine());
        }

        System.Collections.IEnumerator ChapterCardRoutine()
        {
            var rt = chapterCard.transform as RectTransform;
            float t = 0f, total = Mathf.Max(0.8f, chapterCardSeconds);
            const float fadeIn = 0.35f, fadeOut = 0.55f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Min(Mathf.Clamp01(t / fadeIn), Mathf.Clamp01((total - t) / fadeOut));
                chapterCard.alpha = Mathf.SmoothStep(0f, 1f, a);
                if (rt) rt.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, Mathf.Clamp01(t / (fadeIn * 1.6f)));
                yield return null;
            }
            chapterCard.alpha = 0f;
            cardRoutine = null;
        }

        // -------------------------------------------------------------------- gem flight ----
        /// <summary>A gem icon flies from a world point to the HUD counter, which punches when it lands.</summary>
        public void FlyKey(Vector3 worldPosition)
        {
            if (!flyIcon || !keyGroup) return;
            StartCoroutine(FlyRoutine(worldPosition));
        }

        System.Collections.IEnumerator FlyRoutine(Vector3 worldPosition)
        {
            var canvas = GetComponentInParent<Canvas>();
            var canvasRt = canvas ? canvas.transform as RectTransform : null;
            var cam = canvas ? canvas.worldCamera : null;
            if (!canvasRt || !Camera.main) yield break;

            Vector2 startScreen = Camera.main.WorldToScreenPoint(worldPosition);
            Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(cam, keyGroup.transform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, startScreen, cam, out var from);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, endScreen, cam, out var to);

            var rt = flyIcon.rectTransform;
            flyIcon.gameObject.SetActive(true);
            float t = 0f, total = Mathf.Max(0.1f, flySeconds);
            while (t < total)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / total);
                float e = k * k * (3f - 2f * k);                         // ease in-out
                Vector2 p = Vector2.Lerp(from, to, e);
                p.y += Mathf.Sin(k * Mathf.PI) * 90f;                    // a little arc
                rt.anchoredPosition = p;
                rt.localScale = Vector3.one * Mathf.Lerp(1.25f, 0.7f, e);
                yield return null;
            }
            flyIcon.gameObject.SetActive(false);
            PunchOn(keyGroup.transform, 1.1f);
        }

        void HandleKeysChanged(int collected, int required)
        {
            if (keyGroup) keyGroup.SetActive(required > 0);
            if (keyLabel && required > 0) keyLabel.text = $"{collected}/{required}";
            if (collected > 0) PunchOn(keyGroup ? keyGroup.transform : null, 0.9f);
        }

        static void PunchOn(Component c, float strength)
        {
            if (!c) return;
            var p = c.GetComponent<Punch>();
            if (p) p.Hit(strength);
        }

        void HandleWon(LevelDefinition def)
        {
            flash = winFlash;

            bool last = levels && levels.CurrentIndex + 1 >= levels.LevelCount;

            // No deaths on the way through: say so. Consecutive flawless levels build a streak,
            // which is the cheapest replay hook a level game has.
            bool flawless = levels && levels.FailsOnLevel == 0;
            flawlessStreak = flawless ? flawlessStreak + 1 : 0;
            if (completeTitle)
                completeTitle.text = last ? "ALL LEVELS DONE" : flawless ? "FLAWLESS!" : "LEVEL COMPLETE";
            if (completeSubtitle)
            {
                string where = levels ? $"LEVEL {levels.CurrentIndex + 1} OF {levels.LevelCount}" : "";
                completeSubtitle.text = flawlessStreak >= 2 ? $"{where}   ·   {flawlessStreak} FLAWLESS IN A ROW" : where;
            }
            if (flawless) PtwAudio.Play(PtwSfx.Win, 0.6f, 1.25f);   // a brighter chime on top of the usual one
            PullTheWorld.Ads.AdsManager.Instance?.ReportWin();
            if (skipButton) skipButton.gameObject.SetActive(false);

            if (onboarding) onboarding.Stop();
            if (celebrationVfx) celebrationVfx.Play();
            GoTo(Screen.LevelComplete);
            RefreshProgressLabel();
        }

        void HandleFailed(LevelDefinition def)
        {
            flash = failFlash;
            if (onboarding) onboarding.Stop();
            RefreshSkipOffer();
        }

        int flawlessStreak;

        void RefreshProgressLabel()
        {
            if (!progressLabel) return;
            int total = levels ? levels.LevelCount : 0;
            int done = Mathf.Clamp(GameProgress.UnlockedIndex, 0, total);
            progressLabel.text = total > 0
                ? (done >= total ? "ALL LEVELS CLEARED" : $"LEVEL {done + 1}")
                : "";
        }

        static void Click()
        {
            PtwAudio.Play(PtwSfx.UiTap, 0.7f, Random.Range(0.97f, 1.03f));
            Haptics.Play(HapticKind.Pickup);
        }

        void Update()
        {
            if (flash.a > 0.001f)
            {
                flash.a = Mathf.MoveTowards(flash.a, 0f, flashFade * Time.unscaledDeltaTime);
                if (flashImage) flashImage.color = flash;
            }
        }
    }
}
