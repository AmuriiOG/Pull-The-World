using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Assembles every prefab from the baked meshes and materials. Running this is idempotent, so
    /// the whole art set can be regenerated after a palette tweak without losing level layouts
    /// (levels reference prefabs by GUID, and the prefab files are overwritten in place).
    /// </summary>
    public static class PtwPrefabs
    {
        public const string Root = "Assets/PullTheWorld/Prefabs";
        public const string Blocks = Root + "/Blocks";
        public const string Props = Root + "/Props";
        public const string Play = Root + "/Gameplay";

        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(Blocks);
            PtwPaths.EnsureFolder(Props);
            PtwPaths.EnsureFolder(Play);

            BuildBlocks();
            BuildProps();
            BuildDynamic();
            BuildPortal();
            BuildHazards();
            BuildPlateAndGate();
            BuildKey();
            BuildPlatform();
            BuildWaterProp();
            BuildEnemy();
            BuildBreakable();
            BuildBouncePad();
            BuildPlayer();

            AssetDatabase.SaveAssets();
        }

        // ========================================================================= enemy =====
        static void BuildEnemy()
        {
            var e = Node("Enemy");
            var rb = e.AddComponent<Rigidbody>();
            rb.mass = 1.0f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.7f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezePositionZ
                           | RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationY;
            var sc = e.AddComponent<SphereCollider>();
            sc.radius = 0.31f;
            sc.sharedMaterial = EnsurePhysicsMaterial("PM_Rolling", 0.22f, 0.26f, 0.02f);

            // Upright part - body, grin, glaring face, aura. Enemy.cs holds Visual world-upright
            // and leans it towards the player, so the stare never rolls.
            var visual = Node("Visual", e.transform);
            MeshNode("Body", "Mesh_Enemy", visual.transform, PtwArt.MEnemy, PtwArt.MEnemySpike, PtwArt.MEnemyTeeth);
            var face = MeshNode("Face", "Mesh_EnemyFace", visual.transform, PtwArt.MGlowEvil);
            face.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            var aura = MeshNode("Aura", "Mesh_QuadXY", visual.transform, PtwArt.MEnemyAura);
            aura.transform.localPosition = new Vector3(0f, 0f, 0.06f);       // just behind the body
            aura.transform.localScale = new Vector3(1.45f, 1.45f, 1f);
            aura.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            // Rolling part - the spike ring is a sibling of Visual, so it turns with the rigidbody:
            // a creature gliding on a saw.
            var spikes = MeshNode("Spikes", "Mesh_EnemySpikes", e.transform, PtwArt.MEnemySpike);
            spikes.transform.localScale = Vector3.one * 1.12f;    // matches Enemy.visualScale

            var lightGo = Node("EyeLight", visual.transform);
            lightGo.transform.localPosition = new Vector3(0f, 0.08f, -0.42f);
            var eye = lightGo.AddComponent<Light>();
            eye.type = LightType.Point;
            eye.color = PtwArt.Hex("#FF3A2A");
            eye.intensity = 0.6f;
            eye.range = 2.6f;
            eye.shadows = LightShadows.None;

            var trail = Trail("TrailVfx", e.transform, PtwArt.Get(PtwArt.MParticleSoft), PtwArt.Hex("#4A0F2E"));
            var death = Burst("DeathVfx", e.transform, PtwArt.Get(PtwArt.MParticleAdd), PtwArt.Hex("#FF3560"), 36);

            var dp = e.AddComponent<DynamicProp>();        // registry, speed clamp, sinks in water
            Wire(dp, "respawnIfLost", false);              // an enemy that falls off is dead, not back
            var en = e.AddComponent<Enemy>();
            Wire(en, "visual", visual.transform);
            Wire(en, "spikes", spikes.transform);
            Wire(en, "faceRenderer", face.GetComponent<MeshRenderer>());
            Wire(en, "aura", aura.transform);
            Wire(en, "auraRenderer", aura.GetComponent<MeshRenderer>());
            Wire(en, "eyeLight", eye);
            Wire(en, "trail", trail);
            Wire(en, "deathVfx", death);
            Save(e, Play);
        }

        /// <summary>Dark smoke shed while moving. Emission is toggled by Enemy with its speed.</summary>
        static ParticleSystem Trail(string name, Transform parent, Material mat, Color color)
        {
            var ps = BaseSystem(name, parent, mat, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            main.startSpeed = 0.15f;
            main.startColor = new Color(color.r, color.g, color.b, 0.7f);
            main.gravityModifier = -0.06f;                 // smoke rises
            main.loop = true;
            main.playOnAwake = true;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.rateOverDistance = 7f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.16f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(color);
            return ps;
        }

        // ===================================================================== breakable =====
        static void BuildBreakable()
        {
            float h = PtwMeshes.BlockH;
            var b = Node("Breakable_Crate");

            // Everything that disappears on break lives under Intact. The crate mesh is centred
            // (it is the dynamic prop's mesh), so Intact is lifted to stand it on the floor.
            var intact = Node("Intact", b.transform);
            intact.transform.localPosition = new Vector3(0f, h * 0.42f, 0f);
            var visual = MeshNode("Visual", "Mesh_Crate", intact.transform, PtwArt.MWood, PtwArt.MWoodDark);
            visual.transform.localScale = Vector3.one * 1.35f;      // 0.6 mesh -> ~0.8 of a cell
            var box = AddBox(intact, Vector3.zero, new Vector3(0.82f, h * 0.82f, 0.82f));

            var debris = Dust("DebrisVfx", b.transform, PtwArt.Get(PtwArt.MParticleSoft),
                              PtwArt.Wood, 0.09f, 0.6f);

            var br = b.AddComponent<Breakable>();
            Wire(br, "intact", intact);
            Wire(br, "blocker", box);
            Wire(br, "debrisVfx", debris);
            Save(b, Play);
        }

        // ===================================================================== bounce pad ====
        static void BuildBouncePad()
        {
            var pad = Node("Pad_Bounce");
            var visual = MeshNode("Visual", "Mesh_BouncePad", pad.transform, PtwArt.MBounce, PtwArt.MMetal);
            var punch = visual.AddComponent<Punch>();
            // Low enough that a rolling ball simply goes over it; the throw comes from the zone
            // check in BouncePad, not from this collider.
            AddBox(pad, new Vector3(0f, 0.09f, 0f), new Vector3(0.8f, 0.18f, 0.8f));
            var puff = Dust("PuffVfx", pad.transform, PtwArt.Get(PtwArt.MParticleSoft),
                            PtwArt.Hex("#FFB08A"), 0.08f, 0.4f);

            var bp = pad.AddComponent<BouncePad>();
            Wire(bp, "visualPunch", punch);
            Wire(bp, "puffVfx", puff);
            Save(pad, Play);
        }

        // ========================================================================= water =====
        static void BuildWaterProp()
        {
            var water = Node("Prop_Water");

            // Surface: the v1 wave shader on a subdivided XZ tile, sitting at the fill height.
            var surface = MeshNode("Surface", "Mesh_WaterTile", water.transform, PtwArt.MWater);
            surface.transform.localScale = new Vector3(0.94f, 1f, 0.9f);
            var sr = surface.GetComponent<MeshRenderer>();
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            // Body: translucent box from the bed up to the surface. This is what the side-on
            // camera actually sees of a pool; the surface tile is a sliver at 20 degrees of pitch.
            var body = MeshNode("Body", "Mesh_WaterBody", water.transform, PtwArt.MWaterBody);
            body.transform.localScale = new Vector3(1f, 0.5f, 1f);   // depth is half a cell
            var br = body.GetComponent<MeshRenderer>();
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            br.receiveShadows = false;

            var pour = Droplets("PourVfx", water.transform, PtwArt.Get(PtwArt.MParticleSoft));
            var splash = Burst("SplashVfx", water.transform, PtwArt.Get(PtwArt.MParticleSoft),
                               PtwArt.Hex("#9FE8FA"), 12);

            var wv = water.AddComponent<WaterVolume>();
            Wire(wv, "surface", sr);
            Wire(wv, "surfaceTransform", surface.transform);
            Wire(wv, "body", body.transform);
            Wire(wv, "pourVfx", pour);
            Wire(wv, "splashVfx", splash);
            Save(water, Play);
        }

        // ======================================================================== blocks =====
        static void BuildBlocks()
        {
            float h = PtwMeshes.BlockH;

            var g = MeshNode("Block_Grass", "Mesh_BlockGrass", null, PtwArt.MStone, PtwArt.MGrass);
            AddTileBox(g, new Vector3(0f, -h * 0.5f, 0f), new Vector3(1f, h, 1f));
            Save(g, Blocks);

            var gh = MeshNode("Block_Grass_Half", "Mesh_BlockGrassHalf", null, PtwArt.MStone, PtwArt.MGrass);
            AddTileBox(gh, new Vector3(0f, -h * 0.25f, 0f), new Vector3(1f, h * 0.5f, 1f));
            Save(gh, Blocks);

            var s = MeshNode("Block_Stone", "Mesh_BlockStone", null, PtwArt.MStone);
            AddTileBox(s, new Vector3(0f, -h * 0.5f, 0f), new Vector3(1f, h, 1f));
            Save(s, Blocks);

            var sd = MeshNode("Block_Stone_Dark", "Mesh_BlockStone", null, PtwArt.MStoneDark);
            AddTileBox(sd, new Vector3(0f, -h * 0.5f, 0f), new Vector3(1f, h, 1f));
            Save(sd, Blocks);

            var sl = MeshNode("Block_Stone_Light", "Mesh_BlockStone", null, PtwArt.MStoneLight);
            AddTileBox(sl, new Vector3(0f, -h * 0.5f, 0f), new Vector3(1f, h, 1f));
            Save(sl, Blocks);

            // Sunken basin under a pool. Deliberately COLLIDER-FREE: it must not register as
            // ground, or the player could stand in the middle of a lake.
            var basin = MeshNode("Block_Basin", "Mesh_BlockStone", null, PtwArt.MStoneDark);
            Save(basin, Blocks);

            // v2 pool bed: the bottom half of a floor cell, SOLID, dark on every face so it reads
            // as the floor of a pond rather than as a grass block that got wet. WaterVolume sits
            // on its top and fills the other half of the cell.
            var bed = MeshNode("Block_Pool_Bed", "Mesh_BlockGrassHalf", null,
                               PtwArt.MStoneDark, PtwArt.MStoneDark);
            AddTileBox(bed, new Vector3(0f, -h * 0.25f, 0f), new Vector3(1f, h * 0.5f, 1f));
            Save(bed, Blocks);

            var r = MeshNode("Block_Ramp", "Mesh_BlockRamp", null, PtwArt.MStone, PtwArt.MGrass);
            for (int i = 0; i < 3; i++)
            {
                float sh = h * (i + 1) / 3f;
                AddBox(r, new Vector3(0f, -h + sh * 0.5f, -1f / 3f + i / 3f),
                       new Vector3(1f, sh, 1f / 3f));
            }
            Save(r, Blocks);
        }

        // ========================================================================= props =====
        static void BuildProps()
        {
            Save(MeshNode("Prop_Tree", "Mesh_Tree", null, PtwArt.MWoodDark, PtwArt.MFoliage), Props);
            Save(MeshNode("Prop_TreeSmall", "Mesh_TreeSmall", null, PtwArt.MWoodDark, PtwArt.MFoliage), Props);
            Save(MeshNode("Prop_Bush", "Mesh_Bush", null, PtwArt.MWoodDark, PtwArt.MFoliageDark), Props);
            Save(MeshNode("Prop_RockDeco", "Mesh_RockDeco", null, PtwArt.MRock), Props);
            Save(MeshNode("Prop_Fence", "Mesh_Fence", null, PtwArt.MWood), Props);

            var crystal = MeshNode("Prop_Crystal", "Mesh_Crystal", null, PtwArt.MGlowCyan);
            var cl = Node("Light", crystal.transform).AddComponent<Light>();
            cl.type = LightType.Point;
            cl.color = PtwArt.Cyan;
            cl.intensity = 1.1f;
            cl.range = 2.2f;
            cl.shadows = LightShadows.None;
            cl.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            Save(crystal, Props);
        }

        // ====================================================================== dynamic ======
        static void BuildDynamic()
        {
            var boulder = Node("Prop_Boulder");
            var brb = boulder.AddComponent<Rigidbody>();
            brb.mass = 1.4f;
            brb.linearDamping = 0.04f;
            brb.angularDamping = 0.6f;
            brb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            brb.interpolation = RigidbodyInterpolation.None;
            var bsc = boulder.AddComponent<SphereCollider>();
            bsc.radius = 0.34f;
            // Rocks are the main puzzle tool in v2 and they have to ROLL on a modest tilt, so they
            // get a slick physics material for the same reason v1 gave one to its crate.
            bsc.sharedMaterial = EnsurePhysicsMaterial("PM_Rolling", 0.22f, 0.26f, 0.02f);
            // Locked to the puzzle plane, exactly like the player. A rock that drifts in Z ends up
            // visually behind or in front of the level it is supposed to be sitting on.
            brb.constraints = RigidbodyConstraints.FreezePositionZ
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationY;
            MeshNode("Visual", "Mesh_Boulder", boulder.transform, PtwArt.MRock).AddComponent<Punch>();
            Dust("ImpactVfx", boulder.transform, PtwArt.Get(PtwArt.MParticleSoft),
                 PtwArt.StoneLight, 0.09f, 0.45f);
            boulder.AddComponent<DynamicProp>();   // finds Visual / ImpactVfx by name
            Save(boulder, Play);

            var crate = Node("Prop_Crate");
            var crb = crate.AddComponent<Rigidbody>();
            crb.mass = 1.0f;
            crb.linearDamping = 0.05f;
            crb.angularDamping = 0.06f;   // let it tumble; a box that cannot rotate must pure-slide and sticks
            crb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            crb.interpolation = RigidbodyInterpolation.None;
            var cbc = crate.AddComponent<BoxCollider>();
            cbc.size = Vector3.one * 0.7f;
            // A cube on a slope only slides once the tilt beats the friction angle. PhysX defaults
            // (0.6) put that at ~31 degrees, which is almost exactly the tilt a 45 degree spin
            // produces - so the crate just sat there. Slick it up so it starts moving at ~17.
            cbc.sharedMaterial = EnsurePhysicsMaterial("PM_Crate", 0.24f, 0.28f, 0.0f);
            crb.constraints = RigidbodyConstraints.FreezePositionZ
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationY;
            MeshNode("Visual", "Mesh_Crate", crate.transform, PtwArt.MWood, PtwArt.MWoodDark).AddComponent<Punch>();
            Dust("ImpactVfx", crate.transform, PtwArt.Get(PtwArt.MParticleSoft),
                 PtwArt.Wood, 0.08f, 0.4f);
            crate.AddComponent<DynamicProp>();
            Save(crate, Play);
        }

        // ======================================================================== portal =====
        static void BuildPortal()
        {
            var door = Node("ExitPortal_Door");
            MeshNode("Arch", "Mesh_DoorArch", door.transform, PtwArt.MStone, PtwArt.MStoneDark);

            var glow = MeshNode("Glow", "Mesh_ArchFill", door.transform, PtwArt.MPortalEnergy);
            glow.transform.localPosition = new Vector3(0f, 0f, 0f);
            var glowRend = glow.GetComponent<MeshRenderer>();
            glowRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Soft halo billboard so the doorway blooms onto its surroundings.
            var halo = MeshNode("Halo", "Mesh_QuadXY", door.transform, PtwArt.MPortalGlow);
            halo.transform.localPosition = new Vector3(0f, 0.62f, -0.24f);
            halo.transform.localScale = new Vector3(2.6f, 2.8f, 1f);
            halo.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            var mouth = Node("Mouth", door.transform);
            mouth.transform.localPosition = new Vector3(0f, 0.45f, 0f);

            var lightGo = Node("PortalLight", door.transform);
            lightGo.transform.localPosition = new Vector3(0f, 0.7f, 0.1f);
            var pl = lightGo.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = PtwArt.Warm;
            pl.intensity = 2.2f;
            pl.range = 5f;
            pl.shadows = LightShadows.None;

            var idle = Motes("IdleVfx", door.transform, PtwArt.Get(PtwArt.MParticleAdd), PtwArt.Warm);
            var arrive = Burst("ArriveVfx", door.transform, PtwArt.Get(PtwArt.MParticleAdd), PtwArt.Warm, 34);

            var ep = door.AddComponent<ExitPortal>();
            Wire(ep, "mouth", mouth.transform);
            Wire(ep, "glowQuad", glow.transform);
            Wire(ep, "glowRenderer", glowRend);
            Wire(ep, "portalLight", pl);
            Wire(ep, "idleVfx", idle);
            Wire(ep, "arriveVfx", arrive);
            Save(door, Play);
        }

        // ======================================================================= hazards =====
        static void BuildHazards()
        {
            // --- fire ---
            var fire = Node("Hazard_Fire");
            var baseGo = MeshNode("Base", "Mesh_FireBase", fire.transform, PtwArt.MStoneDark);
            AddBox(baseGo, new Vector3(0f, 0.09f, 0f), new Vector3(0.8f, 0.18f, 0.8f));

            var active = Node("ActiveVisuals", fire.transform);
            var core = MeshNode("FlameCore", "Mesh_FlameCore", active.transform, PtwArt.MGlowFire);
            core.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            var flameLightGo = Node("FireLight", active.transform);
            flameLightGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var fl = flameLightGo.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = PtwArt.Fire;
            fl.intensity = 2.6f;
            fl.range = 4f;
            fl.shadows = LightShadows.None;

            var flameVfx = Flame("FlameVfx", active.transform, PtwArt.Get(PtwArt.MParticleAdd));
            var smoke = Smoke("SmotherVfx", fire.transform, PtwArt.Get(PtwArt.MParticleSoft));

            var hz = fire.AddComponent<Hazard>();
            Wire(hz, "activeVisuals", active);
            Wire(hz, "flameVfx", flameVfx);
            Wire(hz, "smotherVfx", smoke);
            Wire(hz, "hazardLight", fl);
            Save(fire, Play);

            // --- spikes ---
            var spikes = Node("Hazard_Spikes");
            var sp = MeshNode("Spikes", "Mesh_Spikes", spikes.transform, PtwArt.MStoneDark, PtwArt.MSpike);
            AddBox(sp, new Vector3(0f, 0.07f, 0f), new Vector3(0.94f, 0.14f, 0.94f));
            var hz2 = spikes.AddComponent<Hazard>();
            Wire(hz2, "canBeSmothered", false);
            Wire(hz2, "killRadius", 0.5f);
            Save(spikes, Play);
        }

        // ================================================================ plate and gate =====
        static void BuildPlateAndGate()
        {
            var plate = Node("PressurePlate");
            var pbase = MeshNode("Base", "Mesh_PlateBase", plate.transform, PtwArt.MStoneDark);
            AddBox(pbase, new Vector3(0f, 0.05f, 0f), new Vector3(0.92f, 0.1f, 0.92f));
            var pad = MeshNode("Pad", "Mesh_PlatePad", plate.transform, PtwArt.MMetal);
            var ind = MeshNode("Indicator", "Mesh_PlateIndicator", plate.transform, PtwArt.MPlateOn);
            ind.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            ind.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            // The pad and the indicator both ride on one "Slab" node so the whole face sinks
            // together - sinking the pad while leaving the glowing inlay behind looked like the
            // indicator was floating off the plate.
            var slab = Node("Slab", plate.transform);
            slab.AddComponent<Punch>();               // flinches when pressed
            pad.transform.SetParent(slab.transform, false);
            ind.transform.SetParent(slab.transform, false);

            var pp = plate.AddComponent<PressurePlate>();
            Wire(pp, "slab", slab.transform);
            Wire(pp, "inlayRenderer", ind.GetComponent<MeshRenderer>());
            Wire(pp, "playerCanPress", false);         // rocks only - see PressurePlate
            Save(plate, Play);

            float h = PtwMeshes.BlockH;
            var gate = Node("Gate_Stone");
            // No Rigidbody: the gate is an ordinary child collider of the level's compound body.
            // See Gate's class comment for why that is fine here but not for a moving platform.
            var gslab = Node("Slab", gate.transform);
            gslab.AddComponent<Punch>();              // flinches when it starts to move
            AddBox(gslab, new Vector3(0f, -h * 0.5f, 0f), new Vector3(1f, h, 0.9f));
            MeshNode("Visual", "Mesh_GateBlock", gslab.transform, PtwArt.MStone, PtwArt.MStoneDark);

            // A thin lit strip at the gate mouth, not a full-cell quad. At 1x1 with an emissive
            // material it rendered as a glowing white card stuck to the level rather than as a
            // threshold, and it was the most obviously wrong thing in the level 5 capture.
            var threshold = MeshNode("Threshold", "Mesh_QuadXY", gate.transform, PtwArt.MGlowCyan);
            threshold.transform.localPosition = new Vector3(0f, -h * 0.97f, -0.47f);
            threshold.transform.localScale = new Vector3(0.86f, 0.09f, 1f);
            threshold.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            var gt = gate.AddComponent<Gate>();
            Wire(gt, "slab", gslab.transform);
            Wire(gt, "travel", h * 1.02f);
            Wire(gt, "localDirection", Vector3.down);
            Wire(gt, "thresholdRenderer", threshold.GetComponent<MeshRenderer>());
            Save(gate, Play);
        }

        // =========================================================================== key =====
        static void BuildKey()
        {
            var key = Node("Key_Gem");

            // Spinner child so the idle spin and bob never touch the root - the root is what the
            // pickup distance is measured from, and a bobbing pickup radius feels arbitrary.
            var spinner = Node("Spinner", key.transform);
            var gem = MeshNode("Gem", "Mesh_Key", spinner.transform, PtwArt.MKey);
            gem.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            var halo = MeshNode("Halo", "Mesh_QuadXY", spinner.transform, PtwArt.MPortalGlow);
            halo.transform.localScale = Vector3.one * 1.5f;
            halo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            halo.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = Node("KeyLight", key.transform);
            var kl = lightGo.AddComponent<Light>();
            kl.type = LightType.Point;
            kl.color = PtwArt.Hex("#FFD96B");
            kl.intensity = 1.3f;
            kl.range = 2.6f;
            kl.shadows = LightShadows.None;

            var collect = Burst("CollectVfx", key.transform,
                                PtwArt.Get(PtwArt.MParticleAdd), PtwArt.Hex("#FFD96B"), 26);

            var visuals = Node("Visuals", key.transform);
            spinner.transform.SetParent(visuals.transform, true);
            lightGo.transform.SetParent(visuals.transform, true);

            var col = key.AddComponent<Collectible>();
            Wire(col, "spinner", spinner.transform);
            Wire(col, "collectVfx", collect);
            Wire(col, "visuals", visuals);
            Save(key, Play);
        }

        // ====================================================================== platform =====
        static void BuildPlatform()
        {
            float h = PtwMeshes.BlockH;

            var plat = Node("Platform_Moving");
            var rb = plat.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // 1.4 wide, not 2. The deck has to fit inside a four-cell pit at BOTH ends of its
            // 2.5-unit travel without clipping into the ledge it is delivering the ball to; at
            // 2 units wide it overlapped the far ledge by half a block.
            AddBox(plat, new Vector3(0f, -h * 0.25f, 0f), new Vector3(1.4f, h * 0.5f, 0.9f));

            var visual = MeshNode("Visual", "Mesh_BlockStone", plat.transform,
                                  PtwArt.MStoneDark);
            visual.transform.localScale = new Vector3(1.4f, 0.5f, 0.9f);

            // A stripe of cyan on the top face so a moving surface is instantly distinguishable
            // from the static terrain it slides past.
            var stripe = MeshNode("Stripe", "Mesh_QuadXZ", plat.transform, PtwArt.MGlowCyan);
            stripe.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            stripe.transform.localScale = new Vector3(1.3f, 1f, 0.28f);
            stripe.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            plat.AddComponent<MovingPlatform>();
            Save(plat, Play);
        }

        static ParticleSystem Droplets(string name, Transform parent, Material mat)
        {
            var ps = BaseSystem(name, parent, mat, true);
            var main = ps.main;
            main.startLifetime = 0.9f;
            main.startSize = 0.08f;
            main.startSpeed = 0f;                 // velocity comes from EmitParams
            main.startColor = PtwArt.Hex("#7FD8F2");
            main.gravityModifier = 1.4f;
            main.maxParticles = 260;
            main.playOnAwake = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(PtwArt.Hex("#9FE8FA"), 0f),
                    new GradientColorKey(PtwArt.Hex("#2FA0CC"), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.75f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0.2f)));
            return ps;
        }

        // ======================================================================== player =====
        /// <summary>
        /// The v2 player: one dynamic sphere.
        ///
        /// No anchor ring, no kinematic blocker capsule, no pinned transform. All of that existed
        /// to sell "you are the fixed point of the universe", which is the fiction v2 threw out.
        /// What is left is a physics body with a face on it.
        ///
        /// The collider lives on the ROOT and the mesh on a child, because the root is spun by the
        /// solver and squash has to be applied without fighting that rotation.
        /// </summary>
        static void BuildPlayer()
        {
            var rig = Node("PlayerRig");

            var rb = rig.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var sc = rig.AddComponent<SphereCollider>();
            sc.radius = 0.335f;
            // Low friction so a gentle tilt starts it moving, and a little bounce so a drop has
            // some life without the ball becoming a pinball.
            sc.sharedMaterial = EnsurePhysicsMaterial("PM_Player", 0.30f, 0.34f, 0.06f);

            var visual = Node("Visual", rig.transform);
            MeshNode("Ball", "Mesh_PlayerBall", visual.transform, PtwArt.MPlayer);
            MeshNode("Face", "Mesh_PlayerFace", visual.transform, PtwArt.MPlayerEye);

            // No fake blob shadow. v1 needed one because its camera was steeply isometric and a
            // flat XZ quad read clearly under the character. This camera is almost front-on
            // (13 degrees), so a horizontal quad is seen edge-on and is effectively invisible.
            // A real shadow from the directional key light does the job properly here, and the
            // chamfered blocks give it something to fall across.

            var pb = rig.AddComponent<PlayerBody>();
            Wire(pb, "visual", visual.transform);

            // Charm and speed, both VISUAL ONLY: a blink and wide eyes in the air, and a soft
            // streak once the ball is really moving. Neither touches the body or its settings.
            var faceAnim = rig.AddComponent<BallFace>();
            Wire(faceAnim, "player", pb);
            Wire(faceAnim, "face", visual.transform.Find("Face"));

            var trailGo = Node("Trail", rig.transform);           // sibling of Visual: does not roll
            var tr = trailGo.AddComponent<TrailRenderer>();
            tr.time = 0.22f;
            tr.minVertexDistance = 0.04f;
            tr.widthCurve = AnimationCurve.Linear(0f, 0.30f, 1f, 0.02f);
            tr.numCapVertices = 4;
            tr.numCornerVertices = 4;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.sharedMaterial = PtwArt.Get(PtwArt.MTrail);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.85f, 1f), 1f) },
                       new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = tg;
            var bt = trailGo.AddComponent<BallTrail>();
            Wire(bt, "player", pb);

            Save(rig, Play);
        }

        // ======================================================================= helpers =====
        public static GameObject Node(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        public static GameObject MeshNode(string name, string meshId, Transform parent, params string[] mats)
        {
            var go = Node(name, parent);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = PtwMeshes.Get(meshId);
            var mr = go.AddComponent<MeshRenderer>();
            var arr = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) arr[i] = PtwArt.Get(mats[i]);
            mr.sharedMaterials = arr;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return go;
        }

        static PhysicsMaterial EnsurePhysicsMaterial(string id, float dynamicFriction,
                                                     float staticFriction, float bounce)
        {
            PtwPaths.EnsureFolder(PtwArt.ArtRoot + "/Physics");
            // ".asset", not ".physicsMaterial". CreateAsset on the latter logs an Error every
            // build ("consider ... change the file type to *.asset") and Unity says it will become
            // an exception. Three red lines in the console on every rebuild is not release quality,
            // and the prefabs are regenerated against whatever this returns, so the rename is free.
            string path = $"{PtwArt.ArtRoot}/Physics/{id}.asset";

            // Values are set BEFORE CreateAsset so they are part of the first serialization.
            // Setting them afterwards and relying on SetDirty silently left the asset at the 0.6
            // default, which is exactly high enough to glue a crate to a 36 degree slope.
            var m = new PhysicsMaterial(id)
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                bounciness = bounce,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(m, existing);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        public static BoxCollider AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
            return bc;
        }

        /// <summary>
        /// Floor/wall collider, widened horizontally so neighbouring tiles overlap slightly.
        ///
        /// Exactly-abutting box colliders leave a hairline seam (each collider carries a 0.01
        /// contactOffset skin), and a sliding crate catches on it and stops dead halfway down a
        /// slope. Overlapping by a couple of centimetres buries the seam. The top face stays
        /// exactly at the authored height, which is what the grid depends on.
        /// </summary>
        static BoxCollider AddTileBox(GameObject go, Vector3 center, Vector3 size)
        {
            const float overlap = 0.04f;
            return AddBox(go, center, new Vector3(size.x + overlap, size.y, size.z + overlap));
        }

        /// <summary>Set a private [SerializeField] without making it public just for the builder.</summary>
        public static void Wire(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"PTW: no serialized field '{field}' on {target.GetType().Name}");
                return;
            }
            switch (value)
            {
                case bool b: prop.boolValue = b; break;
                case float f: prop.floatValue = f; break;
                case int i: prop.intValue = i; break;
                case string s: prop.stringValue = s; break;
                case Vector3 v: prop.vector3Value = v; break;
                case Color c: prop.colorValue = c; break;
                case Object o: prop.objectReferenceValue = o; break;
                default: Debug.LogWarning($"PTW: unhandled wire type for '{field}'"); break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Save(GameObject go, string dir)
        {
            string path = $"{dir}/{go.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        // ---------------------------------------------------------------------- particles ---
        static ParticleSystem BaseSystem(string name, Transform parent, Material mat, bool worldSpace)
        {
            var go = Node(name, parent);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World
                                              : ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            main.maxParticles = 60;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = mat;
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.alignment = ParticleSystemRenderSpace.View;
            return ps;
        }

        static ParticleSystem Dust(string name, Transform parent, Material mat, Color color,
                                   float size, float life)
        {
            var ps = BaseSystem(name, parent, mat, true);
            var main = ps.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = 1.1f;
            main.startColor = new Color(color.r, color.g, color.b, 0.75f);
            main.gravityModifier = 0.35f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = 0.12f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(color);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Shrink());
            return ps;
        }

        static ParticleSystem Motes(string name, Transform parent, Material mat, Color color)
        {
            var ps = BaseSystem(name, parent, mat, false);
            var main = ps.main;
            main.startLifetime = 1.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.1f);
            main.startSpeed = 0.34f;
            main.startColor = new Color(color.r, color.g, color.b, 0.85f);
            main.gravityModifier = -0.04f;
            main.loop = true;
            main.playOnAwake = true;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 12f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(0.55f, 0.1f, 0.2f);
            sh.position = new Vector3(0f, 0.2f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeInOut(color);
            return ps;
        }

        static ParticleSystem Burst(string name, Transform parent, Material mat, Color color, int count)
        {
            var ps = BaseSystem(name, parent, mat, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.95f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.19f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.4f);
            main.startColor = color;
            main.gravityModifier = 0.25f;
            main.maxParticles = 120;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.3f;
            sh.position = new Vector3(0f, 0.5f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(color);
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Shrink());
            return ps;
        }

        static ParticleSystem Flame(string name, Transform parent, Material mat)
        {
            var ps = BaseSystem(name, parent, mat, false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.36f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startColor = PtwArt.Hex("#FFE95A");
            main.gravityModifier = -0.2f;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 140;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 55f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 12f;
            sh.radius = 0.14f;
            sh.position = new Vector3(0f, 0.16f, 0f);
            sh.rotation = new Vector3(-90f, 0f, 0f);

            // Measured off the reference: a pure yellow -> orange emissive ramp, no red falloff.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(PtwArt.Hex("#FFF268"), 0f),
                    new GradientColorKey(PtwArt.Hex("#FEDE36"), 0.35f),
                    new GradientColorKey(PtwArt.Hex("#FA8C11"), 0.75f),
                    new GradientColorKey(PtwArt.Hex("#E0761A"), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.15f),
                    new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Shrink());
            return ps;
        }

        static ParticleSystem Smoke(string name, Transform parent, Material mat)
        {
            var ps = BaseSystem(name, parent, mat, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startColor = new Color(0.85f, 0.87f, 0.9f, 0.55f);
            main.gravityModifier = -0.06f;
            main.maxParticles = 40;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)22) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = 0.3f;
            sh.position = new Vector3(0f, 0.15f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut(new Color(0.88f, 0.9f, 0.93f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, Grow());
            return ps;
        }

        static ParticleSystem.MinMaxGradient FadeOut(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f),
                        new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static ParticleSystem.MinMaxGradient FadeInOut(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.3f),
                        new GradientAlphaKey(0.7f, 0.65f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static AnimationCurve Shrink() => new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f));

        static AnimationCurve Grow() => new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(1f, 1.4f));
    }
}
