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
        static readonly Color AmbientColor = PtwArt.Hex("#5E6E8C");
        static readonly Color KeyColor = PtwArt.Hex("#DCE6FF");   // moonlight: the skies are night, so the key must be too

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

            BuildBackdropScenery();
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

            // Fireflies: a few slow motes drifting in the sky, tinted per chapter by SkyTheme.
            // Parented to the camera like the backdrop so they are always in frame; simulated in
            // world space so a reframe does not drag them.
            PtwPrefabs.Wire(sky, "fireflies", MakeFireflies(go.transform));

            return cam;
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
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
            main.startColor = new Color(0.8f, 0.85f, 1f, 1f);
            main.gravityModifier = -0.004f;                             // drift up, barely

            var em = ps.emission; em.enabled = true; em.rateOverTime = 4.5f;
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
            key.intensity = 1.35f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.78f;
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
            fill.color = PtwArt.Hex("#8FA6D0");
            fill.intensity = 0.42f;
            fill.shadows = LightShadows.None;
            fillGo.AddComponent<UniversalAdditionalLightData>().usePipelineSettings = true;

            // Flat cool ambient. This is a measured value, not a taste call: a white surface in
            // shadow on the reference sits exactly here.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
        }

        // ================================================================ post processing =====
        static void BuildVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                foreach (var c in new List<VolumeComponent>(profile.components))
                {
                    profile.Remove(c.GetType());
                    UnityEngine.Object.DestroyImmediate(c, true);
                }
            }

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.overrideState = true;
            tone.mode.value = TonemappingMode.ACES;   // punchier than Neutral, which flattened the grade

            var bloom = profile.Add<Bloom>(true);
            // Threshold well above 1.0 so ONLY emissives bloom, which is what the style bible asks
            // for: "bloom on emissives only - anchor ring, door glow, fire". At 0.95 it was also
            // catching brightly lit SURFACES; with the island tilted, the grass cap's top face
            // turns towards the key light (dot 0.88 against 0.74 upright) and was close to
            // clipping. The emissives all sit at 1.9-4.5 intensity, so they still bloom from here.
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.15f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 1.15f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true; bloom.tint.value = Color.white;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = false;   // mobile budget

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.overrideState = true; color.postExposure.value = 0.22f;
            // Restrained: the first pass ran contrast 9 / saturation 4 and crushed the backdrop
            // into a slate grey while pushing the grass to a candy green.
            color.contrast.overrideState = true; color.contrast.value = 20f;
            color.saturation.overrideState = true; color.saturation.value = 16f;

            var vig = profile.Add<Vignette>(true);
            // A tall portrait frame puts a lot of screen inside the vignette falloff, so this has
            // to stay very light or the whole backdrop goes dark.
            vig.intensity.overrideState = true; vig.intensity.value = 0.28f;
            vig.smoothness.overrideState = true; vig.smoothness.value = 0.6f;
            vig.color.overrideState = true; vig.color.value = PtwArt.Hex("#05080E");

            // Cool shadows, warm highlights. With a night sky this is what stops the frame reading
            // as "grey blocks in the dark": shadow sides go blue, the lit grass and the ivory ball
            // go warm, and the door's amber sits inside that scheme rather than on top of it.
            var split = profile.Add<SplitToning>(true);
            split.shadows.overrideState = true; split.shadows.value = PtwArt.Hex("#2C3F6E");
            split.highlights.overrideState = true; split.highlights.value = PtwArt.Hex("#FFD6A3");
            split.balance.overrideState = true; split.balance.value = -8f;

            EditorUtility.SetDirty(profile);

            var go = new GameObject("~PostProcessVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = profile;
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
            layer.transform.position = new Vector3(0f, -4.5f, 18f);

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

        static void StripCollidersAndShadows(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c, true);

            var far = PtwArt.Get(PtwArt.MFarStone);
            var farGrass = PtwArt.Get(PtwArt.MFarGrass);

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                // Swap to hazed materials so distance reads without needing fog.
                if (far == null || farGrass == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name : "";
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
            main.startColor = new Color(0.93f, 0.94f, 0.90f, 0.30f);
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

            // ================================================================== HUD =========
            var hudPanel = Panel(canvasGo.transform, "HudPanel", out var hudGroup, popFrom: 1f);

            // Scrims first, so the level number and buttons always have something to sit on.
            // White text over a mid blue-grey sky has almost no contrast on its own.
            AddScrim(hudPanel.transform, "ScrimTop", true, 470f);
            AddScrim(hudPanel.transform, "ScrimBottom", false, 300f);

            var levelLabel = Text(hudPanel.transform, "LevelLabel", "LEVEL 1", bold, 44f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -60f),
                                  new Vector2(420f, 60f), TextAlignmentOptions.TopLeft,
                                  Color.white, shadowBold);
            levelLabel.characterSpacing = 6f;
            levelLabel.gameObject.AddComponent<Punch>();   // punched on every level load

            // The level's name under its number. Levels have had titles since v1 and nothing
            // showed them; a name is a cheap way to make each one feel authored rather than
            // generated, which they all are.
            var levelTitle = Text(hudPanel.transform, "LevelTitle", "TIP IT OVER", semi, 28f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -112f),
                                  new Vector2(560f, 40f), TextAlignmentOptions.TopLeft,
                                  new Color(1f, 1f, 1f, 0.66f), shadowSemi);
            levelTitle.characterSpacing = 9f;

            var restartBtn = RoundButton(hudPanel.transform, "RestartButton", new Vector2(1f, 1f),
                                         new Vector2(-44f, -52f), 96f, MakeRestartSprite());
            var pauseBtn = RoundButton(hudPanel.transform, "PauseButton", new Vector2(1f, 1f),
                                       new Vector2(-156f, -52f), 96f, MakeGearSprite());

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
                                TextAlignmentOptions.Left, Color.white, shadowBold);

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
            rgRt.anchoredPosition = Vector2.zero;
            rgRt.sizeDelta = new Vector2(10f, 10f);
            var rotGroup = rotGroupGo.GetComponent<CanvasGroup>();
            rotGroup.alpha = 0f;
            rotGroup.blocksRaycasts = false;
            rotGroup.interactable = false;

            var finger = new GameObject("Finger", typeof(RectTransform), typeof(Image));
            finger.transform.SetParent(rotGroupGo.transform, false);
            var fRt = finger.GetComponent<RectTransform>();
            fRt.anchorMin = fRt.anchorMax = new Vector2(0.5f, 0.5f);
            fRt.pivot = new Vector2(0.5f, 0.5f);
            fRt.sizeDelta = new Vector2(84f, 84f);
            var fImg = finger.GetComponent<Image>();
            fImg.sprite = MakeCircleSprite();
            fImg.color = new Color(1f, 1f, 1f, 0.92f);
            fImg.raycastTarget = false;

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
            ringImg.color = new Color(0.53f, 0.87f, 0.99f, 0.95f);
            ringImg.raycastTarget = false;

            var onboarding = onboardGo.AddComponent<OnboardingHint>();
            PtwPrefabs.Wire(onboarding, "rotateGroup", rotGroup);
            PtwPrefabs.Wire(onboarding, "rotateFinger", fRt);
            PtwPrefabs.Wire(onboarding, "pointGroup", pointGroup);
            PtwPrefabs.Wire(onboarding, "pointRing", ringRt);
            PtwPrefabs.Wire(onboarding, "worldCamera", cam);

            // ============================================================ main menu =========
            var menuPanel = Panel(canvasGo.transform, "MainMenuPanel", out var menuGroup);
            Dim(menuPanel.transform, "Dim", new Color(0.05f, 0.08f, 0.12f, 0.5f));

            var title = Text(menuPanel.transform, "Title", "PULL\nTHE WORLD", bold, 118f,
                             new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                             new Vector2(0f, 520f), new Vector2(1000f, 340f),
                             TextAlignmentOptions.Center, Color.white, shadowBold);
            title.characterSpacing = 2f;
            title.lineSpacing = -14f;

            var tagline = Text(menuPanel.transform, "Tagline", "TURN THE WORLD. LET IT FALL.",
                               semi, 34f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               new Vector2(0f, 300f), new Vector2(940f, 60f),
                               TextAlignmentOptions.Center,
                               new Color(1f, 1f, 1f, 0.72f), shadowSemi);
            tagline.characterSpacing = 8f;

            // PLAY breathes. The pulse lives on a wrapper so it does not fight the press-juice
            // on the button itself - two components driving one localScale would tear.
            var playWrap = new GameObject("PlayPulse", typeof(RectTransform));
            playWrap.transform.SetParent(menuPanel.transform, false);
            var pwRt = playWrap.GetComponent<RectTransform>();
            pwRt.anchorMin = pwRt.anchorMax = new Vector2(0.5f, 0.5f);
            pwRt.pivot = new Vector2(0.5f, 0.5f);
            pwRt.anchoredPosition = new Vector2(0f, -120f);
            pwRt.sizeDelta = new Vector2(560f, 160f);
            playWrap.AddComponent<UiPulse>();

            var playBtn = PillButton(playWrap.transform, "PlayButton", "PLAY", bold, 62f,
                                     Vector2.zero, new Vector2(560f, 160f),
                                     PtwArt.Hex("#5AC26A"), shadowBold);

            var progressLabel = Text(menuPanel.transform, "ProgressLabel", "0 / 10", semi, 38f,
                                     new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(0f, -270f), new Vector2(500f, 60f),
                                     TextAlignmentOptions.Center,
                                     new Color(1f, 1f, 1f, 0.8f), shadowSemi);
            progressLabel.characterSpacing = 6f;

            var menuSettingsBtn = RoundButton(menuPanel.transform, "SettingsButton",
                                              new Vector2(0.5f, 0.5f), new Vector2(0f, -430f),
                                              108f, MakeGearSprite());

            // ======================================================= level complete =========
            var donePanel = Panel(canvasGo.transform, "LevelCompletePanel", out var doneGroup);
            Dim(donePanel.transform, "Dim", new Color(0.05f, 0.08f, 0.12f, 0.62f));

            var doneTitle = Text(donePanel.transform, "CompleteTitle", "LEVEL COMPLETE", bold, 76f,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 new Vector2(0f, 470f), new Vector2(1000f, 200f),
                                 TextAlignmentOptions.Center, Color.white, shadowBold);
            doneTitle.characterSpacing = 4f;
            doneTitle.enableWordWrapping = true;

            var doneSub = Text(donePanel.transform, "CompleteSubtitle", "LEVEL 1 OF 18", semi, 38f,
                               new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               new Vector2(0f, 350f), new Vector2(800f, 60f),
                               TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.78f), shadowSemi);
            doneSub.characterSpacing = 8f;

            var continueBtn = PillButton(donePanel.transform, "ContinueButton", "CONTINUE",
                                         bold, 54f, new Vector2(0f, -430f), new Vector2(620f, 150f),
                                         PtwArt.Hex("#5AC26A"), shadowBold);

            // Confetti lives in the WORLD, parented to the camera just inside the UI plane, so it
            // draws in front of the panel. A ParticleSystem under a ScreenSpaceCamera canvas
            // sorts behind it and would be invisible exactly when it matters.
            var celebration = MakeCelebrationVfx(cam.transform);

            // ============================================================== settings =========
            var setPanel = Panel(canvasGo.transform, "SettingsPanel", out var setGroup);
            Dim(setPanel.transform, "Dim", new Color(0.04f, 0.07f, 0.10f, 0.6f));

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
            // Lifted from #1B2733: over the dimmed menu that read as a black hole, not a card.
            cardImg.color = PtwArt.Hex("#2A3C51");
            Gloss(card, 0.80f, 0.55f, -10f);

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
            rimImg.color = new Color(1f, 1f, 1f, 0.07f);
            rimImg.raycastTarget = false;
            rim.transform.SetAsFirstSibling();

            Text(card.transform, "SettingsTitle", "SETTINGS", bold, 60f,
                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 430f),
                 new Vector2(700f, 90f), TextAlignmentOptions.Center, Color.white, shadowBold)
                .characterSpacing = 8f;

            var soundToggle = ToggleRow(card.transform, "SoundToggle", "SOUND", semi, 250f, shadowSemi);
            var musicToggle = ToggleRow(card.transform, "MusicToggle", "MUSIC", semi, 120f, shadowSemi);
            var hapticsToggle = ToggleRow(card.transform, "HapticsToggle", "HAPTICS", semi, -10f, shadowSemi);

            // Destructive, so it is coloured like one and sits apart from the toggles. UiRoot makes
            // it a two-tap confirm; the label text is swapped to say so.
            var restartAllBtn = PillButton(card.transform, "RestartAllButton", "RESTART ALL LEVELS",
                                           bold, 36f, new Vector2(0f, -190f), new Vector2(660f, 118f),
                                           PtwArt.Hex("#9B4343"), shadowBold);

            var closeBtn = PillButton(card.transform, "CloseButton", "CLOSE", bold, 48f,
                                      new Vector2(0f, -400f), new Vector2(520f, 132f),
                                      PtwArt.Hex("#55708E"), shadowBold);

            // ================================================================= flash =========
            var flashGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
            flashGo.transform.SetParent(canvasGo.transform, false);
            Stretch(flashGo.GetComponent<RectTransform>());
            var flash = flashGo.GetComponent<Image>();
            flash.color = Color.clear;
            flash.raycastTarget = false;

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
                                     new Vector2(900f, 90f), TextAlignmentOptions.Center, Color.white, shadowBold);
            chapterNumber.characterSpacing = 10f;
            var chapterName = Text(cardGo.transform, "ChapterName", "DUSK", semi, 40f,
                                   new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 516f),
                                   new Vector2(900f, 60f), TextAlignmentOptions.Center,
                                   new Color(1f, 1f, 1f, 0.75f), shadowSemi);
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
        static void Gloss(GameObject go, float bottom = 0.62f, float outlineAlpha = 0.45f,
                          float shadowY = -6f)
        {
            var grad = go.AddComponent<UnityEngine.UI.Extensions.Gradient>();
            grad.GradientDir = UnityEngine.UI.Extensions.GradientDir.Vertical;
            grad.OverwriteAllColor = false;
            grad.Vertex1 = Color.white;
            grad.Vertex2 = new Color(bottom, bottom, Mathf.Min(1f, bottom + 0.04f), 1f);

            // Built-in Outline, not the package's NicerOutline: in this Unity version NicerOutline
            // compiles as an empty stub (its real body is behind an #else for older Unity) and
            // has no members at all.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, outlineAlpha);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            if (Mathf.Abs(shadowY) > 0.01f)
            {
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
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
            // Brighter than the 0.16 it was: on a night sky a faint disc vanished. The outline
            // from Gloss() is what actually defines it now.
            img.color = new Color(1f, 1f, 1f, 0.24f);
            Gloss(go, 0.7f, 0.5f, -4f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(size * 0.55f, size * 0.55f);
            var iimg = iconGo.GetComponent<Image>();
            iimg.sprite = icon;
            iimg.color = Color.white;
            iimg.raycastTarget = false;

            go.AddComponent<UiButtonJuice>();
            return go.GetComponent<Button>();
        }

        static Button PillButton(Transform parent, string name, string label, TMP_FontAsset font,
                                 float fontSize, Vector2 pos, Vector2 size, Color tint,
                                 Material shadowMat)
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
                         size, TextAlignmentOptions.Center, Color.white, shadowMat);
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
                 new Color(1f, 1f, 1f, 0.9f), shadowMat).characterSpacing = 6f;

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
            trImg.color = PtwArt.Hex("#2E3E4E");
            Gloss(track, 0.72f, 0.45f, 0f);

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
            fillImg.color = PtwArt.Hex("#4FBF6A");
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
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0.03f, 0.05f, 0.08f, 0.9f));
            m.SetFloat("_UnderlayOffsetX", 0.5f);
            m.SetFloat("_UnderlayOffsetY", -0.6f);
            m.SetFloat("_UnderlayDilate", 0.15f);
            m.SetFloat("_UnderlaySoftness", 0.22f);

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
