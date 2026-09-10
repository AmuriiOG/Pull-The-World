using System;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Everything that has to survive the app being closed: how far the player got, and the three
    /// audio/haptic toggles.
    ///
    /// PlayerPrefs and nothing more. A vertical slice does not need a save system, and the moment
    /// one exists somebody has to migrate it. Reads are cached in statics so the UI can poll these
    /// every frame without touching the registry.
    /// </summary>
    public static class GameProgress
    {
        const string KeyUnlocked = "ptw.unlocked";
        const string KeySound = "ptw.sound";
        const string KeyMusic = "ptw.music";
        const string KeyHaptics = "ptw.haptics";

        static int unlocked = -1;
        static int sound = -1, music = -1, haptics = -1;

        /// <summary>Fires whenever a setting changes so audio and haptics can react immediately.</summary>
        public static event Action OnSettingsChanged;

        // ------------------------------------------------------------------- progression ----
        /// <summary>Highest level index the player is allowed to start. 0 = only level 1.</summary>
        public static int UnlockedIndex
        {
            get
            {
                if (unlocked < 0) unlocked = PlayerPrefs.GetInt(KeyUnlocked, 0);
                return unlocked;
            }
            set
            {
                int v = Mathf.Max(0, value);
                if (v == UnlockedIndex) return;
                unlocked = v;
                PlayerPrefs.SetInt(KeyUnlocked, v);
                PlayerPrefs.Save();
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
            PlayerPrefs.SetInt(KeyUnlocked, 0);
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------------- settings ----
        public static bool SoundOn
        {
            get { if (sound < 0) sound = PlayerPrefs.GetInt(KeySound, 1); return sound != 0; }
            set => SetFlag(KeySound, ref sound, value);
        }

        public static bool MusicOn
        {
            get { if (music < 0) music = PlayerPrefs.GetInt(KeyMusic, 1); return music != 0; }
            set => SetFlag(KeyMusic, ref music, value);
        }

        public static bool HapticsOn
        {
            get { if (haptics < 0) haptics = PlayerPrefs.GetInt(KeyHaptics, 1); return haptics != 0; }
            set => SetFlag(KeyHaptics, ref haptics, value);
        }

        static void SetFlag(string key, ref int cache, bool value)
        {
            int v = value ? 1 : 0;
            if (cache == v) return;
            cache = v;
            PlayerPrefs.SetInt(key, v);
            PlayerPrefs.Save();
            OnSettingsChanged?.Invoke();
        }

        /// <summary>Used by the tests, which must not inherit the developer's saved state.</summary>
        public static void ClearCache()
        {
            unlocked = sound = music = haptics = -1;
        }
    }
}
