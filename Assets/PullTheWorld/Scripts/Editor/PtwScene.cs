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
        static readonly Color AmbientColor = PtwArt.Hex("#8395B6");
        static readonly Color KeyColor = PtwArt.Hex("#FFF6EA");

        // ==================================================================== entry point ====
        public static void Build()
        {
            PtwPaths.EnsureFolder("Assets/PullTheWorld/Scenes");
            ConfigureUrp();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var systems = new GameObject("~Systems");
            var director = systems.AddComponent<GameDirector>();
            systems.AddComponent<PtwAudio>();

            Camera cam = BuildCamera(out IsometricCameraRig camRig);
            BuildLighting();
            BuildVolume();

            // ---- world ----
            var worldRoot = new GameObject("WorldRoot");
            var wrb = worldRoot.AddComponent<Rigidbody>();
            wrb.isKinematic = true;
            wrb.useGravity = false;
            // One kinematic body means every static level collider below it becomes a single
            // compound PhysX actor: cheap to teleport, and no broadphase churn while dragging.

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PtwPrefabs.Play + "/PlayerRig.prefab");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.transform.position = Vector3.zero;

            PtwPrefabs.Wire(camRig, "lookTarget", player.transform);
            camRig.ApplyFraming();

            var rig = systems.AddComponent<WorldRig>();
            PtwPrefabs.Wire(rig, "worldRoot", worldRoot.transform);
            PtwPrefabs.Wire(rig, "playerAnchor", player.transform);
            PtwPrefabs.Wire(rig, "viewCamera", cam);
            var blocker = player.transform.Find("Blocker");
            if (blocker) PtwPrefabs.Wire(rig, "playerBlocker", blocker.GetComponent<Collider>());

            var input = systems.AddComponent<WorldInput>();
            PtwPrefabs.Wire(input, "rig", rig);

            var levels = systems.AddComponent<LevelManager>();
            PtwPrefabs.Wire(levels, "rig", rig);
            PtwPrefabs.Wire(levels, "levelParent", worldRoot.transform);
            levels.EditorSetLevels(PtwLevels.LoadAll());

            // Ocean removed at the user's request. BuildOcean() is kept below and can be
            // re-enabled with one line; the static reference it provided is now supplied by the
            // hazed distant islands instead.
            BuildGrabMarker(rig, worldRoot.transform);
            BuildBackdropScenery(rig);
            BuildDust(rig);
            BuildUi(rig, levels, cam);

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
        static Camera BuildCamera(out IsometricCameraRig rigOut)
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PtwArt.BgBottom;
            cam.allowHDR = true;
            cam.allowMSAA = true;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            data.renderShadows = true;

            go.AddComponent<AudioListener>();
            rigOut = go.AddComponent<IsometricCameraRig>();

            // Backdrop welded to the camera so it can never drift.
            var bg = PtwPrefabs.MeshNode("Backdrop", "Mesh_QuadXY", go.transform, PtwArt.MBackground);
            var bgr = bg.GetComponent<MeshRenderer>();
            bgr.shadowCastingMode = ShadowCastingMode.Off;
            bgr.receiveShadows = false;
            var fill = bg.AddComponent<ScreenFillQuad>();
            PtwPrefabs.Wire(fill, "targetCamera", cam);

            return cam;
        }

        // ====================================================================== lighting =====
        static void BuildLighting()
        {
            var keyGo = new GameObject("KeyLight");
            // Upper-left, high pitch: verified to throw shadows down-and-right on screen for a
            // camera at (36, 45, 0), which is what the reference does in every panel.
            keyGo.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = KeyColor;
            key.intensity = 1.55f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.78f;
            key.shadowBias = 0.04f;
            key.shadowNormalBias = 0.28f;
            var keyData = keyGo.AddComponent<UniversalAdditionalLightData>();
            keyData.usePipelineSettings = true;

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
            tone.mode.value = TonemappingMode.Neutral;

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.overrideState = true; bloom.threshold.value = 0.95f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.85f;
            bloom.scatter.overrideState = true; bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true; bloom.tint.value = Color.white;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = false;   // mobile budget

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.overrideState = true; color.postExposure.value = 0.05f;
            // Restrained: the first pass ran contrast 9 / saturation 4 and crushed the backdrop
            // into a slate grey while pushing the grass to a candy green.
            color.contrast.overrideState = true; color.contrast.value = 3f;
            color.saturation.overrideState = true; color.saturation.value = 0f;

            var vig = profile.Add<Vignette>(true);
            // A tall portrait frame puts a lot of screen inside the vignette falloff, so this has
            // to stay very light or the whole backdrop goes dark.
            vig.intensity.overrideState = true; vig.intensity.value = 0.12f;
            vig.smoothness.overrideState = true; vig.smoothness.value = 0.75f;
            vig.color.overrideState = true; vig.color.value = PtwArt.Hex("#2B3644");

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
        /// A few distant islands that follow the world at a fraction of its speed. This is the
        /// depth cue that makes "the world moved" unambiguous rather than merely plausible.
        /// </summary>
        static void BuildBackdropScenery(WorldRig rig)
        {
            var layer = new GameObject("ParallaxBackdrop");
            // Close enough to actually be SEEN. These are the static reference that tells the eye
            // the world is what is moving - if they are off-screen (as they were) they contribute
            // nothing. They get hazed materials so they still read as distance, not as level
            // geometry the player cannot reach.
            layer.transform.position = new Vector3(0f, -5.5f, 19f);
            var pl = layer.AddComponent<ParallaxLayer>();
            PtwPrefabs.Wire(pl, "rig", rig);

            var grass = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Grass.prefab");
            var stone = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Blocks + "/Block_Stone.prefab");
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>(PtwPrefabs.Props + "/Prop_TreeSmall.prefab");

            var rnd = new System.Random(4242);
            var spots = new[]
            {
                new Vector3(-9f, 2.5f, -4f), new Vector3(8f, 5f, 2f),
                new Vector3(-5f, -3.5f, 7f), new Vector3(11f, -2f, -7f),
                new Vector3(2f, 8f, 5f), new Vector3(-12f, 0f, 1f),
                new Vector3(6f, -6f, 10f),
            };

            foreach (var s in spots)
            {
                var cluster = new GameObject("Islet");
                cluster.transform.SetParent(layer.transform, false);
                cluster.transform.localPosition = s;
                float scale = 0.45f + (float)rnd.NextDouble() * 0.3f;
                cluster.transform.localScale = Vector3.one * scale;
                cluster.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);

                int w = 1 + rnd.Next(2), d = 1 + rnd.Next(2);
                for (int x = 0; x < w; x++)
                    for (int z = 0; z < d; z++)
                    {
                        var src = rnd.NextDouble() > 0.45 ? grass : stone;
                        if (!src) continue;
                        var b = (GameObject)PrefabUtility.InstantiatePrefab(src, cluster.transform);
                        b.transform.localPosition = new Vector3(x, 0f, z);
                        StripCollidersAndShadows(b);
                    }

                if (tree && rnd.NextDouble() > 0.5)
                {
                    var t = (GameObject)PrefabUtility.InstantiatePrefab(tree, cluster.transform);
                    t.transform.localPosition = new Vector3(0f, 0f, 0f);
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

        /// <summary>Grip ring parented to the world, so it travels with the terrain you grabbed.</summary>
        static void BuildGrabMarker(WorldRig rig, Transform worldRoot)
        {
            var root = new GameObject("GrabMarker");
            root.transform.SetParent(worldRoot, false);

            var visual = PtwPrefabs.MeshNode("Ring", "Mesh_QuadXZ", root.transform, PtwArt.MAnchorRing);
            visual.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            visual.transform.localScale = Vector3.one * 1.15f;
            var vr = visual.GetComponent<MeshRenderer>();
            vr.shadowCastingMode = ShadowCastingMode.Off;
            vr.receiveShadows = false;
            vr.enabled = false;

            var gm = root.AddComponent<GrabMarker>();
            PtwPrefabs.Wire(gm, "rig", rig);
            PtwPrefabs.Wire(gm, "visual", visual.transform);
        }

        static void BuildDust(WorldRig rig)
        {
            var go = new GameObject("WorldDust");
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            main.maxParticles = 90;
            main.startLifetime = 0.6f;
            main.startSize = 0.07f;
            main.startSpeed = 0f;
            main.startColor = new Color(1f, 1f, 1f, 0.5f);
            main.gravityModifier = -0.05f;
            var em = ps.emission; em.enabled = false;   // driven entirely by WorldMotionDust
            var sh = ps.shape; sh.enabled = false;

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
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.25f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var dust = go.AddComponent<WorldMotionDust>();
            PtwPrefabs.Wire(dust, "rig", rig);
            PtwPrefabs.Wire(dust, "dust", ps);
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

        static void BuildUi(WorldRig rig, LevelManager levels, Camera cam)
        {
            var bold = EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-Bold.ttf", FontPath);
            var semi = EnsureFont("Assets/PullTheWorld/Art/Fonts/Poppins-SemiBold.ttf", FontSemiPath);

            var canvasGo = new GameObject("HUD");
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

            // Scrims FIRST so everything else draws on top of them. White text on a mid blue-grey
            // sky has almost no contrast; the reference sheet solves the same problem with dark
            // caption bars, so these are soft dark gradients top and bottom.
            AddScrim(canvasGo.transform, "ScrimTop", true, 340f);
            AddScrim(canvasGo.transform, "ScrimBottom", false, 420f);

            var shadowBold = MakeTmpShadowMaterial(bold, "TMP_PoppinsBold_Shadow");
            var shadowSemi = MakeTmpShadowMaterial(semi, "TMP_PoppinsSemi_Shadow");

            // ---- top bar ----
            var levelLabel = Text(canvasGo.transform, "LevelLabel", "LEVEL 1", bold, 46f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -66f),
                                  new Vector2(420f, 60f), TextAlignmentOptions.TopLeft,
                                  PtwArt.Hex("#FFFFFF"), shadowBold);
            levelLabel.characterSpacing = 6f;

            var titleLabel = Text(canvasGo.transform, "TitleLabel", "PULL THE WORLD", semi, 28f,
                                  new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -116f),
                                  new Vector2(520f, 40f), TextAlignmentOptions.TopLeft,
                                  new Color(1f, 1f, 1f, 0.62f), shadowSemi);
            titleLabel.characterSpacing = 9f;

            // ---- restart ----
            var btnGo = new GameObject("RestartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(canvasGo.transform, false);
            var brt = btnGo.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(-44f, -56f);
            brt.sizeDelta = new Vector2(96f, 96f);
            var bimg = btnGo.GetComponent<Image>();
            bimg.sprite = MakeDiscSprite();
            bimg.color = new Color(1f, 1f, 1f, 0.16f);
            var btn = btnGo.GetComponent<Button>();

            // A drawn icon, not a font glyph. Poppins has no U+21BB, so a text-based restart
            // symbol silently renders as a tofu box.
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(btnGo.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(52f, 52f);
            var iimg = iconGo.GetComponent<Image>();
            iimg.sprite = MakeRestartSprite();
            iimg.color = Color.white;
            iimg.raycastTarget = false;

            // ---- hint ----
            var hintGo = new GameObject("HintGroup", typeof(RectTransform), typeof(CanvasGroup));
            hintGo.transform.SetParent(canvasGo.transform, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 0f); hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 168f);
            hrt.sizeDelta = new Vector2(880f, 120f);
            var hintGroup = hintGo.GetComponent<CanvasGroup>();
            hintGroup.blocksRaycasts = false; hintGroup.interactable = false;
            var hintLabel = Text(hintGo.transform, "HintLabel",
                                 "Drag anywhere. You stay - the world moves.", semi, 34f,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                                 new Vector2(880f, 120f), TextAlignmentOptions.Center, Color.white, shadowSemi);
            hintLabel.enableWordWrapping = true;

            // ---- gesture nudge ----
            var nudgeGo = new GameObject("DragNudge", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            nudgeGo.transform.SetParent(canvasGo.transform, false);
            var nrt = nudgeGo.GetComponent<RectTransform>();
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = new Vector2(0f, 380f);
            nrt.sizeDelta = new Vector2(74f, 74f);
            var nimg = nudgeGo.GetComponent<Image>();
            nimg.sprite = MakeCircleSprite();
            nimg.color = new Color(1f, 1f, 1f, 0.85f);
            nimg.raycastTarget = false;
            var nudgeGroup = nudgeGo.GetComponent<CanvasGroup>();
            nudgeGroup.blocksRaycasts = false; nudgeGroup.alpha = 0f;

            // ---- twist tutorial: two fingers rocking back and forth ----
            var twistGo = new GameObject("TwistNudge", typeof(RectTransform), typeof(CanvasGroup));
            twistGo.transform.SetParent(canvasGo.transform, false);
            var twistRt = twistGo.GetComponent<RectTransform>();
            twistRt.anchorMin = twistRt.anchorMax = new Vector2(0.5f, 0f);
            twistRt.pivot = new Vector2(0.5f, 0.5f);
            twistRt.anchoredPosition = new Vector2(0f, 400f);
            twistRt.sizeDelta = new Vector2(240f, 240f);
            var twistGroup = twistGo.GetComponent<CanvasGroup>();
            twistGroup.blocksRaycasts = false;
            twistGroup.alpha = 0f;

            for (int i = 0; i < 2; i++)
            {
                var dot = new GameObject("Finger" + i, typeof(RectTransform), typeof(Image));
                dot.transform.SetParent(twistGo.transform, false);
                var dotRt = dot.GetComponent<RectTransform>();
                dotRt.anchorMin = dotRt.anchorMax = new Vector2(0.5f, 0.5f);
                dotRt.pivot = new Vector2(0.5f, 0.5f);
                dotRt.sizeDelta = new Vector2(68f, 68f);
                dotRt.anchoredPosition = new Vector2(i == 0 ? -88f : 88f, 0f);
                var dotImg = dot.GetComponent<Image>();
                dotImg.sprite = MakeCircleSprite();
                dotImg.color = new Color(1f, 1f, 1f, 0.92f);
                dotImg.raycastTarget = false;
            }

            // ---- level complete ----
            var doneGo = new GameObject("CompleteGroup", typeof(RectTransform), typeof(CanvasGroup));
            doneGo.transform.SetParent(canvasGo.transform, false);
            var drt = doneGo.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = new Vector2(0f, 430f);
            drt.sizeDelta = new Vector2(900f, 150f);
            var doneGroup = doneGo.GetComponent<CanvasGroup>();
            doneGroup.blocksRaycasts = false; doneGroup.alpha = 0f;
            var doneLabel = Text(doneGo.transform, "CompleteLabel", "LEVEL COMPLETE", bold, 62f,
                                 new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                                 new Vector2(900f, 150f), TextAlignmentOptions.Center, Color.white, shadowBold);
            doneLabel.characterSpacing = 5f;

            // ---- flash ----
            var flashGo = new GameObject("Flash", typeof(RectTransform), typeof(Image));
            flashGo.transform.SetParent(canvasGo.transform, false);
            var frt = flashGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            var flash = flashGo.GetComponent<Image>();
            flash.color = Color.clear;
            flash.raycastTarget = false;

            var hud = canvasGo.AddComponent<HudController>();
            PtwPrefabs.Wire(hud, "levelLabel", levelLabel);
            PtwPrefabs.Wire(hud, "titleLabel", titleLabel);
            PtwPrefabs.Wire(hud, "restartButton", btn);
            PtwPrefabs.Wire(hud, "hintGroup", hintGroup);
            PtwPrefabs.Wire(hud, "hintLabel", hintLabel);
            PtwPrefabs.Wire(hud, "dragNudge", nrt);
            PtwPrefabs.Wire(hud, "dragNudgeGroup", nudgeGroup);
            PtwPrefabs.Wire(hud, "twistNudge", twistRt);
            PtwPrefabs.Wire(hud, "twistNudgeGroup", twistGroup);
            PtwPrefabs.Wire(hud, "completeGroup", doneGroup);
            PtwPrefabs.Wire(hud, "completeLabel", doneLabel);
            PtwPrefabs.Wire(hud, "flashImage", flash);
            PtwPrefabs.Wire(hud, "levels", levels);
            PtwPrefabs.Wire(hud, "rig", rig);
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
            img.color = new Color(0.055f, 0.085f, 0.125f, 0.62f);
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
