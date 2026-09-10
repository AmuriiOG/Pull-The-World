using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// The single source of truth for colour and material in the prototype.
    ///
    /// Everything is untextured URP/Lit with flat colour, low smoothness and zero metallic. All the
    /// richness comes from geometry (chamfers), lighting (warm key + cool fill) and a handful of
    /// emissive accents. That keeps the whole game at ~20 materials, all SRP-batchable and all
    /// instanced, which is what makes it cheap enough for a phone.
    /// </summary>
    public static class PtwArt
    {
        public const string ArtRoot = "Assets/PullTheWorld/Art";
        public const string MatDir = ArtRoot + "/Materials";
        public const string MeshDir = ArtRoot + "/Meshes";
        public const string TexDir = ArtRoot + "/Textures";

        // ------------------------------------------------------------------- palette --------
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }

        // Sampled off the concept sheet (LEVEL 1 panel, 5x5 patch averages) rather than eyeballed.
        // NOTE: the sampled numbers are FINAL FRAME values - already through lighting, tonemapping
        // and bloom. These constants are ALBEDO, so they sit a touch above the measured target and
        // get pulled down into place by the key/fill rig. They were then tuned against real Game
        // View captures; see the compare pass in PtwCapture.
        //
        // The single most important value here is the background. Measured #97A3B3: a MID cool
        // blue-grey, not the near-white it appears to be against the dark concept sheet. On a
        // near-white ground the pale stone and the ivory character both go muddy; on a mid ground
        // the character stays the brightest thing on screen, which is exactly where the eye
        // should go.
        // These are pre-post-processing. Tonemapping, contrast and vignette all pull the frame
        // down, so the authored backdrop sits above the #97A3B3 target to land on it on screen.
        // Raised for v2 after a capture-compare pass. PALETTE.md's target for the ground is
        // #97A3B3, and the v1 values landed nearer #5A6875 on screen once Neutral tonemapping and
        // the vignette had taken their cut - the whole frame read as dusk. These sit high enough
        // to come out at the target.
        public static readonly Color BgTop = Hex("#C6D3E1");
        public static readonly Color BgBottom = Hex("#A9B8C8");

        // Olive-leaning, not Kelly green. The first render came out far too saturated; the v1
        // value then over-corrected into a dark bottle green that killed all the colour in the
        // frame. This is PALETTE.md's measured lit value rather than another guess.
        public static readonly Color Grass = Hex("#648C42");
        public static readonly Color GrassDark = Hex("#5C7A3E");
        // Stone was the one material both independent samples initially got wrong (averaging
        // across the bright and dark concept cards dragged it ~27 points too dark). Re-measured
        // on the LEVEL 1 card alone: lit #A6ABB6, mid #7C8087, shadow #494D52.
        public static readonly Color Stone = Hex("#8D97A8");
        public static readonly Color StoneMid = Hex("#7A8496");
        public static readonly Color StoneDark = Hex("#5E6879");
        public static readonly Color StoneLight = Hex("#A4AEC0");
        public static readonly Color Dirt = Hex("#8A6B4A");
        public static readonly Color Wood = Hex("#B08052");
        public static readonly Color WoodDark = Hex("#7A5433");
        public static readonly Color PlayerBody = Hex("#F2F4F8");
        public static readonly Color PlayerShade = Hex("#D2D8E0");
        // The anchor ring has two populations that must not be averaged: a narrow hot emissive rim
        // and a much larger area of soft spill. Averaging them yields a dull ring. Tune to the RIM
        // and let bloom manufacture the spill - the ring must stay the brightest cyan on screen.
        public static readonly Color Cyan = Hex("#86DFFC");
        public static readonly Color CyanDeep = Hex("#1B7FC0");
        public static readonly Color Warm = Hex("#FFC94F");
        public static readonly Color WarmDeep = Hex("#E8A03D");
        public static readonly Color Fire = Hex("#EF6B1F");
        public static readonly Color Foliage = Hex("#3E7530");
        public static readonly Color FoliageDark = Hex("#2A5522");
        public static readonly Color RockGrey = Hex("#8A9099");
        public static readonly Color Metal = Hex("#6B747E");
        public static readonly Color SpikeSteel = Hex("#AAB2BB");
        public static readonly Color ShadowTint = new Color(0.20f, 0.25f, 0.33f, 1f);

        // ------------------------------------------------------------------ material ids ----
        public const string MGrass = "M_Grass";
        public const string MGrassDark = "M_GrassDark";
        public const string MStone = "M_Stone";
        public const string MStoneMid = "M_StoneMid";
        public const string MStoneDark = "M_StoneDark";
        public const string MStoneLight = "M_StoneLight";
        public const string MDirt = "M_Dirt";
        public const string MWood = "M_Wood";
        public const string MWoodDark = "M_WoodDark";
        public const string MPlayer = "M_Player";
        public const string MPlayerShade = "M_PlayerShade";
        public const string MPlayerEye = "M_PlayerEye";
        public const string MKey = "M_Key";
        public const string MRock = "M_Rock";
        public const string MFoliage = "M_Foliage";
        public const string MFoliageDark = "M_FoliageDark";
        public const string MMetal = "M_Metal";
        public const string MSpike = "M_Spike";
        public const string MGlowWarm = "M_GlowWarm";
        public const string MGlowCyan = "M_GlowCyan";
        public const string MGlowFire = "M_GlowFire";
        public const string MPlateOn = "M_PlateIndicator";
        public const string MAnchorRing = "M_AnchorRing";
        public const string MBlobShadow = "M_BlobShadow";
        public const string MBackground = "M_Background";
        public const string MPortalGlow = "M_PortalGlow";
        public const string MParticleAdd = "M_ParticleAdd";
        public const string MParticleSoft = "M_ParticleSoft";
        public const string MPortalEnergy = "M_PortalEnergy";
        public const string MWater = "M_Water";
        public const string MWaterBody = "M_WaterBody";
        public const string MEnemy = "M_Enemy";
        public const string MEnemySpike = "M_EnemySpike";
        public const string MOcean = "M_Ocean";
        public const string MFarStone = "M_FarStone";
        public const string MFarGrass = "M_FarGrass";

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Get(string id)
        {
            if (cache.TryGetValue(id, out var m) && m) return m;
            m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{id}.mat");
            cache[id] = m;
            return m;
        }

        // ------------------------------------------------------------------- generation -----
        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(MatDir);
            PtwPaths.EnsureFolder(TexDir);
            cache.Clear();

            Lit(MGrass, Grass, 0.12f);
            Lit(MGrassDark, GrassDark, 0.10f);
            Lit(MStone, Stone, 0.14f);
            Lit(MStoneMid, StoneMid, 0.13f);
            Lit(MStoneDark, StoneDark, 0.12f);
            Lit(MStoneLight, StoneLight, 0.16f);
            Lit(MDirt, Dirt, 0.06f);
            Lit(MWood, Wood, 0.10f);
            Lit(MWoodDark, WoodDark, 0.08f);
            Lit(MPlayer, PlayerBody, 0.22f);
            Lit(MPlayerShade, PlayerShade, 0.18f);
            // Eyes are near-black rather than pure black so they still pick up a rim of key light
            // and do not read as holes punched in the ball.
            Lit(MPlayerEye, Hex("#25303C"), 0.30f);
            Lit(MRock, RockGrey, 0.12f);
            Lit(MFoliage, Foliage, 0.10f);
            Lit(MFoliageDark, FoliageDark, 0.08f);
            Lit(MMetal, Metal, 0.42f, 0.35f, specular: true);
            Lit(MSpike, SpikeSteel, 0.55f, 0.5f, specular: true);

            // Atmospheric perspective for the distant scenery: shifted towards the sky colour and
            // heavily desaturated, so it reads as far away rather than as level geometry the
            // player cannot reach. It still has to be VISIBLE though - it is the static reference
            // the moving world is judged against.
            // Darkened for the night skies: at the old values the islets floated in the dark like
            // lit models rather than distant scenery.
            Lit(MFarStone, Hex("#4E5A6C"), 0.08f);
            Lit(MFarGrass, Hex("#3F4D44"), 0.06f);

            // Enemy: a bruised magenta that is in nobody else's palette, with near-black spikes.
            // Hostile has to read in one glance against grey rock and green grass.
            Lit(MEnemy, Hex("#8E3060"), 0.22f);
            Lit(MEnemySpike, Hex("#3A1A2C"), 0.30f);

            // The key has to out-read every rock in the level from across the screen, so it gets a
            // real emissive rather than just a bright albedo. Kept below the door's amber so the
            // goal still wins the frame.
            Emissive(MKey, Hex("#FFD96B"), Hex("#FFC03A"), 1.9f, 0.42f);

            Emissive(MGlowWarm, Warm, Warm, 3.2f, 0.3f);
            Emissive(MGlowCyan, Cyan, Cyan, 2.6f, 0.3f);
            Emissive(MGlowFire, Fire, Fire, 4.5f, 0.2f);
            Emissive(MPlateOn, Hex("#D9615A"), Hex("#D9615A"), 1.2f, 0.3f);

            // Screen furniture and effects.
            var ringTex = MakeRingTexture("Tex_AnchorRing", 256);
            var blobTex = MakeBlobTexture("Tex_BlobShadow", 128);
            var glowTex = MakeBlobTexture("Tex_PortalGlow", 128, 1.6f);
            var bgTex = MakeGradientTexture("Tex_Background", 8, 256, BgTop, BgBottom);
            var shadowTex = MakeContactShadowTexture("Tex_ContactShadow", 128);

            UnlitTextured(MAnchorRing, ringTex, Cyan * 1.5f, additive: true);
            UnlitTextured(MPortalGlow, glowTex, Warm * 1.4f, additive: true);
            MultiplyTextured(MBlobShadow, shadowTex);
            UnlitTextured(MParticleAdd, blobTex, Color.white, additive: true);
            UnlitTextured(MParticleSoft, blobTex, Color.white, additive: false);

            BuildBackdrop();
            BuildPortalEnergy();
            BuildWater();

            // The pool's translucent body. The side-on camera sees this face, not the surface
            // tile, so it carries the colour: shallow blue, ~55% opaque, faintly glossy.
            LitTransparent(MWaterBody, new Color(0.19f, 0.56f, 0.80f, 0.56f), 0.55f);

            AssetDatabase.SaveAssets();
        }

        /// <summary>Animated sky: gradient + a pool of light behind the islands + drifting cloud.</summary>
        static void BuildBackdrop()
        {
            var m = LoadOrCreateShader(MBackground, "PTW/Backdrop");
            if (m == null) return;
            m.SetColor("_TopColor", BgTop);
            m.SetColor("_BottomColor", BgBottom);
            m.SetColor("_GlowColor", Hex("#F0F6FF"));
            m.SetFloat("_GlowStrength", 0.62f);
            m.SetVector("_GlowCenter", new Vector4(0f, 0.04f, 0f, 0f));
            m.SetFloat("_GlowRadius", 0.66f);
            m.SetFloat("_GlowAspect", 1.4f);
            m.SetFloat("_CloudStrength", 0.115f);
            m.SetFloat("_CloudScale", 1.7f);
            m.SetFloat("_CloudSpeed", 0.006f);
            m.SetFloat("_EdgeDarken", 0.09f);
            m.renderQueue = (int)RenderQueue.Background;
            EditorUtility.SetDirty(m);
        }

        /// <summary>Swirling additive energy for the doorway.</summary>
        static void BuildPortalEnergy()
        {
            var m = LoadOrCreateShader(MPortalEnergy, "PTW/PortalEnergy");
            if (m == null) return;
            m.SetColor("_CoreColor", Hex("#FFF1B8"));
            m.SetColor("_EdgeColor", WarmDeep);
            m.SetFloat("_Intensity", 2.3f);
            // Matches the ArchFill mesh: spans y 0.15..1.36, x +/-0.31.
            m.SetVector("_Center", new Vector4(0f, 0.755f, 0f, 0f));
            m.SetVector("_Extents", new Vector4(0.33f, 0.63f, 0f, 0f));
            m.SetFloat("_Speed", 0.85f);
            m.SetFloat("_Swirl", 3f);
            m.SetFloat("_RingFreq", 8f);
            m.SetFloat("_Pulse", 0.16f);
            m.SetFloat("_EdgeSoft", 0.45f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        /// <summary>Water tuned to the measured reference: body #1372A3, lit #1F9CC5, foam #8BEDFA.</summary>
        static void BuildWater()
        {
            var m = LoadOrCreateShader(MWater, "PTW/Water");
            if (m == null) return;
            m.SetColor("_ShallowColor", Hex("#1F9CC5"));
            m.SetColor("_DeepColor", Hex("#0B3F63"));
            m.SetColor("_FoamColor", Hex("#CFF6FE"));
            m.SetColor("_SpecColor2", Hex("#FFFFFF"));
            m.SetFloat("_DepthFade", 0.34f);
            m.SetFloat("_Opacity", 0.78f);
            m.SetFloat("_RefractStrength", 0.03f);
            m.SetFloat("_FoamDepth", 0.16f);
            m.SetFloat("_FoamNoiseScale", 13f);
            m.SetFloat("_FoamSpeed", 0.9f);
            m.SetFloat("_WaveAmp", 0.055f);
            m.SetFloat("_WaveFreq", 3.4f);
            m.SetFloat("_WaveSpeed", 1.1f);
            m.SetFloat("_NormalStrength", 1.5f);
            m.SetFloat("_Gloss", 110f);
            m.SetFloat("_SpecAmount", 1.5f);
            m.SetFloat("_FresnelAmount", 0.35f);
            m.SetFloat("_Tilt", 0f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);

            BuildOceanMaterial();
        }

        /// <summary>
        /// The distant sea. Bigger, slower waves and no refraction (there is nothing behind it),
        /// and it receives the islands' shadows - which is what makes it read as a real surface
        /// the world is floating over rather than a blue backdrop.
        /// </summary>
        static void BuildOceanMaterial()
        {
            var m = LoadOrCreateShader(MOcean, "PTW/Water");
            if (m == null) return;
            m.SetColor("_ShallowColor", Hex("#6A8FAC"));
            m.SetColor("_DeepColor", Hex("#4C7090"));
            m.SetColor("_FoamColor", Hex("#D8EEF8"));
            m.SetColor("_SpecColor2", Hex("#FFFFFF"));
            // Nothing sits under the open sea, so the depth probe returns the far plane. A huge
            // fade keeps it in the SHALLOW colour - otherwise it saturates to deep and reads as a
            // black void that swallows the whole frame.
            m.SetFloat("_DepthFade", 300f);
            m.SetFloat("_Opacity", 1f);
            m.SetFloat("_RefractStrength", 0f);
            m.SetFloat("_FoamDepth", 0.6f);
            m.SetFloat("_FoamNoiseScale", 3.5f);
            m.SetFloat("_FoamSpeed", 0.35f);
            m.SetFloat("_WaveAmp", 0.34f);
            m.SetFloat("_WaveFreq", 1.05f);
            m.SetFloat("_WaveSpeed", 0.55f);
            m.SetFloat("_NormalStrength", 2.1f);
            m.SetFloat("_Gloss", 95f);
            m.SetFloat("_CrestAmount", 0.055f);
            m.SetFloat("_SpecAmount", 1.5f);
            m.SetFloat("_FresnelAmount", 0.28f);
            m.SetFloat("_Tilt", 0f);
            m.renderQueue = (int)RenderQueue.Transparent - 5;   // under the pools
            EditorUtility.SetDirty(m);
        }

        static Material LoadOrCreateShader(string id, string shaderName)
        {
            var sh = Shader.Find(shaderName);
            if (sh == null)
            {
                Debug.LogError($"PTW: shader '{shaderName}' not found - material {id} left as-is.");
                return null;
            }
            var m = LoadOrCreate(id, sh);
            m.enableInstancing = true;
            cache[id] = m;
            return m;
        }

        // ------------------------------------------------------------------ material makers -
        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");
        static Shader UnlitShader => Shader.Find("Universal Render Pipeline/Unlit");

        /// <summary>
        /// Flat-colour URP/Lit. Specular is OFF by default, and that is a fix rather than a
        /// preference.
        ///
        /// The style bible says colour comes from albedo only, but every material was being
        /// created with _SpecularHighlights on. On this art that is not a subtle difference: the
        /// geometry is large flat faces, so when a face turns towards the key light the whole face
        /// takes the specular lobe at once and renders as a uniform bright strip. With the island
        /// tilted, the grass cap's top face did exactly that and read as a white line painted
        /// along the grass - it survived lowering the key light, darkening the grass albedo and
        /// raising the bloom threshold, because none of those were the cause.
        ///
        /// Note that setting the float alone does nothing: URP branches on the
        /// _SPECULARHIGHLIGHTS_OFF shader keyword, so the keyword has to be set too.
        ///
        /// Turning it off is also cheaper per fragment, which matters on the mobile target.
        /// </summary>
        public static Material Lit(string id, Color color, float smoothness, float metallic = 0f,
                                   bool specular = false)
        {
            var m = LoadOrCreate(id, LitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_SpecularHighlights", specular ? 1f : 0f);
            if (specular) m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.enableInstancing = true;
            m.renderQueue = -1;
            EditorUtility.SetDirty(m);
            cache[id] = m;
            return m;
        }

        public static Material Emissive(string id, Color baseColor, Color emission, float intensity,
                                        float smoothness)
        {
            var m = Lit(id, baseColor, smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// URP/Lit in alpha-blended transparent mode. Same keyword dance as UnlitTextured's
        /// transparent branch - setting _Surface alone does nothing, the shader branches on the
        /// _SURFACE_TYPE_TRANSPARENT keyword and the blend/zwrite floats.
        /// </summary>
        public static Material LitTransparent(string id, Color color, float smoothness)
        {
            var m = LoadOrCreate(id, LitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SpecularHighlights", 1f);         // water is the one thing that should glint
            m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_EMISSION");
            m.enableInstancing = true;
            m.renderQueue = (int)RenderQueue.Transparent - 1;   // under the wave surface
            EditorUtility.SetDirty(m);
            cache[id] = m;
            return m;
        }

        public static Material UnlitTextured(string id, Texture2D tex, Color tint,
                                             bool additive, bool opaque = false)
        {
            var m = LoadOrCreate(id, UnlitShader);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.enableInstancing = true;

            if (opaque)
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_ZWrite", 1f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m);
            cache[id] = m;
            return m;
        }

        /// <summary>
        /// Contact shadows are a MULTIPLY, not a black decal.
        /// Measured per-channel linear ratios under an occluder are R 0.28 / G 0.35 / B 0.46 - a
        /// distinctly blue-biased darkening. No amount of black-with-alpha reproduces that hue
        /// shift, so this uses URP's multiply blend with the tint baked into the texture RGB and
        /// the falloff in its alpha (_ALPHAMODULATE_ON lerps the tint toward white by alpha).
        /// </summary>
        public static Material MultiplyTextured(string id, Texture2D tex)
        {
            var m = LoadOrCreate(id, UnlitShader);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 3f);                              // Multiply
            m.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent - 1;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            cache[id] = m;
            return m;
        }

        static Material LoadOrCreate(string id, Shader shader)
        {
            string path = $"{MatDir}/{id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = id };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }

        // ---------------------------------------------------------------------- textures ----
        public static Texture2D MakeGradientTexture(string id, int w, int h, Color top, Color bottom)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                // Slight ease so the horizon band sits a bit low, like the reference.
                Color c = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, Mathf.Pow(t, 0.85f)));
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        public static Texture2D MakeBlobTexture(string id, int size, float power = 2.2f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    a = Mathf.Pow(a, power);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        /// <summary>Soft annulus used for the glowing disc the player stands on.</summary>
        public static Texture2D MakeRingTexture(string id, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;

                    // Bright thin rim near the outside...
                    float rim = Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.055f, 2f));
                    // ...a softer inner ring...
                    float inner = Mathf.Exp(-Mathf.Pow((d - 0.62f) / 0.13f, 2f)) * 0.35f;
                    // ...and a gentle fill so the disc reads as a solid pad of light.
                    float fill = Mathf.Clamp01(1f - d) * 0.22f;

                    float a = Mathf.Clamp01(rim + inner + fill);
                    if (d > 1f) a = 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        /// <summary>
        /// RGB is the measured shadow tint; alpha is the measured falloff profile, which is much
        /// harder-edged than a typical blob: it holds near full strength then collapses to nothing
        /// inside about 12% of a block width.
        /// </summary>
        public static Texture2D MakeContactShadowTexture(string id, int size)
        {
            // sRGB encoding of the measured linear multiply (0.28, 0.35, 0.46).
            Color tint = Hex("#93A1B5");

            // (normalised radius, strength) sampled off the reference.
            float[] rs = { 0.00f, 0.15f, 0.30f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 1.00f };
            float[] ss = { 1.00f, 0.96f, 0.77f, 0.75f, 0.74f, 0.27f, 0.03f, 0.01f, 0.00f };

            float Strength(float r)
            {
                if (r >= 1f) return 0f;
                for (int i = 1; i < rs.Length; i++)
                {
                    if (r > rs[i]) continue;
                    float t = Mathf.InverseLerp(rs[i - 1], rs[i], r);
                    return Mathf.Lerp(ss[i - 1], ss[i], t);
                }
                return 0f;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float rad = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(rad, rad)) / rad;
                    tex.SetPixel(x, y, new Color(tint.r, tint.g, tint.b, Strength(d)));
                }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        static Texture2D SaveTexture(Texture2D tex, string id)
        {
            string path = $"{TexDir}/{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex, existing);
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }
    }
}
