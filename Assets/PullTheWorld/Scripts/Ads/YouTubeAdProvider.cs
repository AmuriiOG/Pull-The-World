using System;
using UnityEngine;

namespace PullTheWorld.Ads
{
    /// <summary>
    /// Ads through YouTube Playables (ytgame.ads), used only inside the Playables environment.
    /// AdsManager's cadence rules already match YouTube's requirements: rewarded ads are only ever
    /// offered behind the opt-in SKIP button and never block progress; interstitials only follow a
    /// win (a natural break), never during play. Monetization must also be configured in the
    /// Playables Developer Portal, or YouTube simply shows no ad.
    ///
    /// The SDK has no "is an ad ready?" query - a request is made and YouTube decides - so both
    /// placements report ready whenever no ad is in progress.
    /// </summary>
    public class YouTubeAdProvider : MonoBehaviour, IAdProvider
    {
        /// <summary>Same id for the same reward, never user data (YouTube's rule).</summary>
        public const string SkipLevelRewardId = "skip-level";

        bool busy;

        public string Name => "YouTube";
        public bool IsRewardedReady => Playables.InEnvironment && !busy;
        public bool IsInterstitialReady => Playables.InEnvironment && !busy;

        public void Initialize(Action<bool> ready) => ready?.Invoke(Playables.InEnvironment);

        public void ShowRewarded(Action<bool> completed)
        {
            if (busy) { completed?.Invoke(false); return; }
            busy = true;
            Debug.Log("PTW_YT requestRewardedAd " + SkipLevelRewardId);
            Playables.RequestRewarded(SkipLevelRewardId, earned =>
            {
                busy = false;
                // Only YouTube's explicit "earned" grants the reward; declined, failed or
                // unavailable ads all come back false.
                Debug.Log("PTW_YT rewarded earned=" + earned);
                completed?.Invoke(earned);
            });
        }

        public void ShowInterstitial(Action closed)
        {
            if (busy) { closed?.Invoke(); return; }
            busy = true;
            Debug.Log("PTW_YT requestInterstitialAd");
            Playables.RequestInterstitial(() =>
            {
                busy = false;
                Debug.Log("PTW_YT interstitial closed");
                closed?.Invoke();
            });
        }
    }
}
