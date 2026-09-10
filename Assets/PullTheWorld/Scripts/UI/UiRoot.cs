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
        [SerializeField] TMP_Text keyLabel;
        [SerializeField] GameObject keyGroup;

        [Header("Level complete widgets")]
        [SerializeField] Button continueButton;
        [SerializeField] TMP_Text completeTitle;
        [SerializeField] ParticleSystem celebrationVfx;

        [Header("Settings widgets")]
        [SerializeField] Button closeSettingsButton;
        [SerializeField] Toggle soundToggle;
        [SerializeField] Toggle musicToggle;
        [SerializeField] Toggle hapticsToggle;

        [Header("Screen flash")]
        [SerializeField] Image flashImage;
        [SerializeField] Color winFlash = new Color(1f, 0.95f, 0.75f, 0.45f);
        [SerializeField] Color failFlash = new Color(1f, 0.35f, 0.25f, 0.42f);
        [SerializeField] float flashFade = 2.6f;

        [Header("Refs")]
        [SerializeField] LevelManager levels;
        [SerializeField] OnboardingHint onboarding;

        Screen screen = Screen.MainMenu;
        Color flash = Color.clear;
        bool suppressToggleCallbacks;

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
        }

        void Start()
        {
            PullSettingsIntoToggles();
            GoTo(Screen.MainMenu, immediate: true);
        }

        // ----------------------------------------------------------------------- flow -------
        void GoTo(Screen s, bool immediate = false)
        {
            screen = s;

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

        void OpenSettings()
        {
            Click();
            PullSettingsIntoToggles();
            if (settings) settings.Show();
            // Rotation input has to stop or a drag behind the overlay still turns the world.
            if (WorldRotator.Instance) WorldRotator.Instance.CancelDrive();
            SetRotationInput(false);
        }

        void CloseSettings()
        {
            Click();
            if (settings) settings.Hide();
            SetRotationInput(screen == Screen.Playing);
        }

        void SetRotationInput(bool on)
        {
            var input = FindFirstObjectByType<RotateInput>();
            if (input) input.InputEnabled = on;
        }

        // -------------------------------------------------------------- level manager hooks --
        void HandleLevelLoaded(LevelDefinition def)
        {
            if (screen != Screen.Playing) GoTo(Screen.Playing);

            if (levelLabel) levelLabel.text = def ? $"LEVEL {def.number}" : "";
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
            if (completeTitle)
                completeTitle.text = levels && levels.CurrentIndex + 1 >= levels.LevelCount
                    ? "ALL LEVELS DONE"
                    : "LEVEL COMPLETE";

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

        // ------------------------------------------------------------------- settings -------
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

        void RefreshProgressLabel()
        {
            if (!progressLabel) return;
            int total = levels ? levels.LevelCount : 0;
            int done = Mathf.Clamp(GameProgress.UnlockedIndex, 0, total);
            progressLabel.text = total > 0 ? $"{done} / {total}" : "";
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
