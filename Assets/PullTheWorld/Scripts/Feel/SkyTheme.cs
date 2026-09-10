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
        [SerializeField] float blendSpeed = 2.6f;

        [SerializeField] Palette[] palettes =
        {
            new Palette
            {
                name = "Meadow",
                top = new Color(0.776f, 0.827f, 0.882f),      // #C6D3E1
                bottom = new Color(0.663f, 0.722f, 0.784f),   // #A9B8C8
                glow = new Color(0.941f, 0.965f, 1.000f),     // #F0F6FF
            },
            new Palette
            {
                name = "Dusk",
                top = new Color(0.851f, 0.769f, 0.753f),      // #D9C4C0
                bottom = new Color(0.718f, 0.624f, 0.651f),   // #B79FA6
                glow = new Color(1.000f, 0.890f, 0.769f),     // #FFE3C4
            },
            new Palette
            {
                name = "Night",
                top = new Color(0.541f, 0.608f, 0.722f),      // #8A9BB8
                bottom = new Color(0.420f, 0.478f, 0.588f),   // #6B7A96
                glow = new Color(0.725f, 0.800f, 0.941f),     // #B9CCF0
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
        }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
        }
    }
}
