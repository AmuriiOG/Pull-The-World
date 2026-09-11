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
        static readonly Color KeyColor = PtwArt.Hex("#FFF4E6");

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
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
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

            // The rest of the sky: mountains, clouds and far islets, all welded to the camera.
            BuildSkyLayers(cam);

            // The shared parallax shift the layers read (tilt of the level, position of the orb).
            // On the camera so it exists exactly once and dies with the scene.
            go.AddComponent<SkyParallax>();

            return cam;
        }

        /// <summary>
        /// The mockup's atmosphere, in layers from the back: three hazed mountain ridges, cloud
        /// puffs drifting between and in front of them, and small floating islets with tiny portals
        /// up in the sky. Everything is a SkyLayer child of the camera: placed by viewport fraction
        /// and sized to the frustum, so it composes the same on every level's framing and never
        /// turns with the world - the reference the turning island is read against.
        ///
        /// It does move, though, in two ways that both scale with depth (see SkyLayer):
        /// clouds DRIFT sideways on their own, the near ones several times faster than the far
        /// ones, and every layer takes a share of the SkyParallax shift (level tilt, orb
        /// position) - a tenth for the far ridge, the whole thing for a cloud in front of the
        /// island. Ridges never drift (a strip cannot wrap) but they do parallax.
        /// </summary>
        static void BuildSkyLayers(Camera cam)
        {
            var root = new GameObject("Sky");
            root.transform.SetParent(cam.transform, false);

            // Mountains: far ridge highest and faintest, near ridge lowest and strongest. Their bases
            // sit below the frame so no bottom edge ever shows. The 1.4x width leaves room for the
            // parallax shift on both sides.
            Ridge(root.transform, cam, "MountainsFar", "Mesh_MountainFar", PtwArt.MMountainFar, 84f, -0.1f, 0.74f, 0.10f);
            Ridge(root.transform, cam, "MountainsMid", "Mesh_MountainMid", PtwArt.MMountainMid, 76f, -0.1f, 0.60f, 0.18f);
            Ridge(root.transform, cam, "MountainsNear", "Mesh_MountainNear", PtwArt.MMountainNear, 68f, -0.1f, 0.46f, 0.28f);

            // Clouds. (viewport x, viewport y, width, height, distance, drift, parallax, near).
            // Behind the mountains in the upper sky, between them in the middle, and three thin
            // ones in FRONT of the island's lower tip, like the mockup's foreground clouds. Smaller
            // and far fewer than the first pass, which buried the ridges under white cotton; the
            // drift speeds are now fast enough to SEE (the near ones cross the frame in ~40 s),
            // and scale with distance so the layers pull apart.
            var clouds = new[]
            {
                (0.18f, 0.73f, 6.5f, 3.0f, 90f, 0.10f, 0.12f, false), (0.74f, 0.65f, 7.5f, 3.4f, 88f, 0.08f, 0.12f, false),
                (0.46f, 0.56f, 6.0f, 2.7f, 80f, 0.14f, 0.22f, false), (0.92f, 0.49f, 6.5f, 3.0f, 79f, 0.12f, 0.22f, false),
                (0.08f, 0.44f, 7.0f, 3.2f, 72f, 0.18f, 0.35f, false), (0.60f, 0.39f, 6.0f, 2.7f, 70f, 0.20f, 0.35f, false),
                (0.28f, 0.10f, 10.0f, 4.2f, 24f, 0.40f, 1.00f, true), (0.82f, 0.05f, 9.5f, 4.0f, 22f, 0.46f, 1.00f, true),
                (0.55f, 0.16f, 7.5f, 3.3f, 26f, 0.36f, 0.85f, true),
            };
            int i = 0;
            foreach (var (x, y, w, h, z, drift, parallax, near) in clouds)
            {
                var c = PtwPrefabs.MeshNode($"Cloud{i++}", "Mesh_QuadXY", root.transform,
                                            near ? PtwArt.MCloudNear : PtwArt.MCloud);
                var r = c.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var layer = c.AddComponent<SkyLayer>();
                PtwPrefabs.Wire(layer, "targetCamera", cam);
                PtwPrefabs.Wire(layer, "distance", z);
                PtwPrefabs.Wire(layer, "centred", true);
                PtwPrefabs.Wire(layer, "viewportPos", new Vector2(x, y));
                PtwPrefabs.Wire(layer, "worldSize", new Vector2(w, h));
                PtwPrefabs.Wire(layer, "driftSpeed", drift);
                PtwPrefabs.Wire(layer, "parallax", parallax);
            }

            BuildSkyIslets(root.transform, cam);
        }

        static void Ridge(Transform parent, Camera cam, string name, string mesh, string mat,
                          float distance, float bottom, float top, float parallax)
        {
            var go = PtwPrefabs.MeshNode(name, mesh, parent, mat);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var layer = go.AddComponent<SkyLayer>();
            PtwPrefabs.Wire(layer, "targetCamera", cam);
            PtwPrefabs.Wire(layer, "distance", distance);
            PtwPrefabs.Wire(layer, "viewportBottom", bottom);
            PtwPrefabs.Wire(layer, "viewportTop", top);
            PtwPrefabs.Wire(layer, "widthScale", 1.4f);
            PtwPrefabs.Wire(layer, "parallax", parallax);
        }

        /// <summary>
        /// Small floating islands up in the sky, each with a grass cap, a fringe, a vine or two and
        /// a tiny glowing portal - the mockup's far islands. Camera-relative, above the island, so
        /// they never overlap the play area or the HUD on any level's framing.
        /// </summary>
        static void BuildSkyIslets(Transform parent, Camera cam)
        {
            var grass = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Grass.prefab");
            var stone = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Stone.prefab");
            var vine = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_Vine.prefab");
            var flower = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_Flower.prefab");
            if (!grass || !stone) return;

            var rnd = new System.Random(4242);
            // (viewport x, viewport y, width in blocks, scale, distance)
            var spots = new[]
            {
                (0.22f, 0.71f, 3, 0.36f, 46f), (0.60f, 0.64f, 5, 0.42f, 44f), (0.86f, 0.72f, 3, 0.30f, 48f),
            };
            foreach (var (vx, vy, w, scale, z) in spots)
            {
                var islet = new GameObject("Islet");
                islet.transform.SetParent(parent, false);
                var layer = islet.AddComponent<SkyLayer>();
                PtwPrefabs.Wire(layer, "targetCamera", cam);
                PtwPrefabs.Wire(layer, "distance", z);
                PtwPrefabs.Wire(layer, "centred", true);
                PtwPrefabs.Wire(layer, "viewportPos", new Vector2(vx, vy));
                PtwPrefabs.Wire(layer, "worldSize", new Vector2(scale, scale));
                // Between the mid clouds and the island in depth, and floating: a slow bob of a
                // fifth of a block, each islet out of phase with the others (SkyLayer seeds it).
                PtwPrefabs.Wire(layer, "parallax", 0.5f);
                PtwPrefabs.Wire(layer, "bobAmplitude", 0.14f);
                PtwPrefabs.Wire(layer, "bobHz", 0.06f);

                // Inverted pyramid of blocks, built in XY like the real islands.
                int rows = w >= 5 ? 3 : 2;
                for (int r = 0; r < rows; r++)
                {
                    int count = w - r * 2;
                    for (int c = 0; c < count; c++)
                    {
                        var src = r == 0 ? grass : stone;
                        var b = (GameObject)PrefabUtility.InstantiatePrefab(src, islet.transform);
                        b.transform.localPosition = new Vector3(c - (count - 1) * 0.5f, -r * PtwMeshes.BlockH, 0f);
                        StripCollidersAndShadows(b);
                        if (r == 0)
                        {
                            var fringe = b.transform.Find("Fringe"); if (fringe) fringe.gameObject.SetActive(true);
                            var tufts = b.transform.Find("Tufts"); if (tufts) tufts.gameObject.SetActive(rnd.NextDouble() < 0.6);
                        }
                    }
                }

                // A tiny portal in the middle of the top row.
                var arch = PtwPrefabs.MeshNode("Arch", "Mesh_DoorArch", islet.transform, PtwArt.MFarStone, PtwArt.MFarStone, PtwArt.MPortalStud);
                arch.transform.localPosition = new Vector3(0f, 0f, 0f);
                arch.transform.localScale = Vector3.one * 0.8f;
                StripCollidersAndShadows(arch, keepMaterials: true);
                var glow = PtwPrefabs.MeshNode("Glow", "Mesh_ArchFill", arch.transform, PtwArt.MPortalEnergyFar);
                StripCollidersAndShadows(glow, keepMaterials: true);
                var halo = PtwPrefabs.MeshNode("Halo", "Mesh_QuadXY", arch.transform, PtwArt.MPortalGlow);
                halo.transform.localPosition = new Vector3(0f, 0.9f, -0.2f);
                halo.transform.localScale = new Vector3(2.4f, 2.8f, 1f);
                StripCollidersAndShadows(halo, keepMaterials: true);

                if (vine)
                {
                    int side = rnd.NextDouble() < 0.5 ? -1 : 1;
                    var v = (GameObject)PrefabUtility.InstantiatePrefab(vine, islet.transform);
                    v.transform.localPosition = new Vector3(side * ((w - 1) * 0.5f + 0.56f), -0.05f, -0.2f);
                    StripCollidersAndShadows(v, keepMaterials: true);
                }
                if (flower)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flower, islet.transform);
                    f.transform.localPosition = new Vector3(-(w - 1) * 0.5f + 0.2f, 0f, -0.3f);
                    StripCollidersAndShadows(f, keepMaterials: true);
                }
            }
        }

        static ParticleSystem MakeFireflies(Transform cameraTransform)
        {
            var go = new GameObject("Fireflies");
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = new Vector3(0f, 2f, 30f);      // about the island's plane

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
            sh.scale = new Vector3(30f, 48f, 8f);

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
            keyGo.transform.rotation = Quaternion.Euler(46f, 40f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = KeyColor;
            key.intensity = 0.2f;    // top faces only ~6% brighter than fronts in the mockup; the ambient carries the frame
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
            fillGo.transform.rotation = Quaternion.Euler(20f, -130f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = PtwArt.Hex("#F2C8D3");   // blush fill from the sky's pink
            fill.intensity = 0.12f;
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
            RenderSettings.fog = false;
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
                SetProp(so, "m_ShadowDistance", 45f);
                SetProp(so, "m_ShadowCascadeCount", 1);
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

        // ================================================================ backdrop scenery ====
        /// <summary>
        /// A few distant islands sitting well behind the play area.
        ///
        /// These are COMPLETELY STATIC, and in v2 that is the entire point. v1 slid them at a
        /// fraction of the world's speed and had a long comment about parallax factors being
        /// deliberately zero, because a backdrop that scrolls is the signature of a moving camera.
        /// v2 has a genuinely rotating object in the middle of the frame, so a fixed backdrop is
        /// the reference that makes the rotation unambiguous: something in shot is definitely not
        /// turning, therefore the island definitely is.
        ///
        /// They get hazed, desaturated materials so they read as distance rather than as level
        /// geometry the player is failing to reach.
        /// </summary>
        static void BuildBackdropScenery()
        {
            var layer = new GameObject("StaticBackdrop");
            // The camera is pitched 20 degrees, so anything far along +Z projects UPWARD on screen
            // (screen height ~ 0.94*y + 0.34*z). At y = -4.5 the nearest islets landed at grass
            // level and, on the thin two-row levels, poked out from behind the floor looking like a
            // stray dark slab with a tree on it. Four metres lower keeps every piece below even the
            // thinnest island; the tall ones still hide the rest behind their blocks.
            layer.transform.position = new Vector3(0f, -8.5f, 18f);

            var grass = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Grass.prefab");
            var stone = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Stone.prefab");
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_TreeSmall.prefab");

            var rnd = new System.Random(4242);
            // Kept in a band BELOW the play area rather than scattered around it.
            //
            // The first v2 pass inherited v1's ring of spots, which put islets in the top corners
            // where they clipped the screen edge and read as stray blocks floating next to the
            // level rather than as scenery. Portrait framing leaves its slack at the bottom (the
            // camera frames the level's width, so there is always spare height), so that is where
            // scenery belongs: it fills the dead space and stays clear of the island and the HUD.
            // Kept inside |x| < 3.5. Framing is per-level now, so the visible width changes from
            // level to level (a 40-degree level is framed much tighter than a free-spinning one) -
            // anything further out than the tightest level's half-width gets sliced by the screen
            // edge on that level and reads as debris. This band is inside all of them.
            var spots = new[]
            {
                new Vector3(-3.2f, -1.5f, -3f), new Vector3(2.6f, -3.2f, 2f),
                new Vector3(-1.2f, -5.8f, 6f),  new Vector3(3.1f, -7.4f, -5f),
                new Vector3(-2.8f, -8.6f, 4f),  new Vector3(0.6f, -11f, 1f),
            };

            foreach (var s in spots)
            {
                var cluster = new GameObject("Islet");
                cluster.transform.SetParent(layer.transform, false);
                cluster.transform.localPosition = s;
                float scale = 0.45f + (float)rnd.NextDouble() * 0.3f;
                cluster.transform.localScale = Vector3.one * scale;
                // Only a small Z tilt. A Y rotation would swing the islet away from a camera that
                // is looking almost straight down the Z axis and it would vanish edge-on.
                cluster.transform.localRotation =
                    Quaternion.Euler(0f, 0f, ((float)rnd.NextDouble() - 0.5f) * 16f);

                // Built in XY like everything else in v2, so an islet reads as a chunk seen
                // face-on rather than as a floor plan.
                int w = 1 + rnd.Next(3), tall = 1 + rnd.Next(2);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < tall; y++)
                    {
                        var src = y == 0 ? (rnd.NextDouble() > 0.4 ? grass : stone) : stone;
                        if (!src) continue;
                        var b = (GameObject)PrefabUtility.InstantiatePrefab(src, cluster.transform);
                        b.transform.localPosition = new Vector3(x, -y * PtwMeshes.BlockH, 0f);
                        StripCollidersAndShadows(b);
                    }

                if (tree && rnd.NextDouble() > 0.45)
                {
                    var t = (GameObject)PrefabUtility.InstantiatePrefab(tree, cluster.transform);
                    t.transform.localPosition = new Vector3(rnd.Next(w), 0f, -0.1f);
                    StripCollidersAndShadows(t);
                }
            }
        }

        static void StripCollidersAndShadows(GameObject go, bool keepMaterials = false)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c, true);

            var far = PtwArt.Get(PtwArt.MFarStone);
            var farGrass = PtwArt.Get(PtwArt.MFarGrass);

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (keepMaterials) continue;

                // Swap to hazed materials so distance reads without needing fog. Vegetation keeps
                // its wind material (already a soft green) so far islets still sway.
                if (far == null || farGrass == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name : "";
                    if (n.Contains("Vine") || n.Contains("FoliageWind") || n.Contains("Flower")) continue;
                    mats[i] = n.Contains("Grass") || n.Contains("Foliage") ? farGrass : far;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// A static sea far below the islands. Deliberately NOT parented to WorldRoot.
        ///
        /// This is the fix for "it still feels like the player is moving". A fixed camera over a
        /// moving world and a moving camera over a fixed world are the same image; the only thing
        /// that tells them apart is something static with visible features. The backdrop was a
        /// featureless gradient, so there was literally nothing to judge against. Now the islands
        /// visibly travel across a textured sea that never moves, and drop their shadows onto it.
        /// </summary>
        static void BuildOcean()
        {
            var go = PtwPrefabs.MeshNode("Ocean", "Mesh_OceanPlane", null, PtwArt.MOcean);
            go.transform.position = new Vector3(0f, -13f, 0f);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        /// <summary>
        /// Two dust emitters, both driven by ImpactFeedback.
        ///
        /// v1 had a single system fed by how fast the world was being dragged. v2 has no drag, and
        /// dust that responded to ROTATION would be wrong anyway - the level turning is not the
        /// thing hitting something. So it is split by cause: a one-shot burst at the contact point
        /// of an impact, and a continuous trickle while the ball is skidding along the ground.
        /// </summary>
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

            var shadowBold = MakeTmpShadowMaterial(bold, "TMP_PoppinsBold_Shadow");
            var shadowSemi = MakeTmpShadowMaterial(semi, "TMP_PoppinsSemi_Shadow");
            // The title's look from the mockup: sage letters with a soft cream halo.
            var titleMat = MakeTmpOutlineMaterial(bold, "TMP_PoppinsBold_Title", PtwArt.Hex("#FBF3E8"), 0.14f);

            // ================================================================== HUD =========
            // No scrims: the pastel sky is light, so the HUD is dark-on-light like the mockup -
            // sage type, cream discs - and needs nothing under it.
            var hudPanel = Panel(canvasGo.transform, "HudPanel", out var hudGroup, popFrom: 1f);

            var levelLabel = Text(hudPanel.transform, "LevelLabel", "LEVEL 1", semi, 46f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -58f),
                                  new Vector2(420f, 64f), TextAlignmentOptions.TopLeft,
                                  UiInk, null);
            levelLabel.characterSpacing = 8f;
            levelLabel.gameObject.AddComponent<Punch>();   // punched on every level load

            // The level's name under its number, small and widely tracked like the mockup's
            // "FIND THE PORTAL".
            var levelTitle = Text(hudPanel.transform, "LevelTitle", "TIP IT OVER", semi, 24f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -118f),
                                  new Vector2(560f, 36f), TextAlignmentOptions.TopLeft,
                                  UiInkSoft, null);
            levelTitle.characterSpacing = 12f;

            var restartBtn = RoundButton(hudPanel.transform, "RestartButton", new Vector2(1f, 1f),
                                         new Vector2(-156f, -52f), 96f, MakeRestartSprite());
            var pauseBtn = RoundButton(hudPanel.transform, "PauseButton", new Vector2(1f, 1f),
                                       new Vector2(-44f, -52f), 96f, MakePauseSprite());

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
            rgRt.anchorMin = rgRt.anchorMax = new Vector2(0.5f, 0.5f);
            rgRt.pivot = new Vector2(0.5f, 0.5f);
            // The gesture lives at the BOTTOM of the screen like the mockup's "TILT TO GUIDE": the
            // pivot sits low, and the finger's arc (radius 235, centred straight below the pivot)
            // lands over the arrow.
            rgRt.anchoredPosition = new Vector2(0f, -560f);
            rgRt.sizeDelta = new Vector2(10f, 10f);
            var rotGroup = rotGroupGo.GetComponent<CanvasGroup>();
            rotGroup.alpha = 0f;
            rotGroup.blocksRaycasts = false;
            rotGroup.interactable = false;

            // A thin two-headed arc for the direction, and the words under it.
            var arc = new GameObject("Arc", typeof(RectTransform), typeof(Image));
            arc.transform.SetParent(rotGroupGo.transform, false);
            var aRt = arc.GetComponent<RectTransform>();
            aRt.anchorMin = aRt.anchorMax = new Vector2(0.5f, 0.5f);
            aRt.pivot = new Vector2(0.5f, 0.5f);
            // The sprite's arc bows down from ITS centre to radius 0.82 * 260 px, so centring it on
            // the pivot puts the arc exactly under the finger's sweep (radius 235).
            aRt.anchoredPosition = Vector2.zero;
            aRt.sizeDelta = new Vector2(560f, 560f);
            var aImg = arc.GetComponent<Image>();
            aImg.sprite = MakeArcArrowSprite();
            aImg.color = UiInk;
            aImg.raycastTarget = false;

            var tilt = Text(rotGroupGo.transform, "TiltLabel", "TILT TO GUIDE", semi, 30f,
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -305f),
                            new Vector2(600f, 44f), TextAlignmentOptions.Center, UiInk, null);
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
            PtwPrefabs.Wire(onboarding, "pointGroup", pointGroup);
            PtwPrefabs.Wire(onboarding, "pointRing", ringRt);
            PtwPrefabs.Wire(onboarding, "worldCamera", cam);

            // ============================================================ main menu =========
            // No dim: the mockup's menu is the sky and the island with the type sitting on them.
            var menuPanel = Panel(canvasGo.transform, "MainMenuPanel", out var menuGroup);

            // "PULL" larger than "THE WORLD", sage with a cream halo, high in the frame.
            var title = Text(menuPanel.transform, "Title", "<size=132>PULL</size>\n<size=100>THE WORLD</size>",
                             bold, 100f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 620f), new Vector2(1000f, 380f),
                             TextAlignmentOptions.Center, UiSage, titleMat);
            title.characterSpacing = 1f;
            title.lineSpacing = -26f;
            title.richText = true;

            // PLAY breathes. The pulse lives on a wrapper so it does not fight the press-juice
            // on the button itself - two components driving one localScale would tear. It sits
            // well below the island like the mockup's, with room to breathe around it.
            var playWrap = new GameObject("PlayPulse", typeof(RectTransform));
            playWrap.transform.SetParent(menuPanel.transform, false);
            var pwRt = playWrap.GetComponent<RectTransform>();
            pwRt.anchorMin = pwRt.anchorMax = new Vector2(0.5f, 0.5f);
            pwRt.pivot = new Vector2(0.5f, 0.5f);
            pwRt.anchoredPosition = new Vector2(0f, -400f);
            pwRt.sizeDelta = new Vector2(600f, 176f);
            playWrap.AddComponent<UiPulse>();

            var playBtn = PillButton(playWrap.transform, "PlayButton", "PLAY", bold, 68f,
                                     Vector2.zero, new Vector2(600f, 176f),
                                     UiGreen, shadowBold, UiCream);

            var progressLabel = Text(menuPanel.transform, "ProgressLabel", "LEVEL 1", bold, 44f,
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(0f, -540f), new Vector2(600f, 60f),
                                     TextAlignmentOptions.Center, UiSage, null);
            progressLabel.characterSpacing = 6f;

            // Level picker entry. Small, cream, under the progress line: a convenience, not the
            // main verb - a first-time player should still just hit PLAY.
            var levelsBtn = PillButton(menuPanel.transform, "LevelsButton", "LEVELS", semi, 32f,
                                       new Vector2(0f, -640f), new Vector2(300f, 84f),
                                       UiCream, null, UiSage);

            var menuSettingsBtn = RoundButton(menuPanel.transform, "SettingsButton",
                                              new Vector2(0.5f, 0.5f), new Vector2(0f, -780f),
                                              116f, MakeGearSprite());

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

            var continueBtn = PillButton(donePanel.transform, "ContinueButton", "CONTINUE",
                                         bold, 54f, new Vector2(0f, -430f), new Vector2(620f, 150f),
                                         UiGreen, shadowBold, UiCream);

            // Confetti lives in the WORLD, parented to the camera just inside the UI plane, so it
            // draws in front of the panel. A ParticleSystem under a ScreenSpaceCamera canvas
            // sorts behind it and would be invisible exactly when it matters.
            var celebration = MakeCelebrationVfx(cam.transform);

            // ============================================================== settings =========
            var setPanel = Panel(canvasGo.transform, "SettingsPanel", out var setGroup);
            Dim(setPanel.transform, "Dim", UiHaze);

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(setPanel.transform, false);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            // Dropped 90px so the card's top edge clears the "THE WORLD" title behind it on the
            // menu; centred, the rim sliced straight through the letters.
            cardRt.anchoredPosition = new Vector2(0f, -90f);
            cardRt.sizeDelta = new Vector2(880f, 1080f);
            var cardImg = card.GetComponent<Image>();
            cardImg.sprite = MakePanelSprite();
            cardImg.color = UiCard;                     // cream card on the haze, like paper
            Gloss(card, 0.95f, 0.10f, -14f);

            // A faint rim behind the card so its edge is defined against the dim rather than
            // dissolving into it.
            var rim = new GameObject("Rim", typeof(RectTransform), typeof(Image));
            rim.transform.SetParent(card.transform, false);
            var rimRt = rim.GetComponent<RectTransform>();
            Stretch(rimRt);
            rimRt.offsetMin = new Vector2(-6f, -6f);
            rimRt.offsetMax = new Vector2(6f, 6f);
            var rimImg = rim.GetComponent<Image>();
            rimImg.sprite = MakePanelSprite();
            rimImg.color = new Color(0.36f, 0.48f, 0.42f, 0.10f);
            rimImg.raycastTarget = false;
            rim.transform.SetAsFirstSibling();

            Text(card.transform, "SettingsTitle", "SETTINGS", bold, 60f,
                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 430f),
                 new Vector2(700f, 90f), TextAlignmentOptions.Center, UiSage, null)
                .characterSpacing = 8f;

            var soundToggle = ToggleRow(card.transform, "SoundToggle", "SOUND", semi, 250f, shadowSemi);
            var musicToggle = ToggleRow(card.transform, "MusicToggle", "MUSIC", semi, 120f, shadowSemi);
            var hapticsToggle = ToggleRow(card.transform, "HapticsToggle", "HAPTICS", semi, -10f, shadowSemi);

            // Destructive, so it is coloured like one and sits apart from the toggles. UiRoot makes
            // it a two-tap confirm; the label text is swapped to say so.
            var restartAllBtn = PillButton(card.transform, "RestartAllButton", "RESTART ALL LEVELS",
                                           bold, 36f, new Vector2(0f, -175f), new Vector2(660f, 118f),
                                           PtwArt.Hex("#D3928A"), shadowBold, UiCream);

            // Development convenience: opens every level in the picker so a build can be tested
            // from any point. UiRoot hides it when showDevUnlock is off - flip that for release.
            var unlockAllBtn = PillButton(card.transform, "UnlockAllButton", "UNLOCK ALL LEVELS  (DEV)",
                                          semi, 30f, new Vector2(0f, -292f), new Vector2(660f, 100f),
                                          PtwArt.Hex("#9BBDB2"), shadowSemi, UiCream);

            var closeBtn = PillButton(card.transform, "CloseButton", "CLOSE", bold, 48f,
                                      new Vector2(0f, -445f), new Vector2(520f, 132f),
                                      UiGreen, shadowBold, UiCream);

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
            grid.cellSize = new Vector2(150f, 150f);
            grid.spacing = new Vector2(22f, 22f);
            grid.padding = new RectOffset(0, 0, 0, 40);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;

            var tile = PillButton(gridGo.transform, "TileTemplate", "1", bold, 52f, Vector2.zero,
                                  new Vector2(150f, 150f), UiGreen, shadowBold, UiCream);
            tile.gameObject.SetActive(false);

            var closePickBtn = PillButton(pickPanel.transform, "CloseLevelsButton", "CLOSE", bold, 48f,
                                          new Vector2(0f, -760f), new Vector2(420f, 120f),
                                          UiCream, null, UiSage);

            // ================================================================= skip (ad) =======
            // Lives in the HUD, hidden. UiRoot shows it only after AdsManager.SkipAfterFails deaths
            // on one level: an offer to a stuck player, never a toll.
            var skipBtn = PillButton(hudPanel.transform, "SkipButton", "STUCK?  SKIP LEVEL  ▶", semi, 34f,
                                     new Vector2(0f, -790f), new Vector2(620f, 100f),
                                     PtwArt.Hex("#E4BC84"), shadowSemi, UiCream);
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
            PtwPrefabs.Wire(root, "soundToggle", soundToggle);
            PtwPrefabs.Wire(root, "musicToggle", musicToggle);
            PtwPrefabs.Wire(root, "hapticsToggle", hapticsToggle);

            PtwPrefabs.Wire(root, "flashImage", flash);
            PtwPrefabs.Wire(root, "levels", levels);
            PtwPrefabs.Wire(root, "onboarding", onboarding);
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
        // The UI palette, from the mockups: sage type, cream surfaces, a muted green for the one
        // button that matters, and a warm haze instead of a dark dim behind popups.
        static readonly Color UiSage = PtwArt.Hex("#4F7A5E");     // titles, PLAY-adjacent labels
        static readonly Color UiInk = PtwArt.Hex("#5A716D");      // HUD and body text
        static readonly Color UiInkSoft = PtwArt.Hex("#748985");  // secondary text
        static readonly Color UiGreen = PtwArt.Hex("#7FA37A");    // PLAY, CONTINUE, toggles on
        static readonly Color UiCream = PtwArt.Hex("#F8F2E6");    // discs, small pills, labels on green
        static readonly Color UiCard = PtwArt.Hex("#FBF6EE");     // popup cards
        static readonly Color UiHaze = new Color(0.99f, 0.91f, 0.89f, 0.42f);   // a blush veil, not a grey one

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

        static Button RoundButton(Transform parent, string name, Vector2 anchor, Vector2 pos,
                                  float size, Sprite icon)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            img.sprite = MakeDiscSprite();
            // A translucent cream disc with a sage icon, like the mockup's restart and pause.
            img.color = new Color(1f, 0.99f, 0.97f, 0.86f);
            Gloss(go, 0.9f, 0.10f, -3f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(size * 0.52f, size * 0.52f);
            var iimg = iconGo.GetComponent<Image>();
            iimg.sprite = icon;
            iimg.color = UiSage;
            iimg.raycastTarget = false;

            go.AddComponent<UiButtonJuice>();
            return go.GetComponent<Button>();
        }

        static Button PillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                 float fontSize, Vector2 pos, Vector2 size, Color tint,
                                 Material shadowMat, Color? labelColor = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            // A very round sprite stretched wide reads as a proper pill, which is why this does
            // not need 9-slicing.
            img.sprite = MakePillSprite();
            img.color = tint;
            Gloss(go);

            var t = Text(go.transform, "Label", label, font, fontSize,
                         new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                         size, TextAlignmentOptions.Center, labelColor ?? Color.white, shadowMat);
            t.characterSpacing = 6f;

            var btn = go.GetComponent<Button>();
            // A visible press state matters more on mobile than anywhere else - there is no
            // hover, so the tap flash is the only confirmation the button was hit.
            var colors = btn.colors;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            go.AddComponent<UiButtonJuice>();   // shrink on press, pop on release
            return btn;
        }

        /// <summary>A label plus a pill-shaped on/off switch, laid out as one row inside a card.</summary>
        static Toggle ToggleRow(Transform parent, string name, string label, TMP_FontAsset font,
                                float y, Material shadowMat)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            row.transform.SetParent(parent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(700f, 110f);

            Text(row.transform, "Label", label, font, 42f,
                 new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f),
                 new Vector2(400f, 80f), TextAlignmentOptions.Left,
                 UiInk, null).characterSpacing = 6f;

            // Track
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(row.transform, false);
            var trRt = track.GetComponent<RectTransform>();
            trRt.anchorMin = trRt.anchorMax = new Vector2(1f, 0.5f);
            trRt.pivot = new Vector2(1f, 0.5f);
            trRt.anchoredPosition = new Vector2(-20f, 0f);
            trRt.sizeDelta = new Vector2(150f, 74f);
            var trImg = track.GetComponent<Image>();
            trImg.sprite = MakePillSprite();
            trImg.color = PtwArt.Hex("#DDD4C7");   // off: warm grey-cream
            Gloss(track, 0.9f, 0.12f, 0f);

            // ON is a green FILL over the whole track, not a knob that slides.
            //
            // The obvious version - a grey knob at the left and a green one at the right, with the
            // green one as the Toggle's graphic - does not work, and the first capture showed why:
            // a plain Toggle only shows and hides its `graphic`, it cannot move anything. So in the
            // ON state BOTH knobs are visible and the control reads as two unrelated dots rather
            // than as a switch. Animating a thumb would need a script per row. A pill that lights
            // up green is unambiguous with no moving parts.
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            Stretch(fill.GetComponent<RectTransform>());
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = MakePillSprite();
            fillImg.color = UiGreen;
            fillImg.raycastTarget = false;

            var pip = new GameObject("Pip", typeof(RectTransform), typeof(Image));
            pip.transform.SetParent(fill.transform, false);
            var pipRt = pip.GetComponent<RectTransform>();
            pipRt.anchorMin = pipRt.anchorMax = new Vector2(1f, 0.5f);
            pipRt.pivot = new Vector2(1f, 0.5f);
            pipRt.anchoredPosition = new Vector2(-8f, 0f);
            pipRt.sizeDelta = new Vector2(46f, 46f);
            var pipImg = pip.GetComponent<Image>();
            pipImg.sprite = MakeDiscSprite();
            pipImg.color = new Color(1f, 1f, 1f, 0.92f);
            pipImg.raycastTarget = false;

            var toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = trImg;
            toggle.graphic = fillImg;      // whole fill (and its pip) hides when off
            toggle.isOn = true;
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
        static Sprite pillSprite;
        /// <summary>Stadium shape. Stretched wide it stays a pill, so it needs no 9-slicing.</summary>
        static Sprite MakePillSprite()
        {
            if (pillSprite) return pillSprite;
            return pillSprite = PaintSpriteXY("Tex_Pill", 128, p =>
            {
                float d = RoundedBox(p, new Vector2(0.62f, 0.62f), 0.36f);
                return Mathf.Clamp01(-d / 0.03f);
            });
        }

        static Sprite panelSprite;
        static Sprite MakePanelSprite()
        {
            if (panelSprite) return panelSprite;
            return panelSprite = PaintSpriteXY("Tex_Panel", 128, p =>
            {
                float d = RoundedBox(p, new Vector2(0.86f, 0.86f), 0.12f);
                return Mathf.Clamp01(-d / 0.02f);
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
            string path = $"{PtwArt.MatDir}/{id}.mat";
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

        static Sprite pauseSprite;
        /// <summary>Two rounded bars: the pause glyph, drawn so it can never be tofu.</summary>
        static Sprite MakePauseSprite()
        {
            if (pauseSprite) return pauseSprite;
            return pauseSprite = PaintSpriteXY("Tex_Pause", 128, p =>
            {
                float d1 = RoundedBox(p - new Vector2(-0.30f, 0f), new Vector2(0.17f, 0.62f), 0.14f);
                float d2 = RoundedBox(p - new Vector2(0.30f, 0f), new Vector2(0.17f, 0.62f), 0.14f);
                return Mathf.Clamp01(-Mathf.Min(d1, d2) / 0.04f);
            });
        }

        static Sprite arcArrowSprite;
        /// <summary>
        /// A thin arc with an arrowhead at each end, the mockup's tilt cue. Centred on the sprite's
        /// centre with the arc bowing DOWN below it (the finger sweeps the same arc).
        /// </summary>
        static Sprite MakeArcArrowSprite()
        {
            if (arcArrowSprite) return arcArrowSprite;
            const float r = 0.82f, band = 0.028f, half = 34f;   // degrees either side of straight down
            return arcArrowSprite = PaintSpriteXY("Tex_ArcArrow", 256, p =>
            {
                float d = p.magnitude;
                float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;   // -90 is straight down
                float off = Mathf.DeltaAngle(-90f, ang);
                float a = 0f;
                if (Mathf.Abs(off) <= half)
                    a = Mathf.Clamp01((band - Mathf.Abs(d - r)) / 0.012f);

                // Arrowheads: small triangles at both ends pointing along the tangent, outward.
                for (int s = -1; s <= 1; s += 2)
                {
                    float endRad = (-90f + s * half) * Mathf.Deg2Rad;
                    var c = new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * r;
                    var tangent = new Vector2(-Mathf.Sin(endRad), Mathf.Cos(endRad)) * s;
                    var radial = c.normalized;
                    var tip = c + tangent * 0.16f;
                    if (InTriangle(p, tip, c + radial * 0.085f, c - radial * 0.085f)) a = 1f;
                }
                return a;
            });
        }

        static Sprite gearSprite;
        /// <summary>
        /// A cog, drawn rather than typed. Poppins has no gear glyph, and v1 already learned that
        /// a missing glyph renders as a silent tofu box rather than as an error.
        /// </summary>
        static Sprite MakeGearSprite()
        {
            if (gearSprite) return gearSprite;
            const int teeth = 8;
            return gearSprite = PaintSprite("Tex_Gear", 128, (d, ang) =>
            {
                // Outer radius pulses with angle to make the teeth.
                float wave = Mathf.Cos(ang * Mathf.Deg2Rad * teeth);
                float outer = 0.72f + 0.16f * Mathf.Clamp01(wave * 2f);
                float body = Mathf.Clamp01((outer - d) / 0.05f);
                float hole = Mathf.Clamp01((d - 0.3f) / 0.05f);
                return body * hole;
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

        /// <summary>Soft dark gradient bar, so overlaid text always has something to sit on.</summary>
        static void AddScrim(Transform parent, string name, bool top, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, height);

            var img = go.GetComponent<Image>();
            img.sprite = MakeScrimSprite("Tex_Scrim" + (top ? "Top" : "Bottom"), top);
            img.color = new Color(0.055f, 0.085f, 0.125f, 0.24f);
            img.raycastTarget = false;
        }

        static readonly Dictionary<string, Sprite> scrimCache = new Dictionary<string, Sprite>();
        static Sprite MakeScrimSprite(string id, bool opaqueAtTop)
        {
            if (scrimCache.TryGetValue(id, out var s) && s) return s;
            s = PaintSpriteXY(id, 64, p =>
            {
                float t = (p.y + 1f) * 0.5f;                 // 0 bottom -> 1 top
                float a = opaqueAtTop ? t : 1f - t;
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(a));
            });
            scrimCache[id] = s;
            return s;
        }

        /// <summary>
        /// A shared TMP material with the underlay (drop shadow) feature on. Shared rather than
        /// per-text so it stays a real asset - touching TMP_Text.fontMaterial would spawn scene-only
        /// material instances.
        /// </summary>
        static Material MakeTmpShadowMaterial(TMP_FontAsset font, string id)
        {
            if (font == null || font.material == null) return null;
            string path = $"{PtwArt.MatDir}/{id}.mat";
            var m = new Material(font.material) { name = id };
            // A soft warm-dark underlay: on the pastel sky a hard black shadow reads as a sticker.
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0.30f, 0.24f, 0.30f, 0.40f));
            m.SetFloat("_UnderlayOffsetX", 0.3f);
            m.SetFloat("_UnderlayOffsetY", -0.5f);
            m.SetFloat("_UnderlayDilate", 0.1f);
            m.SetFloat("_UnderlaySoftness", 0.4f);

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

        static Sprite discSprite;
        static Sprite MakeDiscSprite()
        {
            if (discSprite) return discSprite;
            return discSprite = PaintSprite("Tex_Disc", 128, (d, ang) =>
                Mathf.Clamp01((1f - d) / 0.06f));   // soft-edged filled circle
        }

        static Sprite restartSprite;
        /// <summary>A circular arrow, drawn rather than typed, so it can never render as tofu.</summary>
        static Sprite MakeRestartSprite()
        {
            if (restartSprite) return restartSprite;

            const int s = 128;
            const float rMid = 0.60f, band = 0.115f;
            const float startDeg = 20f, endDeg = 320f;

            // Arrow head sits at the open end of the arc, pointing along the tangent.
            float endRad = endDeg * Mathf.Deg2Rad;
            Vector2 headC = new Vector2(Mathf.Cos(endRad), Mathf.Sin(endRad)) * rMid;
            Vector2 tangent = new Vector2(-Mathf.Sin(endRad), Mathf.Cos(endRad));
            Vector2 radial = headC.normalized;
            Vector2 tip = headC + tangent * 0.42f;
            Vector2 b1 = headC + radial * 0.30f;
            Vector2 b2 = headC - radial * 0.30f;

            return restartSprite = PaintSpriteXY("Tex_Restart", s, p =>
            {
                float d = p.magnitude;
                float ang = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 360f);

                float a = 0f;
                if (ang >= startDeg && ang <= endDeg)
                {
                    float edge = Mathf.Abs(d - rMid);
                    a = Mathf.Clamp01((band - edge) / 0.045f);
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

        /// <summary>Paint by position in [-1,1] space centred on the sprite.</summary>
        static Sprite PaintSpriteXY(string id, int size, System.Func<Vector2, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = id,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f - half) / half, (y + 0.5f - half) / half);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(p))));
                }
            tex.Apply();

            PtwPaths.EnsureFolder(PtwArt.TexDir);
            string path = $"{PtwArt.TexDir}/{id}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
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
