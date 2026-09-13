using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Builds the one and only gameplay scene, plus the render pipeline settings and post stack
    /// that the art direction depends on.
    ///
    /// The lighting rig is the single most important thing in this file. Measured off the
    /// reference, the top face of a block is up to 8.4x brighter than its sides - that enormous
    /// ratio is what makes flat untextured colour read as sculpted form. It is produced by a
    /// bright neutral key from the upper left plus a strongly COOL BLUE ambient (roughly 3:1), and
    /// deepened in the crevices by SSAO. Get that wrong and no amount of palette accuracy helps.
    /// </summary>
    public static class PtwScene
    {
        public const string ScenePath = "Assets/PullTheWorld/Scenes/Game.unity";
        public const string ProfilePath = "Assets/PullTheWorld/Art/PostProcessProfile.asset";
        public const string FontPath = "Assets/PullTheWorld/Art/Fonts/Poppins-Bold SDF.asset";
        public const string FontSemiPath = "Assets/PullTheWorld/Art/Fonts/Poppins-SemiBold SDF.asset";

        // Measured: a white-albedo surface in shadow sits at #697CA5, so that IS the ambient.
        // Pastel dawn: a warm cream sun and a bright, slightly lilac ambient. Shadows on the
        // mockup never go grey - a shaded cream face is still cream.
        static readonly Color AmbientColor = PtwArt.Hex("#E8E5E5");   // near-neutral and high: the mockup is lit almost flat, its stone is grey not pink
        static readonly Color KeyColor = PtwArt.Hex("#FFE7C8");   // warmer: the sun, not a white studio lamp

        // ==================================================================== entry point ====
        public static void Build()
        {
            PtwPaths.EnsureFolder("Assets/PullTheWorld/Scenes");
            ConfigureUrp();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("~Systems");
            systems.AddComponent<GameDirector>();
            systems.AddComponent<PtwAudio>();
            systems.AddComponent<PtwMusic>();
            systems.AddComponent<DangerVignette>();        // finds the Volume itself
            systems.AddComponent<PullTheWorld.Ads.AdsManager>();   // adds the fake provider itself

            Camera cam = BuildCamera(out PlaneCameraRig camRig);
            BuildLighting();
            BuildVolume();

            // ---- world ----
            // One kinematic body means every static level collider below it becomes a single
            // compound PhysX actor: one thing to rotate, and no broadphase churn while turning.
            var worldRoot = new GameObject("WorldRoot");
            var wrb = worldRoot.AddComponent<Rigidbody>();
            wrb.isKinematic = true;
            wrb.useGravity = false;
            wrb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            wrb.interpolation = RigidbodyInterpolation.Interpolate;

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PtwPrefabs.Play + "/PlayerRig.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.position = new Vector3(0f, 1.5f, 0f);

            // The camera frames the LEVEL, not the player. The player moves now, and a camera that
            // followed them would re-introduce exactly the ambiguity v2 exists to remove: a moving
            // camera over a rotating world reads as nothing in particular. The pivot is the origin,
            // so that is what the camera is centred on, forever.
            camRig.Apply();

            var rotator = systems.AddComponent<WorldRotator>();
            PtwPrefabs.Wire(rotator, "worldRoot", worldRoot.transform);
            PtwPrefabs.Wire(rotator, "viewCamera", cam);

            var input = systems.AddComponent<RotateInput>();
            PtwPrefabs.Wire(input, "rotator", rotator);

            var levels = systems.AddComponent<LevelManager>();
            PtwPrefabs.Wire(levels, "rotator", rotator);
            PtwPrefabs.Wire(levels, "player", player.GetComponent<PlayerBody>());
            PtwPrefabs.Wire(levels, "levelParent", worldRoot.transform);
            PtwPrefabs.Wire(levels, "cameraRig", camRig);
            PtwPrefabs.Wire(levels, "sky", cam.GetComponentInChildren<SkyTheme>());
            levels.EditorSetLevels(PtwLevels.LoadAll());
            // The pieces the world's small floating rocks are built from at runtime (LevelManager.BuildDecor).
            PtwPrefabs.WireArray(levels, "decorBlocks", new UnityEngine.Object[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Grass.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Stone.prefab"),
            });
            PtwPrefabs.WireArray(levels, "decorProps", new UnityEngine.Object[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_TreeSmall.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_Bush.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_Vine.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_Flower.prefab"),
            });

            // (The far scenery now lives in the sky, built with the camera - see BuildSkyLayers.)
            var (burst, roll) = BuildDust();

            var feedback = systems.AddComponent<ImpactFeedback>();
            PtwPrefabs.Wire(feedback, "player", player.GetComponent<PlayerBody>());
            PtwPrefabs.Wire(feedback, "rotator", rotator);
            PtwPrefabs.Wire(feedback, "dust", burst);
            PtwPrefabs.Wire(feedback, "rollDust", roll);

            BuildUi(levels, cam);

            // Drop Level 1 into the scene as an authoring preview. Without this, opening Game.unity
            // shows nothing but a character floating in an empty backdrop, because levels are
            // normally spawned at runtime. LevelManager deletes it on Start.
            var previewSrc = AssetDatabase.LoadAssetAtPath<GameObject>(PtwLevels.Dir + "/Level_01.prefab");
            if (previewSrc)
            {
                var preview = (GameObject)PrefabUtility.InstantiatePrefab(previewSrc, worldRoot.transform);
                preview.transform.localPosition = Vector3.zero;
                preview.name = "Level_01 [editor preview - replaced on Play]";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();

            Debug.Log("PTW: scene built at " + ScenePath);
        }

        // ======================================================================== camera =====
        static Camera BuildCamera(out PlaneCameraRig rigOut)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;   // projection, clipping and framing are PlaneCameraRig's
            // Matches the TOP of the backdrop gradient, so if the fill quad ever fails to cover the
            // frame the uncovered area blends instead of banding.
            cam.backgroundColor = PtwArt.BgTop;
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            data.renderShadows = true;

            go.AddComponent<AudioListener>();
            rigOut = go.AddComponent<PlaneCameraRig>();

            // Backdrop welded to the camera so it can never drift.
            var bg = PtwPrefabs.MeshNode("Backdrop", "Mesh_QuadXY", go.transform, PtwArt.MBackground);
            var bgr = bg.GetComponent<MeshRenderer>();
            bgr.shadowCastingMode = ShadowCastingMode.Off;
            bgr.receiveShadows = false;
            var fill = bg.AddComponent<ScreenFillQuad>();
            PtwPrefabs.Wire(fill, "targetCamera", cam);

            // Per-chapter sky colour lives on the same quad; LevelManager drives it.
            var sky = bg.AddComponent<SkyTheme>();
            PtwPrefabs.Wire(sky, "target", bgr);

            // Pollen: a few slow motes drifting in the air, the colour of the sun (SkyTheme tints
            // them). Parented to the camera like the backdrop so they are always in frame;
            // simulated in world space so a reframe does not drag them.
            PtwPrefabs.Wire(sky, "fireflies", MakeFireflies(go.transform));

            // The rest of the sky: the painted mountain ridges and cloud banks, each a child of
            // the camera at its own depth, lagging the push by its share (see BuildSkyLayers).
            BuildSkyLayers(cam);

            // The shared parallax shift the layers read (tilt of the level, position of the orb).
            // On the camera so it exists exactly once and dies with the scene.
            go.AddComponent<SkyParallax>();

            return cam;
        }

        /// <summary>
        /// The sky, built from the artists' painted layers (Art/layered-background) and composed
        /// after their reference painting (reference.png, 941 x 1672 - the same 9:16 as the phone,
        /// so a point in the painting IS a viewport position). Each piece is a quad that is a child
        /// of the camera at its own distance down the line of sight, placed by its PEAK: the peak
        /// is the one point of a ridge the painting shows unambiguously - every base is lost in
        /// haze or a cloud bank - so a ridge is pinned by where its peak is in the painting and
        /// sized by how far its slopes have to run before the cloud in front of it takes over.
        ///
        /// The painting's depth logic, which the ordering here follows exactly: ridges recede UP
        /// the frame, and between each pair of ranges lies a cloud sea that hides the base of the
        /// range behind it and is hidden by the peaks of the range in front. Back to front: the
        /// far lilac skyline and the wisps by the sun; the upper corner clouds (in front of the
        /// lilac slopes); the middle lavender range; the right-hand cloud bank; the sage and blue
        /// ranges behind the island; the diagonal cloud row and the low right cloud (over their
        /// bases); the main sage and blue ranges; the big lower cloud bank; the low lilac range;
        /// the lower right bank; the bottom ranges; the three foreground clouds. The reconstructed
        /// pieces end in straight bases and carry stronger colour than the mist-washed painting,
        /// so each ridge gets a base fade and a haze towards the local sky (PtwArt.SkySprite).
        ///
        /// Left out: the centre-low lavender peak (it stands behind the live island on every
        /// level) and the very faint haze ridge behind the lower bank.
        ///
        /// Every layer stands behind the level after next (185+) except the three foreground
        /// clouds at ~40; distances are staggered so they sort back to front, and the parallax
        /// share grows with nearness - a far ridge lags the camera's push by a twentieth, a
        /// foreground cloud by two fifths.
        /// </summary>
        static void BuildSkyLayers(Camera cam)
        {
            var root = new GameObject("Sky");
            root.transform.SetParent(cam.transform, false);

            // The painting's mist, by band: peach up high, blush in the middle, cream-pink low.
            Color hazeFar = new Color(0.94f, 0.86f, 0.88f), hazeMid = new Color(0.87f, 0.87f, 0.93f),
                  hazeLow = new Color(0.92f, 0.90f, 0.95f), hazeCloud = new Color(1.0f, 0.935f, 0.87f);

            // (piece, distance, parallax, peak x / y IN THE PAINTING, scale (painting px per piece px),
            //  base fade (fraction of content height), haze amount, haze colour, sway amplitude, sway period)
            var layers = new[]
            {
                // The wisps by the sun and the far lilac skyline.
                (PtwSkyAssets.Cloud02, 520f, 0.03f, 380f, 196f, 0.22f, 0f, 0.45f, hazeCloud, 0.004f, 90f),
                (PtwSkyAssets.Cloud03, 510f, 0.03f, 700f, 264f, 0.14f, 0f, 0.45f, hazeCloud, 0.004f, 95f),
                (PtwSkyAssets.Mountain01, 480f, 0.04f, 340f, 261f, 0.50f, 0.6f, 0.45f, hazeFar, 0f, 0f),
                (PtwSkyAssets.Mountain03, 460f, 0.045f, 860f, 300f, 0.32f, 0.6f, 0.45f, hazeFar, 0f, 0f),
                (PtwSkyAssets.Mountain02, 450f, 0.05f, 40f, 266f, 0.50f, 0.6f, 0.45f, hazeFar, 0f, 0f),
                // The upper corner clouds, in front of the lilac slopes.
                (PtwSkyAssets.Cloud04, 440f, 0.05f, 925f, 170f, 0.19f, 0f, 0.45f, hazeCloud, 0.005f, 80f),
                (PtwSkyAssets.Cloud01, 420f, 0.055f, 180f, 176f, 0.33f, 0f, 0.45f, hazeCloud, 0.006f, 72f),
                // The middle lavender range and the right-hand bank over its slopes.
                (PtwSkyAssets.Mountain04, 400f, 0.07f, 500f, 455f, 0.45f, 0.45f, 0.45f, hazeFar, 0f, 0f),
                (PtwSkyAssets.Cloud05, 380f, 0.08f, 900f, 400f, 0.23f, 0f, 0.45f, hazeCloud, 0.006f, 70f),
                // The sage and blue ranges behind the island.
                (PtwSkyAssets.Mountain05, 360f, 0.09f, 75f, 370f, 0.60f, 0.40f, 0.33f, hazeMid, 0f, 0f),
                (PtwSkyAssets.Mountain06, 350f, 0.10f, 1020f, 516f, 0.50f, 0.40f, 0.35f, hazeMid, 0f, 0f),
                // The diagonal cloud row and the low right cloud, over their bases.
                (PtwSkyAssets.Cloud06, 320f, 0.115f, 80f, 540f, 0.48f, 0f, 0.45f, hazeCloud, 0.008f, 62f),
                (PtwSkyAssets.Cloud07, 300f, 0.13f, 900f, 650f, 0.28f, 0f, 0.45f, hazeCloud, 0.008f, 66f),
                // The main sage range (left) and blue range (right), whose peaks rise out of that row.
                (PtwSkyAssets.Mountain07, 290f, 0.14f, 94f, 638f, 0.55f, 0.40f, 0.33f, hazeMid, 0f, 0f),
                (PtwSkyAssets.Mountain09, 275f, 0.15f, 830f, 862f, 0.50f, 0.40f, 0.35f, hazeMid, 0f, 0f),
                // The big lower bank over their lower slopes.
                (PtwSkyAssets.Cloud08, 250f, 0.17f, 70f, 822f, 0.55f, 0f, 0.40f, hazeCloud, 0.010f, 56f),
                // The low lilac range, the lower right bank, the bottom ranges.
                (PtwSkyAssets.Mountain11, 230f, 0.19f, 45f, 1190f, 0.42f, 0.50f, 0.55f, hazeLow, 0f, 0f),
                (PtwSkyAssets.Cloud09, 220f, 0.20f, 900f, 1085f, 0.50f, 0f, 0.40f, hazeCloud, 0.010f, 54f),
                (PtwSkyAssets.Mountain14, 210f, 0.22f, 470f, 1400f, 0.25f, 0.55f, 0.50f, hazeLow, 0f, 0f),
                (PtwSkyAssets.Mountain12, 200f, 0.23f, 960f, 1235f, 0.50f, 0.40f, 0.45f, hazeLow, 0f, 0f),
                (PtwSkyAssets.Mountain13, 185f, 0.25f, 30f, 1300f, 0.45f, 0.35f, 0.30f, hazeLow, 0f, 0f),
                // The foreground clouds, in front of the island's tip.
                (PtwSkyAssets.Cloud10, 40f, 0.38f, 60f, 1402f, 0.40f, 0f, 0.35f, hazeCloud, 0.012f, 48f),
                (PtwSkyAssets.Cloud11, 42f, 0.38f, 370f, 1452f, 0.34f, 0f, 0.35f, hazeCloud, 0.011f, 52f),
                (PtwSkyAssets.Cloud12, 38f, 0.40f, 880f, 1352f, 0.42f, 0f, 0.35f, hazeCloud, 0.012f, 46f),
            };

            const float refW = 941f, refH = 1672f;
            foreach (var (piece, distance, parallax, px, py, scale, baseFade, hazeAmount, haze, sway, period) in layers)
            {
                float fadeStart = 1f - piece.Bottom - 0.015f;
                var mat = PtwArt.SkySprite(piece, haze, hazeAmount, fadeStart, fadeStart + baseFade * piece.Size.y);
                if (!mat) continue;
                var go = PtwPrefabs.MeshNode(piece.Id.Substring(6), "Mesh_QuadXY", root.transform, piece.Id);
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var layer = go.AddComponent<SkyLayer>();
                PtwPrefabs.Wire(layer, "targetCamera", cam);
                PtwPrefabs.Wire(layer, "distance", distance);
                PtwPrefabs.Wire(layer, "viewportPos", new Vector2(px / refW, 1f - py / refH));
                PtwPrefabs.Wire(layer, "anchor", piece.Peak);
                PtwPrefabs.Wire(layer, "heightFraction", piece.Size.y * piece.CanvasHeight * scale / refH);
                PtwPrefabs.Wire(layer, "aspect", piece.Aspect);
                PtwPrefabs.Wire(layer, "contentSize", piece.Size);
                PtwPrefabs.Wire(layer, "parallax", parallax);
                PtwPrefabs.Wire(layer, "swayAmplitude", sway);
                PtwPrefabs.Wire(layer, "swayPeriod", period);
            }
        }

        static ParticleSystem MakeFireflies(Transform cameraTransform)
        {
            var go = new GameObject("Fireflies");
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 60f);      // about the island's plane

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(1f, 0.95f, 0.8f, 1f);
            main.gravityModifier = -0.004f;                             // drift up, barely

            var em = ps.emission; em.enabled = true; em.rateOverTime = 3f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(14f, 24f, 24f);                      // a frame's worth at 60 units, with depth

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f),
                        new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sz = ps.sizeOverLifetime;                               // twinkle
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(0.55f, 0.5f),
                new Keyframe(0.8f, 1f), new Keyframe(1f, 0.3f)));

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = PtwArt.Get(PtwArt.MParticleAdd);
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return ps;
        }

        // ====================================================================== lighting =====
        static void BuildLighting()
        {
            var keyGo = new GameObject("KeyLight");
            // Upper-left-front, re-aimed for v2's near-front-on camera. Worked out rather than
            // eyeballed: this rotation gives a light direction of about (0.47, -0.74, 0.47), so
            // top faces take the most light (dot 0.74), the front faces the camera actually sees
            // are clearly lit (0.47), the left faces are lit, and the right faces fall into shade.
            // That is what gives a flat-shaded chamfered cube its form, and it keeps the reference
            // sheet's "key from upper-left, shadows down and to the right" reading intact.
            // The sun in the paintings is upper-RIGHT (it is where the backdrop draws it), and the
            // warm light comes from that side: the right faces of the blocks and the right edge of
            // the island pick up the warmth, the left falls to the cool fill. Yaw -40 puts the key
            // there; the original +40 had it upper-left, against the sun.
            keyGo.transform.rotation = Quaternion.Euler(46f, -40f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = KeyColor;
            key.intensity = 0.28f;   // top faces only ~6% brighter than fronts in the mockup; the ambient carries the frame
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.42f;          // soft, like the mockup's; the ambient fills the rest
            key.shadowBias = 0.04f;
            key.shadowNormalBias = 0.28f;
            var keyData = keyGo.AddComponent<UniversalAdditionalLightData>();
            keyData.usePipelineSettings = true;

            // Cool fill from the opposite side, no shadows. The key leaves every right-hand face
            // at one flat ambient value, so a tilted island read as a grey slab with a lit top. A
            // weak second directional puts a gradient back on those faces at zero shadow cost.
            // Cool rather than warm so the key stays unambiguously the sun.
            var fillGo = new GameObject("FillLight");
            fillGo.transform.rotation = Quaternion.Euler(20f, 130f, 0f);   // from the left, opposite the sun
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = PtwArt.Hex("#D9D4F0");   // cool lilac fill from the shaded side of the sky
            fill.intensity = 0.13f;
            fill.shadows = LightShadows.None;
            fillGo.AddComponent<UniversalAdditionalLightData>().usePipelineSettings = true;

            // Flat cool ambient. This is a measured value, not a taste call: a white surface in
            // shadow on the reference sits exactly here.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.2f;
            RenderSettings.skybox = null;

            // Atmospheric depth, cheaply: linear fog towards the horizon's lilac. The live island
            // stands ~70 units from the lens and takes nothing; the next level at ~200 takes ~17%,
            // the one after it at ~330 about a third, and the ridges at 340-460 take 35-50% and
            // recede the way the painting's do. A fog keyword on URP Lit/Unlit costs nothing on a
            // phone; the custom island shaders ignore it.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = PtwArt.Hex("#E6DCEC");
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 800f;
        }

        // ================================================================ post processing =====
        static void BuildVolume()
        {
            var profile = EnsureProfile();

            var go = new GameObject("~PostProcessVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = profile;
        }

        /// <summary>
        /// The pastel grade, as a profile asset that is ALSO the pipeline's default volume (see
        /// ConfigureUrp). Both matter, and the second one was missed for a long time:
        ///
        ///  * VolumeProfile.Add() creates the component as a loose ScriptableObject. Unless it is
        ///    made a sub-asset with AddObjectToAsset it serialises as {fileID: 0}, and in the NEXT
        ///    Unity session the profile is six nulls. Every headless capture and every device
        ///    build was therefore graded by the URP template's SampleSceneProfile instead -
        ///    Neutral tonemapping, a black vignette at 0.2 and a bloom threshold of 1.0. That is
        ///    where the "mauve, grey, dark corners, blown-out flowers" of the first theme passes
        ///    came from, and why nothing set here ever seemed to change them.
        ///  * The URP asset's own default volume profile sits under this one, so anything it
        ///    sets that this profile does not override still leaks through. Pointing it at this
        ///    same asset makes the grade the only grade.
        ///
        /// The grade itself is close to identity: the palette is authored in the mockup's colours,
        /// so the frame should show them, not reinterpret them. No tonemapping (the frame is LDR
        /// and Neutral/ACES both pull pastels towards grey), a whisper of saturation and warmth,
        /// a light blush vignette instead of a dark one, and a warm-tinted bloom that only the
        /// portal, the orb rim and the studs can reach.
        /// </summary>
        static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                PtwPaths.EnsureFolder(System.IO.Path.GetDirectoryName(ProfilePath));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                foreach (var c in new List<VolumeComponent>(profile.components))
                {
                    if (c == null) continue;
                    profile.Remove(c.GetType());
                    UnityEngine.Object.DestroyImmediate(c, true);
                }
                profile.components.RemoveAll(c => c == null);
            }

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.None;

            var bloom = profile.Add<Bloom>(true);
            // 1.15: a sunlit ivory face lands just over 1.0 and must not bloom (a white flower once
            // became a halo); the portal (0.9 + light), the orb rim (2.2) and the studs (1.4) do.
            bloom.threshold.value = 1.15f;
            bloom.intensity.value = 0.85f;
            bloom.scatter.value = 0.72f;
            bloom.tint.value = PtwArt.Hex("#FFF0DC");
            bloom.highQualityFiltering.value = false;   // mobile budget
            // Cap what a single pixel may contribute. A rogue NaN or infinity used to hit the
            // default 65472 and bloom into a block-sized white disc (the grass tufts did exactly
            // that). Twelve keeps every intended glow intact.
            bloom.clamp.value = 12f;
            bloom.dirtIntensity.value = 0f;

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = 0f;
            color.contrast.value = 0f;
            color.colorFilter.value = Color.white;
            color.hueShift.value = 0f;
            color.saturation.value = 6f;

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.value = 2f;
            wb.tint.value = 0f;

            var vig = profile.Add<Vignette>(true);
            // Light and blush-coloured: the mockup's corners soften towards the sky's pink rather
            // than darkening. A dark vignette would put a frame of dusk around a dawn.
            vig.color.value = PtwArt.Hex("#F0CDD3");
            vig.center.value = new Vector2(0.5f, 0.5f);
            vig.intensity.value = 0.12f;
            vig.smoothness.value = 0.75f;
            vig.rounded.value = false;

            // Highlights only, towards peach; balance keeps it off the mid-tones.
            var split = profile.Add<SplitToning>(true);
            split.shadows.value = new Color(0.5f, 0.5f, 0.5f);
            split.highlights.value = new Color(0.5f, 0.5f, 0.5f);   // neutral: the palette is authored, toning only washed it
            split.balance.value = 40f;

            // Explicitly neutral, so a template profile underneath can never add these back.
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.value = 0f;
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.value = 0f;
            var blur = profile.Add<MotionBlur>(true);
            blur.intensity.value = 0f;

            // The step that makes all of the above real across sessions.
            foreach (var c in profile.components)
                if (c != null && !AssetDatabase.Contains(c))
                {
                    c.name = c.GetType().Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ==================================================================== URP settings ====
        public static void ConfigureUrp()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!asset) continue;

                var so = new SerializedObject(asset);
                SetProp(so, "m_SupportsHDR", true);              // bloom needs headroom
                // The pipeline's default volume is OUR grade, not the URP template's sample profile
                // (Neutral tonemapping, black vignette) that shipped in Assets/Settings. See EnsureProfile.
                SetProp(so, "m_VolumeProfile", EnsureProfile());
                // The water shader reads both: depth for the shallow/deep gradient and the
                // shoreline foam line, opaque colour for refraction. Without these it falls back
                // to looking like flat blue card.
                SetProp(so, "m_RequireDepthTexture", true);
                SetProp(so, "m_RequireOpaqueTexture", true);
                SetProp(so, "m_MSAA", 4);
                SetProp(so, "m_RenderScale", 1f);
                // Two cascades: the first covers the live island (~70 units out) at about the
                // resolution one cascade over 45 units used to give it, the second reaches the
                // next level standing ~200 units back so it is shaded like the one being played.
                SetProp(so, "m_ShadowDistance", 240f);
                SetProp(so, "m_ShadowCascadeCount", 2);
                SetProp(so, "m_Cascade2Split", 0.4f);
                SetProp(so, "m_SoftShadowsSupported", true);
                SetProp(so, "m_MainLightShadowmapResolution", 2048);
                SetProp(so, "m_MainLightShadowsSupported", true);
                SetProp(so, "m_AdditionalLightsCastShadows", false);
                SetProp(so, "m_ShadowDepthBias", 0.6f);
                SetProp(so, "m_ShadowNormalBias", 0.9f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data) TryAddSsao(data);
            }

            AssetDatabase.SaveAssets();
        }

        static void SetProp(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) return;
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case UnityEngine.Object o: p.objectReferenceValue = o; break;
            }
        }

        /// <summary>
        /// SSAO is what pushes the block top:side contrast from ~4x (pure lighting) up to the ~8x
        /// the reference actually shows, by darkening the crevices where blocks meet. Added
        /// defensively - if the URP internals move, the build carries on without it.
        /// </summary>
        static void TryAddSsao(ScriptableRendererData data)
        {
            try
            {
                var ssaoType = Type.GetType(
                    "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
                if (ssaoType == null) { Debug.LogWarning("PTW: SSAO type not found, skipping."); return; }

                foreach (var f in data.rendererFeatures)
                    if (f != null && f.GetType() == ssaoType) return;   // already present

                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
                feature.name = "SSAO";
                AssetDatabase.AddObjectToAsset(feature, data);

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null)
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = feature.GetInstanceID();
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                // Tune it: tight radius, fairly strong, so it reads as contact darkening rather
                // than a grey haze over everything.
                var fso = new SerializedObject(feature);
                var settings = fso.FindProperty("m_Settings");
                if (settings != null)
                {
                    SetChild(settings, "Intensity", 1.5f);
                    SetChild(settings, "Radius", 0.22f);
                    SetChild(settings, "Falloff", 90f);
                    SetChild(settings, "Downsample", true);
                    SetChild(settings, "SampleCount", 8);
                }
                fso.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(data);
                Debug.Log("PTW: SSAO renderer feature added to " + data.name);
            }
            catch (Exception e)
            {
                Debug.LogWarning("PTW: could not add SSAO - " + e.Message);
            }
        }

        static void SetChild(SerializedProperty parent, string name, object value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) return;
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
            }
        }

        static (ParticleSystem burst, ParticleSystem roll) BuildDust()
        {
            var burst = MakeDustSystem("ImpactDust", 60, 0.06f);
            var roll = MakeDustSystem("RollDust", 40, 0.042f);

            // The roll trickle emits over time; the burst is Emit()-ed by hand.
            var rollEm = roll.emission;
            rollEm.enabled = true;
            rollEm.rateOverTime = 0f;      // multiplier is driven every frame

            return (burst, roll);
        }

        /// <summary>
        /// Dust budgets are deliberately small.
        ///
        /// M_ParticleSoft is ALPHA BLENDED, not additive, so overlapping blobs of it do not glow -
        /// they stack towards solid white. The first pass ran 26 particles a second at 0.055 units
        /// with 0.55 alpha, which is enough overlap to read as a smear behind the ball rather than
        /// as grit. Keep the rate low, the alpha low, and the tint off pure white.
        /// </summary>
        static ParticleSystem MakeDustSystem(string name, int maxParticles, float size)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            // World space, so a puff stays where the impact happened instead of being dragged
            // along by the emitter being repositioned for the next one.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            main.maxParticles = maxParticles;
            main.startLifetime = 0.42f;
            main.startSize = size;
            main.startSpeed = 1.1f;
            main.startColor = new Color(1f, 0.97f, 0.92f, 0.32f);   // cream dust, not grey
            main.gravityModifier = 0.35f;

            var em = ps.emission; em.enabled = false;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.14f;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = PtwArt.Get(PtwArt.MParticleSoft);
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.2f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.15f)));

            return ps;
        }

        // ============================================================================ UI =====
        public static TMP_FontAsset EnsureFont(string ttfPath, string outPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (existing != null) return existing;

            var ttf = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (ttf == null) { Debug.LogWarning("PTW: font missing at " + ttfPath); return null; }

            var fa = TMP_FontAsset.CreateFontAsset(
                ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic);
            fa.name = System.IO.Path.GetFileNameWithoutExtension(outPath);

            AssetDatabase.CreateAsset(fa, outPath);
            if (fa.material) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            if (fa.atlasTexture) { fa.atlasTexture.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa); }
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>
        /// Builds the whole UI: main menu, in-level HUD, level-complete screen, settings overlay
        /// and the wordless onboarding hints.
        ///
        /// Every screen is a full-bleed child of ONE canvas with a UiPanel on it, rather than
        /// separate canvases. One canvas means one draw order to reason about, and UiPanel means
        /// every transition in the game is the same fade-and-pop without any screen owning tween
        /// code. Draw order here is hierarchy order, so the sequence below is deliberate:
        /// HUD, then onboarding over it, then the menu screens over that, then the flash last of
        /// all so it can tint everything.
        /// </summary>
        static void BuildUi(LevelManager levels, Camera cam)
        {
            var bold = EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-Bold.ttf", FontPath);
            var semi = EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-SemiBold.ttf", FontSemiPath);

            var canvasGo = new GameObject("UI");
            var canvas = canvasGo.AddComponent<Canvas>();
            // Screen Space - CAMERA, not Overlay. Overlay bypasses the camera entirely, so it
            // would be missing from every RenderTexture capture - and the automated screenshots
            // are how this project gets art-directed.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // The drawn labels (PLAY.png, CLOSE.png) are cream with a hard sage drop shadow; the
            // typed labels on the other pills get the same treatment from this material.
            var dropBold = MakeTmpDropMaterial(bold, "TMP_PoppinsBold_Drop", PtwUiAssets.LabelShadow);
            var dropSemi = MakeTmpDropMaterial(semi, "TMP_PoppinsSemi_Drop", PtwUiAssets.LabelShadow);
            // The title's look from the mockup: sage letters with a soft cream halo.
            var titleMat = MakeTmpOutlineMaterial(bold, "TMP_PoppinsBold_Title", PtwArt.Hex("#FBF3E8"), 0.14f);

            // ================================================================== HUD =========
            // Laid out from the gameplay mockup (Art/UI/Layer 3.png, 940 px wide; canvas units are
            // mockup px x 1.149). No scrims: the sky is light, so the HUD is dark-on-light and
            // needs nothing under it.
            var hudPanel = Panel(canvasGo.transform, "HudPanel", out var hudGroup, popFrom: 1f);

            // Cap height 39 with its top 46 px down and 50 px in. TMP's TopLeft puts the
            // ascender at the rect top, and Poppins' ascender sits 0.35 em above its caps.
            var levelLabel = Text(hudPanel.transform, "LevelLabel", "LEVEL 1", semi, 56f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -28f),
                                  new Vector2(520f, 76f), TextAlignmentOptions.TopLeft,
                                  UiInk, null);
            levelLabel.characterSpacing = 4f;
            levelLabel.gameObject.AddComponent<Punch>();   // punched on every level load

            // The level's name under its number, small and widely tracked like the mockup's
            // "FIND THE PORTAL" (cap 18, top at 110).
            var levelTitle = Text(hudPanel.transform, "LevelTitle", "TIP IT OVER", semi, 26f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(49f, -101f),
                                  new Vector2(620f, 40f), TextAlignmentOptions.TopLeft,
                                  UiInkSoft, null);
            levelTitle.characterSpacing = 12f;

            // Two 86 px cream discs, centres 67 down and 201 / 76 in from the right, with the
            // mockup's hairline glyphs: a circular arrow and a ringed pause.
            var restartBtn = RoundButton(hudPanel.transform, "RestartButton", new Vector2(1f, 1f),
                                         new Vector2(-201f, -67f), 86f, MakeRestartSprite(), 0.72f, UiIcon);
            var pauseBtn = RoundButton(hudPanel.transform, "PauseButton", new Vector2(1f, 1f),
                                       new Vector2(-76f, -67f), 86f, MakePauseSprite(), 0.84f, UiIcon);

            // Key counter. Hidden unless the level actually needs keys - a permanent 0/0 on screen
            // is exactly the sort of clutter the brief asked to avoid.
            var keyGroup = new GameObject("KeyGroup", typeof(RectTransform));
            keyGroup.transform.SetParent(hudPanel.transform, false);
            var kgRt = keyGroup.GetComponent<RectTransform>();
            kgRt.anchorMin = kgRt.anchorMax = new Vector2(0.5f, 1f);
            kgRt.pivot = new Vector2(0.5f, 1f);
            kgRt.anchoredPosition = new Vector2(0f, -58f);
            kgRt.sizeDelta = new Vector2(220f, 80f);
            keyGroup.AddComponent<Punch>();                // punched on every gem collected

            var keyIcon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            keyIcon.transform.SetParent(keyGroup.transform, false);
            var kiRt = keyIcon.GetComponent<RectTransform>();
            kiRt.anchorMin = kiRt.anchorMax = new Vector2(0.5f, 0.5f);
            kiRt.anchoredPosition = new Vector2(-48f, 0f);
            kiRt.sizeDelta = new Vector2(46f, 46f);
            var kiImg = keyIcon.GetComponent<Image>();
            kiImg.sprite = MakeGemSprite();
            kiImg.color = PtwArt.Hex("#FFD96B");
            kiImg.raycastTarget = false;

            var keyLabel = Text(keyGroup.transform, "KeyLabel", "0/1", bold, 40f,
                                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                new Vector2(26f, 0f), new Vector2(140f, 60f),
                                TextAlignmentOptions.Left, UiInk, null);

            // ========================================================== onboarding =========
            var onboardGo = new GameObject("Onboarding", typeof(RectTransform));
            onboardGo.transform.SetParent(canvasGo.transform, false);
            Stretch(onboardGo.GetComponent<RectTransform>());

            // Rotate hint: a finger that sweeps an arc. Parented to a centred pivot node so
            // OnboardingHint can place it by polar coordinates and nothing else has to know.
            var rotGroupGo = new GameObject("RotateHint", typeof(RectTransform), typeof(CanvasGroup));
            rotGroupGo.transform.SetParent(onboardGo.transform, false);
            var rgRt = rotGroupGo.GetComponent<RectTransform>();
            // The gesture lives at the FOOT of the screen like the mockup's "TILT TO GUIDE", so it
            // is anchored to the bottom edge. The mockup's arrow is a shallow 332 px chord bowing
            // 30 px - a circle of radius 513 spanning +-19 degrees - whose lowest point is 156 px
            // up. The pivot is that circle's centre (669 up), and the finger rides the same circle.
            rgRt.anchorMin = rgRt.anchorMax = new Vector2(0.5f, 0f);
            rgRt.pivot = new Vector2(0.5f, 0.5f);
            rgRt.anchoredPosition = new Vector2(0f, 156f + ArcRadius);
            rgRt.sizeDelta = new Vector2(10f, 10f);
            var rotGroup = rotGroupGo.GetComponent<CanvasGroup>();
            rotGroup.alpha = 0f;
            rotGroup.blocksRaycasts = false;
            rotGroup.interactable = false;

            // A hairline two-headed arc for the direction, and the words under it. The sprite is
            // painted at 1:1 canvas pixels with its circle centre ArcCentreAboveSprite px above
            // its own centre, so it hangs that far below the pivot.
            var arc = new GameObject("Arc", typeof(RectTransform), typeof(Image));
            arc.transform.SetParent(rotGroupGo.transform, false);
            var aRt = arc.GetComponent<RectTransform>();
            aRt.anchorMin = aRt.anchorMax = new Vector2(0.5f, 0.5f);
            aRt.pivot = new Vector2(0.5f, 0.5f);
            aRt.anchoredPosition = new Vector2(0f, -ArcCentreAboveSprite);
            aRt.sizeDelta = new Vector2(ArcSpriteWidth, ArcSpriteHeight);
            var aImg = arc.GetComponent<Image>();
            aImg.sprite = MakeArcArrowSprite();
            aImg.color = UiInk;
            aImg.raycastTarget = false;

            // Cap 17, centred 112 px above the bottom edge.
            var tilt = Text(rotGroupGo.transform, "TiltLabel", "TILT TO GUIDE", semi, 25f,
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(0f, 112f - (156f + ArcRadius)),
                            new Vector2(700f, 44f), TextAlignmentOptions.Center, UiInk, null);
            tilt.characterSpacing = 14f;

            var finger = new GameObject("Finger", typeof(RectTransform), typeof(Image));
            finger.transform.SetParent(rotGroupGo.transform, false);
            var fRt = finger.GetComponent<RectTransform>();
            fRt.anchorMin = fRt.anchorMax = new Vector2(0.5f, 0.5f);
            fRt.pivot = new Vector2(0.5f, 0.5f);
            fRt.sizeDelta = new Vector2(56f, 56f);
            var fImg = finger.GetComponent<Image>();
            fImg.sprite = MakeCircleSprite();
            fImg.color = new Color(1f, 0.99f, 0.96f, 0.95f);
            fImg.raycastTarget = false;
            Gloss(finger, 0.9f, 0.12f, -3f);

            // Point hint: a ring parked on a world object. Positioned in SCREEN space by
            // OnboardingHint every frame, because its target is bolted to a rotating level.
            var pointGroupGo = new GameObject("PointHint", typeof(RectTransform), typeof(CanvasGroup));
            pointGroupGo.transform.SetParent(onboardGo.transform, false);
            Stretch(pointGroupGo.GetComponent<RectTransform>());
            var pointGroup = pointGroupGo.GetComponent<CanvasGroup>();
            pointGroup.alpha = 0f;
            pointGroup.blocksRaycasts = false;
            pointGroup.interactable = false;

            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(pointGroupGo.transform, false);
            var ringRt = ring.GetComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(150f, 150f);
            var ringImg = ring.GetComponent<Image>();
            ringImg.sprite = MakeRingSprite();
            ringImg.color = new Color(1f, 0.97f, 0.90f, 0.95f);   // cream, like the portal light
            ringImg.raycastTarget = false;

            var onboarding = onboardGo.AddComponent<OnboardingHint>();
            PtwPrefabs.Wire(onboarding, "rotateGroup", rotGroup);
            PtwPrefabs.Wire(onboarding, "rotateFinger", fRt);
            PtwPrefabs.Wire(onboarding, "arcRadius", ArcRadius);
            PtwPrefabs.Wire(onboarding, "arcSweep", ArcHalfAngle * 2f);
            PtwPrefabs.Wire(onboarding, "pointGroup", pointGroup);
            PtwPrefabs.Wire(onboarding, "pointRing", ringRt);
            PtwPrefabs.Wire(onboarding, "worldCamera", cam);

            // ============================================================ main menu =========
            // Laid out from the menu mockup (Art/UI/Layer 10.png). No dim: the menu is the sky
            // and the island with the type sitting on them. The title hangs from the top edge
            // and the PLAY / progress / gear stack stands on the bottom one, so on a taller or
            // squarer screen each group keeps its distance from its own edge and only the sky
            // between them changes.
            var menuPanel = Panel(canvasGo.transform, "MainMenuPanel", out var menuGroup);

            // The carved-stone logo (Art/UI/Layer 9.png). In the PSD the artist painted the
            // mockup's typed title out with sky and set this over it, nearly full width, its foot
            // just clearing the portal: centre 377 down, 971 x 428.
            SpriteImage(menuPanel.transform, "Logo", PtwUiAssets.Logo, new Vector2(0.5f, 1f),
                        new Vector2(0f, -377f), new Vector2(971f, 428f));

            // PLAY breathes. The pulse lives on a wrapper so it does not fight the press-juice
            // on the button itself - two components driving one localScale would tear. The pill
            // is 531 x 146 with its centre 438 up; the drawn PLAY label is 260 wide inside it.
            var playWrap = new GameObject("PlayPulse", typeof(RectTransform));
            playWrap.transform.SetParent(menuPanel.transform, false);
            var pwRt = playWrap.GetComponent<RectTransform>();
            pwRt.anchorMin = pwRt.anchorMax = new Vector2(0.5f, 0f);
            pwRt.pivot = new Vector2(0.5f, 0.5f);
            pwRt.anchoredPosition = new Vector2(0f, 438f);
            pwRt.sizeDelta = new Vector2(531f, 146f);
            playWrap.AddComponent<UiPulse>();

            var playBtn = PillButton(playWrap.transform, "PlayButton", PtwUiAssets.Pill,
                                     Vector2.zero, new Vector2(531f, 146f));
            SpriteImage(playBtn.transform, "Label", PtwUiAssets.PlayLabel, new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 1f), new Vector2(260f, 79f));

            // "LEVEL 14" under PLAY: cap 31, centred 302 up.
            var progressLabel = Text(menuPanel.transform, "ProgressLabel", "LEVEL 1", bold, 44f,
                                     new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                     new Vector2(0f, 302f - 30f), new Vector2(700f, 60f),
                                     TextAlignmentOptions.Center, UiSage, null);
            progressLabel.characterSpacing = 4f;

            // The gear on a 124 px disc, centred 173 up.
            var menuSettingsBtn = RoundButton(menuPanel.transform, "SettingsButton",
                                              new Vector2(0.5f, 0f), new Vector2(0f, 173f),
                                              124f, PtwUiAssets.Gear, 0.56f, Color.white);

            // Level picker entry. The mockup has no such button, but the artist supplied the
            // square (LevelsButton.png), so it goes where the HUD keeps its utility buttons - the
            // top-right corner, same centre as PAUSE - leaving the mockup's centre column intact.
            var levelsBtn = SquareButton(menuPanel.transform, "LevelsButton", new Vector2(1f, 1f),
                                         new Vector2(-76f, -67f), 92f, MakeGridSprite(), 0.5f, UiCream);

            // ======================================================= level complete =========
            var donePanel = Panel(canvasGo.transform, "LevelCompletePanel", out var doneGroup);
            Dim(donePanel.transform, "Dim", UiHaze);

            var doneTitle = Text(donePanel.transform, "CompleteTitle", "LEVEL COMPLETE", bold, 76f,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 new Vector2(0f, 470f), new Vector2(1000f, 200f),
                                 TextAlignmentOptions.Center, UiSage, titleMat);
            doneTitle.characterSpacing = 4f;
            doneTitle.enableWordWrapping = true;

            var doneSub = Text(donePanel.transform, "CompleteSubtitle", "LEVEL 1 OF 18", semi, 36f,
                               new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               new Vector2(0f, 350f), new Vector2(800f, 60f),
                               TextAlignmentOptions.Center, UiInk, null);
            doneSub.characterSpacing = 10f;

            var continueBtn = PillButton(donePanel.transform, "ContinueButton", PtwUiAssets.Pill,
                                         new Vector2(0f, -430f), new Vector2(531f, 146f));
            Label(continueBtn.transform, "CONTINUE", bold, 54f, dropBold);

            // Confetti lives in the WORLD, parented to the camera just inside the UI plane, so it
            // draws in front of the panel. A ParticleSystem under a ScreenSpaceCamera canvas
            // sorts behind it and would be invisible exactly when it matters.
            var celebration = MakeCelebrationVfx(cam.transform);

            // ============================================================== settings =========
            var setPanel = Panel(canvasGo.transform, "SettingsPanel", out var setGroup);
            Dim(setPanel.transform, "Dim", UiHaze);

            // Laid out from the settings mockup (Art/UI/Layer 16.png, a 626 px crop that stands in
            // for the screen width; canvas units are mockup px x 1.725). The card is 916 x 1182
            // with 80 px corners, cream, flat, with a soft shadow. Dropped a little so its top
            // edge sits below the logo's midline on the menu.
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(setPanel.transform, false);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2(0f, -60f);
            cardRt.sizeDelta = new Vector2(916f, 1182f);
            var cardImg = card.GetComponent<Image>();
            cardImg.sprite = MakePanelSprite();
            cardImg.type = Image.Type.Sliced;           // true circular corners at any card size
            cardImg.color = UiCard;
            Gloss(card, 0.985f, 0.06f, -16f);

            // Title: cap 62, centred 99 px below the card's top edge.
            var settingsTitle = Text(card.transform, "SettingsTitle", "SETTINGS", bold, 89f,
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 492f),
                                     new Vector2(800f, 120f), TextAlignmentOptions.Center, UiSettingsInk, null);
            settingsTitle.characterSpacing = 4f;

            // Three rows 147 px apart; label cap 41 at 76 px in, switch right edge 71 px in.
            var soundToggle = ToggleRow(card.transform, "SoundToggle", "SOUND", bold, 335f);
            var musicToggle = ToggleRow(card.transform, "MusicToggle", "MUSIC", bold, 187f);
            var hapticsToggle = ToggleRow(card.transform, "HapticsToggle", "HAPTICS", bold, 40f);

            // Two wide pills (707 x 133), then a shorter CLOSE (452 x 117) with 59 px under it.
            // Destructive, so UiRoot makes RESTART a two-tap confirm and swaps the label to say so.
            var restartAllBtn = PillButton(card.transform, "RestartAllButton", PtwUiAssets.PillWide,
                                           new Vector2(0f, -140f), new Vector2(707f, 133f));
            Label(restartAllBtn.transform, "RESTART ALL LEVELS", bold, 46f, dropBold);

            // Development convenience: opens every level in the picker so a build can be tested
            // from any point. UiRoot hides it when showDevUnlock is off - flip that for release.
            var unlockAllBtn = PillButton(card.transform, "UnlockAllButton", PtwUiAssets.PillWide,
                                          new Vector2(0f, -301f), new Vector2(707f, 133f));
            Label(unlockAllBtn.transform, "UNLOCK ALL LEVELS (DEV)", bold, 44f, dropBold);

            // MAIN MENU, for the pause menu only: UiRoot shows it when settings open during play
            // and lays the card out around it (UiRoot.LayoutSettings, which owns every y here);
            // opened from the main menu itself it is hidden and the card is the mockup's.
            var mainMenuBtn = PillButton(card.transform, "MainMenuButton", PtwUiAssets.PillWide,
                                         new Vector2(0f, -462f), new Vector2(707f, 133f));
            Label(mainMenuBtn.transform, "MAIN MENU", bold, 46f, dropBold);

            var closeBtn = PillButton(card.transform, "CloseButton", PtwUiAssets.PillClose,
                                      new Vector2(0f, -472f), new Vector2(452f, 117f));
            SpriteImage(closeBtn.transform, "Label", PtwUiAssets.CloseLabel, new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 1f), new Vector2(166f, 42f));

            // ================================================================= flash =========
            var flashGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
            flashGo.transform.SetParent(canvasGo.transform, false);
            Stretch(flashGo.GetComponent<RectTransform>());
            var flash = flashGo.GetComponent<Image>();
            flash.color = Color.clear;
            flash.raycastTarget = false;

            // ============================================================ level select =======
            // A grid of numbered tiles, cloned from one template by UiRoot when it opens. Five
            // columns of 150 px tiles fits 35 levels without scrolling; past ~45 it will need a
            // ScrollRect.
            var pickPanel = Panel(canvasGo.transform, "LevelSelectPanel", out var pickGroup);
            Dim(pickPanel.transform, "Dim", new Color(UiHaze.r, UiHaze.g, UiHaze.b, 0.82f));
            var pickTitle = Text(pickPanel.transform, "Title", "LEVELS", bold, 72f,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 740f),
                                 new Vector2(800f, 100f), TextAlignmentOptions.Center, UiSage, null);
            pickTitle.characterSpacing = 8f;

            // The grid scrolls: fifty levels do not fit a phone, and the count keeps growing. The
            // viewport carries an invisible Image so a drag that starts between tiles still scrolls.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image),
                                          typeof(RectMask2D), typeof(ScrollRect));
            scrollGo.transform.SetParent(pickPanel.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRt.pivot = new Vector2(0.5f, 1f);
            scrollRt.anchoredPosition = new Vector2(0f, 640f);
            scrollRt.sizeDelta = new Vector2(880f, 1240f);
            scrollGo.GetComponent<Image>().color = Color.clear;

            var gridGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup),
                                        typeof(ContentSizeFitter));
            gridGo.transform.SetParent(scrollGo.transform, false);
            var gridRt = gridGo.GetComponent<RectTransform>();
            gridRt.anchorMin = new Vector2(0f, 1f);
            gridRt.anchorMax = new Vector2(1f, 1f);
            gridRt.pivot = new Vector2(0.5f, 1f);
            gridRt.anchoredPosition = Vector2.zero;
            gridRt.sizeDelta = new Vector2(0f, 1240f);
            gridGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = gridRt;
            scroll.viewport = scrollRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var grid = gridGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150f, 160f);      // the square asset is 173 x 184 with its foot
            grid.spacing = new Vector2(22f, 22f);
            grid.padding = new RectOffset(0, 0, 0, 40);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;

            // The artists' rounded square (LevelsButton.png), numbered in the drawn-label style.
            var tile = SquareButton(gridGo.transform, "TileTemplate", new Vector2(0.5f, 0.5f),
                                    Vector2.zero, 150f, null, 0f, Color.white);
            Label(tile.transform, "1", bold, 52f, dropBold);
            tile.gameObject.SetActive(false);

            var closePickBtn = PillButton(pickPanel.transform, "CloseLevelsButton", PtwUiAssets.PillClose,
                                          new Vector2(0f, -760f), new Vector2(452f, 117f));
            SpriteImage(closePickBtn.transform, "Label", PtwUiAssets.CloseLabel, new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 1f), new Vector2(166f, 42f));

            // ================================================================= skip (ad) =======
            // Lives in the HUD, hidden. UiRoot shows it only after AdsManager.SkipAfterFails deaths
            // on one level: an offer to a stuck player, never a toll.
            var skipBtn = PillButton(hudPanel.transform, "SkipButton", PtwUiAssets.PillWide,
                                     new Vector2(0f, -790f), new Vector2(620f, 110f));
            Label(skipBtn.transform, "STUCK?  SKIP LEVEL  ▶", semi, 34f, dropSemi);
            skipBtn.gameObject.AddComponent<Punch>();
            skipBtn.gameObject.SetActive(false);

            // ============================================================ chapter card =======
            // Two lines over the sky, above the island, faded in by UiRoot on the first level of
            // each chapter. Sits above every panel and never takes input.
            var cardGo = new GameObject("ChapterCard", typeof(RectTransform), typeof(CanvasGroup));
            cardGo.transform.SetParent(canvasGo.transform, false);
            Stretch(cardGo.GetComponent<RectTransform>());
            var cardGroup = cardGo.GetComponent<CanvasGroup>();
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;
            var chapterNumber = Text(cardGo.transform, "ChapterNumber", "CHAPTER I", bold, 64f,
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 580f),
                                     new Vector2(900f, 90f), TextAlignmentOptions.Center, UiSage, titleMat);
            chapterNumber.characterSpacing = 10f;
            var chapterName = Text(cardGo.transform, "ChapterName", "DAWN", semi, 36f,
                                   new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 516f),
                                   new Vector2(900f, 60f), TextAlignmentOptions.Center, UiInk, null);
            chapterName.characterSpacing = 14f;

            // The gem that flies from a pickup to the counter. Hidden until UiRoot animates it.
            var flyGo = new GameObject("FlyingGem", typeof(RectTransform), typeof(Image));
            flyGo.transform.SetParent(canvasGo.transform, false);
            var flyRt = flyGo.GetComponent<RectTransform>();
            flyRt.anchorMin = flyRt.anchorMax = new Vector2(0.5f, 0.5f);
            flyRt.pivot = new Vector2(0.5f, 0.5f);
            flyRt.sizeDelta = new Vector2(56f, 56f);
            var flyImg = flyGo.GetComponent<Image>();
            flyImg.sprite = MakeGemSprite();
            flyImg.color = PtwArt.Hex("#FFD96B");
            flyImg.raycastTarget = false;
            flyGo.SetActive(false);

            // ================================================================== wire =========
            var root = canvasGo.AddComponent<UiRoot>();
            PtwPrefabs.Wire(root, "chapterCard", cardGroup);
            PtwPrefabs.Wire(root, "chapterNumber", chapterNumber);
            PtwPrefabs.Wire(root, "chapterName", chapterName);
            PtwPrefabs.Wire(root, "flyIcon", flyImg);
            PtwPrefabs.Wire(root, "levelsButton", levelsBtn);
            PtwPrefabs.Wire(root, "levelSelect", pickGroup);
            PtwPrefabs.Wire(root, "levelGrid", gridGo.transform);
            PtwPrefabs.Wire(root, "levelTileTemplate", tile);
            PtwPrefabs.Wire(root, "closeLevelsButton", closePickBtn);
            PtwPrefabs.Wire(root, "skipButton", skipBtn);
            PtwPrefabs.Wire(root, "mainMenu", menuGroup);
            PtwPrefabs.Wire(root, "hud", hudGroup);
            PtwPrefabs.Wire(root, "levelComplete", doneGroup);
            PtwPrefabs.Wire(root, "settings", setGroup);

            PtwPrefabs.Wire(root, "playButton", playBtn);
            PtwPrefabs.Wire(root, "menuSettingsButton", menuSettingsBtn);
            PtwPrefabs.Wire(root, "progressLabel", progressLabel);

            PtwPrefabs.Wire(root, "restartButton", restartBtn);
            PtwPrefabs.Wire(root, "pauseButton", pauseBtn);
            PtwPrefabs.Wire(root, "levelLabel", levelLabel);
            PtwPrefabs.Wire(root, "levelTitle", levelTitle);
            PtwPrefabs.Wire(root, "keyLabel", keyLabel);
            PtwPrefabs.Wire(root, "keyGroup", keyGroup);

            PtwPrefabs.Wire(root, "continueButton", continueBtn);
            PtwPrefabs.Wire(root, "completeTitle", doneTitle);
            PtwPrefabs.Wire(root, "completeSubtitle", doneSub);
            PtwPrefabs.Wire(root, "celebrationVfx", celebration);

            PtwPrefabs.Wire(root, "closeSettingsButton", closeBtn);
            PtwPrefabs.Wire(root, "restartAllButton", restartAllBtn);
            PtwPrefabs.Wire(root, "restartAllLabel", restartAllBtn.GetComponentInChildren<TMP_Text>());
            PtwPrefabs.Wire(root, "unlockAllButton", unlockAllBtn);
            PtwPrefabs.Wire(root, "mainMenuButton", mainMenuBtn);
            PtwPrefabs.Wire(root, "settingsCard", cardRt);
            PtwPrefabs.WireArray(root, "settingsTopBlock", new UnityEngine.Object[]
            {
                settingsTitle.rectTransform, soundToggle.GetComponent<RectTransform>(),
                musicToggle.GetComponent<RectTransform>(), hapticsToggle.GetComponent<RectTransform>(),
            });
            PtwPrefabs.Wire(root, "soundToggle", soundToggle);
            PtwPrefabs.Wire(root, "musicToggle", musicToggle);
            PtwPrefabs.Wire(root, "hapticsToggle", hapticsToggle);

            PtwPrefabs.Wire(root, "flashImage", flash);
            PtwPrefabs.Wire(root, "levels", levels);
            PtwPrefabs.Wire(root, "onboarding", onboarding);

            PutUiAboveTheWorld(canvasGo);
        }

        /// <summary>
        /// A Screen Space - Camera canvas is sorted in with the world's transparents: by material
        /// render queue first, distance second. The stock UI material sits at 3000, and the orb's
        /// glass and star are deliberately at Transparent+2 / +3 (see PtwArt), so the star drew
        /// straight through the paused settings card. One shared UI material a hundred above
        /// Transparent, on every Image, and the same queue on the TMP materials (which are
        /// assets), puts the whole canvas after everything in the world without touching the
        /// world's own ordering.
        /// </summary>
        const int UiQueue = (int)RenderQueue.Transparent + 100;

        static void PutUiAboveTheWorld(GameObject canvasGo)
        {
            var shader = Shader.Find("UI/Default");
            var uiMat = SaveTmpMaterial(new Material(shader) { name = "M_UiDefault", renderQueue = UiQueue }, "M_UiDefault");
            foreach (var img in canvasGo.GetComponentsInChildren<Image>(true))
                img.material = uiMat;

            foreach (var t in canvasGo.GetComponentsInChildren<TMP_Text>(true))
            {
                var m = t.fontSharedMaterial;
                if (m == null || m.renderQueue == UiQueue) continue;
                m.renderQueue = UiQueue;
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        // ================================================================= ui helpers =======
        /// <summary>
        /// The "packaged" look, from Unity UI Extensions (OpenUPM, MIT): a vertical gradient so a
        /// flat pill reads as a glossy button, a soft dark outline so it holds its edge on any
        /// background, and a drop shadow for depth. Gradient runs in MULTIPLY mode (Vertex1 white,
        /// Vertex2 darker) so the Image colour still carries the hue and the Button's press tint
        /// still works. Fully qualified because the package's Gradient collides with
        /// UnityEngine.Gradient, which the particle code in this file uses.
        ///
        /// None of this touches TMP text: TextMeshPro does not go through VertexHelper, so mesh
        /// effects silently do nothing on it. Text legibility is the shadow material's job.
        /// </summary>
        // The UI palette, sampled from the mockups in Art/UI: sage type, cream surfaces, and a
        // warm haze instead of a dark dim behind popups. The pills, discs, switches, gear and
        // drawn labels are the artists' PNGs (PtwUiAssets) and carry their own colour.
        static readonly Color UiSage = PtwArt.Hex("#5C7061");        // menu type ("LEVEL 14", titles)
        static readonly Color UiInk = PtwArt.Hex("#5A716D");         // HUD text
        static readonly Color UiInkSoft = PtwArt.Hex("#748985");     // HUD secondary text
        static readonly Color UiSettingsInk = PtwArt.Hex("#46594F"); // settings title and row labels
        static readonly Color UiIcon = PtwArt.Hex("#636E68");        // hairline glyphs on the cream discs
        static readonly Color UiCream = PtwArt.Hex("#F8F2E6");       // drawn icons on green
        static readonly Color UiCard = PtwArt.Hex("#F6EDDE");        // the settings card
        static readonly Color UiHaze = new Color(0.99f, 0.91f, 0.89f, 0.42f);   // a blush veil, not a grey one

        // The tilt cue's geometry, shared by the arc sprite and the onboarding finger.
        const float ArcRadius = 513f;            // canvas px
        const float ArcHalfAngle = 19f;          // degrees either side of straight down
        const float ArcSpriteWidth = 440f, ArcSpriteHeight = 100f;
        const float ArcCentreAboveSprite = ArcRadius - 30f;   // the arc's low point is 30 px below the sprite centre

        static void Gloss(GameObject go, float bottom = 0.86f, float outlineAlpha = 0.14f,
                          float shadowY = -6f)
        {
            var grad = go.AddComponent<UnityEngine.UI.Extensions.Gradient>();
            grad.GradientDir = UnityEngine.UI.Extensions.GradientDir.Vertical;
            grad.OverwriteAllColor = false;
            grad.Vertex1 = Color.white;
            grad.Vertex2 = new Color(bottom, bottom, Mathf.Min(1f, bottom + 0.02f), 1f);

            // Built-in Outline, not the package's NicerOutline: in this Unity version NicerOutline
            // compiles as an empty stub (its real body is behind an #else for older Unity) and
            // has no members at all. Warm-dark rather than black: black edges on pastel read as
            // stickers.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.30f, 0.26f, 0.28f, outlineAlpha);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;

            if (Mathf.Abs(shadowY) > 0.01f)
            {
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.30f, 0.24f, 0.30f, 0.20f);
                shadow.effectDistance = new Vector2(0f, shadowY);
                shadow.useGraphicAlpha = true;
            }
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A full-bleed screen with a CanvasGroup and a UiPanel already on it.</summary>
        static GameObject Panel(Transform parent, string name, out UiPanel panel, float popFrom = 0.92f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());

            panel = go.AddComponent<UiPanel>();
            PtwPrefabs.Wire(panel, "group", go.GetComponent<CanvasGroup>());
            PtwPrefabs.Wire(panel, "popFrom", popFrom);
            return go;
        }

        static Image Dim(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>A non-interactive image at its drawn aspect: logos, drawn labels, icons.</summary>
        static Image SpriteImage(Transform parent, string name, Sprite sprite, Vector2 anchor,
                                 Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// A typed label in the style of the drawn ones (PLAY.png, CLOSE.png): cream Poppins with
        /// a hard sage drop shadow, centred over its parent.
        /// </summary>
        static TMP_Text Label(Transform parent, string text, TMP_FontAsset font, float size, Material drop)
        {
            var t = Text(parent, "Label", text, font, size, Vector2.zero, Vector2.one, Vector2.zero,
                         Vector2.zero, TextAlignmentOptions.Center, PtwUiAssets.LabelCream, drop);
            var rt = t.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 2f);   // the drawn labels sit a hair above centre
            t.characterSpacing = 4f;
            return t;
        }

        static void PressFeedback(Selectable s)
        {
            // A visible press state matters more on mobile than anywhere else - there is no
            // hover, so the tap flash is the only confirmation the button was hit.
            var colors = s.colors;
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            s.colors = colors;
            s.gameObject.AddComponent<UiButtonJuice>();   // shrink on press, pop on release
        }

        static void Icon(Transform parent, Sprite icon, float size, Color color)
        {
            if (!icon) return;
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(parent, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            // The disc and square assets carry a shadow along their foot, so their visual centre
            // is a little above the rect centre; the glyph follows it.
            irt.anchoredPosition = new Vector2(0f, size * 0.04f);
            irt.sizeDelta = new Vector2(size, size);
            var iimg = iconGo.GetComponent<Image>();
            iimg.sprite = icon;
            iimg.preserveAspect = true;
            iimg.color = color;
            iimg.raycastTarget = false;
        }

        /// <summary>
        /// The artists' cream disc (Ellipse 1 copy.png, 166 x 172 with its shadow) with a glyph on
        /// it. <paramref name="pos"/> is the disc CENTRE relative to the anchor.
        /// </summary>
        static Button RoundButton(Transform parent, string name, Vector2 anchor, Vector2 pos,
                                  float diameter, Sprite icon, float iconScale, Color iconColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(diameter, diameter * 172f / 166f);

            var img = go.GetComponent<Image>();
            img.sprite = PtwUiAssets.Disc;
            img.preserveAspect = true;

            Icon(go.transform, icon, diameter * iconScale, iconColor);
            PressFeedback(go.GetComponent<Button>());
            return go.GetComponent<Button>();
        }

        /// <summary>The artists' rounded square (LevelsButton.png, 173 x 184 with its foot).</summary>
        static Button SquareButton(Transform parent, string name, Vector2 anchor, Vector2 pos,
                                   float width, Sprite icon, float iconScale, Color iconColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, width * 184f / 173f);

            var img = go.GetComponent<Image>();
            img.sprite = PtwUiAssets.Square;
            img.preserveAspect = true;

            Icon(go.transform, icon, width * iconScale, iconColor);
            PressFeedback(go.GetComponent<Button>());
            return go.GetComponent<Button>();
        }

        /// <summary>
        /// One of the artists' pills drawn at an arbitrary size. 9-sliced, with the pixel scale
        /// set from the height so the round caps are scaled circles and only the straight middle
        /// stretches - the mockups draw the same pill at three different aspect ratios.
        /// The caller adds the label (a drawn one via SpriteImage, or a typed one via Label).
        /// </summary>
        static Button PillButton(Transform parent, string name, Sprite pill, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.sprite = pill;
            img.type = Image.Type.Sliced;
            if (pill) img.pixelsPerUnitMultiplier = pill.rect.height / size.y;

            PressFeedback(go.GetComponent<Button>());
            return go.GetComponent<Button>();
        }

        /// <summary>
        /// A settings row: label at the left, a sliding switch at the right, in the artists'
        /// assets (Settingson.png / Settingson 2.png tracks, Ellipse 1.png knob). The row spans
        /// the card and the whole of it is tappable, so a thumb on the word toggles it too.
        /// UiSwitch moves the knob and cross-fades the tracks; the Toggle only holds the value.
        /// </summary>
        static Toggle ToggleRow(Transform parent, string name, string label, TMP_FontAsset font, float y)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            row.transform.SetParent(parent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(916f, 120f);
            row.GetComponent<Image>().color = Color.clear;   // invisible, but catches the tap

            Text(row.transform, "Label", label, font, 59f,
                 new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(76f, 0f),
                 new Vector2(560f, 90f), TextAlignmentOptions.Left,
                 UiSettingsInk, null).characterSpacing = 2f;

            // Track: 181 x 78, right edge 71 px in. The clay "off" track underneath, the sage
            // "on" track on top with its alpha driven by UiSwitch.
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(row.transform, false);
            var trRt = track.GetComponent<RectTransform>();
            trRt.anchorMin = trRt.anchorMax = new Vector2(1f, 0.5f);
            trRt.pivot = new Vector2(1f, 0.5f);
            trRt.anchoredPosition = new Vector2(-71f, 0f);
            trRt.sizeDelta = new Vector2(181f, 78f);
            var offImg = track.GetComponent<Image>();
            offImg.sprite = PtwUiAssets.TrackOff;
            offImg.preserveAspect = true;

            var on = new GameObject("On", typeof(RectTransform), typeof(Image));
            on.transform.SetParent(track.transform, false);
            Stretch(on.GetComponent<RectTransform>());
            var onImg = on.GetComponent<Image>();
            onImg.sprite = PtwUiAssets.TrackOn;
            onImg.preserveAspect = true;
            onImg.raycastTarget = false;

            // Knob: 59 px disc with a 7 px margin to either end, riding a hair high because the
            // asset's shadow hangs below it.
            var knob = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            knob.transform.SetParent(track.transform, false);
            var knobRt = knob.GetComponent<RectTransform>();
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.anchoredPosition = new Vector2(54f, 2f);
            knobRt.sizeDelta = new Vector2(59f, 63f);
            var knobImg = knob.GetComponent<Image>();
            knobImg.sprite = PtwUiAssets.Knob;
            knobImg.preserveAspect = true;
            knobImg.raycastTarget = false;

            var toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = offImg;
            toggle.graphic = null;         // UiSwitch owns the visuals
            toggle.isOn = true;
            PressFeedback(toggle);

            var sw = row.AddComponent<UiSwitch>();
            PtwPrefabs.Wire(sw, "toggle", toggle);
            PtwPrefabs.Wire(sw, "trackOn", onImg);
            PtwPrefabs.Wire(sw, "knob", knobRt);
            PtwPrefabs.Wire(sw, "travel", 54f);
            return toggle;
        }

        /// <summary>Confetti for the win moment. Burst only - it is played by UiRoot.</summary>
        static ParticleSystem MakeCelebrationVfx(Transform cameraTransform)
        {
            var go = new GameObject("CelebrationVfx");
            go.transform.SetParent(cameraTransform, false);
            // Just inside the canvas plane (1.0) so it renders over the panel.
            go.transform.localPosition = new Vector3(0f, -0.25f, 0.9f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.6f;
            main.maxParticles = 220;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.026f);
            main.gravityModifier = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var grad = new ParticleSystem.MinMaxGradient(PtwArt.Hex("#FFD96B"), PtwArt.Hex("#7FE39A"));
            grad.mode = ParticleSystemGradientMode.TwoColors;
            main.startColor = grad;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });

            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 42f;
            sh.radius = 0.05f;
            sh.rotation = new Vector3(-90f, 0f, 0f);   // fire upward on screen

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = PtwArt.Get(PtwArt.MParticleAdd);
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            return ps;
        }

        // ================================================================== ui sprites ======
        // Only the glyphs and the card are still painted here; the pills, discs, switch parts,
        // gear and the PLAY / CLOSE words are the artists' PNGs, see PtwUiAssets.

        static Sprite panelSprite;
        /// <summary>
        /// The settings card: an 80 px-radius rounded rectangle painted at 1:1 and 9-sliced, so
        /// the corners are true circles at any card size. (Stretching a small square sprite, as
        /// before, made them ellipses.)
        /// </summary>
        static Sprite MakePanelSprite()
        {
            if (panelSprite) return panelSprite;
            const int s = 256;
            const float radius = 80f, inset = 2f;
            return panelSprite = PaintSpriteXY("Tex_Panel", s, p =>
            {
                float d = RoundedBox(p * (s * 0.5f), Vector2.one * (s * 0.5f - inset), radius);
                return Mathf.Clamp01(0.5f - d);          // one texel of anti-aliasing
            }, border: Vector4.one * (radius + 16f));
        }

        static Sprite gridSprite;
        /// <summary>Four rounded squares: the level-select glyph on the menu's square button.</summary>
        static Sprite MakeGridSprite()
        {
            if (gridSprite) return gridSprite;
            return gridSprite = PaintSpriteXY("Tex_Grid", 128, p =>
            {
                float d = 1f;
                for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j += 2)
                        d = Mathf.Min(d, RoundedBox(p - new Vector2(i, j) * 0.47f,
                                                    new Vector2(0.36f, 0.36f), 0.12f));
                return Mathf.Clamp01(0.5f - d / 0.016f);
            });
        }

        /// <summary>Signed distance to a rounded box, negative inside.</summary>
        static float RoundedBox(Vector2 p, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - Vector2.one * radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static Sprite ringSprite;
        /// <summary>Attention ring for the onboarding "look at this" hint.</summary>
        static Sprite MakeRingSprite()
        {
            if (ringSprite) return ringSprite;
            return ringSprite = PaintSprite("Tex_HintRing", 128, (d, ang) =>
                Mathf.Clamp01(1f - Mathf.Abs(d - 0.74f) / 0.1f));
        }

        /// <summary>
        /// A TMP material with a coloured outline and a very soft underlay: the mockup title's
        /// sage-on-cream halo. Shared as an asset like the shadow material.
        /// </summary>
        static Material MakeTmpOutlineMaterial(TMP_FontAsset font, string id, Color outline, float width)
        {
            if (font == null || font.material == null) return null;
            var m = new Material(font.material) { name = id };
            m.EnableKeyword("OUTLINE_ON");
            m.SetColor("_OutlineColor", outline);
            m.SetFloat("_OutlineWidth", width);
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(outline.r, outline.g, outline.b, 0.55f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", -0.35f);
            m.SetFloat("_UnderlayDilate", 0.35f);
            m.SetFloat("_UnderlaySoftness", 0.55f);
            return SaveTmpMaterial(m, id);
        }

        static Sprite pauseSprite;
        /// <summary>
        /// The mockup's pause glyph: a hairline ring with two short bars inside. Drawn at 0.84 of
        /// the 86 px disc, so the ring is 64 px across with a 2.3 px stroke and the bars are
        /// 4.6 x 30, centred 7.5 px either side.
        /// </summary>
        static Sprite MakePauseSprite()
        {
            if (pauseSprite) return pauseSprite;
            return pauseSprite = PaintSpriteXY("Tex_Pause", 128, p =>
            {
                const float aa = 0.016f;                  // one texel
                float ring = Mathf.Clamp01((0.032f - Mathf.Abs(p.magnitude - 0.865f)) / aa + 0.5f);
                float d1 = RoundedBox(p - new Vector2(-0.21f, 0f), new Vector2(0.064f, 0.415f), 0.05f);
                float d2 = RoundedBox(p - new Vector2(0.21f, 0f), new Vector2(0.064f, 0.415f), 0.05f);
                float bars = Mathf.Clamp01(-Mathf.Min(d1, d2) / aa + 0.5f);
                return Mathf.Max(ring, bars);
            });
        }

        static Sprite arcArrowSprite;
        /// <summary>
        /// The mockup's tilt cue: a hairline arc with a filled arrowhead at each end. Painted at
        /// 1:1 canvas pixels in a 440 x 100 sprite, with the arc's circle centre
        /// ArcCentreAboveSprite px above the sprite's centre - which is where OnboardingHint's
        /// pivot goes, so the finger sweeps exactly this arc.
        /// </summary>
        static Sprite MakeArcArrowSprite()
        {
            if (arcArrowSprite) return arcArrowSprite;
            const float stroke = 3f, headLength = 39f, headHalfWidth = 14f;
            var centre = new Vector2(0f, ArcCentreAboveSprite);
            return arcArrowSprite = PaintSpriteRect("Tex_ArcArrow", (int)ArcSpriteWidth, (int)ArcSpriteHeight, p =>
            {
                Vector2 q = p - centre;
                float ang = Mathf.Atan2(q.x, -q.y) * Mathf.Rad2Deg;   // 0 = straight down, + = right
                float a = 0f;
                if (Mathf.Abs(ang) <= ArcHalfAngle)
                    a = Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(q.magnitude - ArcRadius));

                for (int s = -1; s <= 1; s += 2)
                {
                    float rad = s * ArcHalfAngle * Mathf.Deg2Rad;
                    var end = centre + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * ArcRadius;
                    var tangent = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * s;   // outward along the arc
                    var normal = new Vector2(-tangent.y, tangent.x);
                    if (InTriangle(p, end + tangent * headLength,
                                   end + normal * headHalfWidth, end - normal * headHalfWidth)) a = 1f;
                }
                return a;
            });
        }

        static Sprite gemSprite;
        /// <summary>Diamond, matching the silhouette of the key mesh so the HUD icon reads.</summary>
        static Sprite MakeGemSprite()
        {
            if (gemSprite) return gemSprite;
            return gemSprite = PaintSpriteXY("Tex_Gem", 128, p =>
            {
                // |x|/a + |y|/b <= 1 is a diamond.
                float v = Mathf.Abs(p.x) / 0.58f + Mathf.Abs(p.y) / 0.86f;
                return Mathf.Clamp01((1f - v) / 0.08f);
            });
        }

        /// <summary>
        /// A shared TMP material with the underlay (drop shadow) feature on. Shared rather than
        /// per-text so it stays a real asset - touching TMP_Text.fontMaterial would spawn scene-only
        /// material instances.
        /// </summary>
        static Material MakeTmpShadowMaterial(TMP_FontAsset font, string id)
        {
            if (font == null || font.material == null) return null;
            var m = new Material(font.material) { name = id };
            // A soft warm-dark underlay: on the pastel sky a hard black shadow reads as a sticker.
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0.30f, 0.24f, 0.30f, 0.40f));
            m.SetFloat("_UnderlayOffsetX", 0.3f);
            m.SetFloat("_UnderlayOffsetY", -0.5f);
            m.SetFloat("_UnderlayDilate", 0.1f);
            m.SetFloat("_UnderlaySoftness", 0.4f);
            return SaveTmpMaterial(m, id);
        }

        /// <summary>
        /// The drawn labels' look for typed text: a hard, unblurred drop shadow straight below,
        /// in the sage the artist used under PLAY and CLOSE. Offset -0.45 is about 4.5% of the em,
        /// which is where the shadow sits on PLAY.png.
        /// </summary>
        static Material MakeTmpDropMaterial(TMP_FontAsset font, string id, Color shadow)
        {
            if (font == null || font.material == null) return null;
            var m = new Material(font.material) { name = id };
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(shadow.r, shadow.g, shadow.b, 1f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", -0.45f);
            m.SetFloat("_UnderlayDilate", 0.05f);
            m.SetFloat("_UnderlaySoftness", 0.02f);
            return SaveTmpMaterial(m, id);
        }

        /// <summary>Write a TMP material variant as a shared asset, updating it in place if it exists.</summary>
        static Material SaveTmpMaterial(Material m, string id)
        {
            string path = $"{PtwArt.MatDir}/{id}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(m, existing);
                UnityEngine.Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            PtwPaths.EnsureFolder(PtwArt.MatDir);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static TMP_Text Text(Transform parent, string name, string content, TMP_FontAsset font,
                             float size, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 sd,
                             TextAlignmentOptions align, Color color, Material shadowMat = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            if (shadowMat) t.fontSharedMaterial = shadowMat;
            t.text = content;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(aMin.x, aMax.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sd;
            return t;
        }

        static Sprite circleSprite;
        static Sprite MakeCircleSprite()
        {
            if (circleSprite) return circleSprite;
            return circleSprite = PaintSprite("Tex_NudgeDot", 128, (d, ang) =>
            {
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.2f);
                float core = Mathf.Clamp01(1f - d / 0.5f);
                return ring * 0.8f + core * 0.9f;
            });
        }

        static Sprite restartSprite;
        /// <summary>
        /// The mockup's restart glyph: a thin circular arrow, open at the top right. Drawn at 0.72
        /// of the 86 px disc, so the loop is ~48 px across with a 4.6 px stroke.
        /// </summary>
        static Sprite MakeRestartSprite()
        {
            if (restartSprite) return restartSprite;

            const int s = 128;
            const float rMid = 0.74f, band = 0.075f;
            const float startDeg = 25f, endDeg = 325f;

            // Arrow head sits at the open end of the arc, pointing along the tangent.
            float endRad = endDeg * Mathf.Deg2Rad;
            Vector2 headC = new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * rMid;
            Vector2 tangent = new Vector2(-Mathf.Sin(endRad), Mathf.Cos(endRad));
            Vector2 radial = headC.normalized;
            Vector2 tip = headC + tangent * 0.34f;
            Vector2 b1 = headC + radial * 0.21f;
            Vector2 b2 = headC - radial * 0.21f;

            return restartSprite = PaintSpriteXY("Tex_Restart", s, p =>
            {
                float d = p.magnitude;
                float ang = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 360f);

                float a = 0f;
                if (ang >= startDeg && ang <= endDeg)
                {
                    float edge = Mathf.Abs(d - rMid);
                    a = Mathf.Clamp01((band - edge) / 0.016f + 0.5f);
                }
                if (InTriangle(p, tip, b1, b2)) a = 1f;
                return a;
            });
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        /// <summary>Paint by normalised radius and angle.</summary>
        static Sprite PaintSprite(string id, int size, System.Func<float, float, float> alpha) =>
            PaintSpriteXY(id, size, p => alpha(p.magnitude,
                Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 360f)));

        /// <summary>
        /// Paint by position in [-1,1] space centred on the sprite. A non-zero <paramref name="border"/>
        /// (left, bottom, right, top, in texels) makes the sprite 9-sliceable.
        /// </summary>
        static Sprite PaintSpriteXY(string id, int size, System.Func<Vector2, float> alpha,
                                    Vector4 border = default)
        {
            float half = size * 0.5f;
            return PaintSpriteRect(id, size, size,
                                   p => alpha(p / half), border);
        }

        /// <summary>
        /// Paint a w x h sprite by position in PIXELS from its centre (+y up). For shapes that are
        /// specified in canvas pixels and drawn at 1:1, like the tilt arc.
        /// </summary>
        static Sprite PaintSpriteRect(string id, int w, int h, System.Func<Vector2, float> alpha,
                                      Vector4 border = default)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float hx = w * 0.5f, hy = h * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f - hx, y + 0.5f - hy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(p))));
                }
            tex.Apply();

            PtwPaths.EnsureFolder(PtwArt.TexDir);
            string path = $"{PtwArt.TexDir}/{id}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f),
                                       100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Sprite_" + id;
            AssetDatabase.AddObjectToAsset(sprite, tex);
            return sprite;
        }

        static void RegisterInBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
