using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PullTheWorld.EditorTools
{
    /// <summary>
    /// Authors the five levels from ASCII maps.
    ///
    /// TWO RULES THAT SHAPE EVERY PUZZLE HERE
    ///
    /// 1. Only SOLID TERRAIN stops the player. Loose props cannot, because the player is a
    ///    kinematic pin and PhysX simply shoves a dynamic body aside - which is both correct and
    ///    stable. So walls are the maze, and loose props are tools that press plates and smother
    ///    hazards. Hazards block too, by threatening rather than colliding.
    ///
    /// 2. Rotation happens about the CAMERA axis, so in level space "downhill" always runs along
    ///    the (1,0,-1) diagonal - screen-horizontal. A negative spin tips downhill towards +X and
    ///    slightly -Z. Every rolling channel below is built along +X with a wall on its -Z side to
    ///    catch that drift, and the drop-off points are placed where that -Z drift is wanted.
    ///
    /// MAP LEGEND (row 0 is the HIGHEST z, so a map reads like a top-down picture. The grid is
    /// auto-centred on P, so the player always starts at local origin.)
    ///     .  nothing           g  grass floor        s  stone floor      d  dark stone floor
    ///     #  floor + wall      W  wall only          X  floor + gate     L  floor one step down
    ///     P  player start      D  exit door
    ///     b  boulder           c  crate
    ///     f  fire              k  spikes             p  pressure plate
    ///     T  tree   t  small tree   r  rock   u  bush   y  crystal
    /// </summary>
    public static class PtwLevels
    {
        public const string Dir = "Assets/PullTheWorld/Prefabs/Levels";

        public static void BuildAll()
        {
            PtwPaths.EnsureFolder(Dir);
            Level1(); Level2(); Level3(); Level4(); Level5();
            AssetDatabase.SaveAssets();
        }

        public static LevelDefinition[] LoadAll()
        {
            var list = new List<LevelDefinition>();
            for (int i = 1; i <= 5; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Level_{i:00}.prefab");
                if (go)
                {
                    var def = go.GetComponent<LevelDefinition>();
                    if (def) list.Add(def);
                }
            }
            return list.ToArray();
        }

        // ==================================================================== the levels =====

        /// <summary>One gesture teaches the entire game: drag, and the door comes to you.</summary>
        static void Level1()
        {
            var b = new Builder(1, "Pull the World",
                                "Drag anywhere.\nYou stay still - the WORLD moves.");
            b.AllowRotation = false;
            b.Map(
                ". . . t g g",
                ". . . g D g",
                ". . . g g r",
                ". . g g g .",
                ". g g T . .",
                "g P g . . .",
                "r g g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Teaches that you are solid. The straight line is walled off, so the only way to bring
        /// the door in is to steer the level around yourself. The wall runs the full width of the
        /// drag bounds, so there is no sneaking round the end.
        /// </summary>
        static void Level2()
        {
            var b = new Builder(2, "Around the Wall",
                                "The world cannot pass through you.\nGo around.");
            b.AllowRotation = false;
            b.Map(
                ". . g t g . .",
                ". . g D g . .",
                ". . g g g g g",
                ". . . . . . g",
                "# # # # # . g",
                ". . g g g . g",
                ". . g P g g g",
                ". . r g g . ."
            );
            b.Save();
        }

        /// <summary>
        /// The reveal. The door is locked and dragging cannot help. Twist, gravity keeps pointing
        /// down the screen, and the boulder rolls the length of its trough onto the plate. This is
        /// where most players realise they are steering gravity rather than the rock.
        /// </summary>
        static void Level3()
        {
            var b = new Builder(3, "Tip It Over",
                                "Twist with two fingers.\nGravity always points down.");
            b.LockDoorUntilPlate = true;
            b.TeachRotation = true;   // first level where twisting is unlocked
            b.Map(
                ". . . g g g . .",
                ". . . g D g . .",
                ". . . g g t . .",
                ". . . . . g g .",
                ". G b s p G g .",
                ". G G G G G g .",
                ". . . . . . g .",
                ". . g g g g g .",
                ". g g u . . . .",
                "g P g . . . . .",
                "r g g . . . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Props as tools. A fire holds the only gap in a wall. A boulder sits in a trough one row
        /// above it; tip the world, the boulder runs the trough, and the -Z drift drops it neatly
        /// into the gap and puts the fire out. Then walk the door home through the same gap.
        /// </summary>
        static void Level4()
        {
            var b = new Builder(4, "Smother the Fire", "Drop something heavy on it.");
            b.Map(
                ". . g g g . .",
                ". . g D g . .",
                ". . g g t . .",
                ". . . g . . .",
                ". G b s G . .",
                ". . G f . . .",
                ". . g g g . .",
                ". g g g u . .",
                ". g P g . . ."
            );
            b.Save();
        }

        /// <summary>
        /// Everything at once. Tip a crate down its trough onto the plate, the gate drops out of
        /// the way, then steer the long way round past the spikes to bring the door in.
        /// </summary>
        static void Level5()
        {
            var b = new Builder(5, "The Long Way Round", "");
            b.GateOnPlate = true;
            b.Map(
                ". . g g g . . .",
                ". . g D g . . .",
                ". . g g t . . .",
                ". . . X . . . .",
                ". . g g g g g .",
                ". G b s p G g .",
                ". G G G G G g .",
                ". . . . . . g .",
                ". . g g k g g .",
                ". g P g g g g .",
                ". r g g . . . ."
            );
            b.Save();
        }

        // ======================================================================= builder =====
        class Builder
        {
            readonly int number;
            readonly string title;
            readonly string hint;
            readonly GameObject root;
            readonly LevelDefinition def;

            public bool AllowRotation = true;
            public bool TeachRotation;
            public bool LockDoorUntilPlate;
            public bool GateOnPlate;

            ExitPortal portal;
            PressurePlate plate;
            MovingGate gate;
            Bounds extent;
            bool extentInit;
            bool boundsOverride;
            Vector3 boundsCenter, boundsSize;

            public Builder(int number, string title, string hint)
            {
                this.number = number;
                this.title = title;
                this.hint = hint;
                root = new GameObject($"Level_{number:00}");
                def = root.AddComponent<LevelDefinition>();
            }

            public void SetBounds(Vector3 center, Vector3 size)
            {
                boundsOverride = true;
                boundsCenter = center;
                boundsSize = size;
            }

            public void Map(params string[] rows)
            {
                int h = rows.Length;
                var grid = new List<char[]>();
                foreach (var r in rows) grid.Add(r.Replace(" ", "").ToCharArray());

                int px = 0, pz = 0;
                for (int row = 0; row < h; row++)
                    for (int col = 0; col < grid[row].Length; col++)
                        if (grid[row][col] == 'P') { px = col; pz = h - 1 - row; }

                for (int row = 0; row < h; row++)
                {
                    var line = grid[row];
                    for (int col = 0; col < line.Length; col++)
                        Place(line[col], col - px, (h - 1 - row) - pz);
                }
            }

            void Place(char c, int x, int z)
            {
                if (c == '.' || c == ' ') return;
                Grow(x, z);

                switch (c)
                {
                    case 'g': Floor("Block_Grass", x, 0, z); break;
                    case 's': Floor("Block_Stone", x, 0, z); break;
                    case 'd': Floor("Block_Stone_Dark", x, 0, z); break;
                    case 'L': Floor("Block_Grass", x, -1, z); break;

                    // Man-made stone barrier. Used sparingly - a run of these reads as a grey slab.
                    case '#':
                        Floor("Block_Stone", x, 0, z);
                        Floor("Block_Stone_Light", x, 1, z);
                        break;
                    case 'W': Floor("Block_Stone_Light", x, 1, z); break;

                    // Raised GRASS tier. Blocks the player exactly like a wall, but reads as a
                    // natural step in the terrain rather than masonry, which is what keeps the
                    // islands looking sculpted instead of like a warehouse.
                    case 'G':
                        Floor("Block_Grass", x, 0, z);
                        Floor("Block_Grass", x, 1, z);
                        break;

                    case 'P': Floor("Block_Grass", x, 0, z); break;

                    // Sunken pool. The basin block has no collider, so a water cell is never
                    // walkable - a lake is a hole you have to go round, and it pours out when the
                    // world is tipped.
                    case 'w':
                        Floor("Block_Basin", x, -1, z);
                        Prop("Prop_Water", x, -0.3f, z, 0f);
                        break;

                    case 'D':
                        Floor("Block_Grass", x, 0, z);
                        portal = Prop("ExitPortal_Door", x, 0f, z, 180f)?.GetComponent<ExitPortal>();
                        break;

                    case 'X':
                        Floor("Block_Stone", x, 0, z);
                        gate = Prop("Gate_Stone", x, PtwMeshes.BlockH, z, 0f)?.GetComponent<MovingGate>();
                        break;

                    case 'b':
                        Floor("Block_Stone", x, 0, z);
                        Prop("Prop_Boulder", x, 0.32f, z, 0f);
                        break;
                    case 'c':
                        Floor("Block_Stone", x, 0, z);
                        Prop("Prop_Crate", x, 0.33f, z, 18f);
                        break;

                    case 'f':
                        Floor("Block_Stone", x, 0, z);
                        Prop("Hazard_Fire", x, 0f, z, 0f);
                        break;
                    case 'k':
                        Floor("Block_Stone", x, 0, z);
                        Prop("Hazard_Spikes", x, 0f, z, 0f);
                        break;
                    case 'p':
                        Floor("Block_Stone_Dark", x, 0, z);
                        plate = Prop("PressurePlate", x, 0f, z, 0f)?.GetComponent<PressurePlate>();
                        break;

                    case 'T': Floor("Block_Grass", x, 0, z); Prop("Prop_Tree", x, 0f, z, Rnd(x, z)); break;
                    case 't': Floor("Block_Grass", x, 0, z); Prop("Prop_TreeSmall", x, 0f, z, Rnd(x, z)); break;
                    case 'r': Floor("Block_Grass", x, 0, z); Prop("Prop_RockDeco", x, 0f, z, Rnd(x, z)); break;
                    case 'u': Floor("Block_Grass", x, 0, z); Prop("Prop_Bush", x, 0f, z, Rnd(x, z)); break;
                    case 'y': Floor("Block_Stone", x, 0, z); Prop("Prop_Crystal", x, 0f, z, Rnd(x, z)); break;
                }
            }

            static float Rnd(int x, int z) =>
                Mathf.Repeat(Mathf.Abs(x * 73856093 ^ z * 19349663) * 0.0137f, 360f);

            void Grow(int x, int z)
            {
                var p = new Vector3(x, 0f, z);
                if (!extentInit) { extent = new Bounds(p, Vector3.zero); extentInit = true; }
                else extent.Encapsulate(p);
            }

            GameObject Floor(string prefab, int x, int gy, int z)
            {
                var src = Load(prefab, PtwPrefabs.Blocks);
                if (!src) { Debug.LogWarning("PTW: missing block " + prefab); return null; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                go.transform.localPosition = new Vector3(x, gy * PtwMeshes.BlockH, z);
                return go;
            }

            GameObject Prop(string prefab, float x, float y, float z, float rotY)
            {
                var src = Load(prefab, PtwPrefabs.Play) ?? Load(prefab, PtwPrefabs.Props);
                if (!src) { Debug.LogWarning("PTW: missing prop " + prefab); return null; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                go.transform.localPosition = new Vector3(x, y, z);
                go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
                return go;
            }

            static GameObject Load(string name, string dir) =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}.prefab");

            public void Save()
            {
                def.number = number;
                def.title = title;
                def.hint = hint;
                def.startFocus = Vector3.zero;   // maps are authored relative to P
                def.startSpin = 0f;
                def.allowRotation = AllowRotation;
                def.teachRotation = TeachRotation;
                def.exit = portal;

                def.useBounds = true;
                if (boundsOverride)
                {
                    def.boundsCenter = boundsCenter;
                    def.boundsSize = boundsSize;
                }
                else
                {
                    def.boundsCenter = new Vector3(extent.center.x, 0f, extent.center.z);
                    def.boundsSize = new Vector3(extent.size.x + 2.5f, 0.1f, extent.size.z + 2.5f);
                }

                if (portal) SetBool(portal, "locked", LockDoorUntilPlate);

                // onPressed only, never onChanged. A latch is forgiving; a puzzle that silently
                // re-locks because a boulder settled a centimetre off the pad is just cruel.
                if (plate)
                {
                    var so = new SerializedObject(plate);
                    var evt = so.FindProperty("onPressed");
                    if (LockDoorUntilPlate && portal) AddBoolCall(evt, portal, "SetLocked", false);
                    if (GateOnPlate && gate) AddBoolCall(evt, gate, "SetOpen", true);
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

            /// <summary>Add a persistent UnityEvent listener taking a bool, entirely from script.</summary>
            static void AddBoolCall(SerializedProperty unityEvent, Object target, string method, bool arg)
            {
                if (unityEvent == null) return;
                var calls = unityEvent.FindPropertyRelative("m_PersistentCalls.m_Calls");
                if (calls == null) return;

                int i = calls.arraySize;
                calls.arraySize++;
                var call = calls.GetArrayElementAtIndex(i);
                call.FindPropertyRelative("m_Target").objectReferenceValue = target;
                var tan = call.FindPropertyRelative("m_TargetAssemblyTypeName");
                if (tan != null) tan.stringValue = target.GetType().AssemblyQualifiedName;
                call.FindPropertyRelative("m_MethodName").stringValue = method;
                call.FindPropertyRelative("m_Mode").enumValueIndex = 6;      // Bool
                call.FindPropertyRelative("m_CallState").enumValueIndex = 2; // RuntimeOnly
                call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue = arg;
            }
        }
    }
}
