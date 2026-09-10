using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PullTheWorld
{
    /// <summary>
    /// Minimal HUD: which level you are on, a restart button, one line of teaching text, and a
    /// clean win moment. Nothing else earns its place on a phone screen.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Header("Top bar")]
        [SerializeField] TMP_Text levelLabel;
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] Button restartButton;

        [Header("Hint")]
        [SerializeField] CanvasGroup hintGroup;
        [SerializeField] TMP_Text hintLabel;
        [Tooltip("Hint fades out this long after the player first grabs the world.")]
        [SerializeField] float hintHideDelay = 0.6f;
        [SerializeField] float hintAutoHide = 9f;

        [Header("Gesture nudge")]
        [Tooltip("Animated dot shown on the first level until the player drags.")]
        [SerializeField] RectTransform dragNudge;
        [SerializeField] CanvasGroup dragNudgeGroup;
        [SerializeField] float nudgeTravel = 130f;
        [SerializeField] float nudgePeriod = 2.1f;

        [Header("Twist tutorial")]
        [Tooltip("Two dots that rock back and forth, shown on the first level where rotation " +
                 "unlocks. Without it the twist gesture is undiscoverable.")]
        [SerializeField] RectTransform twistNudge;
        [SerializeField] CanvasGroup twistNudgeGroup;
        [SerializeField] float twistSwing = 26f;
        [SerializeField] float twistPeriod = 2.4f;

        [Header("Win")]
        [SerializeField] CanvasGroup completeGroup;
        [SerializeField] TMP_Text completeLabel;

        [Header("Flash")]
        [SerializeField] Image flashImage;
        [SerializeField] Color winFlash = new Color(1f, 0.95f, 0.75f, 0.5f);
        [SerializeField] Color failFlash = new Color(1f, 0.35f, 0.25f, 0.45f);
        [SerializeField] float flashFade = 2.6f;

        [Header("Refs")]
        [SerializeField] LevelManager levels;
        [SerializeField] WorldRig rig;

        float hintAlpha, hintTarget;
        float nudgeAlpha, nudgeTarget;
        float twistAlpha, twistTarget, twistT;
        float levelStartSpinTarget;
        float completeAlpha, completeTarget;
        Color flash = Color.clear;
        bool grabbedThisLevel;
        Vector2 nudgeHome;
        float nudgeT;

        void Awake()
        {
            if (!levels) levels = LevelManager.Instance ? LevelManager.Instance : FindFirstObjectByType<LevelManager>();
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();
            if (dragNudge) nudgeHome = dragNudge.anchoredPosition;
            if (restartButton) restartButton.onClick.AddListener(OnRestartPressed);
            SetImmediate(0f, 0f, 0f);
        }

        void OnEnable()
        {
            if (levels != null)
            {
                levels.OnLevelLoaded += HandleLevelLoaded;
                levels.OnLevelWon += HandleWon;
                levels.OnLevelFailed += HandleFailed;
            }
            if (rig != null) rig.OnGrabBegin += HandleGrab;
        }

        void OnDisable()
        {
            if (levels != null)
            {
                levels.OnLevelLoaded -= HandleLevelLoaded;
                levels.OnLevelWon -= HandleWon;
                levels.OnLevelFailed -= HandleFailed;
            }
            if (rig != null) rig.OnGrabBegin -= HandleGrab;
        }

        void SetImmediate(float hint, float nudge, float complete)
        {
            hintAlpha = hintTarget = hint;
            nudgeAlpha = nudgeTarget = nudge;
            completeAlpha = completeTarget = complete;
            Push();
        }

        void HandleLevelLoaded(LevelDefinition def)
        {
            grabbedThisLevel = false;
            StopAllCoroutines();

            if (levelLabel) levelLabel.text = def ? $"LEVEL {def.number}" : "";
            if (titleLabel) titleLabel.text = def ? def.title.ToUpperInvariant() : "";

            bool hasHint = def && !string.IsNullOrWhiteSpace(def.hint);
            if (hintLabel && hasHint) hintLabel.text = def.hint;

            hintTarget = hasHint ? 1f : 0f;
            nudgeTarget = def && def.number == 1 ? 1f : 0f;
            twistTarget = def && def.teachRotation ? 1f : 0f;
            // Compare against the TARGET spin, not the live one. The level intro deliberately
            // starts the world a few degrees off and springs it home, and reading the live spin
            // made that intro look like the player had already rotated - so the tutorial hid
            // itself instantly and the gesture stayed undiscoverable.
            levelStartSpinTarget = rig ? rig.SpinTarget : 0f;
            completeTarget = 0f;
            completeAlpha = 0f;

            if (hasHint && hintAutoHide > 0f) StartCoroutine(AutoHideHint());
        }

        IEnumerator AutoHideHint()
        {
            yield return new WaitForSeconds(hintAutoHide);
            hintTarget = 0f;
        }

        void HandleGrab(Vector3 _)
        {
            if (grabbedThisLevel) return;
            grabbedThisLevel = true;
            nudgeTarget = 0f;
            StartCoroutine(HideHintSoon());
        }

        IEnumerator HideHintSoon()
        {
            yield return new WaitForSeconds(hintHideDelay);
            hintTarget = 0f;
        }

        void HandleWon(LevelDefinition def)
        {
            if (completeLabel) completeLabel.text = "LEVEL COMPLETE";
            completeTarget = 1f;
            hintTarget = 0f;
            nudgeTarget = 0f;
            flash = winFlash;
            var player = FindFirstObjectByType<PlayerAnchor>();
            if (player) player.Celebrate();
            Haptics.Medium();
        }

        void HandleFailed(LevelDefinition def)
        {
            flash = failFlash;
            Haptics.Heavy();
        }

        void OnRestartPressed()
        {
            PtwAudio.Play(PtwSfx.PlateOff);
            Haptics.Light();
            if (levels) levels.Restart();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-9f * dt);

            hintAlpha = Mathf.Lerp(hintAlpha, hintTarget, k);
            nudgeAlpha = Mathf.Lerp(nudgeAlpha, nudgeTarget, k);
            completeAlpha = Mathf.Lerp(completeAlpha, completeTarget, k);

            // The twist prompt retires itself the moment the player actually twists.
            if (twistTarget > 0.5f && rig &&
                Mathf.Abs(rig.SpinTarget - levelStartSpinTarget) > 4f) twistTarget = 0f;
            twistAlpha = Mathf.Lerp(twistAlpha, twistTarget, k);
            if (twistNudge && twistAlpha > 0.01f)
            {
                twistT += dt / Mathf.Max(0.1f, twistPeriod);
                if (twistT > 1f) twistT -= 1f;
                float angle = Mathf.Sin(twistT * Mathf.PI * 2f) * twistSwing;
                twistNudge.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            if (twistNudgeGroup) twistNudgeGroup.alpha = twistAlpha;

            if (flash.a > 0.001f)
                flash.a = Mathf.MoveTowards(flash.a, 0f, flashFade * dt);

            if (dragNudge && nudgeAlpha > 0.01f)
            {
                nudgeT += dt / Mathf.Max(0.1f, nudgePeriod);
                if (nudgeT > 1f) nudgeT -= 1f;
                // Ease out and hold, so it reads as a deliberate swipe rather than a bouncing ball.
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nudgeT / 0.62f));
                float fade = Mathf.Clamp01(1f - Mathf.InverseLerp(0.62f, 1f, nudgeT));
                dragNudge.anchoredPosition = nudgeHome + new Vector2(0f, e * nudgeTravel);
                if (dragNudgeGroup) dragNudgeGroup.alpha = nudgeAlpha * fade;
            }

            Push();
        }

        void Push()
        {
            if (hintGroup) hintGroup.alpha = hintAlpha;
            if (completeGroup)
            {
                completeGroup.alpha = completeAlpha;
                completeGroup.transform.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, EaseOutBack(completeAlpha));
            }
            if (dragNudgeGroup && nudgeAlpha <= 0.01f) dragNudgeGroup.alpha = 0f;
            if (flashImage) flashImage.color = flash;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }
    }
}
