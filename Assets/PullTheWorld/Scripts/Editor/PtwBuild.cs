using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// One-button regeneration of the entire game: materials, meshes, prefabs, levels and scene.
    /// Every step is idempotent, so a palette change is "run this again" rather than a manual
    /// pass over dozens of assets.
    /// </summary>
    public static class PtwBuild
    {
        [MenuItem("Pull The World/Build Everything", priority = 0)]
        public static void BuildEverything()
        {
            Bootstrap();
            BuildArt();
            BuildPrefabs();
            BuildLevels();
            BuildScene();
            Debug.Log("PTW: full build complete.");
        }

        [MenuItem("Pull The World/1. Bootstrap (fonts + TMP)", priority = 20)]
        public static void Bootstrap()
        {
            EnsureTmpEssentials();
            PtwGameView.TryAdd();
            EnsurePlayerSettings();
            PtwPaths.EnsureFolder(PtwArt.ArtRoot);
            PtwScene.EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-Bold.ttf", PtwScene.FontPath);
            PtwScene.EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-SemiBold.ttf", PtwScene.FontSemiPath);
            AssetDatabase.SaveAssets();
            Debug.Log("PTW: bootstrap done.");
        }

        [MenuItem("Pull The World/2. Rebuild Art (materials + meshes)", priority = 21)]
        public static void BuildArt()
        {
            PtwArt.BuildAll();
            PtwMeshes.BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log("PTW: art rebuilt.");
        }

        [MenuItem("Pull The World/3. Rebuild Prefabs", priority = 22)]
        public static void BuildPrefabs()
        {
            PtwPrefabs.BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log("PTW: prefabs rebuilt.");
        }

        [MenuItem("Pull The World/4. Rebuild Levels", priority = 23)]
        public static void BuildLevels()
        {
            PtwLevels.BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log("PTW: levels rebuilt.");
        }

        [MenuItem("Pull The World/5. Rebuild Scene", priority = 24)]
        public static void BuildScene()
        {
            PtwScene.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("PTW: scene rebuilt.");
        }

        /// <summary>
        /// Renders each chapter's music loop to a 16-bit WAV in Captures/, so the soundtrack can be
        /// auditioned in any player without launching the game - and so a headless run can at
        /// least measure it (length, peak, silence).
        /// </summary>
        [MenuItem("Pull The World/Render Music To WAV", priority = 41)]
        public static void RenderMusic()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            for (int c = 0; c < PtwMusic.ChapterCount; c++)
            {
                var data = PtwMusic.Render(c);
                string path = Path.Combine(dir, $"music_{c + 1}_{PtwMusic.SongName(c)}.wav");
                WriteWav(path, data, PtwMusic.SampleRate);
                Debug.Log($"PTW_WAV {path} seconds={data.Length / (float)PtwMusic.SampleRate:F1}");
            }
        }

        public static void BatchRenderMusic() => RunBatch(RenderMusic);

        static void WriteWav(string path, float[] samples, int rate)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int bytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var s in samples) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }

        /// <summary>
        /// Player settings the APK actually needs. The application identifier was empty, which
        /// leaves Unity falling back to a com.DefaultCompany.* package - fine for a sideload,
        /// but it is one more thing that makes a test build behave oddly, and it blocks a store
        /// upload later.
        /// </summary>
        static void EnsurePlayerSettings()
        {
            if (string.IsNullOrEmpty(PlayerSettings.companyName) ||
                PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "Obscure Games";

            // The app label on the phone. It was the project folder name, "3D Pull-The-World",
            // which is not what anyone wants under an icon.
            if (string.IsNullOrEmpty(PlayerSettings.productName) ||
                PlayerSettings.productName.StartsWith("3D "))
                PlayerSettings.productName = "Pull The World";

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            string id = PlayerSettings.GetApplicationIdentifier(android);
            if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany") || id.EndsWith(".") )
                PlayerSettings.SetApplicationIdentifier(android, "com.obscuregames.pulltheworld");

            // Portrait only - the whole composition is built for it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
        }

        /// <summary>
        /// TextMeshPro needs its shipped resources present before any TMP_Text can be created.
        /// Imported from the ugui package rather than asking a human to click a dialog.
        /// </summary>
        static void EnsureTmpEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMPro.TMP_Settings>(
                    "Assets/TextMesh Pro/Resources/TMP Settings.asset") != null)
                return;
            if (TMPro.TMP_Settings.instance != null) return;

            string pkgRoot = Path.GetFullPath("Packages/com.unity.ugui");
            string unitypackage = Path.Combine(pkgRoot, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(unitypackage))
            {
                Debug.LogWarning("PTW: TMP essentials package not found at " + unitypackage);
                return;
            }
            Debug.Log("PTW: importing TMP essential resources...");
            AssetDatabase.ImportPackage(unitypackage, false);
            AssetDatabase.Refresh();
        }

        // ================================================================== android ==========
        /// <summary>
        /// Builds a sideloadable APK. Headless:
        ///   Unity.exe -batchmode -quit -buildTarget Android -projectPath . \
        ///     -executeMethod PullTheWorld.EditorTools.PtwBuild.BatchAndroid [-ptwOut path.apk]
        ///
        /// IL2CPP + ARM64 only. Mono would be faster to build but is 32-bit, and a store upload
        /// requires 64-bit anyway; ARM64 alone halves the IL2CPP time versus building both.
        /// Debug keystore, so this installs on a phone but cannot go to a store as-is.
        /// </summary>
        [MenuItem("Pull The World/Build Android APK", priority = 60)]
        public static void BuildAndroidMenu() => BuildAndroid(DefaultApkPath());

        public static void BatchAndroid()
        {
            string outPath = ArgAfter("-ptwOut") ?? DefaultApkPath();
            RunBatch(() => BuildAndroid(outPath));
        }

        static string DefaultApkPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "AmuriiBuild", "AmuriiBuild.apk");
        }

        static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        public static void BuildAndroid(string outPath)
        {
            EnsurePlayerSettings();

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { PtwScene.ScenePath },
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            Debug.Log("PTW: building Android APK -> " + outPath);
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            if (s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception($"Android build {s.result}: {s.totalErrors} error(s). See the log above.");

            Debug.Log($"PTW_APK {Path.GetFullPath(outPath)} bytes={s.totalSize} " +
                      $"time={s.totalTime.TotalSeconds:F0}s warnings={s.totalWarnings}");
        }

        // ============================================================= batch entry points ====
        public static void BatchBootstrap() => RunBatch(Bootstrap);
        public static void BatchArt() => RunBatch(() => { BuildArt(); BuildPrefabs(); });
        public static void BatchLevelsAndScene() => RunBatch(() => { BuildLevels(); BuildScene(); });
        public static void BatchScene() => RunBatch(BuildScene);
        public static void BatchAll() => RunBatch(BuildEverything);

        static void RunBatch(Action action)
        {
            int code = 0;
            try
            {
                action();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("PTW_BATCH_SUCCESS");
            }
            catch (Exception e)
            {
                Debug.LogError("PTW_BATCH_FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
