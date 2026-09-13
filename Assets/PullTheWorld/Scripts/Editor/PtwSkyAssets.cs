using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// The painted sky: the mountain ridges and cloud banks the artists cut from the reference
    /// painting into Art/layered-background. Each PNG has its own canvas with generous
    /// transparent margins and RECONSTRUCTED hidden parts (a full base under every ridge, tails
    /// beyond the frame), so alongside the texture this records, measured off the alpha:
    ///  * the content bounds within the canvas, which give the piece its on-screen size;
    ///  * the PEAK - the topmost painted point - which is what a ridge is placed by. The peak is
    ///    the one point of a mountain the reference shows unambiguously; its base is hidden.
    /// </summary>
    public static class PtwSkyAssets
    {
        public const string Dir = "Assets/PullTheWorld/Art/layered-background/layered-background";

        /// <summary>One painted piece: file, canvas aspect, content bounds and peak, all as canvas fractions (y from the TOP).</summary>
        public readonly struct Piece
        {
            public readonly string File;
            /// <summary>Canvas width / height.</summary>
            public readonly float Aspect;
            /// <summary>Content centre.</summary>
            public readonly Vector2 Centre;
            /// <summary>Content width and height.</summary>
            public readonly Vector2 Size;
            /// <summary>Topmost painted point.</summary>
            public readonly Vector2 Peak;
            /// <summary>Content bottom edge.</summary>
            public readonly float Bottom;
            /// <summary>Canvas height in pixels, for turning a painting-space scale into a frame fraction.</summary>
            public readonly float CanvasHeight;

            public Piece(string file, int w, int h, float x0, float x1, float y0, float y1, float peakX, float peakY)
            {
                File = file;
                Aspect = w / (float)h;
                Centre = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
                Size = new Vector2(x1 - x0, y1 - y0);
                Peak = new Vector2(peakX, peakY);
                Bottom = y1;
                CanvasHeight = h;
            }

            public string Path => Dir + "/" + File;
            public string Id => "M_Sky_" + System.IO.Path.GetFileNameWithoutExtension(File);
        }

        // Measured off the PNGs (alpha > 60, 2 px steps): bounds x0 x1 y0 y1, then the peak.
        public static readonly Piece Mountain01 = new Piece("mountains/mountain-01-distant-top-center.png", 1896, 830, 0.039f, 0.962f, 0.255f, 0.720f, 0.360f, 0.255f);
        public static readonly Piece Mountain02 = new Piece("mountains/mountain-02-upper-left-lilac.png", 1774, 887, 0.044f, 0.946f, 0.268f, 0.798f, 0.340f, 0.268f);
        public static readonly Piece Mountain03 = new Piece("mountains/mountain-03-upper-right-lilac.png", 2172, 724, 0.035f, 0.970f, 0.213f, 0.898f, 0.626f, 0.213f);
        public static readonly Piece Mountain04 = new Piece("mountains/mountain-04-middle-center-lavender.png", 1774, 887, 0.027f, 0.972f, 0.300f, 0.776f, 0.440f, 0.300f);
        public static readonly Piece Mountain05 = new Piece("mountains/mountain-05-upper-left-sage.png", 1774, 887, 0.006f, 0.961f, 0.194f, 0.870f, 0.297f, 0.194f);
        public static readonly Piece Mountain06 = new Piece("mountains/mountain-06-middle-right-blue.png", 1774, 887, 0.008f, 0.999f, 0.214f, 0.868f, 0.793f, 0.214f);
        public static readonly Piece Mountain07 = new Piece("mountains/mountain-07-main-left-sage.png", 1774, 887, 0.007f, 0.993f, 0.214f, 0.936f, 0.280f, 0.214f);
        public static readonly Piece Mountain09 = new Piece("mountains/mountain-09-main-right-blue.png", 1774, 887, 0.046f, 0.966f, 0.246f, 0.803f, 0.713f, 0.246f);
        public static readonly Piece Mountain11 = new Piece("mountains/mountain-11-lower-left-lilac.png", 1774, 887, 0.030f, 0.952f, 0.352f, 0.821f, 0.282f, 0.352f);
        public static readonly Piece Mountain12 = new Piece("mountains/mountain-12-bottom-right-lilac.png", 1774, 887, 0.086f, 0.975f, 0.340f, 0.893f, 0.694f, 0.340f);
        public static readonly Piece Mountain13 = new Piece("mountains/mountain-13-bottom-left-sage.png", 1774, 887, 0.014f, 0.983f, 0.196f, 0.882f, 0.328f, 0.196f);
        public static readonly Piece Mountain14 = new Piece("mountains/mountain-14-bottom-center-lilac.png", 2172, 724, 0.053f, 0.947f, 0.304f, 0.870f, 0.466f, 0.304f);

        public static readonly Piece Cloud01 = new Piece("clouds/cloud-01-upper-left.png", 1774, 887, 0.035f, 0.945f, 0.169f, 0.873f, 0.244f, 0.169f);
        public static readonly Piece Cloud02 = new Piece("clouds/cloud-02-high-thin-left.png", 2172, 724, 0.050f, 0.977f, 0.387f, 0.649f, 0.305f, 0.387f);
        public static readonly Piece Cloud03 = new Piece("clouds/cloud-03-high-thin-right.png", 2172, 724, 0.059f, 0.941f, 0.423f, 0.633f, 0.485f, 0.423f);
        public static readonly Piece Cloud04 = new Piece("clouds/cloud-04-upper-right.png", 1918, 820, 0.053f, 0.964f, 0.200f, 0.880f, 0.765f, 0.200f);
        public static readonly Piece Cloud05 = new Piece("clouds/cloud-05-middle-right.png", 1774, 887, 0.025f, 0.977f, 0.174f, 0.875f, 0.809f, 0.174f);
        public static readonly Piece Cloud06 = new Piece("clouds/cloud-06-middle-diagonal.png", 1942, 809, 0.035f, 0.956f, 0.173f, 0.858f, 0.195f, 0.173f);
        public static readonly Piece Cloud07 = new Piece("clouds/cloud-07-middle-right-low.png", 1774, 887, 0.063f, 0.950f, 0.223f, 0.839f, 0.729f, 0.223f);
        public static readonly Piece Cloud08 = new Piece("clouds/cloud-08-lower-diagonal.png", 1774, 887, 0.033f, 0.966f, 0.212f, 0.855f, 0.184f, 0.212f);
        public static readonly Piece Cloud09 = new Piece("clouds/cloud-09-lower-right-bank.png", 1774, 887, 0.053f, 0.946f, 0.183f, 0.841f, 0.862f, 0.183f);
        public static readonly Piece Cloud10 = new Piece("clouds/cloud-10-bottom-left.png", 1672, 941, 0.038f, 0.961f, 0.225f, 0.820f, 0.193f, 0.225f);
        public static readonly Piece Cloud11 = new Piece("clouds/cloud-11-bottom-center.png", 1672, 941, 0.048f, 0.951f, 0.219f, 0.803f, 0.368f, 0.219f);
        public static readonly Piece Cloud12 = new Piece("clouds/cloud-12-bottom-right.png", 982, 1602, 0.037f, 0.974f, 0.356f, 0.744f, 0.796f, 0.356f);

        /// <summary>
        /// The texture, with its import fixed for a soft-edged painted sprite: transparency,
        /// clamped edges, trilinear mips (the far ridges are drawn at a fraction of their size).
        /// Only reimports when something differs.
        /// </summary>
        public static Texture2D Load(Piece piece)
        {
            var importer = AssetImporter.GetAtPath(piece.Path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("PTW: sky sprite missing at " + piece.Path);
                return null;
            }

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            bool dirty = s.textureType != TextureImporterType.Default
                         || !s.alphaIsTransparency
                         || !s.mipmapEnabled
                         || s.filterMode != FilterMode.Trilinear
                         || s.wrapMode != TextureWrapMode.Clamp
                         || !s.sRGBTexture
                         || s.npotScale != TextureImporterNPOTScale.None;
            if (dirty)
            {
                s.textureType = TextureImporterType.Default;
                s.alphaIsTransparency = true;
                s.mipmapEnabled = true;
                s.filterMode = FilterMode.Trilinear;
                s.wrapMode = TextureWrapMode.Clamp;
                s.sRGBTexture = true;
                s.npotScale = TextureImporterNPOTScale.None;
                importer.SetTextureSettings(s);
                importer.SaveAndReimport();
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(piece.Path);
            if (tex == null) Debug.LogWarning("PTW: sky sprite failed to import at " + piece.Path);
            return tex;
        }
    }
}
