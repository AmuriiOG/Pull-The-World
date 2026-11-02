using System.Collections.Generic;
using NUnit.Framework;

namespace PullTheWorld.Tests
{
    /// <summary>
    /// The YouTube cloud-save rules (PlayablesSave). The one that matters most: a save can never
    /// make a player's progress go backwards by accident.
    /// </summary>
    public class PlayablesSaveTests
    {
        static Dictionary<string, int> Values(int unlocked, int sound = 1, int music = 1, int haptics = 1) =>
            new Dictionary<string, int>
            {
                { GameProgress.KeyUnlocked, unlocked }, { GameProgress.KeySound, sound },
                { GameProgress.KeyMusic, music }, { GameProgress.KeyHaptics, haptics },
            };

        [Test]
        public void EmptyOrMissingSave_IsANewPlayer()
        {
            Assert.AreEqual(PlayablesSave.ParseStatus.Empty, PlayablesSave.Parse("", out var v));
            Assert.AreEqual(0, v[GameProgress.KeyUnlocked]);
            Assert.AreEqual(PlayablesSave.ParseStatus.Empty, PlayablesSave.Parse(null, out _));
            Assert.AreEqual(PlayablesSave.ParseStatus.Empty, PlayablesSave.Parse("   ", out _));
        }

        [Test]
        public void RoundTrip_PreservesEverything()
        {
            var original = Values(17, sound: 0, music: 1, haptics: 0);
            string json = PlayablesSave.Serialize(original);
            Assert.AreEqual(PlayablesSave.ParseStatus.Ok, PlayablesSave.Parse(json, out var back));
            CollectionAssert.AreEquivalent(original, back);
            StringAssert.Contains("\"v\":1", json);
            Assert.Less(json.Length, 512, "a save must stay tiny (YouTube SHOULD < 500 KiB, exit flush <= 64 KiB)");
        }

        [Test]
        public void MissingFields_FallBackToDefaults_AndOldSavesStillLoad()
        {
            Assert.AreEqual(PlayablesSave.ParseStatus.Ok, PlayablesSave.Parse("{\"unlocked\":4}", out var v));
            Assert.AreEqual(4, v[GameProgress.KeyUnlocked]);
            Assert.AreEqual(1, v[GameProgress.KeySound]);
            Assert.AreEqual(1, v[GameProgress.KeyMusic]);
        }

        [Test]
        public void UnknownExtraFields_AreTolerated()
        {
            Assert.AreEqual(PlayablesSave.ParseStatus.Ok, PlayablesSave.Parse("{\"v\":1,\"unlocked\":3,\"future\":\"x\"}", out var v));
            Assert.AreEqual(3, v[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void CorruptOrForeignData_IsUnreadable_NotDefaults()
        {
            Assert.AreEqual(PlayablesSave.ParseStatus.Unreadable, PlayablesSave.Parse("{not json", out _));
            Assert.AreEqual(PlayablesSave.ParseStatus.Unreadable, PlayablesSave.Parse("42", out _));
            Assert.AreEqual(PlayablesSave.ParseStatus.Unreadable, PlayablesSave.Parse("[1,2]", out _));
        }

        [Test]
        public void NewerVersion_IsNotTouched()
        {
            Assert.AreEqual(PlayablesSave.ParseStatus.NewerVersion, PlayablesSave.Parse("{\"v\":2,\"unlocked\":30}", out _));
        }

        [Test]
        public void NegativeProgress_IsClamped()
        {
            PlayablesSave.Parse("{\"v\":1,\"unlocked\":-5}", out var v);
            Assert.AreEqual(0, v[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void LateLoad_NeverLowersProgress()
        {
            // The cloud says level 12; this session (load still pending) only reached level 2.
            var merged = PlayablesSave.Merge(Values(12), Values(2), new[] { GameProgress.KeyUnlocked }, false);
            Assert.AreEqual(12, merged[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void LateLoad_KeepsProgressMadeThisSession()
        {
            var merged = PlayablesSave.Merge(Values(3), Values(5), new[] { GameProgress.KeyUnlocked }, false);
            Assert.AreEqual(5, merged[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void UntouchedKeys_ComeFromTheCloud()
        {
            // Session defaults (sound on, level 0) must not overwrite what the cloud holds.
            var merged = PlayablesSave.Merge(Values(9, sound: 0), Values(0, sound: 1), new string[0], false);
            Assert.AreEqual(9, merged[GameProgress.KeyUnlocked]);
            Assert.AreEqual(0, merged[GameProgress.KeySound]);
        }

        [Test]
        public void SettingChangedThisSession_Wins()
        {
            var merged = PlayablesSave.Merge(Values(9, music: 1), Values(0, music: 0), new[] { GameProgress.KeyMusic }, false);
            Assert.AreEqual(0, merged[GameProgress.KeyMusic]);
            Assert.AreEqual(9, merged[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void ExplicitResetThisSession_IsHonoured()
        {
            var merged = PlayablesSave.Merge(Values(20), Values(0), new[] { GameProgress.KeyUnlocked }, true);
            Assert.AreEqual(0, merged[GameProgress.KeyUnlocked]);
        }

        [Test]
        public void DefaultStore_IsPlayerPrefs_SoAndroidAndEditorAreUnchanged()
        {
            Assert.IsInstanceOf<PlayerPrefsStore>(GameProgress.Store);
            Assert.IsFalse(Playables.InEnvironment);
        }
    }
}
