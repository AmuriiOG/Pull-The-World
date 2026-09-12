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
    ///     e  enemy - rolls like a rock, crawls towards you, kills on touch. A fast heavy rock,
    ///        spikes or fire kill it.
    ///     B  breakable crate - blocks the way until a heavy rock hits it hard. The ball cannot.
    ///     j  spring pad - throws whatever lands on it along the LEVEL's up, so a tilt aims the
    ///        throw. 2.5 m straight up: clears a two-block ledge, never a three-block one.
    ///     T  tree   t  small tree   r  rock   u  bush   y  crystal
    /// </summary>
    public static class PtwLevels
    {
        public const string Dir = "Assets/PullTheWorld/Prefabs/Levels";
        public const int Count = 50;

        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(Dir);
            Level01(); Level02(); Level03(); Level04(); Level05();
            Level06(); Level07(); Level08(); Level09(); Level10();
            Level11(); Level12(); Level13(); Level14();
            Level15(); Level16(); Level17(); Level18();
            Level19(); Level20();
            Level21(); Level22(); Level23();
            Level24(); Level25(); Level26(); Level27(); Level28(); Level29();
            Level30(); Level31(); Level32(); Level33(); Level34(); Level35();
            Level36(); Level37(); Level38(); Level39(); Level40();
            Level41(); Level42(); Level43(); Level44(); Level45();
            Level46(); Level47(); Level48(); Level49(); Level50();
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

        // ------------------------------------------------- chapter three: springs and combos --
        // Levels 24-35 assume everything before them is known and add ONE new toy, the spring pad,
        // then combine it with rocks, crates, enemies, water and ferries. Each was reasoned out
        // against the numbers in the legend (11 m/s throw under 24 m/s^2 gravity = 2.5 m; a rock
        // needs about two free cells of run-up to break a crate or crush an enemy; the ball floats,
        // rocks sink) rather than tuned by hand - see the note below on why hands are still needed.

        // ------------------------------------------------- chapter four: tiers and timing ------
        // Levels 36-50 stop being trays. Floating tiers the ball rolls UNDER and is thrown ONTO,
        // holes it must clear, ferries in sequence, rocks dropped from height. Same numbers as the
        // batch above, plus: a rock falling one block lands at ~7 m/s (enough to crush or break), a
        // rock in a one-cell pit leaves a 0.32 m gap the ball rolls over, and the gap under a tier
        // needs to be at least one block for the ball (0.67 m) to pass.

        /// <summary>Two throws up two ledges. The second has to be aimed.</summary>
        static void Level36()
        {
            var b = new Builder(36, "Two Steps Up") { AngleLimit = 40f };
            b.Map(
                "# . . . . . . . . . . . . . . #",
                "# . . . . . . . . . . . . . D #",
                "# . . . . . . . . . . . g g g #",
                "# . . . . . . . . j . . g g g #",
                "# . . . . . . . . g g g g g g #",
                "# . P . . j . . . g g g g g g #",
                "# g g g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>The rock rides the ferry to the plate on the far side. Miss the timing and it falls out and comes back.</summary>
        static void Level37()
        {
            var b = new Builder(37, "Rock Ferry") { AngleLimit = 45f };
            b.Map(
                "# . P . b . . . . . . . X D #",
                "# g g g g M . . . g g p g g #"
            );
            b.Save();
        }

        /// <summary>Throw the rock over a hole onto the plate. Too little tilt and it bounces in place; a miss falls out and respawns.</summary>
        static void Level38()
        {
            var b = new Builder(38, "Mind the Gap") { AngleLimit = 45f };
            b.Map(
                "# . P . . b j . . . . X D #",
                "# g g g g g g . . g p g g #"
            );
            b.Save();
        }

        /// <summary>One rock, three enemies down a long alley. It has to keep its speed.</summary>
        static void Level39()
        {
            var b = new Builder(39, "Bowling Alley") { AngleLimit = 70f };
            b.Map(
                "# . P . b . . . e . . . e . . . e . D #",
                "# g g g g g g g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Two ferries in a row.</summary>
        static void Level40()
        {
            var b = new Builder(40, "Double Ferry") { Teach = TeachHint.Timing, AngleLimit = 45f };
            b.Map(
                "# . P . . . . . . . . . . . . D #",
                "# g g M . . . g g M . . . g g g #"
            );
            b.Save();
        }

        /// <summary>The door is at the bottom of a pit, and so is an enemy. Drop the rock on it first.</summary>
        static void Level41()
        {
            var b = new Builder(41, "Drop In") { AngleLimit = 45f };
            b.Map(
                "# . P b . . . . . #",
                "# g g g g . . g g #",
                "# g g g g e D g g #",
                "# g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Roll right under the tier, bounce on the pad at its end, land on top, roll back left to the door.</summary>
        static void Level42()
        {
            var b = new Builder(42, "Switchback") { AngleLimit = 35f };
            b.Map(
                "# D . . . . . . . . . . . #",
                "# g g g g g g g g g . . . #",
                "# . P . . . . . . . j . . #",
                "# g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>An enemy sits in a one-cell pit in the floor. Roll the rock off the tier into the pit, then roll over the plug.</summary>
        static void Level43()
        {
            var b = new Builder(43, "Plug the Hole") { AngleLimit = 45f };
            b.Map(
                "# . . b . . . . . . . . . . #",
                "# g g g g g . . . . . . . . #",
                "# . . . . . . . . . . . . . #",
                "# D . . . . . . . . . . P . #",
                "# g g g g g e g g g g g g g #",
                "# g g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Off the ferry straight onto a pad, and up to the door.</summary>
        static void Level44()
        {
            var b = new Builder(44, "Ferry, Then Fly") { AngleLimit = 40f };
            b.Map(
                "# . . . . . . . . . . . . D #",
                "# . . . . . . . . . . . g g #",
                "# . P . . . . . . j . . g g #",
                "# g g g M . . . g g g g g g #"
            );
            b.Save();
        }

        /// <summary>A ferry across the first gap, a throw across the second.</summary>
        static void Level45()
        {
            var b = new Builder(45, "Twin Gaps") { AngleLimit = 40f };
            b.Map(
                "# . P . . . . . j . . . . D #",
                "# g g M . . . g g g . . g g #"
            );
            b.Save();
        }

        /// <summary>Two one-cell holes, a pad before each. Small tilts.</summary>
        static void Level46()
        {
            var b = new Builder(46, "Hopscotch") { AngleLimit = 35f };
            b.Map(
                "# . P . j . . . j . . . D #",
                "# g g g g . g g g . g g g #"
            );
            b.Save();
        }

        /// <summary>The gem hangs over the hole. Only one arc takes it: flatter throws fly lower.</summary>
        static void Level47()
        {
            var b = new Builder(47, "Long Shot") { RequiredKeys = 1, AngleLimit = 45f };
            b.Map(
                "# . . . . . K . . . . . #",
                "# . P . j . . . . . . D #",
                "# g g g g . . . g g g g #"
            );
            b.Save();
        }

        /// <summary>Rock into crate into rock into crate into enemy onto the plate. One tilt, if it is a hard one.</summary>
        static void Level48()
        {
            var b = new Builder(48, "Rolling Thunder") { AngleLimit = 65f };
            b.Map(
                "# . P . b . B . b . B . e . . X D #",
                "# g g g g g g g g g g g g g p g g #"
            );
            b.Save();
        }

        /// <summary>A tier with a two-cell gap and a pad beneath it. Up through the skylight and onto the roof.</summary>
        static void Level49()
        {
            var b = new Builder(49, "Skylight") { AngleLimit = 30f };
            b.Map(
                "# . . . . . . . . D #",
                "# g g g g . . g g g #",
                "# . P . . j . . . . #",
                "# g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Everything, then up two ledges to the door.</summary>
        static void Level50()
        {
            var b = new Builder(50, "Grand Finale") { AngleLimit = 45f };
            b.Map(
                "# . . . . . . . . . . . . . . . . . . . . D #",
                "# . . . . . . . . . . . . . . . . . . . g g #",
                "# . . . . . . . . . . . . . . . . . j . g g #",
                "# . . . . . . . . . . . . . . . . g g g g g #",
                "# . P . b . . B . e . . . . . j . g g g g g #",
                "# g g g g g g g g g M . . . g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Teach the pad: roll onto it, get thrown onto a two-block ledge, roll home.</summary>
        static void Level24()
        {
            var b = new Builder(24, "Spring Step") { AngleLimit = 45f };
            b.Map(
                "# . . . . . . . . D #",
                "# . . . . . . g g g #",
                "# . P . j . . g g g #",
                "# g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Plate at the near end, ferry over the pit, gate before the door: two tilts and a wait.</summary>
        static void Level25()
        {
            var b = new Builder(25, "Hold, Then Cross") { AngleLimit = 45f };
            b.Map(
                "# . P . b . . . . X . D #",
                "# p g g g M . . . g g g #"
            );
            b.Save();
        }

        /// <summary>The crate guards the gem. Smash it with the rock, then go and get it.</summary>
        static void Level26()
        {
            var b = new Builder(26, "Smash and Grab")
            { Teach = TeachHint.RockIsATool, RequiredKeys = 1, AngleLimit = 65f };
            b.Map(
                "# . P . b . . B K . . D #",
                "# g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>The rock has to go OVER a wall: roll it onto the pad, and the tilt aims the throw onto the plate.</summary>
        static void Level27()
        {
            var b = new Builder(27, "Over the Wall") { AngleLimit = 50f };
            b.Map(
                "# . . . . . . . . . . . . #",
                "# . P . . b j # . . . X D #",
                "# g g g g g g g g g p g g #"
            );
            b.Save();
        }

        /// <summary>A hole right through the island. Tip the enemy into it, then jump it with the pad.</summary>
        static void Level28()
        {
            var b = new Builder(28, "Long Jump") { AngleLimit = 40f };
            b.Map(
                "# . P . j . . . . e . . D #",
                "# g g g g . . . g g g g g #",
                "# g g g g . . . g g g g g #"
            );
            b.Save();
        }

        /// <summary>One rock, two enemies in a line. It has to keep its speed through the first to reach the second.</summary>
        static void Level29()
        {
            var b = new Builder(29, "Double Trouble") { AngleLimit = 65f };
            b.Map(
                "# . P . b . . . e . . . e . D #",
                "# g g g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Tilt to pour the pool onto the fire, float across, and the pad throws you over the wall.</summary>
        static void Level30()
        {
            var b = new Builder(30, "Steam") { AngleLimit = 50f };
            b.Map(
                "# . . . . . . . . . . . . #",
                "# P . . . f . . j # . . D #",
                "# g g g w g g g g g g g g #",
                "# g g g g g g g g g g g g #"   // a pool needs a block under its bed
            );
            b.Save();
        }

        /// <summary>The crate stands between the rock and the plate. It needs a hard tilt to break through.</summary>
        static void Level31()
        {
            var b = new Builder(31, "Break In") { AngleLimit = 65f };
            b.Map(
                "# . B . b . . P . . X . D #",
                "# p g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Two pads, three ledges. The second throw has to be aimed to reach the top.</summary>
        static void Level32()
        {
            var b = new Builder(32, "Stairway") { AngleLimit = 40f };
            b.Map(
                "# . . . . . . . . . . . D #",
                "# . . . . . . . . . g g g #",
                "# . . . . . j . . g g g g #",
                "# . P . j . g g g g g g g #",
                "# g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Right first: the rock bowls the enemy. Then left: pour on the fire, float across, take the gem home.</summary>
        static void Level33()
        {
            var b = new Builder(33, "Gauntlet") { RequiredKeys = 1, AngleLimit = 60f };
            b.Map(
                "# D K f . . . P . . b . . e . #",
                "# g g g w g g g g g g g g g g #",
                "# g g g g g g g g g g g g g g #"   // a pool needs a block under its bed
            );
            b.Save();
        }

        /// <summary>A gem on a floating slab between two pads. Land on the slab, take it, fall onto the second pad.</summary>
        static void Level34()
        {
            var b = new Builder(34, "Trampoline Park") { RequiredKeys = 1, AngleLimit = 45f };
            b.Map(
                "# . . . . . . . . . . . . #",
                "# . . . . . K . . . . . . #",
                "# . . . . # # # . . . . . #",
                "# . P . j . . . j . . . D #",
                "# g g g g g g g g g g g g #"
            );
            b.Save();
        }

        /// <summary>Everything, in order: rock through crate and enemy to the right; then left over the water, past the doused fire, up the pad to the door.</summary>
        static void Level35()
        {
            var b = new Builder(35, "The Long Way") { AngleLimit = 55f };
            b.Map(
                "# D . . . . . . . . . . . . . . . . . #",
                "# g g g . . . . . . . . . . . . . . . #",
                "# g g g j . f . . P . . b . . B . . e #",
                "# g g g g g g w g g g g g g g g g g g #",
                "# g g g g g g g g g g g g g g g g g g #"   // a pool needs a block under its bed
            );
            b.Save();
        }

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

        /// <summary>
        /// The enemy arrives, and the rock is already the answer. It sits between you and the door;
        /// the rock sits between you and it. Tip towards the door and the rock gets there first,
        /// at speed, and bowls it over. Dawdle and it starts crawling towards you.
        /// </summary>
        static void Level21()
        {
            var b = new Builder(21, "Bowl It Over")
            { Teach = TeachHint.RockIsATool, AngleLimit = 65f };
            b.Map(
                "# . P . b . e . . . D #",
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
        /// A crate blocks the way. Roll into it yourself and nothing happens - it needs the rock,
        /// arriving from three cells uphill. The only puzzle is realising you are not heavy enough.
        /// </summary>
        static void Level22()
        {
            var b = new Builder(22, "Break Through")
            { Teach = TeachHint.RockIsATool, AngleLimit = 70f };
            b.Map(
                "# . P . b . . B . . D #",
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
        /// One rock, everything in its path: it smashes the crate, keeps rolling, bowls the enemy,
        /// and you follow it through to the gem and the door. A bowling-alley finale.
        /// </summary>
        static void Level23()
        {
            var b = new Builder(23, "Clear the Way")
            { RequiredKeys = 1, AngleLimit = 75f };
            b.Map(
                "# . P . b . B . e . K D #",
                "# g g g g g g g g g g g #",
                "# g g g g g g g g g g g #",
                "# g g g g g g g g g g g #",
                ". g g g g g g g g g g g .",
                ". . g g g g g g g g g . .",
                ". . . g g g g g g g . . ."
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
            // Exposed grass blocks by cell, so the vegetation pass can dress them afterwards.
            readonly Dictionary<(int col, int row), GameObject> grassAt = new Dictionary<(int, int), GameObject>();
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

                Dress(At, h);
                Recentre();
            }

            /// <summary>
            /// Turf and vegetation, after the mockups: a fringe of blades over most cap edges, tufts
            /// on some, a flower now and then, vines hanging over open sides and occasionally down
            /// the front face. Seeded from the level number so a rebuild reproduces the same island.
            /// Every detail is a prefab child switched on or a small prop, so it costs nothing in
            /// physics and nothing in level-author effort: a plain 'g' gets dressed by itself.
            /// </summary>
            void Dress(System.Func<int, int, char> at, int rows)
            {
                var rnd = new System.Random(number * 7919 + 13);
                foreach (var kv in grassAt)
                {
                    var (col, row) = kv.Key;
                    var go = kv.Value;
                    if (!go) continue;
                    float top = RowTop(row, rows);

                    var fringe = go.transform.Find("Fringe");
                    if (fringe)
                    {
                        fringe.gameObject.SetActive(rnd.NextDouble() < 0.9);
                        // Mirror half of them and vary the length so no two edges repeat.
                        fringe.localScale = new Vector3(rnd.NextDouble() < 0.5 ? 1f : -1f,
                                                        0.85f + (float)rnd.NextDouble() * 0.4f, 1f);
                    }
                    var tufts = go.transform.Find("Tufts");
                    if (tufts)
                    {
                        tufts.gameObject.SetActive(rnd.NextDouble() < 0.5);
                        tufts.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    }
                    // Flowers: a third of the cells at random, and ALWAYS the cells either side of the
                    // doorway, leaning towards it - the painting has flowers flanking the door.
                    bool byDoor = portalCell.HasValue && row == portalCell.Value.row + 1
                                  && Mathf.Abs(col - portalCell.Value.col) == 1;
                    var flower = go.transform.Find("Flower");
                    if (flower && (byDoor || rnd.NextDouble() < 0.3))
                    {
                        flower.gameObject.SetActive(true);
                        float fx = byDoor ? Mathf.Sign(portalCell.Value.col - col) * 0.22f
                                          : ((float)rnd.NextDouble() - 0.5f) * 0.6f;
                        flower.localPosition = new Vector3(fx, 0f, -0.12f - (float)rnd.NextDouble() * 0.28f);
                        flower.localScale = Vector3.one * ((byDoor ? 1.45f : 1.25f) + (float)rnd.NextDouble() * 0.5f);   // the mockup's flowers read from arm's length
                        flower.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                        Petals(flower.gameObject, rnd);
                    }

                    // Vines over an open side (just outside the face, pulled forward so they read) -
                    // nearly always, often two of different lengths so a corner drapes - and now and
                    // then one down the front face.
                    for (int side = -1; side <= 1; side += 2)
                        if (!IsSolid(at(col + side, row)))
                        {
                            if (rnd.NextDouble() < 0.92) HangVine(col + side * 0.56f, top - 0.04f, -0.22f, rnd);
                            if (rnd.NextDouble() < 0.45) HangVine(col + side * 0.56f, top - 0.06f, 0.12f, rnd, 0.75f);
                        }
                    if (rnd.NextDouble() < 0.3)
                        HangVine(col + ((float)rnd.NextDouble() - 0.5f) * 0.6f, top - 0.03f, -0.53f, rnd);

                    // Small life on the cap, sparingly: a low bush now and then, a pebble at the
                    // front edge rarer still. Never next to the door (the flowers own that spot).
                    if (!byDoor && rnd.NextDouble() < 0.07)
                    {
                        var bush = Prop("Prop_Bush", col + ((float)rnd.NextDouble() - 0.5f) * 0.5f, top, Rnd(col, row));
                        if (bush) { bush.transform.localScale = Vector3.one * (0.42f + (float)rnd.NextDouble() * 0.16f); bush.transform.localPosition += new Vector3(0f, 0f, -0.1f); }
                    }
                    else if (!byDoor && rnd.NextDouble() < 0.05)
                    {
                        var stone = Prop("Prop_RockDeco", col + ((float)rnd.NextDouble() - 0.5f) * 0.6f, top, Rnd(col, row));
                        if (stone) { stone.transform.localScale = Vector3.one * (0.38f + (float)rnd.NextDouble() * 0.14f); stone.transform.localPosition += new Vector3(0f, 0f, -0.28f); }
                    }
                }
            }

            (int col, int row)? portalCell;

            void HangVine(float x, float y, float z, System.Random rnd, float scale = 1f)
            {
                var go = Prop(rnd.NextDouble() < 0.5 ? "Prop_Vine" : "Prop_VineShort", x, y);
                if (!go) return;
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f,
                                                              ((float)rnd.NextDouble() - 0.5f) * 10f);
                // Length varies vine to vine, so a row of them never reads as one repeated strip.
                go.transform.localScale = new Vector3(1f, scale * (0.8f + (float)rnd.NextDouble() * 0.45f), 1f);
            }

            static void Petals(GameObject flower, System.Random rnd)
            {
                var r = flower.GetComponent<MeshRenderer>();
                if (!r) return;
                var mats = r.sharedMaterials;
                if (mats.Length < 2) return;
                string id = rnd.Next(3) switch { 0 => PtwArt.MFlowerWhite, 1 => PtwArt.MFlowerYellow, _ => PtwArt.MFlowerPink };
                mats[1] = PtwArt.Get(id);
                r.sharedMaterials = mats;
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
                const string mustBeGrounded = "DfktTruywBj";
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
                    {
                        // Grass only where the top face is actually exposed. A buried block with a
                        // green cap on it produces a stripe of grass running through solid rock,
                        // which is the single fastest way to make a generated island look wrong.
                        bool exposed = !IsSolid(at(col, row - 1));
                        // Buried stone mixes three close values by a stable hash, darker the deeper
                        // it sits: the mockup's gentle block-to-block variation, and the soft shade
                        // a wall has towards its base without any extra lighting.
                        int depth = 0;
                        while (depth < 6 && IsSolid(at(col, row - 1 - depth))) depth++;
                        float pick = Rnd(col, row) / 360f;
                        string stoneId = depth <= 1 ? (pick < 0.30f ? "Block_Stone_Mid" : "Block_Stone")
                                       : depth == 2 ? (pick < 0.12f ? "Block_Stone_Dark" : pick < 0.60f ? "Block_Stone_Mid" : "Block_Stone")
                                                    : (pick < 0.30f ? "Block_Stone_Dark" : pick < 0.80f ? "Block_Stone_Mid" : "Block_Stone");
                        var go = Block(exposed ? "Block_Grass" : stoneId, col, top);
                        if (exposed && go) grassAt[(col, row)] = go;
                        break;
                    }
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
                        portalCell = (col, row);
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
                    case 'e': Prop("Enemy", col, bottom + 0.4f); break;

                    // Static: stands on the floor and is part of the level's compound collider
                    // until a rock breaks it.
                    case 'B': Prop("Breakable_Crate", col, bottom); break;

                    // Spring pad: stands on the floor, throws along the level's up.
                    case 'j': Prop("Pad_Bounce", col, bottom); break;

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

                // Laid stone, not tile grid: the block's VISUAL child (the collider stays on the
                // root and on the grid) gets a hair of tilt and scale, stable per cell and level.
                // Under two degrees and two percent - enough that no two edges are parallel, not
                // enough to read as broken or to poke visibly out of the collider.
                var v = go.transform.Find("Visual");
                if (v)
                {
                    uint h = (uint)(col * 73856093 ^ (int)(topY * 4f) * 19349663 ^ number * 83492791);
                    float U(uint k) { k ^= k >> 13; k *= 0x5bd1e995; k ^= k >> 15; return (k & 0xFFFF) / 65535f; }
                    v.localRotation = Quaternion.Euler((U(h) - 0.5f) * 1.4f, (U(h * 7) - 0.5f) * 2.4f, (U(h * 13) - 0.5f) * 2.0f);
                    float s = 0.985f + U(h * 29) * 0.03f;
                    v.localScale = new Vector3(s, 1f, 0.985f + U(h * 31) * 0.03f);
                }
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
