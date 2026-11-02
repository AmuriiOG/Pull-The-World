using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace PullTheWorld
{
    /// <summary>
    /// The game's only door to the YouTube Playables SDK (the `ytgame` JavaScript API, through
    /// Plugins/WebGL/PtwPlayables.jslib).
    ///
    /// Outside a WebGL player - Android, iOS, the Editor - every member compiles to a stub:
    /// <see cref="InEnvironment"/> is false, nothing is created, and the game behaves exactly as it
    /// always has. In a WebGL player served outside YouTube the SDK reports IN_PLAYABLES_ENV false,
    /// with the same result. Only inside YouTube does this boot the cloud save, pause/resume and
    /// audio handling (<see cref="PlayablesHost"/>) and the YouTube ad provider.
    /// </summary>
    public static class Playables
    {
        public enum LoadState { Pending = 0, Loaded = 1, Failed = 2, Unavailable = 3 }

        /// <summary>Result codes from the bridge.</summary>
        public const int Ok = 1, NotEarned = 0, Unavailable = -1, InvalidParams = -2, SizeLimit = -3, Unknown = -4;

        public static bool InEnvironment { get; private set; }
        /// <summary>YouTube's own audio setting / mute button. Always true outside YouTube.</summary>
        public static bool AudioEnabled { get; private set; } = true;

#pragma warning disable CS0067   // raised only by the WebGL bridge; unused in Editor/Android builds
        public static event Action Paused;
        public static event Action Resumed;
#pragma warning restore CS0067

        static readonly Dictionary<int, Action<int>> pending = new Dictionary<int, Action<int>>();
        static int nextRequest = 1;

#if UNITY_WEBGL && !UNITY_EDITOR
        delegate void VoidCallback();
        delegate void IntCallback(int value);
        delegate void ResultCallback(int requestId, int code);

        [DllImport("__Internal")] static extern int PtwYt_Init(VoidCallback onPause, VoidCallback onResume, IntCallback onAudio, ResultCallback onResult);
        [DllImport("__Internal")] static extern int PtwYt_InEnv();
        [DllImport("__Internal")] static extern int PtwYt_AudioEnabled();
        [DllImport("__Internal")] static extern void PtwYt_GameReady();
        [DllImport("__Internal")] static extern int PtwYt_LoadState();
        [DllImport("__Internal")] static extern string PtwYt_LoadedData();
        [DllImport("__Internal")] static extern void PtwYt_RetryLoad();
        [DllImport("__Internal")] static extern void PtwYt_Save(int requestId, string json);
        [DllImport("__Internal")] static extern void PtwYt_RequestRewarded(int requestId, string rewardId);
        [DllImport("__Internal")] static extern void PtwYt_RequestInterstitial(int requestId);
        [DllImport("__Internal")] static extern void PtwYt_LogWarning();
        [DllImport("__Internal")] static extern void PtwYt_LogError();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            InEnvironment = PtwYt_Init(OnPauseNative, OnResumeNative, OnAudioNative, OnResultNative) == 1;
            if (!InEnvironment)
            {
                Debug.Log("PTW_YT not in the Playables environment: normal WebGL behaviour.");
                return;
            }
            AudioEnabled = PtwYt_AudioEnabled() == 1;
            ApplyAudio();
            GameProgress.UseStore(new CloudProgressStore());
            var host = new GameObject("PtwPlayables").AddComponent<PlayablesHost>();
            UnityEngine.Object.DontDestroyOnLoad(host.gameObject);
            Debug.Log($"PTW_YT boot inEnv=1 audio={(AudioEnabled ? 1 : 0)}");
        }

        [MonoPInvokeCallback(typeof(VoidCallback))]
        static void OnPauseNative()
        {
            try { Paused?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        [MonoPInvokeCallback(typeof(VoidCallback))]
        static void OnResumeNative()
        {
            try { Resumed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        [MonoPInvokeCallback(typeof(IntCallback))]
        static void OnAudioNative(int enabled)
        {
            AudioEnabled = enabled != 0;
            ApplyAudio();
            Debug.Log($"PTW_YT audio={enabled}");
        }

        [MonoPInvokeCallback(typeof(ResultCallback))]
        static void OnResultNative(int requestId, int code)
        {
            if (!pending.TryGetValue(requestId, out var done)) return;
            pending.Remove(requestId);
            try { done?.Invoke(code); } catch (Exception e) { Debug.LogException(e); }
        }
#endif

        // YouTube's mute is a master volume, separate from the game's own SOUND and MUSIC switches
        // (YouTube allows those as granular controls; it only forbids an overall in-game mute).
        static void ApplyAudio() => AudioListener.volume = AudioEnabled ? 1f : 0f;

        static int Track(Action<int> done)
        {
            int id = nextRequest++;
            pending[id] = done;
            return id;
        }

        public static void GameReady()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) PtwYt_GameReady();
#endif
        }

        public static LoadState CurrentLoadState
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (InEnvironment) return (LoadState)PtwYt_LoadState();
#endif
                return LoadState.Unavailable;
            }
        }

        public static string LoadedData
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (InEnvironment) return PtwYt_LoadedData() ?? "";
#endif
                return "";
            }
        }

        public static void RetryLoad()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) PtwYt_RetryLoad();
#endif
        }

        /// <summary>Cloud save. <paramref name="done"/> gets <see cref="Ok"/> or a negative error code.</summary>
        public static void Save(string json, Action<int> done)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) { PtwYt_Save(Track(done), json); return; }
#endif
            done?.Invoke(Unavailable);
        }

        /// <summary>
        /// Rewarded ad. <paramref name="earned"/> is true ONLY when YouTube confirms the reward was
        /// earned; a declined, skipped, failed or unavailable ad is always false.
        /// </summary>
        public static void RequestRewarded(string rewardId, Action<bool> earned)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) { PtwYt_RequestRewarded(Track(code => earned?.Invoke(code == Ok)), rewardId); return; }
#endif
            earned?.Invoke(false);
        }

        /// <summary>Interstitial. <paramref name="closed"/> always runs, whether or not an ad showed.</summary>
        public static void RequestInterstitial(Action closed)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) { PtwYt_RequestInterstitial(Track(_ => closed?.Invoke())); return; }
#endif
            closed?.Invoke();
        }

        public static void LogWarning()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) PtwYt_LogWarning();
#endif
        }

        public static void LogError()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (InEnvironment) PtwYt_LogError();
#endif
        }
    }
}
