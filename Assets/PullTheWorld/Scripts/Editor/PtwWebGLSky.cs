using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Build-time only: GPU-compresses the painted sky for a WebGL Playables build without touching
    /// the artwork, and puts everything back afterwards.
    ///
    /// Why it exists: Unity only GPU-compresses power-of-two textures for WebGL. The 24 sky layers
    /// are cut to their content (1774x887 and so on), so they shipped as uncompressed RGBA32 - about
    /// 190 MB in memory, 20 MiB of the gzip download, and the load-time spike that pushed the WASM
    /// heap to its cap. For the duration of a WebGL build this:
    ///  1. reads each layer's level-0 pixels exactly as the current import produces them (including
    ///     the 2048 downscale of the 2172-wide layers),
    ///  2. writes a copy padded to the next power of two (content at the bottom-left, transparent
    ///     texels beyond) into a generated folder and imports it in the requested GPU format,
    ///  3. points the layer's material at the copy and sets _UVScale = content / padded size, so the
    ///     painted pixels land exactly where they always did (verified to the pixel in the
    ///     experiment: best alignment at offset 0,0 for every layer).
    /// Dispose restores the materials and import settings byte for byte and deletes the copies, so
    /// the source PNGs, Android and iOS never see any of it.
    ///
    /// Crash safety: every original file is backed up under Temp/ BEFORE it is changed, and
    /// <see cref="Recover"/> (run automatically at the start of the next Apply) puts them back if a
    /// previous build died half way.
    /// </summary>
    public sealed class PtwWebGLSky : IDisposable
    {
        const string SkyDir = "Assets/PullTheWorld/Art/layered-background/layered-background";
        const string MatDir = "Assets/PullTheWorld/Art/Materials";
        const string GeneratedDir = "Assets/PullTheWorld/Art/_WebGLSkyBuild";
        const string BackupDir = "Temp/PtwWebGLSkyBackup";

        readonly List<string> touched = new List<string>();

        PtwWebGLSky() { }

        /// <summary>Layers that are actually used: the ones with an M_Sky_ material.</summary>
        static string[] SkyPngs() =>
            Directory.GetFiles(SkyDir, "*.png", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => File.Exists(MatPath(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();

        static string MatPath(string png) => $"{MatDir}/M_Sky_{Path.GetFileNameWithoutExtension(png)}.mat";

        public static PtwWebGLSky Apply(TextureImporterFormat format)
        {
            Recover();
            var expected = ExpectedFormat(format);
            var sky = new PtwWebGLSky();
            try
            {
                Directory.CreateDirectory(BackupDir);
                if (AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.DeleteAsset(GeneratedDir);
                AssetDatabase.CreateFolder(Path.GetDirectoryName(GeneratedDir).Replace('\\', '/'), Path.GetFileName(GeneratedDir));

                var pngs = SkyPngs();
                if (pngs.Length == 0) throw new Exception("PtwWebGLSky: no sky layers found under " + SkyDir);
                var padded = new Dictionary<string, (string path, Vector2 scale)>();

                foreach (var png in pngs)
                {
                    string meta = png + ".meta";
                    sky.Backup(meta);
                    sky.Backup(MatPath(png));

                    // 1. Level-0 pixels exactly as shipped today: readable, RGBA32, same 2048 cap.
                    var ti = (TextureImporter)AssetImporter.GetAtPath(png);
                    var settings = new TextureImporterSettings();
                    ti.ReadTextureSettings(settings);
                    ti.isReadable = true;
                    var ps = ti.GetPlatformTextureSettings("WebGL");
                    ps.overridden = true; ps.format = TextureImporterFormat.RGBA32; ps.maxTextureSize = 2048;
                    ti.SetPlatformTextureSettings(ps);
                    ti.SaveAndReimport();
                    var src = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
                    int w = src.width, h = src.height;
                    var px = src.GetPixels32(0);
                    sky.RestoreOne(meta);                     // the source is back to normal straight away

                    // 2. Power-of-two copy, content bottom-left, transparent padding.
                    int pw = Mathf.NextPowerOfTwo(w), ph = Mathf.NextPowerOfTwo(h);
                    var dst = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
                    dst.SetPixels32(new Color32[pw * ph]);
                    dst.SetPixels32(0, 0, w, h, px);
                    dst.Apply();
                    string copy = $"{GeneratedDir}/{Path.GetFileNameWithoutExtension(png)}.png";
                    File.WriteAllBytes(copy, dst.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(dst);
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceUpdate);

                    var ci = (TextureImporter)AssetImporter.GetAtPath(copy);
                    settings.readable = false;
                    ci.SetTextureSettings(settings);          // same filtering, wrap, mips, sRGB, alpha
                    var cs = ci.GetPlatformTextureSettings("WebGL");
                    cs.overridden = true; cs.format = format; cs.maxTextureSize = 2048; cs.compressionQuality = 100;
                    ci.SetPlatformTextureSettings(cs);
                    ci.SaveAndReimport();

                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(copy);
                    if (t.format != expected || t.width != pw || t.height != ph)
                        throw new Exception($"PtwWebGLSky: {copy} imported as {t.width}x{t.height} {t.format}, expected {pw}x{ph} {expected}");
                    padded[png] = (copy, new Vector2(w / (float)pw, h / (float)ph));
                }

                // 3. Materials last (texture re-imports reload materials and drop unsaved edits), and
                // by editing the YAML: in batch mode Material.SetVector + SaveAssets was seen to write
                // the shader default back to disk while the in-memory value was right.
                foreach (var png in pngs)
                {
                    var (copy, scale) = padded[png];
                    sky.PointMaterialAt(MatPath(png), AssetDatabase.AssetPathToGUID(copy), scale);
                }
                Debug.Log($"PtwWebGLSky: {pngs.Length} sky layers -> padded {expected} for this build.");
                return sky;
            }
            catch
            {
                sky.Dispose();
                throw;
            }
        }

        static TextureFormat ExpectedFormat(TextureImporterFormat f)
        {
            switch (f)
            {
                case TextureImporterFormat.BC7: return TextureFormat.BC7;
                case TextureImporterFormat.DXT5: return TextureFormat.DXT5;
                case TextureImporterFormat.ASTC_4x4: return TextureFormat.ASTC_4x4;
                case TextureImporterFormat.ASTC_6x6: return TextureFormat.ASTC_6x6;
                default: throw new ArgumentException("PtwWebGLSky: unsupported format " + f);
            }
        }

        void PointMaterialAt(string matPath, string textureGuid, Vector2 scale)
        {
            var lines = File.ReadAllLines(matPath).ToList();
            int baseMap = lines.FindIndex(l => l.Trim() == "- _BaseMap:");
            if (baseMap < 0 || !lines[baseMap + 1].TrimStart().StartsWith("m_Texture:"))
                throw new Exception("PtwWebGLSky: no _BaseMap texture slot in " + matPath);
            string indent = lines[baseMap + 1].Substring(0, lines[baseMap + 1].IndexOf('m'));
            lines[baseMap + 1] = $"{indent}m_Texture: {{fileID: 2800000, guid: {textureGuid}, type: 3}}";

            string uv = string.Format(CultureInfo.InvariantCulture, "    - _UVScale: {{r: {0}, g: {1}, b: 0, a: 0}}", scale.x, scale.y);
            int at = lines.FindIndex(l => l.TrimStart().StartsWith("- _UVScale:"));
            if (at >= 0) lines[at] = uv;
            else
            {
                int colors = lines.FindIndex(l => l.Trim() == "m_Colors:");
                if (colors < 0) throw new Exception("PtwWebGLSky: no m_Colors block in " + matPath);
                lines.Insert(colors + 1, uv);
            }
            File.WriteAllLines(matPath, lines);
            AssetDatabase.ImportAsset(matPath, ImportAssetOptions.ForceUpdate);

            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var tex = m.GetTexture("_BaseMap");
            var got = m.GetVector("_UVScale");
            if (!tex || AssetDatabase.GetAssetPath(tex) != AssetDatabase.GUIDToAssetPath(textureGuid)
                || Mathf.Abs(got.x - scale.x) > 1e-4f || Mathf.Abs(got.y - scale.y) > 1e-4f)
                throw new Exception($"PtwWebGLSky: {matPath} did not take the padded texture/_UVScale (got {got}).");
        }

        // ------------------------------------------------------------------ backup / restore --
        static string BackupPathFor(string assetPath) => Path.Combine(BackupDir, assetPath.Replace('/', '~'));

        void Backup(string assetPath)
        {
            string b = BackupPathFor(assetPath);
            if (!File.Exists(b)) File.Copy(assetPath, b);
            if (!touched.Contains(assetPath)) touched.Add(assetPath);
        }

        void RestoreOne(string assetPath)
        {
            string b = BackupPathFor(assetPath);
            if (!File.Exists(b)) return;
            File.Copy(b, assetPath, true);
            string imported = assetPath.EndsWith(".meta") ? assetPath.Substring(0, assetPath.Length - 5) : assetPath;
            AssetDatabase.ImportAsset(imported, ImportAssetOptions.ForceUpdate);
        }

        public void Dispose()
        {
            foreach (var p in touched) RestoreOne(p);
            touched.Clear();
            if (AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.DeleteAsset(GeneratedDir);
            if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true);
            AssetDatabase.Refresh();
        }

        /// <summary>Puts back anything a crashed WebGL build left changed. Safe to run any time.</summary>
        [MenuItem("Pull The World/Recover Sky After Failed WebGL Build", priority = 62)]
        public static void Recover()
        {
            bool any = false;
            if (Directory.Exists(BackupDir))
            {
                foreach (var b in Directory.GetFiles(BackupDir))
                {
                    string asset = Path.GetFileName(b).Replace('~', '/');
                    File.Copy(b, asset, true);
                    string imported = asset.EndsWith(".meta") ? asset.Substring(0, asset.Length - 5) : asset;
                    AssetDatabase.ImportAsset(imported, ImportAssetOptions.ForceUpdate);
                    any = true;
                }
                Directory.Delete(BackupDir, true);
            }
            if (AssetDatabase.IsValidFolder(GeneratedDir)) { AssetDatabase.DeleteAsset(GeneratedDir); any = true; }
            if (any) { AssetDatabase.Refresh(); Debug.LogWarning("PtwWebGLSky: restored sky assets left over from an interrupted WebGL build."); }
        }
    }
}
