using System;

namespace PullTheWorld.Ads
{
    /// <summary>
    /// What the game needs from an ad network, and nothing more. One implementation per SDK
    /// (AdMob, LevelPlay, AppLovin...) plus <see cref="FakeAdProvider"/> for development builds.
    /// Every callback must arrive on the main thread; adapters are responsible for marshalling.
    /// </summary>
    public interface IAdProvider
    {
        string Name { get; }

        /// <summary>Start the SDK. <paramref name="ready"/> is called once with success/failure.</summary>
        void Initialize(Action<bool> ready);

        bool IsRewardedReady { get; }
        bool IsInterstitialReady { get; }

        /// <summary>Show a rewarded ad. <paramref name="completed"/> gets true only if the reward was earned.</summary>
        void ShowRewarded(Action<bool> completed);

        /// <summary>Show an interstitial. <paramref name="closed"/> is called when the player is back, always.</summary>
        void ShowInterstitial(Action closed);
    }
}
