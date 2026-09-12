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
        // ---- The pastel-dawn palette, sampled from the two mockups in Art/Mockup. ----
        // Sky: peach at the top through blush to a lilac horizon. Sun: pale cream. Everything on
        // the island is lit warm and sits high in value; the only saturated things in frame are the
        // grass, the orb's cyan and the portal's amber, which is exactly the mockup's hierarchy.
        // Authored a step MORE saturated than the mockup reads: the grade (bloom tint, white
        // balance, vignette) and the pastel ambient all pull towards grey, and the first capture
        // came out mauve. These land on the mockup's peach/blush/lilac on screen.
        public static readonly Color BgTop = Hex("#FBDCC6");
        public static readonly Color BgMid = Hex("#F2CBD6");
        public static readonly Color BgBottom = Hex("#E6DCEF");
        public static readonly Color SunColor = Hex("#FFF0C4");
        public static readonly Color HaloColor = Hex("#FFD6B4");

        // Fresh spring green, lighter and warmer than the olive of the night theme. The cap in the
        // mockup is almost a single flat value with a slightly darker fringe.
        public static readonly Color Grass = Hex("#8AB069");
        public static readonly Color GrassDark = Hex("#729D57");
        public static readonly Color GrassTip = Hex("#A2CC78");
        // Cream limestone. Lit face #E8E1D5 on the mockup, mortar lines a shade darker, lower rows
        // in a warm shadow that never goes grey.
        // Stone. Measured off the GAMEPLAY mockup's wall, not the menu's: every front face there
        // samples within a few points of (172,161,153), a warm mid grey. The first pastel pass
        // used the menu painting's lighter reading and the island came out near-white (fronts at
        // ~211,204,198 in capture) - the user's "overkill of white". Under the flat #E8E5E5
        // ambient these albedos land the fronts at ~176,165,156.
        public static readonly Color Stone = Hex("#BFB7B0");
        public static readonly Color StoneMid = Hex("#B8B1AA");
        public static readonly Color StoneDark = Hex("#988F86");
        public static readonly Color StoneLight = Hex("#C8C0B9");
        public static readonly Color Dirt = Hex("#B48F6C");
        public static readonly Color Wood = Hex("#BE8F62");
        public static readonly Color WoodDark = Hex("#8A6444");
        public static readonly Color PlayerBody = Hex("#F2F4F8");
        public static readonly Color PlayerShade = Hex("#D2D8E0");
        // The orb's cyan and the portal's amber are the two "light" colours of the theme.
        public static readonly Color Cyan = Hex("#7FE6FF");
        public static readonly Color CyanDeep = Hex("#3FB6DC");
        public static readonly Color Warm = Hex("#FFD98F");
        public static readonly Color WarmDeep = Hex("#F5B15E");
        public static readonly Color Fire = Hex("#FF8A3D");
        public static readonly Color Foliage = Hex("#6AA64B");
        public static readonly Color FoliageDark = Hex("#4F8B3C");
        public static readonly Color RockGrey = Hex("#B9B6B0");
        public static readonly Color Metal = Hex("#A3A8AE");
        public static readonly Color SpikeSteel = Hex("#C9CDD2");
        public static readonly Color ShadowTint = new Color(0.62f, 0.55f, 0.62f, 1f);

        // Background mountains, three layers of haze towards the sky. Sampled off the mockup:
        // the far ridges are almost the sky's lilac, the near ones a soft teal-grey.
        // The mockup's ridges go from lilac at the back to grey-teal at the front (near ridges
        // sample ~(150,165,160)); the first pass had every ridge lilac-blue.
        // Clouds are peach-cream where they are thick (mockup: 254,229,208) and thin out to the
        // sky's lilac (220,212,220). They were pure white before and covered half the frame.
        public static readonly Color CloudColor = Hex("#FEE5D0");
        public static readonly Color CloudShade = Hex("#E6D3DA");

        // Flowers: three petal colours from the mockup's grass edge.
        // Not pure white: a lit white petal crossed the bloom threshold and turned into a
        // fist-sized halo on the grass. Ivory stays under it.
        public static readonly Color FlowerWhite = Hex("#EEE3D0");
        public static readonly Color FlowerYellow = Hex("#FFE07A");
        public static readonly Color FlowerPink = Hex("#FFB7C9");
        public static readonly Color FlowerCenter = Hex("#FFC847");

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
        public const string MEnemyTeeth = "M_EnemyTeeth";
        public const string MGlowEvil = "M_GlowEvil";
        public const string MEnemyAura = "M_EnemyAura";
        public const string MTrail = "M_Trail";
        public const string MBounce = "M_Bounce";
        public const string MOcean = "M_Ocean";
        public const string MFarStone = "M_FarStone";
        public const string MFarGrass = "M_FarGrass";
        // Pastel theme additions.
        public const string MFoliageWind = "M_FoliageWind";   // tufts, flower stems: sway up from the base
        public const string MVine = "M_Vine";
        public const string MFringe = "M_Fringe";                 // hangs down, sways from the attachment
        public const string MFlowerWhite = "M_FlowerWhite";
        public const string MFlowerYellow = "M_FlowerYellow";
        public const string MFlowerPink = "M_FlowerPink";
        public const string MFlowerCenter = "M_FlowerCenter";
        public const string MOrbGlass = "M_OrbGlass";
        public const string MOrbCore = "M_OrbCore";
        public const string MOrbRing = "M_OrbRing";
        public const string MOrbGlow = "M_OrbGlow";
        public const string MOrbSpark = "M_OrbSpark";
        public const string MPortalStud = "M_PortalStud";
        public const string MPortalFloor = "M_PortalFloor";   // the pool of light on the grass at the threshold
        public const string MArchStone = "M_ArchStone";   // the doorway's cream masonry
        public const string MArchCarve = "M_ArchCarve";   // its carved diamonds
        public const string MArchInner = "M_ArchInner";   // its faces that look into the doorway
        public const string MPortalSpark = "M_PortalSpark"; // warm four-point glints in the doorway
        public const string MPortalEnergyFar = "M_PortalEnergyFar";

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
            // Pastel theme: the far islets are the same cream and green, hazed towards the sky.
            Lit(MFarStone, Hex("#C7BFB7"), 0.08f);
            Lit(MFarGrass, Hex("#A8C68F"), 0.06f);

            // Flowers and the portal's diamond studs.
            Lit(MFlowerWhite, FlowerWhite, 0.25f);
            Lit(MFlowerYellow, FlowerYellow, 0.25f);
            Lit(MFlowerPink, FlowerPink, 0.25f);
            Lit(MFlowerCenter, FlowerCenter, 0.3f);
            // The doorway is CREAM, clearly lighter than the island's grey stone: the mockup's arch
            // samples at (250-255, 234-243, 205-221) against a wall at (172,161,153). The diamonds
            // are carved, a shade darker than the stone; only the keystone's glows, faintly.
            // A soft specular sheen on the masonry: with the flat ambient the chamfers were the only
            // thing giving the stones form, and the painting's stone has a gentle satin surface.
            Lit(MArchStone, Hex("#F9F1E4"), 0.32f, 0f, specular: true);   // capture jambs were (224,207,188) at #F2E8D8; the painting's are ~(250,238,210)
            Lit(MArchCarve, Hex("#DCD0BE"), 0.22f);
            Emissive(MPortalStud, Hex("#FFF2D0"), Hex("#FFE7B0"), 0.45f, 0.4f);
            // The faces that look into the doorway: cream stone with the portal's warmth falling on
            // it. Emission, not a light, so it is stable and free; kept well under bloom.
            Emissive(MArchInner, Hex("#F7E9D2"), Hex("#FFC66E"), 0.55f, 0.3f);

            // Enemy: a bruised magenta that is in nobody else's palette, with near-black spikes.
            // Hostile has to read in one glance against grey rock and green grass.
            // The enemy: a wet, dark bruise-purple body, near-black spikes and mouth, bone teeth.
            // Darker than anything else on the island so it reads as a hole in the scene.
            // Painting samples: body centre (130,79,88), lit upper-left (207,145,149) - a dark plum
            // with a soft satin sheen, not a hot magenta. The sheen is real specular; the body used to
            // read pink because its own red eye light and aura lit it.
            Lit(MEnemy, Hex("#6E2046"), 0.55f, 0f, specular: true);
            Lit(MEnemySpike, Hex("#2A1020"), 0.45f);
            Lit(MEnemyTeeth, Hex("#EDE6D6"), 0.35f);   // no longer on the enemy; kept for any level that references it
            // Spring pad cap: soft coral, the one warm-saturated thing on the island, so "that
            // throws you" reads at once without fighting the pastel.
            Lit(MBounce, Hex("#FF9E86"), 0.50f);

            // The key has to out-read every rock in the level from across the screen, so it gets a
            // real emissive rather than just a bright albedo. Kept below the door's amber so the
            // goal still wins the frame.
            Emissive(MKey, Hex("#FFE08A"), Hex("#FFD060"), 1.7f, 0.42f);

            Emissive(MGlowWarm, Warm, Warm, 2.6f, 0.3f);
            Emissive(MGlowCyan, Cyan, Cyan, 2.4f, 0.3f);
            Emissive(MGlowFire, Fire, Fire, 3.8f, 0.2f);
            // Enemy eyes. Enemy.cs drives the emission per instance; this is only the resting look.
            // The eyes are painted sprites (Tex_EnemyEye on alpha-blended Unlit), not emissive
            // slabs: an almond shape with a soft edge that Enemy.cs pushes past the bloom threshold
            // for the white-hot core. Colour is driven per instance via _BaseColor.
            UnlitTextured(MGlowEvil, MakeEyeTexture("Tex_EnemyEye", 128), new Color(1f, 0.30f, 0.30f, 1f), additive: false);
            Emissive(MPlateOn, Hex("#F08A7A"), Hex("#F08A7A"), 1.1f, 0.3f);

            // Screen furniture and effects.
            var ringTex = MakeRingTexture("Tex_AnchorRing", 256);
            var blobTex = MakeBlobTexture("Tex_BlobShadow", 128);
            var glowTex = MakeBlobTexture("Tex_PortalGlow", 128, 1.6f);
            var bgTex = MakeGradientTexture("Tex_Background", 8, 256, BgTop, BgBottom);
            var shadowTex = MakeContactShadowTexture("Tex_ContactShadow", 128);
            var starTex = MakeStarTexture("Tex_Star", 128);
            var whiteTex = MakeSolidTexture("Tex_White", 4, Color.white);

            UnlitTextured(MAnchorRing, ringTex, Cyan * 1.5f, additive: true);
            // Two additive spills. These blend SrcAlpha One, so what gets ADDED is tint (linear) x
            // alpha x blob: keep that small wherever it overlaps the doorway or the frame, or the
            // field clips to white (it did). The halo round the arch is faint; the pool on the
            // grass in front is the strong one - the mockup's grass under the door goes yellow,
            // (252,234,155) against (184,236,163) beside it, which a warm add of ~(0.4,0.2,0.05)
            // linear on our grass reproduces.
            UnlitTextured(MPortalGlow, glowTex, new Color(1f, 0.80f, 0.50f, 0.08f), additive: true);
            UnlitTextured(MPortalFloor, glowTex, new Color(1f, 0.72f, 0.38f, 0.14f), additive: true);
            UnlitTextured(MPortalSpark, starTex, new Color(1f, 0.93f, 0.78f, 1f), additive: true);
            // Soft pink-red, wide and faint (painting: (218,190,196) round the body), not a red flare.
            UnlitTextured(MEnemyAura, glowTex, new Color(1f, 0.32f, 0.42f, 0.30f), additive: true);
            // Cyan and faint: additive over a pale sky, a brighter trail just reads as white.
            UnlitTextured(MTrail, glowTex, new Color(0.45f, 0.86f, 1f, 0.34f), additive: true);
            MultiplyTextured(MBlobShadow, shadowTex);
            UnlitTextured(MParticleAdd, blobTex, Color.white, additive: true);
            UnlitTextured(MParticleSoft, blobTex, Color.white, additive: false);

            // The orb's parts. The glass is its own shader; the rest are additive sprites.
            // Core star white; sparkles and the ring's bead mint-white; the orbit ring itself a thin
            // pale translucent line (it was a thick bright cyan hoop); the halo mint-white so the
            // grass under the orb takes a soft cool tint instead of a green flare.
            // The star is PAINTED (alpha-blended), not added: an additive white star over a body that
            // is already near-white saturates to nothing. Painted, it is crisp white on tinted pearl.
            UnlitTextured(MOrbCore, starTex, new Color(1f, 1f, 1f, 1f), additive: false);
            UnlitTextured(MOrbSpark, starTex, new Color(0.90f, 0.99f, 0.97f, 1f), additive: true);
            UnlitTextured(MOrbRing, whiteTex, new Color(0.94f, 1f, 0.98f, 0.22f), additive: true);
            UnlitTextured(MOrbGlow, glowTex, new Color(0.78f, 0.98f, 0.93f, 0.8f), additive: true);
            // Draw order round the pearl. The glass is Transparent+2 and WRITES depth (see PtwOrb.shader),
            // so the star, the ring and its bead go AFTER it (+3): the star sits just inside the front
            // surface and paints over the body, the ring's front half shows and its back half is
            // depth-rejected by the sphere. Drawn before the glass, as they were, an 88%-opaque body
            // simply covered them - the star vanished. The halo stays at 3000, behind the ball.
            foreach (var id in new[] { MOrbCore, MOrbSpark, MOrbRing })
            {
                var om = Get(id);
                // URP's material validation (run on import, including in batch runs) recomputes a
                // URP Unlit material's queue from its surface type whenever _QueueControl is Auto,
                // which silently put these back to 3000 after every build. User override + offset
                // is the supported way to hold a custom queue on a URP material.
                om.SetFloat("_QueueOffset", 3f);
                om.SetFloat("_QueueControl", 1f);
                om.renderQueue = (int)RenderQueue.Transparent + 3;
                EditorUtility.SetDirty(om);
            }

            // The sky's mountains and clouds are the artists' painted layers now - see
            // SkySprite, called by the scene builder for each piece it places.

            BuildBackdrop();
            BuildPortalEnergy();
            BuildWater();
            BuildFoliage();
            BuildOrb();

            // The pool's translucent body. The side-on camera sees this face, not the surface
            // tile, so it carries the colour: a pastel pool blue, ~55% opaque, faintly glossy.
            LitTransparent(MWaterBody, new Color(0.55f, 0.83f, 0.93f, 0.55f), 0.55f);

            AssetDatabase.SaveAssets();
        }

        /// <summary>Wind-swaying vegetation, two configurations of the one shader.</summary>
        static void BuildFoliage()
        {
            var up = LoadOrCreateShader(MFoliageWind, "PTW/Foliage");
            if (up != null)
            {
                up.SetColor("_BaseColor", GrassDark);
                up.SetColor("_TipColor", GrassTip);
                up.SetFloat("_WindAmount", 0.045f);
                up.SetFloat("_WindSpeed", 1.4f);
                up.SetFloat("_PivotY", 0f);
                up.SetFloat("_WeightSign", 1f);
                up.SetFloat("_Length", 0.32f);
                up.SetFloat("_Wrap", 0.5f);
                EditorUtility.SetDirty(up);
            }

            var down = LoadOrCreateShader(MVine, "PTW/Foliage");
            if (down != null)
            {
                down.SetColor("_BaseColor", Hex("#6BA34C"));
                down.SetColor("_TipColor", Hex("#92C86A"));
                down.SetFloat("_WindAmount", 0.07f);
                down.SetFloat("_WindSpeed", 1.1f);
                down.SetFloat("_PivotY", 0f);
                down.SetFloat("_WeightSign", -1f);
                down.SetFloat("_Length", 1.3f);
                down.SetFloat("_Wrap", 0.5f);
                EditorUtility.SetDirty(down);
            }

            // The turf overhang along every grass edge. Its own material: lighter than the vines and
            // lit almost flat (high wrap), because blades hanging off a ledge face away from the key
            // light and, on the vine material, drew a dark saw-tooth line under every cap.
            var fringe = LoadOrCreateShader(MFringe, "PTW/Foliage");
            if (fringe != null)
            {
                fringe.SetColor("_BaseColor", Hex("#7DB65A"));
                fringe.SetColor("_TipColor", Hex("#9ACB70"));
                fringe.SetFloat("_WindAmount", 0.03f);
                fringe.SetFloat("_WindSpeed", 1.2f);
                fringe.SetFloat("_PivotY", 0f);
                fringe.SetFloat("_WeightSign", -1f);
                fringe.SetFloat("_Length", 0.35f);
                fringe.SetFloat("_Wrap", 0.85f);
                EditorUtility.SetDirty(fringe);
            }
        }

        /// <summary>The glass body of the orb. See PtwOrb.shader for what each term does.</summary>
        static void BuildOrb()
        {
            var m = LoadOrCreateShader(MOrbGlass, "PTW/Orb");
            if (m == null) return;
            // An opalescent pearl, mostly opaque, after the paintings: body samples there are
            // (207,239,251) upper-left, (241,223,242) right, (214,249,235) below, white at the core.
            // The first version was clear glass (alpha 0.32) with a hard cyan rim at 2.2 and read as
            // a bubble outline with a flare in it.
            m.SetColor("_BodyColor", new Color(0.86f, 0.95f, 0.99f, 0.88f));   // a tinted body, so the white star has something to stand on
            m.SetColor("_TintCyan", new Color(0.62f, 0.90f, 0.99f));
            m.SetColor("_TintPink", new Color(0.95f, 0.80f, 0.94f));
            m.SetColor("_TintMint", new Color(0.72f, 0.96f, 0.84f));
            m.SetFloat("_CentreGlow", 0.05f);   // at 0.35 the white centre swallowed the star; even 0.12 did
            m.SetColor("_RimColor", Hex("#9FE8E4"));
            m.SetFloat("_RimPower", 3.0f);
            m.SetFloat("_RimStrength", 0.6f);
            m.SetColor("_IridA", Hex("#DBD6FA"));   // lilac, not pink: the painting's left edge is cyan-lilac, only its right is pink
            m.SetColor("_IridB", Hex("#B3F5EA"));
            m.SetFloat("_IridStrength", 0.22f);
            m.SetVector("_SpecDir", new Vector4(0.70f, 0.75f, -0.35f, 0f));   // the highlight sits upper-right in the paintings, clear of the star
            m.SetFloat("_SpecPower", 40f);
            m.SetFloat("_SpecStrength", 0.75f);
            m.SetFloat("_CrescentStrength", 0.9f);
            m.renderQueue = (int)RenderQueue.Transparent + 2;   // over the water, under the UI
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// The pastel dawn sky: three-stop gradient, a pale sun upper right, drifting cloud puffs.
        /// The sun sits where the mockup's does - about 73% across and 13% down the frame, which
        /// on the 1.35x overscanned quad is (0.17, 0.27) in quad space.
        /// </summary>
        static void BuildBackdrop()
        {
            var m = LoadOrCreateShader(MBackground, "PTW/Backdrop");
            if (m == null) return;
            m.SetColor("_TopColor", BgTop);
            m.SetColor("_MidColor", BgMid);
            m.SetColor("_BottomColor", BgBottom);
            m.SetFloat("_MidPoint", 0.5f);
            m.SetColor("_GlowColor", SunColor);
            m.SetColor("_HaloColor", HaloColor);
            m.SetVector("_GlowCenter", new Vector4(0.17f, 0.27f, 0f, 0f));
            m.SetFloat("_SunRadius", 0.058f);
            m.SetFloat("_SunSoft", 0.02f);
            m.SetFloat("_GlowRadius", 0.5f);
            m.SetFloat("_GlowStrength", 0.36f);
            m.SetFloat("_GlowAspect", 1.0f);
            m.SetColor("_CloudColor", CloudColor);
            m.SetColor("_CloudShade", CloudShade);
            m.SetFloat("_CloudStrength", 0f);      // the clouds are painted layers now (BuildSkyLayers); the sky is a clean gradient
            m.SetFloat("_CloudCover", 0.36f);
            m.SetFloat("_CloudScale", 2.1f);
            m.SetFloat("_CloudSpeed", 0.007f);
            m.SetVector("_CloudBand", new Vector4(-0.5f, 0.34f, 0f, 0f));
            m.SetFloat("_EdgeDarken", 0f);
            m.renderQueue = (int)RenderQueue.Background;
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// Swirling additive energy for the doorway: warm cream core, amber edge, like the mockup.
        /// Intensity is modest so the rings stay visible - the first pass blew out to a white oval.
        /// (ExitPortal drives the runtime intensity; the far islets use the dimmer copy below.)
        /// </summary>
        static void BuildPortalEnergy()
        {
            // Geometry of the ArchFill mesh (see PtwMeshes): x +/-0.40, y 0.02..ArchApex.
            float top = PtwMeshes.ArchApex, bottom = 0.02f;
            var centre = new Vector4(0f, (top + bottom) * 0.5f, 0f, 0f);
            var extents = new Vector4(0.40f, (top - bottom) * 0.5f, 0f, 0f);

            // Mockup interior samples: bottom/centre (253,231,184), top (246,184,95), core (254,237,201).
            void Field(Material mat, float intensity, float lineStrength)
            {
                mat.SetColor("_CoreColor", Hex("#FFE7B8"));
                mat.SetColor("_EdgeColor", Hex("#F6B85F"));
                mat.SetColor("_LineColor", Hex("#FFF8EC"));
                mat.SetFloat("_Intensity", intensity);
                mat.SetVector("_Center", centre);
                mat.SetVector("_Extents", extents);
                mat.SetVector("_StarOffset", new Vector4(0f, -0.09f, 0f, 0f));
                mat.SetFloat("_RingSpacing", 0.115f);
                mat.SetFloat("_RingCount", 5f);
                mat.SetFloat("_Spokes", 12f);
                mat.SetFloat("_LineWidth", 0.009f);
                mat.SetFloat("_LineStrength", lineStrength);
                mat.SetFloat("_CoreSize", 0.06f);
                mat.SetFloat("_GlowRadius", 0.30f);
                mat.SetFloat("_Pulse", 0.08f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
            }

            var far = LoadOrCreateShader(MPortalEnergyFar, "PTW/PortalEnergy");
            if (far != null) Field(far, 0.92f, 0.30f);

            var m = LoadOrCreateShader(MPortalEnergy, "PTW/PortalEnergy");
            if (m == null) return;
            Field(m, 1.0f, 0.42f);
        }

        /// <summary>Water in the pastel theme: a clear pool blue, soft foam.</summary>
        static void BuildWater()
        {
            var m = LoadOrCreateShader(MWater, "PTW/Water");
            if (m == null) return;
            m.SetColor("_ShallowColor", Hex("#8BD3E6"));
            m.SetColor("_DeepColor", Hex("#3F8FB8"));
            m.SetColor("_FoamColor", Hex("#F2FCFF"));
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
        /// <summary>A painted sky piece (Art/layered-background) on the fog-free sprite shader.</summary>
        public static Material SkySprite(PtwSkyAssets.Piece piece)
        {
            var tex = PtwSkyAssets.Load(piece);
            if (tex == null) return null;
            var m = LoadOrCreateShader(piece.Id, "PTW/SkySprite");
            if (m == null) return null;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

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

        /// <summary>
        /// One angry eye: an almond, rounder at the inner end (u = 0) and pointed at the outer (u = 1),
        /// solid in the middle with a soft edge and a faint glow round it. White; the material tints it.
        /// </summary>
        public static Texture2D MakeEyeTexture(string id, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f - half) / half, py = (y + 0.5f - half) / half;   // -1..1
                    // Half-height of the almond at this u: an ellipse squeezed towards the outer end.
                    // Chunky: the painted eye is nearly half as tall as it is long. At 0.36 it was a slit.
                    float taper = Mathf.Lerp(1f, 0.5f, (px + 1f) * 0.5f);
                    float hh = 0.52f * Mathf.Pow(Mathf.Clamp01(1f - px * px), 0.5f) * taper;
                    float inside = hh > 0.001f ? Mathf.Abs(py) / hh : 9f;
                    float body = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((inside - 0.82f) / 0.30f));
                    float glow = Mathf.Clamp01(1f - Mathf.Sqrt(px * px * 0.5f + py * py * 2.2f)) * 0.30f;
                    float a = Mathf.Clamp01(body + glow * (1f - body));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        /// <summary>A four-point star with a soft glow: the orb's core and its sparkles.</summary>
        public static Texture2D MakeStarTexture(string id, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f - half) / half, py = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(px * px + py * py);
                    // A four-point TWINKLE, the painting's star: the astroid |x|^(2/3)+|y|^(2/3)<=1,
                    // whose sides curve inward to sharp points on the axes. (The earlier long thin
                    // arms read as a lens flare.) Soft-edged, over a soft round glow.
                    const float reach = 0.80f;
                    float ax = Mathf.Pow(Mathf.Abs(px) / reach, 2f / 3f);
                    float ay = Mathf.Pow(Mathf.Abs(py) / reach, 2f / 3f);
                    // NB Mathf.SmoothStep(a, b, t) INTERPOLATES a..b by t - it is not HLSL's threshold
                    // smoothstep. Written the other way round this star peaked at alpha 0.1 and the
                    // orb had no star at all (and every sparkle built from it was a ghost).
                    float star = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((ax + ay - 0.90f) / 0.16f));
                    float glow = Mathf.Pow(Mathf.Clamp01(1f - d / 0.95f), 2.5f) * 0.42f;
                    float a = Mathf.Clamp01(star + glow);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, false);
            return SaveTexture(tex, id);
        }

        public static Texture2D MakeSolidTexture(string id, int size, Color color)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) tex.SetPixel(x, y, color);
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
