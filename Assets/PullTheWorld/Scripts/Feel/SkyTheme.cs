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

        // All three are NIGHT skies now, differing in hue rather than in brightness. The daytime
        // meadow read as flat and grey next to the reference sheet's night card (#0D151C ground),
        // and a dark backdrop is what lets the lit island, the amber door and the gems carry the
        // frame. The glow is the pool of light the backdrop shader puts behind the island; it does
        // most of the work of making a dark sky read as atmosphere rather than as a black screen.
        [SerializeField] Palette[] palettes =
        {
            new Palette
            {
                name = "Dusk",
                top = new Color(0.180f, 0.239f, 0.388f),      // #2E3D63
                bottom = new Color(0.082f, 0.110f, 0.184f),   // #151C2F
                glow = new Color(0.420f, 0.525f, 0.769f),     // #6B86C4
            },
            new Palette
            {
                name = "Ember",
                top = new Color(0.239f, 0.165f, 0.275f),      // #3D2A46
                bottom = new Color(0.106f, 0.071f, 0.133f),   // #1B1222
                glow = new Color(0.769f, 0.451f, 0.369f),     // #C4735E
            },
            new Palette
            {
                name = "Night",
                top = new Color(0.071f, 0.118f, 0.200f),      // #121E33
                bottom = new Color(0.031f, 0.051f, 0.090f),   // #080D17
                glow = new Color(0.302f, 0.435f, 0.651f),     // #4D6FA6
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
                Color c = current.glow * 1.7f;
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
