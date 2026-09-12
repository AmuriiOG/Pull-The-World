using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// The artists' UI sprites, exported from Art/PullTheWorldUI.psd into Art/UI. This is the one
    /// place that knows which file is which: the exports keep their Photoshop layer names ("Layer
    /// 17.png" is the gear), so every use in the scene builder goes through a named property here
    /// rather than a string.
    ///
    /// Loading also fixes the import settings. Unity imported the folder as Sprite/Multiple with no
    /// slices defined, which loads as NO sprite at all, and the pills need a 9-slice border so a
    /// 564 px asset can be drawn at any width with round ends. Settings are only written when
    /// they differ, so a rebuild with nothing to change does not reimport anything.
    /// </summary>
    public static class PtwUiAssets
    {
        public const string Dir = "Assets/PullTheWorld/Art/UI";

        // Pills. All three share one style (bevelled sage, darker extrusion at the foot); the
        // border keeps the round caps intact when the middle is stretched.
        static readonly Vector4 PillBorder = new Vector4(88f, 0f, 88f, 0f);
        public static Sprite Pill => Load("PlayButton.png", PillBorder);           // 564x180
        public static Sprite PillWide => Load("Layer 15.png", PillBorder);         // 715x180
        public static Sprite PillClose => Load("ClloseButton.png", PillBorder);    // 564x180

        /// <summary>Rounded square, PLAY-button style. Level tiles and the level-select entry.</summary>
        public static Sprite Square => Load("LevelsButton.png");                   // 173x184

        /// <summary>Cream disc with a soft shadow: restart, pause and the settings gear.</summary>
        public static Sprite Disc => Load("Ellipse 1 copy.png");                   // 166x172
        /// <summary>The smaller cream disc that rides in a toggle track.</summary>
        public static Sprite Knob => Load("Ellipse 1.png");                        // 87x93

        static readonly Vector4 TrackBorder = new Vector4(56f, 0f, 56f, 0f);
        public static Sprite TrackOn => Load("Settingson.png", TrackBorder);       // 268x115 sage
        public static Sprite TrackOff => Load("Settingson 2.png", TrackBorder);    // 268x115 clay

        public static Sprite Gear => Load("Layer 17.png");                         // 92x93
        /// <summary>The word PLAY, cream with a hard sage drop shadow, as drawn.</summary>
        public static Sprite PlayLabel => Load("PLAY.png");                        // 240x73
        public static Sprite CloseLabel => Load("CLOSE.png");                      // 305x78
        /// <summary>The carved-stone title logo that replaces the typed "PULL THE WORLD".</summary>
        public static Sprite Logo => Load("Layer 9.png");                          // 845x373

        /// <summary>Cream of the drawn labels and sage of their shadow, sampled from PLAY.png.</summary>
        public static readonly Color LabelCream = new Color32(253, 241, 221, 255);
        public static readonly Color LabelShadow = new Color32(98, 121, 102, 255);

        static Sprite Load(string file, Vector4 border = default)
        {
            string path = Dir + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("PTW: UI sprite missing at " + path);
                return null;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool dirty = settings.textureType != TextureImporterType.Sprite
                         || settings.spriteMode != (int)SpriteImportMode.Single
                         || settings.spriteMeshType != SpriteMeshType.FullRect   // sliced images need the full quad
                         || settings.spriteBorder != border
                         || !Mathf.Approximately(settings.spritePixelsPerUnit, 100f)
                         || settings.spriteAlignment != (int)SpriteAlignment.Center
                         || !settings.alphaIsTransparency
                         || settings.mipmapEnabled
                         || settings.filterMode != FilterMode.Bilinear
                         || settings.wrapMode != TextureWrapMode.Clamp
                         || !settings.sRGBTexture
                         || settings.npotScale != TextureImporterNPOTScale.None;
            if (dirty)
            {
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteBorder = border;
                settings.spritePixelsPerUnit = 100f;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.alphaIsTransparency = true;
                settings.mipmapEnabled = false;
                settings.filterMode = FilterMode.Bilinear;
                settings.wrapMode = TextureWrapMode.Clamp;
                settings.sRGBTexture = true;
                settings.npotScale = TextureImporterNPOTScale.None;
                importer.SetTextureSettings(settings);
            }

            // Uncompressed: these are a few hundred KB each, and block compression smears the
            // drawn type and the pill highlights on Android.
            var platform = importer.GetDefaultPlatformTextureSettings();
            if (platform.textureCompression != TextureImporterCompression.Uncompressed
                || platform.maxTextureSize < 1024)
            {
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                platform.maxTextureSize = Mathf.Max(platform.maxTextureSize, 1024);
                importer.SetPlatformTextureSettings(platform);
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("PTW: UI sprite failed to import at " + path);
            return sprite;
        }
    }
}
