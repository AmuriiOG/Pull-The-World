using System;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>Where GameProgress keeps its numbers. PlayerPrefs everywhere except YouTube Playables.</summary>
    public interface IProgressStore
    {
        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        /// <summary>Persist pending changes (PlayerPrefs.Save, or a cloud save request).</summary>
        void Save();
    }

    /// <summary>The original storage, unchanged: Android, iOS, the Editor and local WebGL runs.</summary>
    public sealed class PlayerPrefsStore : IProgressStore
    {
        public int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>
    /// Everything that has to survive the app being closed: how far the player got, and the three
    /// audio/haptic toggles.
    ///
    /// PlayerPrefs and nothing more - except inside YouTube Playables, where Playables swaps in a
    /// cloud store (YouTube forbids any other save mechanism there). Reads are cached in statics
    /// so the UI can poll these every frame without touching the store.
    /// </summary>
    public static class GameProgress
    {
        public const string KeyUnlocked = "ptw.unlocked";
        public const string KeySound = "ptw.sound";
        public const string KeyMusic = "ptw.music";
        public const string KeyHaptics = "ptw.haptics";

        static int unlocked = -1;
        static int sound = -1, music = -1, haptics = -1;
        static IProgressStore store = new PlayerPrefsStore();

        /// <summary>Fires whenever a setting changes so audio and haptics can react immediately.</summary>
        public static event Action OnSettingsChanged;

        /// <summary>Fires when the store's contents were replaced wholesale (a cloud save arriving).</summary>
        public static event Action OnReloaded;

        public static IProgressStore Store => store;

        /// <summary>Swap the backing store (Playables at boot). Drops the cached reads.</summary>
        public static void UseStore(IProgressStore s)
        {
            store = s ?? new PlayerPrefsStore();
            ClearCache();
        }

        /// <summary>Called by a store whose contents changed underneath the cache.</summary>
        public static void NotifyReloaded()
        {
            ClearCache();
            OnSettingsChanged?.Invoke();
            OnReloaded?.Invoke();
        }

        // ------------------------------------------------------------------- progression ----
        /// <summary>Highest level index the player is allowed to start. 0 = only level 1.</summary>
        public static int UnlockedIndex
        {
            get
            {
                if (unlocked < 0) unlocked = store.GetInt(KeyUnlocked, 0);
                return unlocked;
            }
            set
            {
                int v = Mathf.Max(0, value);
                if (v == UnlockedIndex) return;
                unlocked = v;
                store.SetInt(KeyUnlocked, v);
                store.Save();
            }
        }

        /// <summary>Call on a win. Only ever moves forward.</summary>
        public static void ReportCleared(int levelIndex)
        {
            if (levelIndex + 1 > UnlockedIndex) UnlockedIndex = levelIndex + 1;
        }

        public static void ResetProgress()
        {
            unlocked = 0;
            store.SetInt(KeyUnlocked, 0);
            store.Save();
        }

        // ---------------------------------------------------------------------- settings ----
        public static bool SoundOn
        {
            get { if (sound < 0) sound = store.GetInt(KeySound, 1); return sound != 0; }
            set => SetFlag(KeySound, ref sound, value);
        }

        public static bool MusicOn
        {
            get { if (music < 0) music = store.GetInt(KeyMusic, 1); return music != 0; }
            set => SetFlag(KeyMusic, ref music, value);
        }

        public static bool HapticsOn
        {
            get { if (haptics < 0) haptics = store.GetInt(KeyHaptics, 1); return haptics != 0; }
            set => SetFlag(KeyHaptics, ref haptics, value);
        }

        static void SetFlag(string key, ref int cache, bool value)
        {
            int v = value ? 1 : 0;
            if (cache == v) return;
            cache = v;
            store.SetInt(key, v);
            store.Save();
            OnSettingsChanged?.Invoke();
        }

        /// <summary>Used by the tests, which must not inherit the developer's saved state.</summary>
        public static void ClearCache()
        {
            unlocked = sound = music = haptics = -1;
        }
    }
}
