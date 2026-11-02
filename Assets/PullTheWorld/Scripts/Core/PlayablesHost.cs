using UnityEngine;
using UnityEngine.Rendering;

namespace PullTheWorld
{
    /// <summary>
    /// Lives only inside YouTube Playables (created by <see cref="Playables"/> at boot). Drives the
    /// cloud save, tells YouTube when the game is ready, and freezes/unfreezes the game around
    /// ytgame.system.onPause / onResume.
    /// </summary>
    public sealed class PlayablesHost : MonoBehaviour
    {
        [Tooltip("Call gameReady even if the cloud save has not answered by then (it keeps retrying).")]
        [SerializeField] float maxWaitForSave = 8f;

        bool gameReadySent;
        int readyFrames;
        float started;

        bool pausedByYouTube;
        float timeScaleBeforePause = 1f;
        bool listenerPausedBeforePause;

        CloudProgressStore Cloud => GameProgress.Store as CloudProgressStore;

        void Awake()
        {
            started = Time.realtimeSinceStartup;
            Playables.Paused += OnYouTubePause;
            Playables.Resumed += OnYouTubeResume;
        }

        void OnDestroy()
        {
            Playables.Paused -= OnYouTubePause;
            Playables.Resumed -= OnYouTubeResume;
        }

        void Update()
        {
            Cloud?.Tick(Time.unscaledDeltaTime);
            if (!gameReadySent) TryGameReady();
        }

        // gameReady only once the player can actually act: Unity's splash has finished, the main
        // menu exists, and the save has answered (so the menu shows the right level). Two clean
        // frames after that, so the menu has really been drawn.
        void TryGameReady()
        {
            bool saveSettled = Cloud == null || Cloud.Resolved
                               || Time.realtimeSinceStartup - started > maxWaitForSave;
            if (!SplashScreen.isFinished || UiRoot.Instance == null || !saveSettled)
            {
                readyFrames = 0;
                return;
            }
            if (++readyFrames < 2) return;
            gameReadySent = true;
            Playables.GameReady();
            Debug.Log($"PTW_YT gameReady t={Time.realtimeSinceStartup:F2}s save={Cloud?.Status}");
        }

        // Called from inside YouTube's onPause handler, just before the bridge stops Unity's main
        // loop and suspends its audio context: save now, then freeze the clock and the listener so
        // nothing has moved when the loop starts again.
        void OnYouTubePause()
        {
            if (pausedByYouTube) return;
            pausedByYouTube = true;
            Cloud?.FlushNow();
            timeScaleBeforePause = Time.timeScale;
            listenerPausedBeforePause = AudioListener.pause;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Debug.Log($"PTW_YT paused (timeScale was {timeScaleBeforePause}, listener paused {listenerPausedBeforePause})");
        }

#if DEVELOPMENT_BUILD
        // Development builds only - test hooks driven from the browser with
        // unityInstance.SendMessage("PtwPlayables", "<method>"). Never compiled into release.
        void DevRewarded()
        {
            var ads = Ads.AdsManager.Instance;
            if (!ads) { Debug.Log("PTW_YT dev no AdsManager"); return; }
            ads.ShowRewarded(Ads.AdPlacement.SkipLevel,
                () => Debug.Log("PTW_YT dev reward GRANTED"),
                () => Debug.Log("PTW_YT dev reward NOT granted"));
            StartCoroutine(ReportDuringAd());
        }

        void DevInterstitial()
        {
            var ads = Ads.AdsManager.Instance;
            if (!ads) { Debug.Log("PTW_YT dev no AdsManager"); return; }
            ads.DevShowInterstitialNow(() => Debug.Log($"PTW_YT dev interstitial done timeScale={Time.timeScale} listenerPaused={AudioListener.pause}"));
            StartCoroutine(ReportDuringAd());
        }

        System.Collections.IEnumerator ReportDuringAd()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            Debug.Log($"PTW_YT dev during ad timeScale={Time.timeScale} listenerPaused={AudioListener.pause} provider={Ads.AdsManager.Instance?.ProviderName}");
        }

        void DevState() =>
            Debug.Log($"PTW_YT dev state timeScale={Time.timeScale} listenerPaused={AudioListener.pause} volume={AudioListener.volume} unlocked={GameProgress.UnlockedIndex} provider={Ads.AdsManager.Instance?.ProviderName}");
#endif

        // The bridge has already restarted the main loop. Put back exactly what was there - the
        // settings panel, an ad or a hit-stop may have had the game paused already.
        void OnYouTubeResume()
        {
            if (!pausedByYouTube) return;
            pausedByYouTube = false;
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = listenerPausedBeforePause;
            Debug.Log($"PTW_YT resumed (timeScale {Time.timeScale}, listener paused {AudioListener.pause})");
        }
    }
}
