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
            Save(Enemy(), "Mesh_Enemy");
            Save(EnemySpikes(), "Mesh_EnemySpikes");
            Save(EnemyFace(), "Mesh_EnemyFace");
            Save(BouncePad(), "Mesh_BouncePad");

            // Pastel theme: sky layers, vegetation, the orb.
            Save(MountainRidge(11, 0.22f, 0.78f, 5), "Mesh_MountainFar");
            Save(MountainRidge(23, 0.18f, 0.70f, 4), "Mesh_MountainMid");
            Save(MountainRidge(37, 0.14f, 0.62f, 3), "Mesh_MountainNear");
            Save(GrassFringe(), "Mesh_GrassFringe");
            Save(GrassTufts(), "Mesh_GrassTufts");
            Save(Flower(), "Mesh_Flower");
            Save(Vine(5, 7), "Mesh_Vine");
            Save(Vine(9, 5), "Mesh_VineShort");
            Save(OrbSphere(), "Mesh_OrbSphere");
            Save(Annulus(0.46f, 0.50f, 56), "Mesh_OrbRing");
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
        // The portal, after the mockup: a tall POINTED (equilateral) arch of cream stone with small
        // diamond studs down the jambs, not a round Roman one. Shared numbers between the arch and
        // the fill pane below so the glow sits exactly inside the masonry.
        // The doorway is an EQUILATERAL pointed arch, like the mockups': the opening is W wide, each
        // inner arc is centred on the opposite inner springing point with radius W, so the two meet
        // at the apex 60 degrees round. Proportions measured off the paintings: the opening is a
        // little over half as wide as it is tall, the frame about a third of the opening, and the
        // jambs stand straight on the grass - no step.
        const float ArchInner = 0.40f;                              // inner half width of the opening
        const float ArchStoneH = 0.27f;                             // frame thickness, all the way round
        const float ArchR = ArchInner + ArchStoneH * 0.5f;          // half span to the centre of the frame
        const float ArchSpring = 0.78f;                             // where the jambs stop and the arch begins
        const float ArchInnerRadius = 2f * ArchInner;               // inner curve radius
        const float ArchOuterRadius = ArchInnerRadius + ArchStoneH; // outer curve radius
        const float ArchDepth = 0.40f;
        const float ArchBottom = 0.02f;                             // the fill starts just above the grass
        /// <summary>Height of the inside of the arch at the apex; the fill and the halo are sized from it.</summary>
        public const float ArchApex = ArchSpring + 0.8660254f * ArchInnerRadius;

        /// <summary>
        /// The arch, after the mockups: two tall cream jambs a side, three voussoirs a side SWEPT
        /// along the arc (so the inner and outer curves are smooth, not a stack of tilted boxes),
        /// a pointed keystone filling the notch where the arcs meet, and small carved diamonds -
        /// flat and stone-coloured, not lamps. Sub 0 stone, sub 1 carved diamonds, sub 2 the
        /// keystone's diamond, which alone may glow a little, sub 3 the faces that look into the
        /// doorway (they take a warm material, as if the light inside fell on them).
        /// </summary>
        static Mesh DoorArch()
        {
            var mb = new MeshBuilder();
            const float joint = 0.012f;                              // hairline between stones

            // Jambs.
            for (int side = -1; side <= 1; side += 2)
            {
                float h = (ArchSpring - joint) * 0.5f;
                for (int i = 0; i < 2; i++)
                {
                    float y = h * 0.5f + i * (h + joint);
                    mb.AddChamferBox(0, new Vector3(ArchR * side, y, 0f), new Vector3(ArchStoneH, h, ArchDepth), 0.028f);
                }
            }

            // Arch. Each side is swept from its springing (0 or 180 degrees round its centre) to the
            // 60-degree apex, split into three voussoirs with hairline joints.
            const int perSide = 3;
            float midR = ArchInnerRadius + ArchStoneH * 0.5f;
            float gap = joint / midR * Mathf.Rad2Deg * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new Vector2(-side * ArchInner, ArchSpring);
                float th0 = side < 0 ? 180f : 0f;
                float th1 = side < 0 ? 120f : 60f;
                float dir = Mathf.Sign(th1 - th0);
                for (int i = 0; i < perSide; i++)
                {
                    float a0 = Mathf.Lerp(th0, th1, i / (float)perSide) + dir * (i == 0 ? 0f : gap);
                    float a1 = Mathf.Lerp(th0, th1, (i + 1) / (float)perSide) - dir * (i == perSide - 1 ? 0f : gap);
                    ArcStone(mb, 0, 3, c, a0, a1, ArchInnerRadius, ArchOuterRadius, ArchDepth, 0.028f, 6);
                }
            }

            // The jambs' faces that look into the doorway, and the chamfer beside them on the front:
            // thin plates in the inner-face submesh, a hair inside the box so they do not fight it.
            {
                float hz = ArchDepth * 0.5f, ch = 0.028f, eps = 0.002f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * (ArchInner + eps);
                    mb.AddFlatQuad(3, new Vector3(x, 0f, -hz + ch), new Vector3(x, ArchSpring - joint, -hz + ch),
                                      new Vector3(x, ArchSpring - joint, hz - ch), new Vector3(x, 0f, hz - ch),
                                   new Vector3(-side, 0f, 0f));
                    float x1 = side * (ArchInner + ch + eps);
                    mb.AddFlatQuad(3, new Vector3(x, 0f, -hz + ch), new Vector3(x1, 0f, -hz - eps),
                                      new Vector3(x1, ArchSpring - joint, -hz - eps), new Vector3(x, ArchSpring - joint, -hz + ch),
                                   new Vector3(-side, 0f, -1f));
                }
            }

            // Keystone: the arcs stop where their INNER curves meet, leaving a V-notch between their
            // outer ends; a diamond-set stone fills it and points down into the doorway a touch, as
            // the mockup's peak stone does.
            float keyY = ArchApex + 0.15f;
            mb.AddChamferBox(0, new Vector3(0f, keyY, 0f), new Vector3(0.30f, 0.30f, ArchDepth + 0.02f), 0.03f,
                             Quaternion.Euler(0f, 0f, 45f));

            // Carved diamonds: two down each jamb, one on each arch side, a larger one on the keystone.
            float front = -(ArchDepth * 0.5f + 0.012f);
            for (int side = -1; side <= 1; side += 2)
            {
                foreach (float y in new[] { 0.24f, 0.56f })
                    mb.AddChamferBox(1, new Vector3(ArchR * side, y, front), new Vector3(0.075f, 0.075f, 0.024f), 0.008f,
                                     Quaternion.Euler(0f, 0f, 45f));
                var c = new Vector2(-side * ArchInner, ArchSpring);
                float th = (side < 0 ? 150f : 30f) * Mathf.Deg2Rad;
                var pos = new Vector3(c.x + Mathf.Cos(th) * midR, c.y + Mathf.Sin(th) * midR, front);
                mb.AddChamferBox(1, pos, new Vector3(0.065f, 0.065f, 0.024f), 0.008f, Quaternion.Euler(0f, 0f, 45f));
            }
            mb.AddChamferBox(2, new Vector3(0f, keyY, front - 0.01f), new Vector3(0.11f, 0.11f, 0.024f), 0.01f,
                             Quaternion.Euler(0f, 0f, 45f));
            return mb.ToMesh("DoorArch");
        }

        /// <summary>
        /// One curved stone: a CHAMFERED rectangular section (rIn..rOut by depth, corners cut by
        /// <paramref name="chamfer"/>) swept round centre c from th0 to th1 degrees, with end caps.
        /// The chamfer is what gives the voussoirs the same edge highlights as the chamfered boxes
        /// everywhere else - without it they read as flat paper. The inner wall and the two
        /// chamfers beside it go to <paramref name="innerSub"/>, so the faces that look into the
        /// doorway can carry the warm material that says "light is falling on this stone".
        /// </summary>
        static void ArcStone(MeshBuilder mb, int sub, int innerSub, Vector2 c, float th0, float th1,
                             float rIn, float rOut, float depth, float chamfer, int steps)
        {
            float hz = depth * 0.5f;
            float ch = Mathf.Min(chamfer, Mathf.Min(rOut - rIn, depth) * 0.3f);
            // Profile in (r, z), anticlockwise, starting at the front face's inner end.
            var prof = new (float r, float z)[]
            {
                (rIn + ch, -hz), (rOut - ch, -hz),      // 0: front face
                (rOut, -hz + ch), (rOut, hz - ch),      // 1: front-outer chamfer, 2: outer wall
                (rOut - ch, hz), (rIn + ch, hz),        // 3: back-outer chamfer, 4: back face
                (rIn, hz - ch), (rIn, -hz + ch),        // 5: back-inner chamfer, 6: inner wall, 7: front-inner chamfer
            };
            int n = prof.Length;

            Vector3 P(float deg, float r, float z)
            {
                float a = deg * Mathf.Deg2Rad;
                return new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, z);
            }
            Vector3 Radial(float deg) => P(deg, 1f, 0f) - new Vector3(c.x, c.y, 0f);

            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(th0, th1, i / (float)steps);
                float a1 = Mathf.Lerp(th0, th1, (i + 1) / (float)steps);
                Vector3 rad = Radial((a0 + a1) * 0.5f);
                for (int e = 0; e < n; e++)
                {
                    var p0 = prof[e];
                    var p1 = prof[(e + 1) % n];
                    // Outward normal of an anticlockwise edge (dr, dz) is (dz, -dr).
                    Vector3 hint = rad * (p1.z - p0.z) + Vector3.forward * -(p1.r - p0.r);
                    int s = e >= 5 ? innerSub : sub;
                    mb.AddFlatQuad(s, P(a0, p0.r, p0.z), P(a0, p1.r, p1.z), P(a1, p1.r, p1.z), P(a1, p0.r, p0.z), hint);
                }
            }

            float dir = Mathf.Sign(th1 - th0);
            foreach (var (th, hint) in new[] { (th0, Radial(th0 - dir * 90f)), (th1, Radial(th1 + dir * 90f)) })
            {
                var centre = P(th, (rIn + rOut) * 0.5f, 0f);
                for (int e = 0; e < n; e++)
                {
                    var p0 = prof[e];
                    var p1 = prof[(e + 1) % n];
                    mb.AddFlatTri(sub, centre, P(th, p0.r, p0.z), P(th, p1.r, p1.z), hint);
                }
            }
        }

        /// <summary>Flat glowing pane that fills the pointed doorway. Double sided - the door can face anywhere.</summary>
        static Mesh ArchFill()
        {
            var mb = new MeshBuilder();
            const float bottom = ArchBottom;

            // Boundary of the opening: left inner arc up to the apex, then the right one down. Each
            // arc is centred on the OPPOSITE inner springing point (see the constants).
            var rim = new List<Vector3>();
            const int seg = 12;
            for (int i = 0; i <= seg; i++)
            {
                float th = Mathf.Lerp(180f, 180f - ApexAngle(), i / (float)seg) * Mathf.Deg2Rad;
                rim.Add(new Vector3(ArchInner + Mathf.Cos(th) * ArchInnerRadius, ArchSpring + Mathf.Sin(th) * ArchInnerRadius, 0f));
            }
            for (int i = seg; i >= 0; i--)
            {
                float th = Mathf.Lerp(0f, ApexAngle(), i / (float)seg) * Mathf.Deg2Rad;
                rim.Add(new Vector3(-ArchInner + Mathf.Cos(th) * ArchInnerRadius, ArchSpring + Mathf.Sin(th) * ArchInnerRadius, 0f));
            }

            foreach (float z in new[] { 0f, -0.001f })
            {
                Vector3 hint = z < -0.0005f ? Vector3.back : Vector3.forward;
                mb.AddFlatQuad(0, new Vector3(-ArchInner, bottom, z), new Vector3(ArchInner, bottom, z),
                                  new Vector3(ArchInner, ArchSpring, z), new Vector3(-ArchInner, ArchSpring, z), hint);
                var fan = new Vector3(0f, ArchSpring, z);
                for (int i = 0; i + 1 < rim.Count; i++)
                {
                    var p0 = rim[i]; p0.z = z;
                    var p1 = rim[i + 1]; p1.z = z;
                    mb.AddFlatTri(0, fan, p0, p1, hint);
                }
            }
            return mb.ToMesh("ArchFill");
        }

        /// <summary>Angle (from the arc centre) at which the inner arc reaches x = 0, the apex.</summary>
        static float ApexAngle() => Mathf.Acos(ArchInner / ArchInnerRadius) * Mathf.Rad2Deg;   // 60 for an equilateral arch

        // ======================================================================= sky ========
        /// <summary>
        /// One layer of background mountains: a unit-wide strip (x -0.5..0.5, y 0..1) whose top
        /// edge is a ridgeline of a few soft peaks with noise on top. UV.y runs 0 at the base and 1
        /// at the ridge so a gradient texture hazes the tops. SkyLayer stretches it to the frustum.
        /// </summary>
        static Mesh MountainRidge(int seed, float baseline, float amplitude, int peaks)
        {
            var mb = new MeshBuilder();
            var rnd = new System.Random(seed);
            var centres = new float[peaks];
            var widths = new float[peaks];
            var heights = new float[peaks];
            for (int i = 0; i < peaks; i++)
            {
                centres[i] = Mathf.Lerp(-0.55f, 0.55f, (i + 0.5f) / peaks) + ((float)rnd.NextDouble() - 0.5f) * 0.18f;
                widths[i] = 0.16f + (float)rnd.NextDouble() * 0.16f;
                heights[i] = 0.55f + (float)rnd.NextDouble() * 0.45f;
            }

            float Ridge(float x)
            {
                float h = 0f;
                for (int i = 0; i < peaks; i++)
                {
                    float d = Mathf.Abs(x - centres[i]) / widths[i];
                    // Slightly rounded peak: a triangle blended with a cosine bump.
                    float tri = Mathf.Max(0f, 1f - d);
                    float bump = d < 1f ? 0.5f + 0.5f * Mathf.Cos(d * Mathf.PI) : 0f;
                    h = Mathf.Max(h, heights[i] * Mathf.Lerp(tri, bump, 0.35f));
                }
                float n = Mathf.PerlinNoise(x * 9f + seed * 3.1f, seed * 0.7f) - 0.5f;
                return Mathf.Clamp01(baseline + amplitude * h + n * 0.06f);
            }

            // Shared vertices with UV.y = absolute height, so the haze gradient is continuous
            // across the strip. Per-quad UVs (0 at each quad's base, 1 at ITS ridge) stretched the
            // gradient differently in every column and drew vertical bands down the mountains.
            const int cols = 96;
            var bottom = new int[cols + 1];
            var top = new int[cols + 1];
            for (int i = 0; i <= cols; i++)
            {
                float x = -0.5f + i / (float)cols;
                float h = Ridge(x);
                bottom[i] = mb.AddVertex(new Vector3(x, 0f, 0f), Vector3.back, new Vector2(0.5f, 0f));
                top[i] = mb.AddVertex(new Vector3(x, h, 0f), Vector3.back, new Vector2(0.5f, h));
            }
            for (int i = 0; i < cols; i++)
            {
                // Two triangles per column, wound to face -Z (the camera looks along +Z).
                mb.AddTriangle(0, bottom[i], top[i], top[i + 1]);
                mb.AddTriangle(0, bottom[i], top[i + 1], bottom[i + 1]);
            }
            return mb.ToMesh("MountainRidge");
        }

        // ================================================================ vegetation ========
        /// <summary>
        /// Tufts hanging over the FRONT top edge of a grass block, so the cap reads as turf with a
        /// fringe rather than a green slab. Pivot at the cap edge; hangs down and out.
        /// </summary>
        static Mesh GrassFringe()
        {
            var mb = new MeshBuilder();
            var rnd = new System.Random(77);
            // Fuller than the first pass (which read as a row of dark dashes): more blades, fatter
            // at the root, hanging further, plus a row of short leaf-blobs so the edge is turf.
            const int blades = 12;
            for (int i = 0; i < blades; i++)
            {
                float x = Mathf.Lerp(-0.45f, 0.45f, (i + 0.5f) / blades) + ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                float len = 0.22f + (float)rnd.NextDouble() * 0.14f;
                var dir = new Vector3(((float)rnd.NextDouble() - 0.5f) * 0.5f, -0.8f, -0.5f).normalized;
                mb.AddCylinder(0, new Vector3(x, 0.0f, -0.5f), 0.05f, 0.006f, len, 4, false, true,
                               Quaternion.FromToRotation(Vector3.up, dir));
                if (i % 2 == 0)
                    mb.AddBlob(0, new Vector3(x + 0.03f, -0.05f, -0.53f), new Vector3(0.06f, 0.05f, 0.03f), 1, 0.05f, 40 + i, flat: false);
            }
            return mb.ToMesh("GrassFringe");
        }

        /// <summary>A few blades standing up on the cap. Base at y 0.</summary>
        static Mesh GrassTufts()
        {
            var mb = new MeshBuilder();
            var rnd = new System.Random(91);
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(((float)rnd.NextDouble() - 0.5f) * 0.7f, 0f, ((float)rnd.NextDouble() - 0.5f) * 0.6f);
                var dir = new Vector3(((float)rnd.NextDouble() - 0.5f) * 0.5f, 1f, ((float)rnd.NextDouble() - 0.5f) * 0.4f).normalized;
                float len = 0.12f + (float)rnd.NextDouble() * 0.1f;
                mb.AddCylinder(0, p, 0.03f, 0.003f, len, 4, false, true, Quaternion.FromToRotation(Vector3.up, dir));
            }
            return mb.ToMesh("GrassTufts");
        }

        /// <summary>A small flower: stem (sub 0, wind material), five petals (sub 1), centre (sub 2).</summary>
        static Mesh Flower()
        {
            var mb = new MeshBuilder();
            // Big enough to read at phone size: the first pass's flowers were specks.
            mb.AddCylinder(0, Vector3.zero, 0.018f, 0.012f, 0.24f, 5);
            mb.AddBlob(0, new Vector3(0.05f, 0.09f, 0.01f), new Vector3(0.05f, 0.02f, 0.03f), 1, 0f, 9, flat: false);   // a leaf
            const int petals = 5;
            for (int i = 0; i < petals; i++)
            {
                float a = i / (float)petals * Mathf.PI * 2f;
                var c = new Vector3(Mathf.Cos(a) * 0.055f, 0.26f + Mathf.Sin(a * 2f) * 0.005f, Mathf.Sin(a) * 0.055f);
                mb.AddBlob(1, c, new Vector3(0.048f, 0.02f, 0.046f), 1, 0f, 3 + i, flat: false);
            }
            mb.AddBlob(2, new Vector3(0f, 0.268f, 0f), new Vector3(0.03f, 0.024f, 0.03f), 1, 0f, 2, flat: false);
            return mb.ToMesh("Flower");
        }

        /// <summary>A hanging vine: a thin stem down a gentle curve with leaves alternating sides. Pivot at the top.</summary>
        static Mesh Vine(int seed, int segments)
        {
            var mb = new MeshBuilder();
            var rnd = new System.Random(seed);
            var prev = Vector3.zero;
            float drift = ((float)rnd.NextDouble() - 0.5f) * 0.08f;
            // Lush, like the mockup's: a leaf pair at every node, big enough to read at phone size.
            for (int i = 1; i <= segments; i++)
            {
                var p = new Vector3(Mathf.Sin(i * 0.8f + seed) * 0.05f + drift * i, -0.19f * i, 0.02f * Mathf.Sin(i * 1.3f));
                var d = p - prev;
                mb.AddCylinder(0, prev, 0.02f, 0.015f, d.magnitude, 4, false, false,
                               Quaternion.FromToRotation(Vector3.up, d.normalized));
                float side = i % 2 == 0 ? 1f : -1f;
                mb.AddBlob(0, p + new Vector3(side * 0.085f, 0.03f, 0f), new Vector3(0.085f, 0.045f, 0.05f), 1, 0.06f, seed * 7 + i, flat: false);
                mb.AddBlob(0, p + new Vector3(-side * 0.06f, 0.06f, 0.01f), new Vector3(0.06f, 0.035f, 0.04f), 1, 0.06f, seed * 5 + i, flat: false);
                prev = p;
            }
            mb.AddBlob(0, prev + new Vector3(0f, -0.04f, 0f), new Vector3(0.07f, 0.06f, 0.05f), 1, 0.05f, seed * 11, flat: false);
            return mb.ToMesh("Vine");
        }

        // ======================================================================= orb ========
        /// <summary>The glass body: a smooth-shaded sphere the size of the player's collider.</summary>
        static Mesh OrbSphere()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(0, Vector3.zero, Vector3.one * 0.335f, 3, 0f, 1, flat: false);
            return mb.ToMesh("OrbSphere");
        }

        /// <summary>A flat ring in the XY plane, both faces, for the orb's orbit line.</summary>
        static Mesh Annulus(float rIn, float rOut, int segs)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f, a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                var i0 = new Vector3(Mathf.Cos(a0) * rIn, Mathf.Sin(a0) * rIn, 0f);
                var i1 = new Vector3(Mathf.Cos(a1) * rIn, Mathf.Sin(a1) * rIn, 0f);
                var o0 = new Vector3(Mathf.Cos(a0) * rOut, Mathf.Sin(a0) * rOut, 0f);
                var o1 = new Vector3(Mathf.Cos(a1) * rOut, Mathf.Sin(a1) * rOut, 0f);
                mb.AddFlatQuad(0, i0, o0, o1, i1, Vector3.back);
                mb.AddFlatQuad(0, i0, o0, o1, i1, Vector3.forward);
            }
            return mb.ToMesh("Annulus");
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
        /// The enemy: a faceted ball like the player, but lumpier and ringed with spikes in the
        /// puzzle plane so its silhouette is hostile at a glance and unmistakably not a rock.
        /// sub0 body, sub1 spikes. The glowing eyes are the PlayerFace mesh in a hot material.
        /// </summary>
        /// <summary>
        /// The enemy's body: a lumpy blob with a grin. Submesh 0 body, 1 the dark mouth slot, 2 the
        /// teeth. The spike ring is a SEPARATE mesh (<see cref="EnemySpikes"/>) because Enemy.cs
        /// holds the body upright and lets the spikes roll with the rigidbody.
        /// </summary>
        static Mesh Enemy()
        {
            var mb = new MeshBuilder();
            mb.AddBlob(0, Vector3.zero, new Vector3(0.30f, 0.28f, 0.30f), 2, 0.09f, 23);
            // A grin: a dark slot low on the face, and a row of uneven teeth hanging into it.
            mb.AddChamferBox(1, new Vector3(0f, -0.10f, -0.265f), new Vector3(0.27f, 0.075f, 0.06f), 0.02f);
            const int teeth = 5;
            for (int i = 0; i < teeth; i++)
            {
                float x = Mathf.Lerp(-0.10f, 0.10f, i / (teeth - 1f));
                float len = i % 2 == 0 ? 0.07f : 0.05f;
                mb.AddCylinder(2, new Vector3(x, -0.065f, -0.29f), 0.02f, 0.003f, len, 4, false, true,
                               Quaternion.FromToRotation(Vector3.up, Vector3.down));
            }
            return mb.ToMesh("Enemy");
        }

        /// <summary>
        /// The spring pad: a stone plinth, a three-ring coil and a fat bright cap. Submesh 0 is the
        /// cap (bright), 1 the plinth and coil (metal). BouncePad punches the whole visual.
        /// </summary>
        static Mesh BouncePad()
        {
            var mb = new MeshBuilder();
            mb.AddCylinder(1, Vector3.zero, 0.36f, 0.34f, 0.10f, 14);
            mb.AddCylinder(1, new Vector3(0f, 0.10f, 0f), 0.16f, 0.16f, 0.06f, 10);
            mb.AddCylinder(1, new Vector3(0f, 0.17f, 0f), 0.21f, 0.21f, 0.05f, 10);
            mb.AddCylinder(1, new Vector3(0f, 0.23f, 0f), 0.16f, 0.16f, 0.06f, 10);
            mb.AddBlob(0, new Vector3(0f, 0.35f, 0f), new Vector3(0.38f, 0.075f, 0.38f), 2, 0f, 5);
            return mb.ToMesh("BouncePad");
        }

        /// <summary>Nine uneven spikes on a ring: the part that rolls. Uneven so it reads jagged, not gear-like.</summary>
        static Mesh EnemySpikes()
        {
            var mb = new MeshBuilder();
            const int spikes = 9;
            for (int i = 0; i < spikes; i++)
            {
                float a = (i + 0.5f) / spikes * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                float len = i % 3 == 0 ? 0.24f : 0.19f;
                mb.AddCylinder(0, dir * 0.20f, 0.065f, 0f, len, 5, false, false,
                               Quaternion.FromToRotation(Vector3.up, dir));
            }
            return mb.ToMesh("EnemySpikes");
        }

        /// <summary>
        /// Two narrow slabs tilted so the inner ends drop: the universal angry brow. Glowing
        /// material, driven per instance by Enemy.cs.
        /// </summary>
        static Mesh EnemyFace()
        {
            var mb = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                mb.AddChamferBox(0, new Vector3(0.115f * s, 0.075f, -0.285f), new Vector3(0.15f, 0.05f, 0.05f),
                                 0.018f, Quaternion.Euler(0f, 0f, 22f * s));
            return mb.ToMesh("EnemyFace");
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
