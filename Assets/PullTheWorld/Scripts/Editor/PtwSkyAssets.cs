using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// The painted sky: the mountain ridges and cloud banks the artists cut from the reference
    /// painting into Art/layered-background. Each PNG has its own canvas with generous
    /// transparent margins, so alongside the texture this records where the painted content
    /// actually sits in that canvas (measured alpha bounds), which is what lets the scene builder
    /// place a sprite by where its CONTENT should land on screen rather than its canvas.
    /// </summary>
    public static class PtwSkyAssets
    {
        public const string Dir = "Assets/PullTheWorld/Art/layered-background/layered-background";

        /// <summary>One painted piece: file, canvas aspect and the content's bounds within the canvas.</summary>
        public readonly struct Piece
        {
            public readonly string File;
            /// <summary>Canvas width / height.</summary>
            public readonly float Aspect;
            /// <summary>Content centre as canvas fractions, y measured from the TOP.</summary>
            public readonly Vector2 Centre;
            /// <summary>Content width and height as fractions of the canvas.</summary>
            public readonly Vector2 Size;

            public Piece(string file, int w, int h, int x0, int x1, int y0, int y1)
            {
                File = file;
                Aspect = w / (float)h;
                Centre = new Vector2((x0 + x1) * 0.5f / w, (y0 + y1) * 0.5f / h);
                Size = new Vector2((x1 - x0) / (float)w, (y1 - y0) / (float)h);
            }

            public string Path => Dir + "/" + File;
            public string Id => "M_Sky_" + System.IO.Path.GetFileNameWithoutExtension(File);
        }

        // Alpha bounds measured off the PNGs (alpha > 24, 4 px steps).
        public static readonly Piece Mountain01 = new Piece("mountains/mountain-01-distant-top-center.png", 1896, 830, 72, 1828, 212, 600);
        public static readonly Piece Mountain02 = new Piece("mountains/mountain-02-upper-left-lilac.png", 1774, 887, 80, 1676, 240, 708);
        public static readonly Piece Mountain03 = new Piece("mountains/mountain-03-upper-right-lilac.png", 2172, 724, 80, 2104, 156, 648);
        public static readonly Piece Mountain04 = new Piece("mountains/mountain-04-middle-center-lavender.png", 1774, 887, 48, 1724, 268, 688);
        public static readonly Piece Mountain05 = new Piece("mountains/mountain-05-upper-left-sage.png", 1774, 887, 12, 1704, 172, 772);
        public static readonly Piece Mountain06 = new Piece("mountains/mountain-06-middle-right-blue.png", 1774, 887, 12, 1772, 192, 768);
        public static readonly Piece Mountain07 = new Piece("mountains/mountain-07-main-left-sage.png", 1774, 887, 12, 1760, 192, 832);
        public static readonly Piece Mountain09 = new Piece("mountains/mountain-09-main-right-blue.png", 1774, 887, 84, 1712, 220, 712);
        public static readonly Piece Mountain11 = new Piece("mountains/mountain-11-lower-left-lilac.png", 1774, 887, 52, 1688, 312, 728);
        public static readonly Piece Mountain12 = new Piece("mountains/mountain-12-bottom-right-lilac.png", 1774, 887, 152, 1728, 304, 792);
        public static readonly Piece Mountain13 = new Piece("mountains/mountain-13-bottom-left-sage.png", 1774, 887, 24, 1744, 176, 780);

        public static readonly Piece Cloud01 = new Piece("clouds/cloud-01-upper-left.png", 1774, 887, 64, 1676, 148, 772);
        public static readonly Piece Cloud02 = new Piece("clouds/cloud-02-high-thin-left.png", 2172, 724, 104, 2124, 280, 468);
        public static readonly Piece Cloud04 = new Piece("clouds/cloud-04-upper-right.png", 1918, 820, 104, 1848, 164, 720);
        public static readonly Piece Cloud06 = new Piece("clouds/cloud-06-middle-diagonal.png", 1942, 809, 68, 1856, 140, 692);
        public static readonly Piece Cloud08 = new Piece("clouds/cloud-08-lower-diagonal.png", 1774, 887, 56, 1716, 188, 756);
        public static readonly Piece Cloud09 = new Piece("clouds/cloud-09-lower-right-bank.png", 1774, 887, 96, 1676, 164, 744);
        public static readonly Piece Cloud10 = new Piece("clouds/cloud-10-bottom-left.png", 1672, 941, 64, 1604, 212, 772);
        public static readonly Piece Cloud11 = new Piece("clouds/cloud-11-bottom-center.png", 1672, 941, 80, 1588, 208, 756);
        public static readonly Piece Cloud12 = new Piece("clouds/cloud-12-bottom-right.png", 982, 1602, 36, 956, 572, 1192);

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
