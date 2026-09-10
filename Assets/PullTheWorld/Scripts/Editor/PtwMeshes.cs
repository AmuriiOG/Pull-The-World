using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Bakes every mesh in the game. No modelling package, no store assets, no default primitives.
    ///
    /// Conventions that make level authoring painless:
    ///  * BLOCKS pivot at their TOP centre and hang down to y = -1. Drop one at y = 0 and its
    ///    walkable surface is exactly at the player's foot height. Stack one at y = 1 and it is
    ///    a wall.
    ///  * PROPS pivot at their base, so they sit on a surface at y = 0.
    ///  * DYNAMIC props pivot at their centre of mass.
    ///  * Submesh 0 / 1 map to the two materials each prefab uses.
    /// </summary>
    public static class PtwMeshes
    {
        /// <summary>Horizontal cell size.</summary>
        public const float Grid = 1f;
        /// <summary>
        /// Cell height. v1 used 0.75 because it measured the reference sheet's isometric blocks as
        /// noticeably squat, and that squatness was part of why the diorama read as a friendly toy.
        ///
        /// v2 uses square cells, and the reason is the mechanic rather than taste: the level is
        /// rotated continuously through every angle, so a non-square grid makes the same island
        /// read as a different shape at 0 and 90 degrees, and a puzzle the player has already
        /// solved visually stops looking solved. Square cells rotate to themselves. The friendly
        /// proportion is carried by the 0.06 chamfer and the grass cap instead, which do survive
        /// rotation.
        /// </summary>
        public const float BlockH = 1f;
        /// <summary>Grass cap is ~18% of block height and caps grey stone directly - no dirt layer.</summary>
        public const float GrassCap = 0.26f;

        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(PtwArt.MeshDir);

            Save(BlockGrass(BlockH), "Mesh_BlockGrass");
            Save(BlockGrass(BlockH * 0.5f), "Mesh_BlockGrassHalf");
            Save(BlockStone(), "Mesh_BlockStone");
            Save(GateBlock(), "Mesh_GateBlock");
            Save(Ramp(), "Mesh_BlockRamp");

            Save(Tree(0.34f, 0.085f, 0.062f, 0.36f, 0.66f, 7), "Mesh_Tree");
            Save(Tree(0.22f, 0.062f, 0.048f, 0.25f, 0.44f, 19), "Mesh_TreeSmall");
            Save(Bush(), "Mesh_Bush");
            Save(RockDeco(), "Mesh_RockDeco");
            Save(Crystal(), "Mesh_Crystal");
            Save(Fence(), "Mesh_Fence");

            Save(Boulder(), "Mesh_Boulder");
            Save(Crate(), "Mesh_Crate");

            Save(DoorArch(), "Mesh_DoorArch");
            Save(ArchFill(), "Mesh_ArchFill");
            Save(Spikes(), "Mesh_Spikes");
            Save(FireBase(), "Mesh_FireBase");
            Save(FlameCore(), "Mesh_FlameCore");
            Save(PlateBase(), "Mesh_PlateBase");
            Save(PlatePad(), "Mesh_PlatePad");
            Save(PlateIndicator(), "Mesh_PlateIndicator");

            Save(Player(), "Mesh_Player");
            Save(PlayerBall(), "Mesh_PlayerBall");
            Save(PlayerFace(), "Mesh_PlayerFace");
            Save(Key(), "Mesh_Key");
            Save(QuadXZ(), "Mesh_QuadXZ");
            Save(QuadXY(), "Mesh_QuadXY");
            Save(WaterTile(6, 1f), "Mesh_WaterTile");
            Save(WaterBody(), "Mesh_WaterBody");
            Save(WaterTile(30, 72f), "Mesh_OceanPlane");

            AssetDatabase.SaveAssets();
        }

        // =============================================================== terrain blocks =====
        /// <summary>Grass-topped block. sub0 = stone body, sub1 = grass cap.</summary>
        static Mesh BlockGrass(float height)
        {
            var mb = new MeshBuilder();
            float cap = Mathf.Min(GrassCap, height * 0.4f);
            float bodyH = height - cap;

            mb.AddChamferBox(0, new Vector3(0f, -height + bodyH * 0.5f, 0f),
                             new Vector3(1f, bodyH, 1f), 0.05f);
            // The cap overhangs the body slightly. In the reference the grass is essentially a TOP
            // face with a small tufted fringe, so the cap is thin and the overhang throws the
            // little shadow line that separates green from stone.
            //
            // Chamfer is near-zero ON THE CAP, deliberately. A 45-degree bevel along the cap top
            // front edge faces both up AND towards the camera, which makes it the single most-lit
            // surface in the scene once the island tilts (dot 0.95 against 0.88 for the top face)
            // - it rendered as a hard pale line the length of every grass row and survived every
            // lighting change aimed at it. The body block below keeps its full chamfer, so the
            // block silhouette stays soft; only the thin green slab goes crisp.
            mb.AddChamferBox(1, new Vector3(0f, -cap * 0.5f, 0f),
                             new Vector3(1.035f, cap, 1.035f), 0.012f);
            return mb.ToMesh("BlockGrass");
        }

        static Mesh BlockStone()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, -BlockH * 0.5f, 0f),
                             new Vector3(1f, BlockH, 1f), 0.055f);
            return mb.ToMesh("BlockStone");
        }

        /// <summary>Visually distinct from plain stone so a gate reads as machinery.</summary>
        static Mesh GateBlock()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, -BlockH * 0.5f, 0f),
                             new Vector3(1f, BlockH, 1f), 0.055f);
            for (int i = 0; i < 2; i++)
            {
                float y = -BlockH * (0.32f + i * 0.4f);
                mb.AddChamferBox(1, new Vector3(0f, y, 0f), new Vector3(1.025f, 0.06f, 1.025f), 0.02f);
            }
            return mb.ToMesh("GateBlock");
        }

        static Mesh Ramp()
        {
            var mb = new MeshBuilder();
            // A stepped wedge reads better than a smooth slope in a chunky block world.
            for (int i = 0; i < 3; i++)
            {
                float h = BlockH * (i + 1) / 3f;
                mb.AddChamferBox(0, new Vector3(0f, -BlockH + h * 0.5f, -1f / 3f + i / 3f),
                                 new Vector3(1f, h, 1f / 3f), 0.03f);
            }
            mb.AddChamferBox(1, new Vector3(0f, -0.02f, 1f / 3f),
                             new Vector3(1.02f, 0.05f, 1f / 3f), 0.018f);
            return mb.ToMesh("Ramp");
        }

        // ======================================================================= flora ======
        static Mesh Tree(float trunkH, float r0, float r1, float canopy, float canopyY, int seed)
        {
            var mb = new MeshBuilder();
            mb.AddCylinder(0, Vector3.zero, r0, r1, trunkH, 6);
            mb.AddBlob(1, new Vector3(0f, canopyY, 0f),
                       new Vector3(canopy, canopy * 0.9f, canopy), 1, 0.11f, seed);
            mb.AddBlob(1, new Vector3(canopy * 0.13f, canopyY + canopy * 0.72f, -canopy * 0.08f),
                       new Vector3(canopy * 0.64f, canopy * 0.6f, canopy * 0.64f), 1, 0.13f, seed + 4);
            return mb.ToMesh("Tree");
        }

        static Mesh Bush()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(1, new Vector3(0f, 0.13f, 0f), new Vector3(0.24f, 0.17f, 0.22f), 1, 0.14f, 31);
            mb.AddBlob(1, new Vector3(0.15f, 0.10f, 0.07f), new Vector3(0.15f, 0.12f, 0.14f), 1, 0.16f, 37);
            return mb.ToMesh("Bush");
        }

        static Mesh RockDeco()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(0, new Vector3(0f, 0.11f, 0f), new Vector3(0.19f, 0.14f, 0.17f), 1, 0.2f, 3);
            mb.AddBlob(0, new Vector3(0.2f, 0.07f, 0.06f), new Vector3(0.11f, 0.09f, 0.1f), 1, 0.24f, 9);
            return mb.ToMesh("RockDeco");
        }

        static Mesh Crystal()
        {
            var mb = new MeshBuilder();
            mb.AddCylinder(0, Vector3.zero, 0f, 0.11f, 0.16f, 6, false, false);
            mb.AddCylinder(0, new Vector3(0f, 0.16f, 0f), 0.11f, 0f, 0.34f, 6, false, false);
            return mb.ToMesh("Crystal");
        }

        static Mesh Fence()
        {
            var mb = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                mb.AddChamferBox(0, new Vector3(0.3f * s, 0.21f, 0f),
                                 new Vector3(0.07f, 0.42f, 0.07f), 0.015f);
            for (int i = 0; i < 2; i++)
                mb.AddChamferBox(0, new Vector3(0f, 0.16f + i * 0.16f, 0f),
                                 new Vector3(0.68f, 0.055f, 0.05f), 0.012f);
            return mb.ToMesh("Fence");
        }

        // =================================================================== dynamic props ==
        static Mesh Boulder()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(0, Vector3.zero, new Vector3(0.3f, 0.27f, 0.3f), 1, 0.2f, 21);
            return mb.ToMesh("Boulder");
        }

        static Mesh Crate()
        {
            var mb = new MeshBuilder();
            const float s = 0.6f, half = 0.3f, bar = 0.085f;
            mb.AddChamferBox(0, Vector3.zero, new Vector3(s, s, s), 0.05f);

            // Corner posts.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    mb.AddChamferBox(1, new Vector3(half * x, 0f, half * z),
                                     new Vector3(bar, s + 0.02f, bar), 0f);

            // Top and bottom rails on all four faces.
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                    mb.AddChamferBox(1, new Vector3(0f, half * y, half * z),
                                     new Vector3(s + 0.02f, bar, bar), 0f);
                for (int x = -1; x <= 1; x += 2)
                    mb.AddChamferBox(1, new Vector3(half * x, half * y, 0f),
                                     new Vector3(bar, bar, s + 0.02f), 0f);
            }
            return mb.ToMesh("Crate");
        }

        // ======================================================================== portal ====
        /// <summary>
        /// The hero prop, so it gets real geometry: individually laid stones for the jambs and
        /// seven voussoirs around the arch, exactly like the reference doorway.
        /// </summary>
        static Mesh DoorArch()
        {
            var mb = new MeshBuilder();
            const float springY = 1.05f;   // where the posts stop and the arch begins
            const float R = 0.44f;         // radius to the centre of the arch stones
            var stone = new Vector3(0.26f, 0.3f, 0.44f);

            // Base step.
            mb.AddChamferBox(1, new Vector3(0f, 0.075f, 0f), new Vector3(1.52f, 0.15f, 0.62f), 0.04f);

            // Jambs: three stones a side, alternating depth a touch so the masonry reads.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.3f + i * 0.3f;
                    float d = stone.z + (i % 2 == 0 ? 0.03f : -0.02f);
                    mb.AddChamferBox(0, new Vector3(R * side, y, 0f),
                                     new Vector3(stone.x, stone.y, d), 0.035f);
                }

            // Arch: seven voussoirs from 180 deg round to 0 deg.
            const int count = 7;
            for (int i = 0; i < count; i++)
            {
                float th = Mathf.Lerp(180f, 0f, i / (float)(count - 1));
                float rad = th * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Cos(rad) * R, springY + Mathf.Sin(rad) * R, 0f);
                // Local +Y points radially outward.
                var rot = Quaternion.Euler(0f, 0f, th - 90f);
                float d = stone.z + (i % 2 == 0 ? 0.03f : -0.02f);
                mb.AddChamferBox(0, pos, new Vector3(0.25f, stone.y, d), 0.035f, rot);
            }
            return mb.ToMesh("DoorArch");
        }

        /// <summary>Flat glowing pane that fills the doorway. Double sided - the door can face anywhere.</summary>
        static Mesh ArchFill()
        {
            var mb = new MeshBuilder();
            const float hw = 0.31f, bottom = 0.15f, spring = 1.05f;

            foreach (float z in new[] { 0f, -0.001f })
            {
                Vector3 hint = z < -0.0005f ? Vector3.back : Vector3.forward;
                mb.AddFlatQuad(0, new Vector3(-hw, bottom, z), new Vector3(hw, bottom, z),
                                  new Vector3(hw, spring, z), new Vector3(-hw, spring, z), hint);

                const int seg = 12;
                var apexC = new Vector3(0f, spring, z);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = Mathf.Lerp(0f, 180f, i / (float)seg) * Mathf.Deg2Rad;
                    float a1 = Mathf.Lerp(0f, 180f, (i + 1) / (float)seg) * Mathf.Deg2Rad;
                    var p0 = apexC + new Vector3(Mathf.Cos(a0) * hw, Mathf.Sin(a0) * hw, 0f);
                    var p1 = apexC + new Vector3(Mathf.Cos(a1) * hw, Mathf.Sin(a1) * hw, 0f);
                    mb.AddFlatTri(0, apexC, p0, p1, hint);
                }
            }
            return mb.ToMesh("ArchFill");
        }

        // ======================================================================= hazards ====
        static Mesh Spikes()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, 0.07f, 0f), new Vector3(0.94f, 0.14f, 0.94f), 0.03f);
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    mb.AddCylinder(1, new Vector3(x * 0.29f, 0.13f, z * 0.29f),
                                   0.085f, 0f, 0.3f, 5, false, false);
            return mb.ToMesh("Spikes");
        }

        static Mesh FireBase()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, 0.045f, 0f), new Vector3(0.78f, 0.09f, 0.78f), 0.03f);
            // A few charred logs so the fire has something to be burning.
            for (int i = 0; i < 3; i++)
            {
                float a = i * 60f;
                mb.AddChamferBox(0, new Vector3(0f, 0.13f, 0f), new Vector3(0.62f, 0.085f, 0.1f), 0.02f,
                                 Quaternion.Euler(0f, a, 0f));
            }
            return mb.ToMesh("FireBase");
        }

        static Mesh FlameCore()
        {
            var mb = new MeshBuilder();
            mb.AddCylinder(0, new Vector3(0f, 0.14f, 0f), 0.17f, 0f, 0.44f, 6, false, false);
            mb.AddCylinder(0, new Vector3(0.1f, 0.14f, 0.06f), 0.1f, 0f, 0.28f, 5, false, false);
            mb.AddCylinder(0, new Vector3(-0.09f, 0.14f, -0.05f), 0.085f, 0f, 0.22f, 5, false, false);
            return mb.ToMesh("FlameCore");
        }

        // ======================================================================== plate =====
        static Mesh PlateBase()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, 0.05f, 0f), new Vector3(0.92f, 0.1f, 0.92f), 0.03f);
            return mb.ToMesh("PlateBase");
        }

        static Mesh PlatePad()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, 0.05f, 0f), new Vector3(0.66f, 0.1f, 0.66f), 0.035f);
            return mb.ToMesh("PlatePad");
        }

        static Mesh PlateIndicator()
        {
            var mb = new MeshBuilder();
            // Thin recessed ring around the pad.
            const int seg = 24;
            const float rOut = 0.4f, rIn = 0.34f, y = 0.008f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var o0 = new Vector3(Mathf.Cos(a0) * rOut, y, Mathf.Sin(a0) * rOut);
                var o1 = new Vector3(Mathf.Cos(a1) * rOut, y, Mathf.Sin(a1) * rOut);
                var i0 = new Vector3(Mathf.Cos(a0) * rIn, y, Mathf.Sin(a0) * rIn);
                var i1 = new Vector3(Mathf.Cos(a1) * rIn, y, Mathf.Sin(a1) * rIn);
                mb.AddFlatQuad(0, i0, o0, o1, i1, Vector3.up);
            }
            return mb.ToMesh("PlateIndicator");
        }

        // ======================================================================== player ====
        /// <summary>
        /// Smooth shaded rather than faceted - the character should feel soft and friendly next to
        /// the hard faceted terrain, and being the only smooth thing on screen helps it read as
        /// the one fixed point in the world.
        /// </summary>
        static Mesh Player()
        {
            var mb = new MeshBuilder();

            // Legs (sub 1, very slightly darker).
            for (int s = -1; s <= 1; s += 2)
                mb.AddCylinder(1, new Vector3(0.078f * s, 0f, 0f), 0.06f, 0.055f, 0.2f, 7);

            // Arms, held clear of the torso so they actually read in silhouette at phone size.
            for (int s = -1; s <= 1; s += 2)
                mb.AddCylinder(1, new Vector3(0.205f * s, 0.56f, 0f), 0.05f, 0.045f, 0.24f, 7,
                               true, true, Quaternion.Euler(0f, 0f, 168f * s));

            // Torso and an oversized head - the reference character is all head, and that is what
            // makes it read as a friendly little person rather than a peg.
            mb.AddBlob(0, new Vector3(0f, 0.4f, 0f), new Vector3(0.17f, 0.21f, 0.145f), 2, 0f, 1, false);
            mb.AddBlob(0, new Vector3(0f, 0.75f, 0f), new Vector3(0.2f, 0.198f, 0.19f), 2, 0f, 2, false);
            return mb.ToMesh("Player");
        }

        /// <summary>
        /// The v2 character: a faceted ball.
        ///
        /// A humanoid was the wrong body for this game. The player is now a dynamic physics object
        /// that rolls, and a rolling upright character either has to be animated into a skate
        /// (expensive, and it hides the physics) or tumble end over end (which looks broken). A ball
        /// is honest about what the simulation is doing, and it is the only shape that behaves
        /// predictably on tilting geometry - a box sits at PhysX's friction angle on a slope and
        /// catches on tile seams, which is why v1 abandoned crates as puzzle pieces.
        ///
        /// Kept FLAT shaded and deliberately low-poly (subdiv 2 octahedron, 128 tris) so the facets
        /// catch the key light and the spin is readable. A smooth sphere rolling looks static.
        /// </summary>
        static Mesh PlayerBall()
        {
            var mb = new MeshBuilder();
            // A hair of vertical squash reads as soft and toy-like rather than as a billiard ball,
            // and is small enough that it never looks wrong while rolling.
            mb.AddBlob(0, Vector3.zero, new Vector3(0.34f, 0.325f, 0.34f), 2, 0f, 11);
            return mb.ToMesh("PlayerBall");
        }

        /// <summary>
        /// Two eyes on the ball, as a separate mesh so they can carry their own dark material.
        ///
        /// The style bible says the character has no features at all, and for a fixed upright
        /// figure that was right. For a ball it is not: without a mark on it, a rolling sphere
        /// under flat-ish lighting barely reads as rotating, and the rotation IS the feedback that
        /// gravity is doing something. Two eyes fix that and buy the cute read at the same time.
        /// </summary>
        static Mesh PlayerFace()
        {
            var mb = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                mb.AddBlob(0, new Vector3(0.115f * s, 0.055f, -0.295f),
                           new Vector3(0.062f, 0.072f, 0.05f), 1, 0f, 3 + s);
            return mb.ToMesh("PlayerFace");
        }

        /// <summary>
        /// The key: a faceted gem. Reads as valuable at phone size without needing a texture, and
        /// an octahedron silhouette is distinct from every rock in the game at a glance.
        /// </summary>
        static Mesh Key()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(0, Vector3.zero, new Vector3(0.15f, 0.23f, 0.15f), 1, 0f, 7);
            return mb.ToMesh("Key");
        }

        // ========================================================================= utility ==
        /// <summary>
        /// Subdivided 1x1 plane. The water shader displaces vertices, so a 4-vertex quad would
        /// have nothing to wave with - this gives it a grid to push around.
        /// </summary>
        static Mesh WaterTile(int div, float size)
        {
            var mb = new MeshBuilder();
            div = Mathf.Max(1, div);
            float step = size / div;
            float half = size * 0.5f;
            for (int i = 0; i < div; i++)
                for (int j = 0; j < div; j++)
                {
                    float x0 = -half + i * step, x1 = x0 + step;
                    float z0 = -half + j * step, z1 = z0 + step;
                    mb.AddFlatQuad(0, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0),
                                      new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), Vector3.up);
                }
            return mb.ToMesh("WaterTile");
        }

        static Mesh QuadXZ()
        {
            var mb = new MeshBuilder();
            mb.AddFlatQuad(0, new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                              new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f), Vector3.up);
            return mb.ToMesh("QuadXZ");
        }

        static Mesh QuadXY()
        {
            var mb = new MeshBuilder();
            mb.AddFlatQuad(0, new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                              new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), Vector3.back);
            return mb.ToMesh("QuadXY");
        }

        /// <summary>
        /// The translucent body of a pool: a unit-height box pivoted at its BOTTOM so WaterVolume
        /// can scale Y to drain it. Slightly narrower than a cell so it sits inside the basin, and
        /// almost unchamfered so its top edge meets the surface tile cleanly. Only the front face
        /// is ever really seen; the surface tile does the waves.
        /// </summary>
        static Mesh WaterBody()
        {
            var mb = new MeshBuilder();
            mb.AddChamferBox(0, new Vector3(0f, 0.5f, 0f), new Vector3(0.94f, 1f, 0.9f), 0.01f);
            return mb.ToMesh("WaterBody");
        }

        // ========================================================================== saving ==
        static readonly Dictionary<string, Mesh> built = new Dictionary<string, Mesh>();

        static void Save(Mesh mesh, string id)
        {
            string path = $"{PtwArt.MeshDir}/{id}.asset";
            mesh.name = id;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Keep the same asset instance so prefab references survive a rebuild.
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = id;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                built[id] = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
                built[id] = mesh;
            }
        }

        public static Mesh Get(string id)
        {
            if (built.TryGetValue(id, out var m) && m) return m;
            m = AssetDatabase.LoadAssetAtPath<Mesh>($"{PtwArt.MeshDir}/{id}.asset");
            built[id] = m;
            return m;
        }
    }
}
