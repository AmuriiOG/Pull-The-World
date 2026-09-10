using System;
using UnityEngine;

namespace PullTheWorld.Ads
{
    public enum AdPlacement
    {
        /// <summary>Offered only after several failures on one level. Watching it advances to the next level.</summary>
        SkipLevel,
        /// <summary>Reserved: shows the level's first tilt as a ghost gesture. Needs authored hints per level.</summary>
        Hint,
    }

    /// <summary>
    /// The one place the game talks to ads. Owns the provider, the cadence rules and the pause
    /// around a showing, so gameplay code only ever says "offer a skip?" or "we just won, is it
    /// time?". Swap the network by swapping <see cref="IAdProvider"/>; nothing else changes.
    ///
    /// The rules are the design, not a detail:
    ///  * Interstitials only ever follow a WIN, never a death. A death is already the low point;
    ///    the restart has to be instant (that was a user complaint) and an ad there is how a
    ///    hyper-casual game gets uninstalled.
    ///  * No interstitial before <see cref="firstInterstitialLevel"/>. The first minutes are for
    ///    learning the verb, and retention is decided there.
    ///  * At most one interstitial per <see cref="minSecondsBetweenInterstitials"/> and one per
    ///    <see cref="interstitialEveryNWins"/> wins, whichever is later.
    ///  * Rewarded ads are only ever OFFERED, never forced, and only when they help: the skip
    ///    appears after <see cref="skipAfterFails"/> failures on the same level, which is exactly
    ///    when a stuck player would otherwise quit. An ad that keeps someone playing is the one
    ///    kind that improves the game.
    ///  * <see cref="AdsRemoved"/> (a future "remove ads" purchase) turns off interstitials only;
    ///    rewarded offers stay, because they are a favour to the player.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class AdsManager : MonoBehaviour
    {
        public static AdsManager Instance { get; private set; }

        [Header("Cadence")]
        [Tooltip("Zero-based level index before which no interstitial is ever shown.")]
        [SerializeField] int firstInterstitialLevel = 5;
        [SerializeField] int interstitialEveryNWins = 3;
        [SerializeField] float minSecondsBetweenInterstitials = 150f;
        [Tooltip("Failures on one level before the rewarded skip is offered.")]
        [SerializeField] int skipAfterFails = 3;

        [Header("Provider")]
        [Tooltip("Use the on-screen fake provider when no real SDK adapter is present. Leave on for " +
                 "development; a release build should have a real adapter added below.")]
        [SerializeField] bool fakeWhenNoProvider = true;

        const string KeyNoAds = "ptw.noads";

        IAdProvider provider;
        bool ready;
        int winsSinceInterstitial;
        float lastInterstitialTime = float.NegativeInfinity;
        bool showing;
        float pausedScale = 1f;

        /// <summary>Fired after any ad closes, with the placement or "interstitial". Analytics hook.</summary>
        public event Action<string> OnAdClosed;

        public bool Ready => ready && provider != null;
        public bool Showing => showing;
        public string ProviderName => provider?.Name ?? "none";
        public int SkipAfterFails => skipAfterFails;

        public static bool AdsRemoved
        {
            get => PlayerPrefs.GetInt(KeyNoAds, 0) != 0;
            set { PlayerPrefs.SetInt(KeyNoAds, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        void Awake()
        {
            Instance = this;
            provider = GetComponent<IAdProvider>();
            if (provider == null && fakeWhenNoProvider) provider = gameObject.AddComponent<FakeAdProvider>();
            provider?.Initialize(ok => ready = ok);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Never leave the game frozen because we vanished mid-ad (scene reload, app teardown).
            if (showing) { Time.timeScale = pausedScale <= 0f ? 1f : pausedScale; AudioListener.pause = false; showing = false; }
        }

        /// <summary>Install a real network adapter (call before Awake runs, or replace at runtime).</summary>
        public void SetProvider(IAdProvider p)
        {
            provider = p;
            ready = false;
            provider?.Initialize(ok => ready = ok);
        }

        // ---------------------------------------------------------------------- rewarded -----
        /// <summary>Should the HUD offer a skip right now?</summary>
        public bool CanOfferSkip(int failsOnLevel) =>
            Ready && !showing && failsOnLevel >= skipAfterFails && provider.IsRewardedReady;

        /// <summary>Show a rewarded ad for a placement. <paramref name="onReward"/> only if it was earned.</summary>
        public void ShowRewarded(AdPlacement placement, Action onReward, Action onNoReward = null)
        {
            if (!Ready || showing || !provider.IsRewardedReady) { onNoReward?.Invoke(); return; }
            BeginShow();
            provider.ShowRewarded(earned =>
            {
                EndShow(placement.ToString());
                if (earned) onReward?.Invoke(); else onNoReward?.Invoke();
            });
        }

        // ------------------------------------------------------------------ interstitial -----
        /// <summary>Pure cadence rule, exposed for tests.</summary>
        public bool IsInterstitialDue(int levelIndex, int wins, float secondsSinceLast) =>
            !AdsRemoved
            && levelIndex >= firstInterstitialLevel
            && wins >= interstitialEveryNWins
            && secondsSinceLast >= minSecondsBetweenInterstitials;

        /// <summary>Call on a win, before advancing.</summary>
        public void ReportWin() => winsSinceInterstitial++;

        /// <summary>
        /// Show an interstitial if one is due, then continue; otherwise continue immediately.
        /// Returns true if an ad was shown.
        /// </summary>
        public bool TryShowInterstitial(int levelIndex, Action then)
        {
            float since = Time.unscaledTime - lastInterstitialTime;
            if (!Ready || showing || !provider.IsInterstitialReady
                || !IsInterstitialDue(levelIndex, winsSinceInterstitial, since))
            {
                then?.Invoke();
                return false;
            }

            winsSinceInterstitial = 0;
            lastInterstitialTime = Time.unscaledTime;
            BeginShow();
            provider.ShowInterstitial(() =>
            {
                EndShow("interstitial");
                then?.Invoke();
            });
            return true;
        }

        // -------------------------------------------------------------------- pausing --------
        void BeginShow()
        {
            showing = true;
            pausedScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        void EndShow(string what)
        {
            Time.timeScale = pausedScale <= 0f ? 1f : pausedScale;
            AudioListener.pause = false;
            showing = false;
            OnAdClosed?.Invoke(what);
        }
    }
}
