using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Authors the ten levels from ASCII maps.
    ///
    /// v1 maps were TOP-DOWN pictures of a horizontal island in XZ, and a whole paragraph of this
    /// comment used to be about how "downhill" ran along the (1,0,-1) diagonal because rotation
    /// happened about the isometric camera axis. All of that is gone. v2 levels live in the XY
    /// plane and the map is a SIDE VIEW - what you type is very nearly what you see on screen,
    /// with gravity straight down the page.
    ///
    /// HOW TO THINK ABOUT A PUZZLE HERE
    ///
    /// The player is a ball, gravity always points down the screen, and the only verb is "rotate
    /// the level". So a ball always ends up in the lowest pocket it can reach, and a level is a
    /// CHAIN OF POCKETS. Designing one is choosing which pocket the ball falls into at each angle,
    /// and making the route to the door a sequence of tips rather than one lucky spin.
    ///
    /// Three rules that follow from that:
    ///  1. A flat floor is a level-1 floor. It solves at any angle, which is perfect for teaching
    ///     and useless afterwards. Everything past level 2 wants lips, walls and pockets.
    ///  2. Anything the player must NOT reach has to be uphill of every pocket, or behind a lip.
    ///     A hazard on an open slope is not an obstacle, it is a coin flip.
    ///  3. Rocks obey exactly the same rules, which is what makes them interesting: a rock and the
    ///     player in the same pocket will race, and the pocket only holds one of them.
    ///
    /// MAP LEGEND (row 0 is the TOP of the screen; the grid is auto-centred on its own extent, so
    /// the level always pivots about its middle.)
    ///     .  empty
    ///     g  terrain block - grassed automatically if the cell above it is empty, stone if not
    ///     s  stone block        d  dark stone block        #  built stone block (walls, plinths)
    ///     P  player spawn       D  exit door               K  key
    ///     b  boulder            c  crate
    ///     f  fire               k  spikes                  p  plate (a dark block with a pad)
    ///     X  gate               M  moving platform
    ///     w  pool - goes IN THE FLOOR ROW in place of a block: a half-height bed with water in
    ///        the top half, so the surface is level with the grass. Floats the ball, sinks rocks,
    ///        pours downhill when tilted and puts out fire it pours onto.
    ///     T  tree   t  small tree   r  rock   u  bush   y  crystal
    /// </summary>
    public static class PtwLevels
    {
        public const string Dir = "Assets/PullTheWorld/Prefabs/Levels";
        public const int Count = 20;

        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(Dir);
            Level01(); Level02(); Level03(); Level04(); Level05();
            Level06(); Level07(); Level08(); Level09(); Level10();
            Level11(); Level12(); Level13(); Level14();
            Level15(); Level16(); Level17(); Level18();
            Level19(); Level20();
            AssetDatabase.SaveAssets();
        }

        public static LevelDefinition[] LoadAll()
        {
            var list = new List<LevelDefinition>();
            for (int i = 1; i <= Count; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Level_{i:00}.prefab");
                if (!go) continue;
                var def = go.GetComponent<LevelDefinition>();
                if (def) list.Add(def);
            }
            return list.ToArray();
        }

        // ==================================================================== the levels =====

        // A note on how conservative these layouts are.
        //
        // A rolling ball on geometry that can be rotated to any angle is not a system you can
        // design for on paper. Whether a ball escapes a one-deep pocket at 45 degrees depends on
        // its radius, the friction pair, the chamfer on the block it is resting against and how
        // much speed it arrived with. The first draft of these levels assumed several of those
        // answers and got them wrong in both directions - two levels were unsolvable because the
        // only route ran through a hazard, and one was trivially solvable by tilting either way.
        //
        // So every level below is built from patterns whose traversability does not depend on any
        // of that: a flat tray the ball can always cross, walls a cell taller than the floor at
        // both ends so it cannot leave, and mechanics arranged along that tray. Hazards sit where
        // OVERSHOOTING reaches them rather than across the only path, so a mistake is a mistake
        // rather than a dead end. Props are stopped by the end wall, which is a reliable way to
        // park a rock on a plate rather than a hopeful one.
        //
        // This makes the set solvable and readable, and it leaves the puzzle depth thinner than it
        // should be. That needs a play pass with hands on it - the automated tests can prove
        // nothing explodes and that a level loads, but they cannot tell whether a puzzle is
        // interesting.

        /// <summary>
        /// One gesture teaches the whole game. A flat tray with the door at one end: any tip rolls
        /// the ball home, so the player cannot fail to discover what rotation does. Limited to 40
        /// degrees so the island never goes near upside down while they are still working out
        /// which way is which.
        /// </summary>
        static void Level01()
        {
            var b = new Builder(1, "Tip It Over") { Teach = TeachHint.Rotate, AngleLimit = 40f };
            b.Map(
                "# . P . t . . D #",
                "# g g g g g g g #",
                "# g g g g g g g #",
                "# g g g g g g g #",
                ". g g g g g g g .",
                ". . g g g g g . .",
                ". . . g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// A dip in the middle of the tray. A gentle tip now drops the ball into the dip and leaves
        /// it there, so this is the first level where how FAR you turn matters and not just which
        /// way. The door is still on the flat, so there is no way to lose.
        /// </summary>
        static void Level02()
        {
            var b = new Builder(2, "Out of the Dip") { AngleLimit = 60f };
            b.Map(
                "# u P . . . . D #",
                "# g g . . . g g #",
                "# g g g s g g g #",
                "# g g g g g g g #",
                ". g g g g g g g .",
                ". . g g g g g . .",
                ". . . g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Spikes, in the only arrangement that cannot kill by surprise: in a notch at the OPPOSITE
        /// end from the door, visible from the first frame. Tip towards the door and you are fine;
        /// tip away and you are not. That is the whole lesson, and it is survivable to learn.
        /// </summary>
        static void Level03()
        {
            var b = new Builder(3, "Mind the Spikes")
            { Teach = TeachHint.AvoidHazard, AngleLimit = 60f };
            b.Map(
                "# . . P . . . D #",
                "# k . g g g g g #",
                "# s g g g g g g #",
                "# g g g g g g g #",
                ". g g g g g g g .",
                ". . g g g g g . .",
                ". . . g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// The rock arrives. Nothing depends on it yet - it just shares the tray, races the ball to
        /// whichever end is downhill and gets in the way. Introducing it with no stakes means the
        /// player has already learned how it behaves before level 5 asks them to use it.
        /// </summary>
        static void Level04()
        {
            var b = new Builder(4, "Share the Slope")
            { Teach = TeachHint.RockIsATool, AngleLimit = 65f };
            b.Map(
                "# . P . b . . D #",
                "# g g g g g g g #",
                "# g g g g g g g #",
                "# g g g g g g g #",
                ". g g g g g g g .",
                ". . g g g g g . .",
                ". . . g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Plate and gate, the first two-step puzzle.
        ///
        /// The plate is against the left wall, which is what makes this reliable: tip left and the
        /// rock runs into the corner and STAYS on the pad, because the wall stops it. The plate
        /// latches, the gate drops, and a tip back to the right takes the ball to the door. Two
        /// deliberate moves in opposite directions, with no timing.
        /// </summary>
        static void Level05()
        {
            var b = new Builder(5, "Hold It Down")
            { Teach = TeachHint.PlateOpensGate, AngleLimit = 70f };
            b.Map(
                "# . P . b . X . D #",
                "# p g g g g g g g #",
                "# d g g g g g g g #",
                "# g g g g g g g g #",
                ". g g g g g g g g .",
                ". . g g g g g g . .",
                ". . . g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Fire, put out with the rock. The rock starts between the ball and the fire, so tipping
        /// towards the door sends the rock through the flame first and it smothers on the way past.
        /// Being permanently out once smothered is what makes this fair rather than a race.
        /// </summary>
        static void Level06()
        {
            var b = new Builder(6, "Smother It") { AngleLimit = 65f };
            b.Map(
                "# . P . b . f . D #",
                "# g g g g g g g g #",
                "# g g g g g g g g #",
                "# g g g g g g g g #",
                ". g g g g g g g g .",
                ". . g g g g g g . .",
                ". . . g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Key before door. The gem sits at the far end from the exit, so the tray has to be swung
        /// one way and then the other - the same two-move shape as the plate level, but with the
        /// lock on the door instead of on a gate.
        /// </summary>
        static void Level07()
        {
            var b = new Builder(7, "Fetch the Gem")
            { Teach = TeachHint.CollectKey, RequiredKeys = 1, AngleLimit = 65f };
            b.Map(
                "# K . . P . . . D #",
                "# g g g g g g g g #",
                "# g g g g g g g g #",
                "# g g g g g g g g #",
                ". g g g g g g g g .",
                ". . g g g g g g . .",
                ". . . g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Timing. A ferry shuttles across a spiked pit that splits the tray. The platform is the
        /// only floor over the gap, so the ball has to be sent across while it is actually there.
        /// </summary>
        static void Level08()
        {
            var b = new Builder(8, "Catch the Ferry")
            { Teach = TeachHint.Timing, AngleLimit = 45f };
            b.Map(
                "# . P . . . . . D #",
                "# g g M . . . g g #",
                "# g g . . . . g g #",
                "# g g k k k k g g #",
                "# g g s s s s g g #",
                ". g g g g g g g g .",
                ". . g g g g g g . .",
                ". . . g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Plate and gate again, now with a punishment for overshooting: a spiked notch just past
        /// the door. Getting the rock onto the plate is the same move as level 5; stopping at the
        /// right place afterwards is new.
        /// </summary>
        static void Level09()
        {
            var b = new Builder(9, "Don't Overshoot") { AngleLimit = 70f };
            b.Map(
                "# . P . b . X . D . #",
                "# p g g g g g g g k #",
                "# d g g g g g g g s #",
                "# g g g g g g g g g #",
                ". g g g g g g g g g .",
                ". . g g g g g g g . .",
                ". . . g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// The finale, and a genuine three-move puzzle built only out of the pieces already taught:
        ///
        ///   tip right - the rock smothers the fire, the ball follows and picks up the gem, then
        ///               stops against the closed gate
        ///   tip left  - the rock runs into the corner onto the plate and latches it, opening the
        ///               gate behind the ball
        ///   tip right - the ball rolls through the open gate to the door
        ///
        /// Nothing new is introduced. It is a test of the vocabulary.
        /// </summary>
        static void Level10()
        {
            var b = new Builder(10, "All Together") { RequiredKeys = 1, AngleLimit = 75f };
            b.Map(
                "# . P . b . f K X . D #",
                "# p g g g g g g g g g #",
                "# d g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }


        /// <summary>
        /// Two gems, on opposite sides, with the door between them. The first level that cannot be
        /// solved by picking a direction and committing - you have to go left, come back, and the
        /// door you passed on the way is only live once you hold both.
        /// </summary>
        static void Level11()
        {
            var b = new Builder(11, "Both Gems")
            { RequiredKeys = 2, AngleLimit = 70f };
            b.Map(
                "# . K P . . D . . K . #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Fire at both ends and the door in the middle. Nothing here is hard to reach; the whole
        /// level is about NOT overshooting, which is the first time the angle has to be judged
        /// rather than just chosen.
        /// </summary>
        static void Level12()
        {
            var b = new Builder(12, "Careful Now") { AngleLimit = 60f };
            b.Map(
                "# f . . P . . D . . . f #",
                "# g g g g g g g g g g g #",
                "# g g g g g g g g g g g #",
                "# g g g g g g g g g g g #",
                ". g g g g g g g g g g g .",
                ". . g g g g g g g g g . .",
                ". . . g g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Two rocks and one plate. Only one of them can be in the corner, and the other ends up in
        /// the way - so this is the first level where the props interfere with each other rather
        /// than just with you.
        /// </summary>
        static void Level13()
        {
            var b = new Builder(13, "Rock Traffic") { AngleLimit = 70f };
            b.Map(
                "# . P . b . b . X . D #",
                "# p g g g g g g g g g #",
                "# d g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Gem, door, and a spiked notch immediately past the door. The gem is on the way, so the
        /// only real question is whether you can stop.
        /// </summary>
        static void Level14()
        {
            var b = new Builder(14, "Stop at the Door")
            { RequiredKeys = 1, AngleLimit = 70f };
            b.Map(
                "# . P . . . . . K D . #",
                "# g g g g g g g g g k #",
                "# g g g g g g g g g s #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Everything from the first half at once, in three deliberate moves: left to park the rock
        /// and pick up the near gem, right for the far gem through the opened gate, and the door is
        /// on the way back.
        /// </summary>
        static void Level15()
        {
            var b = new Builder(15, "Three Moves")
            { RequiredKeys = 2, AngleLimit = 75f };
            b.Map(
                "# . K P . b . X . K D #",
                "# p g g g g g g g g g #",
                "# d g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// The ferry again, now carrying you to a gem rather than just across. The deck sits level
        /// with both ledges, so boarding is a roll rather than a climb.
        /// </summary>
        static void Level16()
        {
            var b = new Builder(16, "Ferry to the Gem")
            { RequiredKeys = 1, AngleLimit = 45f };
            b.Map(
                "# . P . . . . . K D . #",
                "# g g g M . . . g g g #",
                "# g g g . . . . g g g #",
                "# g g g k k k k g g g #",
                "# g g g s s s s g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . ."
            );
            b.Save();
        }

        /// <summary>
        /// Two fires, one rock. The rock puts out both on a single pass, which is the point - it
        /// teaches that a smothered fire stays smothered before level 18 relies on it.
        /// </summary>
        static void Level17()
        {
            var b = new Builder(17, "One Rock, Two Fires")
            { RequiredKeys = 1, AngleLimit = 70f };
            b.Map(
                "# . P . b . f . f K D #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// The finale. Every piece, in a fixed order that has to be worked out:
        ///
        ///   right - the rock runs ahead and puts the fire out; you follow and stop at the gate
        ///   left  - the rock parks in the corner on the plate, the gate opens behind you, and you
        ///           collect the near gem on the way
        ///   right - through the open gate, the far gem, then the door
        /// </summary>
        static void Level18()
        {
            var b = new Builder(18, "The Last Turn")
            { RequiredKeys = 2, AngleLimit = 85f };
            b.Map(
                "# . K P . b . f . X K D #",
                "# p g g g g g g g g g g #",
                "# d g g g g g g g g g g #",
                "# g g g g g g g g g g g #",
                ". g g g g g g g g g g g .",
                ". . g g g g g g g g g . .",
                ". . . g g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Water arrives with no stakes. A two-cell pool sits in the floor between start and door;
        /// the ball rolls in, bobs, and rolls out the other side. The only thing being taught is
        /// that water is safe, slow, and floats you.
        /// </summary>
        static void Level19()
        {
            var b = new Builder(19, "Wade Through") { AngleLimit = 60f };
            b.Map(
                "# . P . . . . . . . D #",
                "# g g g g w w g g g g #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Water as a tool. A fire blocks the way to the door and there is a pool right next to it,
        /// on the uphill side. Tip towards the door and the pool pours onto the flame and puts it
        /// out; the ball follows, floats across the emptying pool and rolls on. Tip the other way
        /// first and the water pours out harmlessly - it is finite, so that is a real mistake.
        /// </summary>
        static void Level20()
        {
            var b = new Builder(20, "Pour It Out") { AngleLimit = 65f };
            b.Map(
                "# . P . . . f . . . D #",
                "# g g g g w g g g g g #",
                "# g g g g g g g g g g #",
                "# g g g g g g g g g g #",
                ". g g g g g g g g g g .",
                ". . g g g g g g g g . .",
                ". . . g g g g g g . . ."
            );
            b.Save();
        }

        // ======================================================================= builder =====
        class Builder
        {
            readonly int number;
            readonly string title;
            readonly GameObject root;
            readonly LevelDefinition def;

            public TeachHint Teach = TeachHint.None;
            public float AngleLimit;
            public float StartAngle;
            public int RequiredKeys;

            ExitPortal portal;
            PressurePlate plate;
            readonly List<Gate> gates = new List<Gate>();
            Vector3 rawSpawn;
            bool spawnSet;

            // Extent of every occupied cell, in raw grid space, used to centre the level.
            Bounds extent;
            bool extentInit;

            public Builder(int number, string title)
            {
                this.number = number;
                this.title = title;
                root = new GameObject($"Level_{number:00}");
                def = root.AddComponent<LevelDefinition>();
            }

            /// <summary>
            /// Grid geometry, in one place so the rest of the builder can stay readable.
            ///
            /// A block prefab's origin is its TOP face (v1 convention, kept so the meshes and
            /// colliders did not have to be re-authored), and a block is BlockH tall. So for a map
            /// of h rows, the block in row r occupies Y in [h-r-1, h-r].
            /// </summary>
            static float RowTop(int row, int rows) => (rows - row) * PtwMeshes.BlockH;
            static float RowBottom(int row, int rows) => (rows - row - 1) * PtwMeshes.BlockH;
            static float RowCentre(int row, int rows) => (rows - row - 0.5f) * PtwMeshes.BlockH;

            public void Map(params string[] rows)
            {
                int h = rows.Length;
                var grid = new List<char[]>();
                foreach (var r in rows) grid.Add(r.Replace(" ", "").ToCharArray());

                char At(int col, int row)
                {
                    if (row < 0 || row >= h) return '.';
                    var line = grid[row];
                    return col < 0 || col >= line.Length ? '.' : line[col];
                }

                for (int row = 0; row < h; row++)
                {
                    var line = grid[row];
                    for (int col = 0; col < line.Length; col++)
                        Place(line[col], col, row, h, At);
                }

                Recentre();
            }

            // 'w' counts as solid for the grass rule: the pool bed covers the block beneath it.
            static bool IsSolid(char c) =>
                c == 'g' || c == 's' || c == 'd' || c == '#' || c == 'p' || c == 'w';

            void Place(char c, int col, int row, int rows, System.Func<int, int, char> at)
            {
                if (c == '.' || c == ' ') return;

                float top = RowTop(row, rows);
                float bottom = RowBottom(row, rows);
                float centre = RowCentre(row, rows);

                // Anything that STANDS on the terrain needs terrain directly beneath it. Worth
                // asserting at generation time rather than discovering in a capture: the first
                // build put a tree one cell too high and it hung in mid-air, which costs five
                // seconds to fix and a surprisingly long time to notice in a screenshot.
                const string mustBeGrounded = "DfktTruyw";
                if (mustBeGrounded.IndexOf(c) >= 0 && !IsSolid(at(col, row + 1)))
                {
                    Debug.LogWarning($"PTW: level {number} has '{c}' at col {col}, row {row} " +
                                     "with no solid block below it - skipped so it cannot float.");
                    return;
                }

                Grow(col, centre);

                switch (c)
                {
                    // --- terrain -------------------------------------------------------------
                    case 'g':
                        // Grass only where the top face is actually exposed. A buried block with a
                        // green cap on it produces a stripe of grass running through solid rock,
                        // which is the single fastest way to make a generated island look wrong.
                        Block(IsSolid(at(col, row - 1)) ? "Block_Stone" : "Block_Grass", col, top);
                        break;
                    case 's': Block("Block_Stone", col, top); break;
                    case 'd': Block("Block_Stone_Dark", col, top); break;
                    case '#': Block("Block_Stone_Light", col, top); break;

                    case 'p':
                        Block("Block_Stone_Dark", col, top);
                        plate = Prop("PressurePlate", col, top)?.GetComponent<PressurePlate>();
                        break;

                    // A pool replaces a FLOOR block: half-height bed in the bottom of the cell,
                    // water prop pivoted on the bed's top filling the rest. The surface ends up
                    // level with the neighbouring grass, so the ball rolls in and floats through.
                    case 'w':
                    {
                        float bedTop = bottom + PtwMeshes.BlockH * 0.5f;
                        Block("Block_Pool_Bed", col, bedTop);
                        Prop("Prop_Water", col, bedTop);
                        break;
                    }

                    // --- spawn and goal ------------------------------------------------------
                    case 'P':
                        rawSpawn = new Vector3(col, centre, 0f);
                        spawnSet = true;
                        break;

                    case 'D':
                        portal = Prop("ExitPortal_Door", col, bottom)?.GetComponent<ExitPortal>();
                        break;

                    case 'K':
                        Prop("Key_Gem", col, centre);
                        break;

                    // --- gate ----------------------------------------------------------------
                    case 'X':
                    {
                        var g = Prop("Gate_Stone", col, top)?.GetComponent<Gate>();
                        if (g) gates.Add(g);
                        break;
                    }

                    // --- moving platform -----------------------------------------------------
                    // Mounted at the TOP of its own row, not the centre, so the platform deck is
                    // level with the ledges either side of the pit it crosses. At cell centre it
                    // sat half a block low and the ball had to climb onto it.
                    case 'M': Prop("Platform_Moving", col, top); break;

                    // --- dynamic props -------------------------------------------------------
                    // Dropped a little above the floor so they settle on load instead of starting
                    // in penetration, which PhysX resolves by launching them.
                    case 'b': Prop("Prop_Boulder", col, bottom + 0.4f); break;
                    case 'c': Prop("Prop_Crate", col, bottom + 0.4f); break;

                    // --- hazards -------------------------------------------------------------
                    case 'f': Prop("Hazard_Fire", col, bottom); break;
                    case 'k': Prop("Hazard_Spikes", col, bottom); break;

                    // --- dressing ------------------------------------------------------------
                    case 'T': Prop("Prop_Tree", col, bottom, Rnd(col, row)); break;
                    case 't': Prop("Prop_TreeSmall", col, bottom, Rnd(col, row)); break;
                    case 'r': Prop("Prop_RockDeco", col, bottom, Rnd(col, row)); break;
                    case 'u': Prop("Prop_Bush", col, bottom, Rnd(col, row)); break;
                    case 'y': Prop("Prop_Crystal", col, bottom, Rnd(col, row)); break;
                }
            }

            /// <summary>Stable pseudo-random yaw so dressing does not look stamped from one die.</summary>
            static float Rnd(int x, int y) =>
                Mathf.Repeat(Mathf.Abs(x * 73856093 ^ y * 19349663) * 0.0137f, 360f);

            void Grow(float x, float y)
            {
                var p = new Vector3(x, y, 0f);
                if (!extentInit) { extent = new Bounds(p, Vector3.zero); extentInit = true; }
                else extent.Encapsulate(p);
            }

            GameObject Block(string prefab, int col, float topY)
            {
                var src = Load(prefab, PtwPrefabs.Blocks);
                if (!src) { Debug.LogWarning("PTW: missing block " + prefab); return null; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                go.transform.localPosition = new Vector3(col, topY, 0f);
                return go;
            }

            GameObject Prop(string prefab, float x, float y, float rotZ = 0f)
            {
                var src = Load(prefab, PtwPrefabs.Play) ?? Load(prefab, PtwPrefabs.Props);
                if (!src) { Debug.LogWarning("PTW: missing prop " + prefab); return null; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                go.transform.localPosition = new Vector3(x, y, 0f);
                // Dressing is rotated about Z, not Y: the puzzle plane is XY, so a Y rotation
                // would swing a tree away from the camera instead of tilting it in frame.
                if (!Mathf.Approximately(rotZ, 0f))
                    go.transform.localRotation = Quaternion.Euler(0f, 0f, (rotZ % 24f) - 12f);
                return go;
            }

            static GameObject Load(string name, string dir) =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}.prefab");

            /// <summary>
            /// Shift every child so the level's own extent is centred on the origin. The origin is
            /// the rotation pivot, so an off-centre level would swing around a corner of itself
            /// and leave the frame.
            /// </summary>
            void Recentre()
            {
                if (!extentInit) return;
                Vector3 shift = -new Vector3(extent.center.x, extent.center.y, 0f);

                foreach (Transform child in root.transform)
                    child.localPosition += shift;

                rawSpawn += shift;
            }

            /// <summary>
            /// Worst-case visible area needed, swept over the level's whole rotation range.
            ///
            /// This is the bit that stops every island looking like a model on a big empty table.
            /// A single global framing has to assume the largest level at the worst angle, so every
            /// other level ends up tiny; and framing the upright bounding box alone would clip the
            /// moment the level was turned. So the half-extents are rotated through the range the
            /// level actually permits and the maximum is taken. A level limited to 40 degrees gets
            /// framed far tighter than a free-spinning one, which is exactly right - it can never
            /// present its diagonal to the camera.
            /// </summary>
            Vector2 ComputeViewExtents()
            {
                if (!extentInit) return new Vector2(12f, 12f);

                // Cell centres were tracked, so add half a cell to reach the real edges.
                float hx = extent.extents.x + PtwMeshes.Grid * 0.5f;
                float hy = extent.extents.y + PtwMeshes.BlockH * 0.5f;

                // A free-spinning level has to be framed as its circumscribed circle.
                float limit = AngleLimit > 0f ? Mathf.Min(AngleLimit, 90f) : 90f;

                float maxW = 0f, maxH = 0f;
                for (float a = -limit; a <= limit + 0.01f; a += 5f)
                {
                    float c = Mathf.Abs(Mathf.Cos(a * Mathf.Deg2Rad));
                    float s = Mathf.Abs(Mathf.Sin(a * Mathf.Deg2Rad));
                    maxW = Mathf.Max(maxW, hx * c + hy * s);
                    maxH = Mathf.Max(maxH, hx * s + hy * c);
                }

                const float margin = 0.7f;   // per side
                return new Vector2((maxW + margin) * 2f, (maxH + margin) * 2f);
            }

            public void Save()
            {
                def.number = number;
                def.title = title;
                def.startAngle = StartAngle;
                def.allowRotation = true;
                def.angleLimit = AngleLimit;
                def.requiredKeys = RequiredKeys;
                def.teach = Teach;
                def.exit = portal;
                def.viewExtents = ComputeViewExtents();

                // Spawn a little above the floor so the ball drops in rather than starting in
                // contact - the small fall is also the first thing that tells the player gravity
                // is on and pointing down.
                def.spawnPoint = spawnSet
                    ? rawSpawn + new Vector3(0f, 0.15f, 0f)
                    : new Vector3(0f, 2f, 0f);

                // A key level's door starts locked and is opened by the key count; a plate level's
                // door is opened by the plate. Both are wired here so a level author only has to
                // place the characters.
                if (portal)
                {
                    bool keyDriven = RequiredKeys > 0;
                    SetBool(portal, "locked", keyDriven);
                    SetBool(portal, "unlockedByKeys", keyDriven);
                }

                if (plate)
                {
                    var so = new SerializedObject(plate);
                    SetArray(so, "gates", gates.ToArray());
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/Level_{number:00}.prefab");
                Object.DestroyImmediate(root);
            }

            static void SetBool(Object target, string field, bool value)
            {
                var so = new SerializedObject(target);
                var p = so.FindProperty(field);
                if (p != null) { p.boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            static void SetArray(SerializedObject so, string field, Object[] values)
            {
                var p = so.FindProperty(field);
                if (p == null) return;
                p.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }
    }
}
