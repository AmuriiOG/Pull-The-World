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
        [MenuItem("Pull The World/Build Android APK", priority = 59)]
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

        // ======================================================== webgl (youtube playables) ===
        /// <summary>
        /// Builds the WebGL player for YouTube Playables. Headless:
        ///   Unity.exe -batchmode -quit -buildTarget WebGL -projectPath . \
        ///     -executeMethod PullTheWorld.EditorTools.PtwBuild.BatchWebGLPlayables
        ///     [-ptwOut folder] [-ptwRelease]
        ///
        /// Two texture sets, one game. The output folder holds ONE .wasm/.framework.js and TWO .data
        /// files, and index.html picks the .data at load time: ASTC where the GPU has it (phones,
        /// Apple-silicon Macs), BC7/DXT everywhere else. Unity's software fallback would otherwise
        /// decompress the wrong format to RGBA at load - it works, but costs the memory this build
        /// exists to save. Only one .data is ever downloaded.
        ///  * desktop: sky BC7, every other texture DXT
        ///  * mobile:  sky ASTC 6x6, every other texture ASTC
        /// The sky is padded to power-of-two copies for the build only (see PtwWebGLSky); the
        /// source PNGs are never touched.
        ///
        /// Everything else is applied for the duration of the build and then put back, so the
        /// project's own Player Settings - and every other platform's - stay exactly as they were.
        /// Only WebGL-scoped settings are touched:
        ///  * Compression Disabled - the YouTube Unity wrapper guide: "no support for Unity projects
        ///    built with Unity's gzip or brotli compression". YouTube compresses on submission.
        ///  * No data caching - Playables must not keep its own copy of the game in IndexedDB.
        ///  * Code size: High managed stripping, IL2CPP OptimizeSize + Master, wasm "Disk Size with
        ///    LTO". Took the .wasm from 46.5 to 21.8 MiB raw, under YouTube's hard 30 MiB per-file
        ///    limit (which Google's bundle analyzer checks on raw size).
        ///  * WASM heap: starts at 256 MB, may grow to 320 MB. The measured peak with the compressed
        ///    sky is 229 MB reserved (down from 384 MB with the raw sky). How YouTube counts the WASM
        ///    heap against its 512 MB peak JavaScript heap rule is not documented, so re-measure on a
        ///    real phone before relying on the headroom.
        /// </summary>
        [MenuItem("Pull The World/Build WebGL (Playables, Development)", priority = 61)]
        public static void BuildWebGLPlayablesMenu()
        {
            if (!EnsureWebGLTarget()) return;
            BuildWebGLPlayables(DefaultWebGLPath() + "_Dev", true);
        }

        /// <summary>
        /// THE button for YouTube: release build -> checked against YouTube's bundle rules ->
        /// a ZIP with index.html at its root, ready to upload in the Playables Developer Portal.
        /// </summary>
        [MenuItem("Pull The World/Build YouTube Playables Upload ZIP", priority = 60)]
        public static void BuildPlayablesZipMenu()
        {
            if (!EnsureWebGLTarget()) return;
            string zip = BuildPlayablesZip(DefaultWebGLPath());
            EditorUtility.RevealInFinder(zip);
            EditorUtility.DisplayDialog("YouTube Playables",
                "Upload ZIP is ready:\n\n" + zip + "\n\nUpload it in the YouTube Playables Developer Portal.", "OK");
        }

        public static void BatchWebGLPlayables()
        {
            bool development = Array.IndexOf(Environment.GetCommandLineArgs(), "-ptwRelease") < 0;
            string outPath = ArgAfter("-ptwOut") ?? DefaultWebGLPath();
            bool zip = Array.IndexOf(Environment.GetCommandLineArgs(), "-ptwZip") >= 0;
            RunBatch(() => { if (zip) BuildPlayablesZip(outPath); else BuildWebGLPlayables(outPath, development); });
        }

        // Interactive builds only: the first switch to Web reimports every texture (minutes). Ask.
        static bool EnsureWebGLTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL) return true;
            if (!EditorUtility.DisplayDialog("Switch to Web?",
                    "The YouTube Playables build needs the Web platform. Switching re-imports the project's " +
                    "textures for Web (the first time takes a few minutes). Android settings are not changed; " +
                    "switch back via File > Build Profiles when you next build the APK.",
                    "Switch and build", "Cancel"))
                return false;
            return EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        }

        // YouTube Playables bundle rules (certification/requirements_stability): each file < 30 MiB,
        // whole bundle < 250 MiB, at most 8000 files, names only [A-Za-z0-9_.-], relative paths.
        const long MaxFileBytes = 30L * 1024 * 1024, MaxBundleBytes = 250L * 1024 * 1024;
        const int MaxFiles = 8000;

        /// <summary>Release build, rule check, ZIP. Returns the ZIP path.</summary>
        public static string BuildPlayablesZip(string outPath)
        {
            outPath = Path.GetFullPath(outPath).TrimEnd('\\', '/');
            BuildWebGLPlayables(outPath, false);

            var files = Directory.GetFiles(outPath, "*", SearchOption.AllDirectories);
            var problems = new System.Collections.Generic.List<string>();
            long total = 0;
            foreach (var f in files)
            {
                long len = new FileInfo(f).Length;
                total += len;
                string rel = f.Substring(outPath.Length + 1).Replace('\\', '/');
                if (len >= MaxFileBytes) problems.Add($"{rel} is {len / 1048576.0:F1} MiB (limit 30 MiB)");
                if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^[A-Za-z0-9_.\-]+$"))
                    problems.Add($"{rel} has characters YouTube does not allow in file names");
            }
            if (files.Length > MaxFiles) problems.Add($"{files.Length} files (limit {MaxFiles})");
            if (total >= MaxBundleBytes) problems.Add($"bundle is {total / 1048576.0:F1} MiB (limit 250 MiB)");
            if (!File.Exists(Path.Combine(outPath, "index.html"))) problems.Add("index.html is missing");
            if (problems.Count > 0)
                throw new Exception("Build breaks YouTube Playables bundle rules:\n  " + string.Join("\n  ", problems));

            string version = PlayerSettings.bundleVersion.Replace('.', '_');
            string zip = Path.Combine(Path.GetDirectoryName(outPath), $"PullTheWorld_Playables_v{version}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            // index.html at the ZIP root, as the portal expects.
            System.IO.Compression.ZipFile.CreateFromDirectory(outPath, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"PTW_ZIP {zip} bytes={new FileInfo(zip).Length} files={files.Length} bundle={total}");
            return zip;
        }

        // Folder name becomes the file names (<folder>.wasm etc.), and Playables only allows
        // alphanumerics, '_', '-' and '.' in bundle file names.
        static string DefaultWebGLPath()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "AmuriiBuild", "PlayablesWebGL");
        }

        const string PlayablesTemplate = "PROJECT:PTWPlayables";
        const int PlayablesInitialMemoryMB = 256;
        const int PlayablesMaximumMemoryMB = 320;

        public static void BuildWebGLPlayables(string outPath, bool development)
        {
            outPath = Path.GetFullPath(outPath).TrimEnd('\\', '/');
            string name = Path.GetFileName(outPath);
            string mobilePath = outPath + "_mobile_tmp";
            var webgl = UnityEditor.Build.NamedBuildTarget.WebGL;

            // Snapshot every setting we touch so the build leaves no trace in ProjectSettings.
            var compression = PlayerSettings.WebGL.compressionFormat;
            bool caching = PlayerSettings.WebGL.dataCaching;
            bool fallback = PlayerSettings.WebGL.decompressionFallback;
            bool hashes = PlayerSettings.WebGL.nameFilesAsHashes;
            bool diagnostics = PlayerSettings.WebGL.showDiagnostics;
            int initialMem = PlayerSettings.WebGL.initialMemorySize;
            int maximumMem = PlayerSettings.WebGL.maximumMemorySize;
            var growth = PlayerSettings.WebGL.memoryGrowthMode;
            var stripping = PlayerSettings.GetManagedStrippingLevel(webgl);
            var codeGen = PlayerSettings.GetIl2CppCodeGeneration(webgl);
            var compilerConfig = PlayerSettings.GetIl2CppCompilerConfiguration(webgl);
            var wasmOpt = UnityEditor.WebGL.UserBuildSettings.codeOptimization;
            string template = PlayerSettings.WebGL.template;
            bool runInBackground = PlayerSettings.runInBackground;
            try
            {
                // The YouTube page (Assets/WebGLTemplates/PTWPlayables): loads the Playables SDK
                // first, picks the texture set, starts loadData, calls firstFrameReady.
                PlayerSettings.WebGL.template = PlayablesTemplate;
                // Unity must not pause or resume itself on focus changes: inside YouTube only
                // ytgame onPause/onResume may do that (PlayablesHost + PtwPlayables.jslib).
                PlayerSettings.runInBackground = true;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.dataCaching = false;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.nameFilesAsHashes = false;
                PlayerSettings.WebGL.showDiagnostics = development;   // on-page memory/FPS overlay
                PlayerSettings.WebGL.initialMemorySize = PlayablesInitialMemoryMB;
                PlayerSettings.WebGL.maximumMemorySize = PlayablesMaximumMemoryMB;
                PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
                PlayerSettings.SetManagedStrippingLevel(webgl, ManagedStrippingLevel.High);
                PlayerSettings.SetIl2CppCodeGeneration(webgl, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
                PlayerSettings.SetIl2CppCompilerConfiguration(webgl, Il2CppCompilerConfiguration.Master);
                UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;

                foreach (var dir in new[] { outPath, mobilePath })
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);

                using (PtwWebGLSky.Apply(TextureImporterFormat.BC7))
                    BuildWebGLOnce(outPath, WebGLTextureSubtarget.DXT, development);
                using (PtwWebGLSky.Apply(TextureImporterFormat.ASTC_6x6))
                    BuildWebGLOnce(mobilePath, WebGLTextureSubtarget.ASTC, development);

                MergeMobileData(outPath, name, mobilePath);
                Directory.Delete(mobilePath, true);

                long total = 0;
                var files = Directory.GetFiles(outPath, "*", SearchOption.AllDirectories);
                Array.Sort(files, (a, b) => new FileInfo(b).Length.CompareTo(new FileInfo(a).Length));
                foreach (var f in files)
                {
                    long len = new FileInfo(f).Length;
                    total += len;
                    Debug.Log($"PTW_WEBGL_FILE {len,12} {f.Substring(outPath.Length + 1)}");
                }
                Debug.Log($"PTW_WEBGL {outPath} files={files.Length} bytes={total} " +
                          $"({(development ? "development" : "release")}, desktop BC7/DXT + mobile ASTC)");
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.dataCaching = caching;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                PlayerSettings.WebGL.nameFilesAsHashes = hashes;
                PlayerSettings.WebGL.showDiagnostics = diagnostics;
                PlayerSettings.WebGL.initialMemorySize = initialMem;
                PlayerSettings.WebGL.maximumMemorySize = maximumMem;
                PlayerSettings.WebGL.memoryGrowthMode = growth;
                PlayerSettings.SetManagedStrippingLevel(webgl, stripping);
                PlayerSettings.SetIl2CppCodeGeneration(webgl, codeGen);
                PlayerSettings.SetIl2CppCompilerConfiguration(webgl, compilerConfig);
                UnityEditor.WebGL.UserBuildSettings.codeOptimization = wasmOpt;
                PlayerSettings.WebGL.template = template;
                PlayerSettings.runInBackground = runInBackground;
                if (Directory.Exists(mobilePath)) Directory.Delete(mobilePath, true);
            }
        }

        static void BuildWebGLOnce(string path, WebGLTextureSubtarget textures, bool development)
        {
            Directory.CreateDirectory(path);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { PtwScene.ScenePath },
                locationPathName = path,
                target = BuildTarget.WebGL,
                subtarget = (int)textures,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            Debug.Log($"PTW: building WebGL ({textures}, {(development ? "development" : "release")}) -> {path}");
            var s = BuildPipeline.BuildPlayer(opts).summary;
            if (s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception($"WebGL build ({textures}) {s.result}: {s.totalErrors} error(s). See the log above.");
            Debug.Log($"PTW: WebGL {textures} build done in {s.totalTime.TotalSeconds:F0}s, warnings={s.totalWarnings}");
        }

        // Unity's documented two-build pattern: the code must be identical (it is - only textures
        // differ), so the mobile .data drops in beside the desktop one and the loader chooses.
        static void MergeMobileData(string outPath, string name, string mobilePath)
        {
            string mobileName = Path.GetFileName(mobilePath);
            foreach (var ext in new[] { ".wasm", ".framework.js" })
            {
                byte[] a = File.ReadAllBytes(Path.Combine(outPath, "Build", name + ext));
                byte[] b = File.ReadAllBytes(Path.Combine(mobilePath, "Build", mobileName + ext));
                if (!a.AsSpan().SequenceEqual(b))
                    throw new Exception($"WebGL desktop and mobile builds produced different {ext} files; " +
                                        "they can only share one page if the code is identical.");
            }
            File.Copy(Path.Combine(mobilePath, "Build", mobileName + ".data"),
                      Path.Combine(outPath, "Build", name + "_mobile.data"), true);

            // The PTWPlayables template does the choosing (ptwUseAstc); make sure it is the page
            // that was built, or the mobile .data would never be fetched.
            string index = Path.Combine(outPath, "index.html");
            string html = File.ReadAllText(index);
            if (!html.Contains("ptwUseAstc") || !html.Contains("https://www.youtube.com/game_api/v1"))
                throw new Exception("index.html is not the PTWPlayables template - the texture-set selection and the SDK tag are missing.");

            // The loading screen shows real "MB of total": give it the sizes of the files it counts.
            const string marker = "/*PTW_SIZES*/{}";
            if (!html.Contains(marker)) throw new Exception("index.html has no " + marker + " marker for the loading screen.");
            var sizes = new System.Collections.Generic.List<string>();
            foreach (var f in new[] { name + ".data", name + "_mobile.data", name + ".wasm" })
                sizes.Add($"\"{f}\":{new FileInfo(Path.Combine(outPath, "Build", f)).Length}");
            File.WriteAllText(index, html.Replace(marker, "{" + string.Join(",", sizes) + "}"));
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
