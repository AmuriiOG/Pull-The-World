using UnityEngine;

namespace PullTheWorld
{
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
