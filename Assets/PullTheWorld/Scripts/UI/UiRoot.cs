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

        void Awake()
        {
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

            if (immediate)
            {
                if (mainMenu) mainMenu.SetImmediate(menu);
                if (hud) hud.SetImmediate(playing);
                if (levelComplete) levelComplete.SetImmediate(complete);
                if (settings) settings.SetImmediate(false);
            }
            else
            {
                if (mainMenu) mainMenu.SetVisible(menu);
                if (hud) hud.SetVisible(playing);
                if (levelComplete) levelComplete.SetVisible(complete);
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
            GoTo(Screen.Playing);
            if (levels) levels.Next();
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
            SetRotationInput(true);
            if (onboarding) onboarding.Begin(def);
        }

        void HandleKeysChanged(int collected, int required)
        {
            if (keyGroup) keyGroup.SetActive(required > 0);
            if (keyLabel && required > 0) keyLabel.text = $"{collected}/{required}";
        }

        void HandleWon(LevelDefinition def)
        {
            flash = winFlash;

            bool last = levels && levels.CurrentIndex + 1 >= levels.LevelCount;
            if (completeTitle) completeTitle.text = last ? "ALL LEVELS DONE" : "LEVEL COMPLETE";
            if (completeSubtitle)
                completeSubtitle.text = levels
                    ? $"LEVEL {levels.CurrentIndex + 1} OF {levels.LevelCount}"
                    : "";

            if (onboarding) onboarding.Stop();
            if (celebrationVfx) celebrationVfx.Play();
            GoTo(Screen.LevelComplete);
            RefreshProgressLabel();
        }

        void HandleFailed(LevelDefinition def)
        {
            flash = failFlash;
            if (onboarding) onboarding.Stop();
        }

        void RefreshProgressLabel()
        {
            if (!progressLabel) return;
            int total = levels ? levels.LevelCount : 0;
            int done = Mathf.Clamp(GameProgress.UnlockedIndex, 0, total);
            progressLabel.text = total > 0
                ? (done >= total ? "ALL LEVELS CLEARED" : $"LEVEL {done + 1} OF {total}")
                : "";
        }

        static void Click()
        {
            PtwAudio.Play(PtwSfx.PlateOn, 0.55f, 1.5f);
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
