using System;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The YouTube cloud-save format and the rules for combining it with this session. Pure
    /// functions, so the rules are unit-tested in the Editor (PlayablesSaveTests).
    ///
    /// Format, version 1: {"v":1,"unlocked":7,"sound":1,"music":1,"haptics":1}. Under 100 bytes,
    /// far inside YouTube's 3 MiB limit and its 64 KiB best-effort exit flush.
    ///
    /// The one rule that matters most: a save is never allowed to make the player's progress go
    /// backwards by accident. A cloud save that is missing, late, unreadable or from a newer game
    /// version must not be replaced with this session's defaults.
    /// </summary>
    public static class PlayablesSave
    {
        public const int Version = 1;

        [Serializable]
        class SaveV1
        {
            public int v = 0;
            public int unlocked = 0;
            public int sound = 1;
            public int music = 1;
            public int haptics = 1;
        }

        public enum ParseStatus
        {
            /// <summary>No save yet (a new player): defaults are correct and may be saved.</summary>
            Empty,
            /// <summary>A save this version understands.</summary>
            Ok,
            /// <summary>Not valid JSON. Keep it: never overwrite what we cannot read.</summary>
            Unreadable,
            /// <summary>Written by a newer game version. Keep it: never overwrite what we cannot read.</summary>
            NewerVersion,
        }

        public static readonly string[] Keys =
            { GameProgress.KeyUnlocked, GameProgress.KeySound, GameProgress.KeyMusic, GameProgress.KeyHaptics };

        public static Dictionary<string, int> Defaults() => new Dictionary<string, int>
        {
            { GameProgress.KeyUnlocked, 0 },
            { GameProgress.KeySound, 1 },
            { GameProgress.KeyMusic, 1 },
            { GameProgress.KeyHaptics, 1 },
        };

        public static ParseStatus Parse(string json, out Dictionary<string, int> values)
        {
            values = Defaults();
            if (string.IsNullOrWhiteSpace(json)) return ParseStatus.Empty;

            SaveV1 s;
            try { s = JsonUtility.FromJson<SaveV1>(json); }
            catch (Exception) { return ParseStatus.Unreadable; }
            if (s == null || !json.TrimStart().StartsWith("{")) return ParseStatus.Unreadable;
            if (s.v > Version) return ParseStatus.NewerVersion;

            // v == 0 (field missing) is read as version 1: there was never an older format.
            values[GameProgress.KeyUnlocked] = Mathf.Max(0, s.unlocked);
            values[GameProgress.KeySound] = s.sound != 0 ? 1 : 0;
            values[GameProgress.KeyMusic] = s.music != 0 ? 1 : 0;
            values[GameProgress.KeyHaptics] = s.haptics != 0 ? 1 : 0;
            return ParseStatus.Ok;
        }

        public static string Serialize(IReadOnlyDictionary<string, int> values)
        {
            int Get(string k, int d) => values.TryGetValue(k, out var x) ? x : d;
            return JsonUtility.ToJson(new SaveV1
            {
                v = Version,
                unlocked = Mathf.Max(0, Get(GameProgress.KeyUnlocked, 0)),
                sound = Get(GameProgress.KeySound, 1) != 0 ? 1 : 0,
                music = Get(GameProgress.KeyMusic, 1) != 0 ? 1 : 0,
                haptics = Get(GameProgress.KeyHaptics, 1) != 0 ? 1 : 0,
            });
        }

        /// <summary>
        /// Combines a freshly loaded cloud save with whatever this session changed before it
        /// arrived. Progress takes the furthest of the two - unless the player explicitly reset it
        /// this session - and a setting the player changed this session keeps the new value.
        /// Keys the session never touched come from the cloud.
        /// </summary>
        public static Dictionary<string, int> Merge(IReadOnlyDictionary<string, int> cloud,
                                                    IReadOnlyDictionary<string, int> session,
                                                    ICollection<string> changedThisSession,
                                                    bool progressResetThisSession)
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in cloud) result[kv.Key] = kv.Value;
            foreach (var key in changedThisSession)
            {
                if (!session.TryGetValue(key, out int mine)) continue;
                if (key == GameProgress.KeyUnlocked && !progressResetThisSession)
                    result[key] = Math.Max(mine, cloud.TryGetValue(key, out int theirs) ? theirs : 0);
                else
                    result[key] = mine;
            }
            return result;
        }
    }
}
