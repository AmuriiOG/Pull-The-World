using UnityEngine;

namespace PullTheWorld
{
    /// <summary>What just happened, rather than how hard to buzz. See Haptics.Play.</summary>
    public enum HapticKind
    {
        RotateTick,
        Pickup,
        Plate,
        Gate,
        Impact,
        Break,
        Win,
        Fail,
    }

    /// <summary>
    /// Very short haptic taps on Android. Deliberately not Handheld.Vibrate() - that is a ~500ms
    /// buzz and would feel awful on every rotation tick. No-ops everywhere else, including the
    /// editor, so nothing here can affect play mode or tests.
    /// </summary>
    public static class Haptics
    {
        public static bool Enabled = true;

        /// <summary>
        /// NEVER CALLED. It exists purely so Unity's Android manifest generator sees a
        /// Handheld.Vibrate() call in the IL and auto-adds android.permission.VIBRATE.
        ///
        /// The real haptics below go through android.os.Vibrator by reflection, which the
        /// generator cannot detect. The obvious fix - hand-writing
        /// Assets/Plugins/Android/AndroidManifest.xml - is a trap: that file REPLACES Unity's
        /// manifest rather than merging with it, so unless it declares exactly the right launcher
        /// activity for the project's Application Entry Point (GameActivity here, so
        /// UnityPlayerGameActivity, not UnityPlayerActivity) the APK installs with no launcher
        /// icon at all. This one-line hack avoids the whole problem.
        ///
        /// If a future build genuinely needs a custom manifest, generate it from
        /// Player Settings > Publishing Settings > Custom Main Manifest so it starts from Unity's
        /// correct template, then add permissions to that.
        /// </summary>
        static void ManifestPermissionHint()
        {
            Handheld.Vibrate();
        }

        public static void Light() => Tap(12, 55);
        public static void Medium() => Tap(22, 120);
        public static void Heavy() => Tap(38, 200);

        /// <summary>
        /// Semantic entry point. Call sites name the EVENT, not a strength, so the whole game can
        /// be re-tuned here rather than by hunting for Medium() calls. Also the single place the
        /// player's haptics setting is honoured.
        /// </summary>
        public static void Play(HapticKind kind)
        {
            if (!GameProgress.HapticsOn) return;

            switch (kind)
            {
                case HapticKind.RotateTick: Tap(8, 40); break;   // fires often - must stay tiny
                case HapticKind.Pickup: Tap(14, 80); break;
                case HapticKind.Plate: Tap(20, 110); break;
                case HapticKind.Gate: Tap(24, 130); break;
                case HapticKind.Impact: Tap(18, 100); break;
                case HapticKind.Break: Tap(30, 170); break;
                case HapticKind.Win: Tap(38, 200); break;
                case HapticKind.Fail: Tap(45, 220); break;
            }
        }

        /// <summary>Scaled impact tap, so a light bump and a heavy landing differ.</summary>
        public static void Impact(float strength01)
        {
            if (!GameProgress.HapticsOn) return;
            float s = Mathf.Clamp01(strength01);
            if (s < 0.15f) return;
            Tap((long)Mathf.Lerp(8f, 34f, s), (int)Mathf.Lerp(45f, 190f, s));
        }

        static void Tap(long milliseconds, int amplitude)
        {
            if (!Enabled) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;

                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        if (version.GetStatic<int>("SDK_INT") >= 26)
                        {
                            using (var fx = new AndroidJavaClass("android.os.VibrationEffect"))
                            using (var oneShot = fx.CallStatic<AndroidJavaObject>(
                                       "createOneShot", milliseconds, Mathf.Clamp(amplitude, 1, 255)))
                            {
                                vibrator.Call("vibrate", oneShot);
                            }
                        }
                        else
                        {
                            vibrator.Call("vibrate", milliseconds);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // A device without a vibrator is not worth a log spam.
            }
#endif
        }
    }
}
