using System;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Re-colours the backdrop per chapter, so eighteen levels do not all share one sky.
    ///
    /// Cheap on purpose. The backdrop is a single unlit quad with three colour properties, so a
    /// chapter change is three SetColor calls on one material - no extra draw calls, no textures,
    /// and it works identically on every level because nothing in the level itself is touched.
    /// The blend is smoothed so a level load reads as the light shifting rather than a cut.
    ///
    /// Palettes stay MUTED, deliberately. The style bible's whole argument is that restraint is
    /// what makes the reference read as premium; a chapter colour that goes candy would undo it.
    /// The ivory player has to stay the brightest non-emissive thing on screen in every chapter.
    /// </summary>
    public class SkyTheme : MonoBehaviour
    {
        [Serializable]
        public struct Palette
        {
            public string name;
            public Color top;
            public Color bottom;
            public Color glow;
        }

        [SerializeField] Renderer target;
        [Tooltip("Optional drifting motes over the sky - fireflies - tinted to the chapter's glow.")]
        [SerializeField] ParticleSystem fireflies;
        [SerializeField] float blendSpeed = 2.6f;

        // Three pastel skies from the art-direction mockups (Art/Mockup): all warm, all light, and
        // deliberately close to each other so the game stays one world - the chapters shift the
        // hour of the day, not the theme. `glow` is the sun's colour in the backdrop shader.
        [SerializeField] Palette[] palettes =
        {
            new Palette
            {
                name = "Dawn",
                top = new Color(0.996f, 0.839f, 0.776f),      // #FED6C6 peach, the mockup sky
                bottom = new Color(0.871f, 0.839f, 0.910f),   // #DED6E8 lilac
                glow = new Color(1.000f, 0.945f, 0.800f),     // #FFF1CC
            },
            new Palette
            {
                name = "Morning",
                top = new Color(0.949f, 0.894f, 0.851f),      // #F2E4D9
                bottom = new Color(0.867f, 0.898f, 0.937f),   // #DDE5EF pale blue
                glow = new Color(1.000f, 0.965f, 0.870f),     // #FFF6DE
            },
            new Palette
            {
                name = "Golden Hour",
                top = new Color(0.965f, 0.816f, 0.753f),      // #F6D0C0 rose
                bottom = new Color(0.894f, 0.827f, 0.902f),   // #E4D3E6
                glow = new Color(1.000f, 0.851f, 0.690f),     // #FFD9B0
            },
        };

        static readonly int TopId = Shader.PropertyToID("_TopColor");
        static readonly int BottomId = Shader.PropertyToID("_BottomColor");
        static readonly int GlowId = Shader.PropertyToID("_GlowColor");

        Material mat;
        Palette current, want;
        bool initialised;

        public int ChapterCount => palettes != null ? palettes.Length : 0;

        void Awake()
        {
            if (!target) target = GetComponent<Renderer>();
            if (target) mat = target.material;   // instance, so the asset is never dirtied
            if (palettes != null && palettes.Length > 0)
            {
                current = want = palettes[0];
                Push();
                initialised = true;
            }
        }

        /// <summary>Pick the palette for a chapter; blends over a moment rather than cutting.</summary>
        public void Apply(int chapter)
        {
            if (palettes == null || palettes.Length == 0) return;
            want = palettes[Mathf.Clamp(chapter, 0, palettes.Length - 1)];
            if (!initialised) { current = want; Push(); initialised = true; }
        }

        /// <summary>The palette's name, used for the chapter title card.</summary>
        public string ChapterName(int chapter) =>
            palettes != null && palettes.Length > 0
                ? palettes[Mathf.Clamp(chapter, 0, palettes.Length - 1)].name
                : "";

        /// <summary>Which chapter a level index falls in, spreading the palettes evenly.</summary>
        public int ChapterFor(int levelIndex, int levelCount)
        {
            if (ChapterCount <= 1 || levelCount <= 0) return 0;
            return Mathf.Clamp(levelIndex * ChapterCount / levelCount, 0, ChapterCount - 1);
        }

        void Update()
        {
            if (mat == null) return;
            float k = 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime);
            current.top = Color.Lerp(current.top, want.top, k);
            current.bottom = Color.Lerp(current.bottom, want.bottom, k);
            current.glow = Color.Lerp(current.glow, want.glow, k);
            Push();
        }

        void Push()
        {
            if (mat == null) return;
            mat.SetColor(TopId, current.top);
            mat.SetColor(BottomId, current.bottom);
            mat.SetColor(GlowId, current.glow);
            if (fireflies)
            {
                var main = fireflies.main;
                Color c = current.glow;      // pollen the colour of the sun
                c.a = 1f;
                main.startColor = c;
            }
        }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
        }
    }
}
