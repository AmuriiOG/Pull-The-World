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

        // ============================================================= batch entry points ====
        public static void BatchBootstrap() => RunBatch(Bootstrap);
        public static void BatchArt() => RunBatch(() => { BuildArt(); BuildPrefabs(); });
        public static void BatchLevelsAndScene() => RunBatch(() => { BuildLevels(); BuildScene(); });
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
