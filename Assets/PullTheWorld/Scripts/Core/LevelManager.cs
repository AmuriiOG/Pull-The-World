using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    public enum LevelState { Menu, Playing, Won, Failed }

    /// <summary>
    /// Owns the levels and the win / fail / restart / advance flow - and, since the world pass,
    /// the ONE WORLD they all stand in.
    ///
    /// Levels are no longer swapped in and out at the origin. Each level has a slot in the world
    /// (<see cref="SlotFor"/>) further DOWN THE LINE OF SIGHT than the one before: deeper along
    /// +Z, a little lower (the camera looks down, so a deeper island at the same height would
    /// climb up the screen) and weaving a little sideways. Seen through the narrow perspective
    /// lens (see <see cref="PlaneCameraRig"/>) that puts the next level in the upper part of the
    /// frame at about a third of the size, and the one after it smaller again above and beside it
    /// - the mockup's far islands, except that they are the real levels. Four things exist at once:
    ///
    ///  * CURRENT - the live one, under the rotating root, the only thing with physics.
    ///  * NEXT and the one AFTER - the complete prefabs, stripped to their meshes (see
    ///    <see cref="StripToVisual"/>), standing in their slots in the distance.
    ///  * PREVIOUS - only for the length of the journey: the finished level, frozen where it
    ///    stands, so the camera has something to sail over. It ends up behind the lens and goes.
    ///
    /// Reaching a portal no longer cuts to the next level. The orb is drawn in, the door flares,
    /// the finished level is frozen, the real next level replaces its stand-in in the same slot,
    /// and the camera PUSHES FORWARD to it (<see cref="PlaneCameraRig.TravelTo"/>): the finished
    /// island swells and slides out under the frame, the next grows from a silhouette into the
    /// level. Control comes back only when the camera has settled, through <see cref="ArrivalGate"/>
    /// so the UI can put an interstitial there. Nothing more than two levels ahead exists, so the
    /// world costs a phone a little more than one level did, not a lot.
    ///
    /// The one ordering rule that matters: the level is parented and the rotator is bound BEFORE
    /// the player is spawned, because the spawn point is expressed in level-local space and would
    /// otherwise be resolved against the previous level's rotation.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Content")]
        [Tooltip("Played in order. A prefab in this list is in the game.")]
        [SerializeField] LevelDefinition[] levels;

        [Header("References")]
        [SerializeField] WorldRotator rotator;
        [SerializeField] PlayerBody player;
        [SerializeField] Transform levelParent;
        [Tooltip("Re-framed on every level load so each island fills the screen the same amount.")]
        [SerializeField] PlaneCameraRig cameraRig;
        [Tooltip("Framing used while the main menu is up. Deliberately much wider than any level: " +
                 "the preview island is scenery behind the title here, not the subject, and at " +
                 "gameplay framing it fills the screen and collides with every menu widget.")]
        [SerializeField] Vector2 menuViewExtents = new Vector2(19f, 19f);   // the mockup menu island fills most of the width
        [Tooltip("Recolours the backdrop per chapter so the eighteen levels do not share one sky.")]
        [SerializeField] SkyTheme sky;

        [Header("World")]
        [Tooltip("World units each level stands DEEPER (+Z) than the one before, down the line of " +
                 "sight. With the 18-degree lens ~70 units in front of a level, this puts the next " +
                 "one at about a third of the size.")]
        [SerializeField] float slotDepth = 126f;
        [Tooltip("World units each level stands LOWER than the one before. The camera looks down " +
                 "20 degrees, so a deeper island at the same height would climb up the screen; " +
                 "this drop holds the next level at about 78% of the frame height, in the sky " +
                 "above the live island, and the one after it a little higher.")]
        [SerializeField] float slotDrop = 27f;
        [Tooltip("Sideways wander of the slots (0, right, left, 0, ...) so the path into the " +
                 "distance weaves and the far levels stand beside each other, not in a stack.")]
        [SerializeField] float slotWander = 3.5f;
        [Tooltip("Seconds the camera takes to push from a finished level to the next.")]
        [SerializeField] float travelSeconds = 2.2f;
        [Tooltip("How many levels ahead of the live one stand in the distance as scenery. One: " +
                 "the next level alone waits in the sky; the one after it appears as the push " +
                 "towards it begins.")]
        [SerializeField, Range(1, 3)] int lookAhead = 1;

        [Header("Decoration")]
        [Tooltip("Blocks the small floating rocks beside and below each level are built from: " +
                 "grass first, then stone.")]
        [SerializeField] GameObject[] decorBlocks;
        [Tooltip("Props for those rocks, any of: a small tree, a bush, a vine, a flower.")]
        [SerializeField] GameObject[] decorProps;
        [Tooltip("Rocks per level. Two is deliberately sparse - they are there so the sky is not " +
                 "empty, not to compete with the next level.")]
        [SerializeField, Range(0, 4)] int decorPerLevel = 2;

        [Header("Timing")]
        [Tooltip("How long the celebration runs before the camera sets off for the next level.")]
        [SerializeField] float winCelebrateTime = 0.9f;
        [Tooltip("Death is instant but the reset is not, so the player sees what killed them.")]
        [SerializeField] float failRestartDelay = 0.75f;
        [Tooltip("Restart delay when the ball fell out of the world instead. There is nothing on " +
                 "screen to look at, so the pause that lets a death pop read is just a wait.")]
        [SerializeField] float fallRestartDelay = 0.2f;
        [Tooltip("Realtime seconds of slow motion on the frame of an on-screen death. Zero disables.")]
        [SerializeField] float hitStopSeconds = 0.09f;
        [SerializeField, Range(0.02f, 1f)] float hitStopScale = 0.12f;

        LevelDefinition current;
        int index;
        LevelState state = LevelState.Menu;
        Coroutine pending;
        int keysCollected;
        int keysRequired;
        bool travelling;

        Transform stage;                 // static parent for the frozen level and the stand-ins ahead
        GameObject previous;             // the level just left, frozen into scenery for the journey
        readonly List<GameObject> ahead = new List<GameObject>();   // the levels after index, as scenery, nearest first
        GameObject menuIsland;           // the menu only: the level about to be played, as scenery
        readonly Dictionary<int, GameObject> decor = new Dictionary<int, GameObject>();   // floating rocks, by level index

        public event Action<LevelDefinition> OnLevelLoaded;
        public event Action<LevelDefinition> OnLevelWon;
        public event Action<LevelDefinition> OnLevelFailed;
        /// <summary>collected, required</summary>
        public event Action<int, int> OnKeysChanged;
        public event Action<LevelState> OnStateChanged;

        /// <summary>
        /// Set by the UI. Called when the camera has arrived at the next level with (levelIndex,
        /// resume): show an interstitial if one is due, then call resume - which spawns the player
        /// and hands control back. Left null, the level starts the moment the camera settles.
        /// </summary>
        public Action<int, Action> ArrivalGate;

        public LevelDefinition Current => current;
        public int CurrentIndex => index;
        /// <summary>Deaths on the current level since it was first entered. Drives the skip offer and "flawless".</summary>
        public int FailsOnLevel => failsOnLevel;
        int failsOnLevel;
        public int LevelCount => levels != null ? levels.Length : 0;
        public LevelState State => state;
        public bool IsPlaying => state == LevelState.Playing;
        /// <summary>True from the moment the camera sets off for the next level until control returns.</summary>
        public bool IsTravelling => travelling;
        public int KeysCollected => keysCollected;
        public int KeysRequired => keysRequired;
        public bool DoorUnlocked => keysCollected >= keysRequired;

        /// <summary>World position of the active level's pivot. Fall checks measure from here, not from the origin.</summary>
        public Vector3 Pivot => levelParent ? levelParent.position : Vector3.zero;
        public static Vector3 PivotOrOrigin => Instance ? Instance.Pivot : Vector3.zero;

        /// <summary>
        /// Where level <paramref name="i"/> stands in the world: deeper, lower and a little to
        /// one side of the one before, so from any level's camera the next two recede into the
        /// sky above it. Level 1 is at the origin.
        /// </summary>
        public Vector3 SlotFor(int i)
        {
            int k = ((i % 3) + 3) % 3;
            float x = k == 1 ? slotWander : k == 2 ? -slotWander : 0f;
            return new Vector3(x, -i * slotDrop, i * slotDepth);
        }

        void Awake()
        {
            Instance = this;
            if (!rotator) rotator = FindFirstObjectByType<WorldRotator>();
            if (!player) player = FindFirstObjectByType<PlayerBody>();
            if (!cameraRig) cameraRig = FindFirstObjectByType<PlaneCameraRig>();
            if (!sky) sky = FindFirstObjectByType<SkyTheme>();
            if (!levelParent && rotator) levelParent = rotator.WorldRoot;
            stage = new GameObject("WorldStage").transform;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (stage) Destroy(stage.gameObject);
        }

        void OnEnable()
        {
            if (player) player.OnDied += HandlePlayerDied;
        }

        void OnDisable()
        {
            if (player) player.OnDied -= HandlePlayerDied;
            // Time.timeScale is global and outlives this object. Going away mid hit-stop (a scene
            // reload, the test harness between tests) must not leave the world at 12 % speed.
            CancelPending();
        }

        void Start()
        {
            // The scene opens on the menu; UiRoot decides when to actually start a level. The
            // menu looks at the level the player will play next, standing in ITS slot with the
            // two after it in the distance, so PLAY is a zoom into the same view rather than a
            // cut to somewhere else.
            ShowMenuWorld();
        }

        /// <summary>The menu world: the level about to be played, in its slot, under the wide menu framing.</summary>
        void ShowMenuWorld()
        {
            ClearEditorPreview();
            if (current) { Destroy(current.gameObject); current = null; }
            ClearScenery();
            DynamicRegistry.Prune();
            if (player) player.gameObject.SetActive(false);

            index = LevelCount > 0 ? Mathf.Clamp(GameProgress.UnlockedIndex, 0, LevelCount - 1) : 0;
            Vector3 slot = SlotFor(index);
            PlaceRoot(slot);
            if (rotator) { rotator.CancelDrive(); rotator.IdleSway = true; rotator.RotationAllowed = true; }
            if (cameraRig) cameraRig.SnapTo(slot, menuViewExtents);
            if (sky) sky.Apply(sky.ChapterFor(index, LevelCount));

            // Something to look at behind the title: the level they will play next, as scenery.
            // The two beyond it are NOT stood up here - from the menu framing they land exactly
            // behind the logo and read as clutter around the letters. They appear on PLAY, while
            // the menu fades.
            if (LevelCount > 0) { menuIsland = BuildScenery(index, slot); EnsureDecor(index); }
            SetState(LevelState.Menu);
        }

        /// <summary>
        /// The scene ships with a level already placed under the world root so opening it in the
        /// editor shows the game rather than an empty void, and so the main menu has something
        /// behind it. Thrown away the moment a real level is loaded.
        /// </summary>
        void ClearEditorPreview()
        {
            if (!levelParent) return;
            for (int i = levelParent.childCount - 1; i >= 0; i--)
            {
                var child = levelParent.GetChild(i);
                if (child.GetComponent<LevelDefinition>()) Destroy(child.gameObject);
            }
        }

        // -------------------------------------------------------------------- level loading --
        /// <summary>A cut, not a glide: PLAY, restart, level select, and the tests.</summary>
        public void LoadLevel(int i)
        {
            if (levels == null || levels.Length == 0) return;
            CancelPending();

            int next = Mathf.Clamp(i, 0, levels.Length - 1);
            if (next != index || current == null) failsOnLevel = 0;   // a restart keeps the count
            index = next;

            // Clears the previous level, the editor preview island and the scenery copies, which
            // are all the same kind of thing as far as the world is concerned.
            ClearEditorPreview();
            if (current) Destroy(current.gameObject);
            current = null;
            bool sameView = cameraRig && cameraRig.Focus == SlotFor(index);
            ClearScenery();
            DynamicRegistry.Prune();

            PlaceRoot(SlotFor(index));
            current = SpawnLevel(index);

            // Framed before the player spawns so the first frame is already composed. From the
            // menu the camera is already looking at this slot, so it eases in to the level's
            // framing rather than cutting; a restart is already AT the level's framing and the
            // camera simply stays put; anywhere else it stands straight at the level's framing.
            // (No settling zoom: it read as a pop on every load and restart.)
            if (cameraRig)
            {
                bool sameFraming = sameView && (cameraRig.Extents - current.viewExtents).sqrMagnitude < 1e-4f;
                if (sameFraming || !sameView) cameraRig.SnapTo(SlotFor(index), current.viewExtents);
                else cameraRig.TravelTo(SlotFor(index), current.viewExtents, 0.8f);
            }
            if (sky) sky.Apply(sky.ChapterFor(index, LevelCount));

            if (player) player.Spawn(current.WorldSpawnPoint);
            SetState(LevelState.Playing);
            OnLevelLoaded?.Invoke(current);

            TopUpAhead();
            PruneDecor(index);
        }

        /// <summary>
        /// Stand levels in their slots as scenery until <see cref="lookAhead"/> of them wait beyond
        /// the live one, each with its floating rocks.
        /// </summary>
        void TopUpAhead()
        {
            EnsureDecor(index);
            while (ahead.Count < lookAhead)
            {
                int i = index + ahead.Count + 1;
                var go = BuildScenery(i, SlotFor(i));
                if (!go) break;                       // past the last level
                ahead.Add(go);
                EnsureDecor(i);
            }
        }

        public void Restart() => LoadLevel(index);

        /// <summary>Glide to the next level (or back to the menu after the last one). A no-op mid-glide.</summary>
        public void Next()
        {
            if (travelling) return;
            if (index + 1 < LevelCount)
            {
                CancelPending();
                pending = StartCoroutine(TravelRoutine());
            }
            else ReturnToMenu();   // finished the slice; back to the menu rather than a dead end
        }

        public void ReturnToMenu()
        {
            CancelPending();
            ShowMenuWorld();
        }

        void PlaceRoot(Vector3 slot)
        {
            if (!levelParent) return;
            levelParent.position = slot;
            // BindLevel / ApplyImmediate pushes the kinematic body along and syncs PhysX.
        }

        LevelDefinition SpawnLevel(int i)
        {
            var lvl = Instantiate(levels[i], levelParent);
            lvl.transform.localPosition = Vector3.zero;
            lvl.transform.localRotation = Quaternion.identity;
            lvl.transform.localScale = Vector3.one;
            lvl.name = levels[i].name;
            lvl.EnsureWiring();

            // Order matters - see the class comment.
            if (rotator)
            {
                rotator.IdleSway = false;
                rotator.BindLevel(lvl.startAngle, lvl.allowRotation, lvl.angleLimit);
            }

            keysRequired = Mathf.Max(0, lvl.requiredKeys);
            keysCollected = 0;
            OnKeysChanged?.Invoke(keysCollected, keysRequired);
            return lvl;
        }

        // ----------------------------------------------------------------- the journey ------
        IEnumerator TravelRoutine()
        {
            travelling = true;
            if (current && current.exit) current.exit.Flare();
            if (rotator) { rotator.CancelDrive(); rotator.RotationAllowed = false; }

            // 1. The finished level becomes scenery where it stands, for the camera to sail over.
            if (previous) Destroy(previous);
            previous = null;
            if (current)
            {
                previous = current.gameObject;
                FreezeIntoScenery(current);
                current = null;
            }
            DynamicRegistry.Prune();

            // 2. The stand-in ahead gives way to the real level in the same slot - same prefab,
            //    same pose, so nothing visibly changes but the portal's light coming on. The
            //    level beyond it stays where it is and is now simply "next".
            if (ahead.Count > 0)
            {
                if (ahead[0]) Destroy(ahead[0]);
                ahead.RemoveAt(0);
            }
            index++;
            failsOnLevel = 0;
            PlaceRoot(SlotFor(index));
            current = SpawnLevel(index);
            if (rotator) rotator.RotationAllowed = false;             // not until the camera has settled
            if (sky) sky.Apply(sky.ChapterFor(index, LevelCount));
            // The new far level stands up NOW, tiny and fogged at the top of the frame, so it is
            // simply there as the camera approaches rather than popping in on arrival.
            TopUpAhead();

            // 3. The push forward.
            if (cameraRig) cameraRig.TravelTo(SlotFor(index), current.viewExtents, travelSeconds);
            float guard = travelSeconds + 2f;
            while (cameraRig && cameraRig.IsTravelling && guard > 0f)
            {
                guard -= Time.deltaTime;
                yield return null;
            }

            // 4. Arrival. The UI may hold the door for an interstitial; either way Release lets go.
            bool released = false;
            void Release()
            {
                if (released) return;
                released = true;
                Arrive();
            }
            if (ArrivalGate != null) ArrivalGate(index, Release);
            else Release();
            while (!released) yield return null;

            pending = null;
            travelling = false;
        }

        void Arrive()
        {
            if (!current) { travelling = false; return; }
            // Nothing here touches the camera: the push ended in the standing framing, and the
            // first frame of play must be that same frame.
            if (rotator) rotator.RotationAllowed = current.allowRotation;
            if (player) player.Spawn(current.WorldSpawnPoint);
            SetState(LevelState.Playing);
            OnLevelLoaded?.Invoke(current);

            // The finished level and its rocks are behind the lens now.
            if (previous) Destroy(previous);
            previous = null;
            PruneDecor(index);
        }

        // ---------------------------------------------------------------------- scenery -----
        /// <summary>
        /// A level as pure scenery: the prefab, standing in a slot, with every collider, body,
        /// behaviour, light, particle and sound removed. What is left is meshes on transforms,
        /// which batch with the live level's and cost a phone nothing it was not already paying.
        /// </summary>
        GameObject BuildScenery(int i, Vector3 at)
        {
            if (levels == null || i < 0 || i >= levels.Length || !stage) return null;
            var def = Instantiate(levels[i], stage);
            def.transform.SetPositionAndRotation(at, Quaternion.AngleAxis(levels[i].startAngle, Vector3.forward));
            def.transform.localScale = Vector3.one;
            def.name = levels[i].name + " [scenery]";
            // Platforms detach themselves in Awake and would shuttle about in the live level's
            // frame; they go before the strip, which then cannot see them.
            MovingPlatform.DestroyOwnedBy(def);
            var go = def.gameObject;
            StripToVisual(go);
            DynamicRegistry.Prune();
            return go;
        }

        /// <summary>The finished level stays exactly where and how it is, but stops being a level.</summary>
        void FreezeIntoScenery(LevelDefinition lvl)
        {
            lvl.transform.SetParent(stage, true);
            lvl.name = lvl.name + " [scenery]";
            MovingPlatform.DestroyOwnedBy(lvl);
            StripToVisual(lvl.gameObject);
        }

        void ClearScenery()
        {
            if (previous) Destroy(previous);
            previous = null;
            if (menuIsland) Destroy(menuIsland);
            menuIsland = null;
            foreach (var go in ahead) if (go) Destroy(go);
            ahead.Clear();
            foreach (var go in decor.Values) if (go) Destroy(go);
            decor.Clear();
        }

        // ------------------------------------------------------------------- decoration -----
        /// <summary>
        /// The small floating rocks that keep the sky from being empty: two per level, standing
        /// in the world BESIDE AND BELOW the level (never in the sky above it, which belongs to
        /// the next level), so from the lens they sit low at the sides of the frame at about
        /// two-thirds size and drift past as the camera pushes on. Built from the real block and
        /// prop prefabs, stripped like scenery, with a slow float. Deterministic per level, so a
        /// restart shows the same rocks.
        /// </summary>
        void EnsureDecor(int i)
        {
            if (decor.ContainsKey(i) || decorPerLevel <= 0 || !stage) return;
            if (decorBlocks == null || decorBlocks.Length < 2 || !decorBlocks[0] || !decorBlocks[1]) return;

            var group = new GameObject($"Decor {i + 1}").transform;
            group.SetParent(stage, false);
            group.position = SlotFor(i);
            decor[i] = group.gameObject;

            var rnd = new System.Random(1009 * (i + 1));
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int k = 0; k < decorPerLevel; k++)
            {
                // Alternate sides; the second rock stands deeper and lower. Both land at about a
                // fifth of the frame height from the bottom, well under the island's tip, and
                // inside the frame's edges - and from the level before, they are mostly hidden
                // behind that level's island rather than floating loose in its sky.
                bool right = ((k + i) & 1) == 0;
                float x = (right ? 1f : -1f) * R(5.4f, 6.6f);
                float y = -R(24f, 25.5f) - k * 1.5f;
                float z = R(36f, 40f) + k * 4f;
                BuildRock(group, new Vector3(x, y, z), rnd);
            }
        }

        void BuildRock(Transform parent, Vector3 localPos, System.Random rnd)
        {
            var rock = new GameObject("Rock").transform;
            rock.SetParent(parent, false);
            rock.localPosition = localPos;
            rock.localRotation = Quaternion.Euler(0f, 0f, ((float)rnd.NextDouble() - 0.5f) * 10f);
            rock.localScale = Vector3.one * (0.75f + (float)rnd.NextDouble() * 0.25f);

            // An inverted pyramid: a grass-capped top row of two or three, stone beneath.
            int w = 2 + rnd.Next(2);
            int rows = w == 3 ? 2 : 1 + rnd.Next(2);
            for (int r = 0; r < rows; r++)
            {
                int count = Mathf.Max(1, w - r);
                for (int c = 0; c < count; c++)
                {
                    var src = r == 0 ? decorBlocks[0] : decorBlocks[1];
                    var b = Instantiate(src, rock);
                    b.transform.localPosition = new Vector3(c - (count - 1) * 0.5f, -r, 0f);
                    b.transform.localRotation = Quaternion.identity;
                    b.transform.localScale = Vector3.one;
                    if (r == 0)
                    {
                        var fringe = b.transform.Find("Fringe"); if (fringe) fringe.gameObject.SetActive(true);
                        var tufts = b.transform.Find("Tufts"); if (tufts) tufts.gameObject.SetActive(rnd.NextDouble() < 0.6);
                    }
                }
            }

            // One prop at most: a small tree or a bush on top, or a vine down a side.
            if (decorProps != null && decorProps.Length > 0 && rnd.NextDouble() < 0.75)
            {
                var src = decorProps[rnd.Next(decorProps.Length)];
                if (src)
                {
                    var p = Instantiate(src, rock);
                    bool vine = src.name.Contains("Vine");
                    int side = rnd.NextDouble() < 0.5 ? -1 : 1;
                    p.transform.localPosition = vine
                        ? new Vector3(side * ((w - 1) * 0.5f + 0.56f), -0.05f, -0.2f)
                        : new Vector3(side * (w - 1) * 0.25f, 0.5f, -0.1f);
                    p.transform.localRotation = Quaternion.identity;
                }
            }

            StripToVisual(rock.gameObject);
            rock.gameObject.AddComponent<SkyBob>();
        }

        /// <summary>Rocks of levels before <paramref name="firstKept"/> are behind the lens and go.</summary>
        void PruneDecor(int firstKept)
        {
            List<int> gone = null;
            foreach (var kv in decor)
                if (kv.Key < firstKept) (gone ??= new List<int>()).Add(kv.Key);
            if (gone == null) return;
            foreach (int i in gone) { if (decor[i]) Destroy(decor[i]); decor.Remove(i); }
        }

        /// <summary>
        /// Remove everything but the renderable from a hierarchy, immediately. Behaviours go in
        /// dependency order - anything another present component [RequireComponent]s waits for the
        /// next pass - so Unity never refuses a removal. Particle systems take their whole object.
        /// </summary>
        static void StripToVisual(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps) DestroyImmediate(ps.gameObject);
            foreach (var tr in root.GetComponentsInChildren<TrailRenderer>(true)) DestroyImmediate(tr);
            foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);

            var required = new HashSet<Type>();
            for (int pass = 0; pass < 8; pass++)
            {
                var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                if (behaviours.Length == 0) break;
                required.Clear();
                foreach (var b in behaviours)
                {
                    if (!b) continue;
                    foreach (var attr in b.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    {
                        var rc = (RequireComponent)attr;
                        if (rc.m_Type0 != null) required.Add(rc.m_Type0);
                        if (rc.m_Type1 != null) required.Add(rc.m_Type1);
                        if (rc.m_Type2 != null) required.Add(rc.m_Type2);
                    }
                }
                bool removed = false;
                foreach (var b in behaviours)
                {
                    if (!b) continue;
                    bool isRequired = false;
                    foreach (var t in required) if (t.IsAssignableFrom(b.GetType())) { isRequired = true; break; }
                    if (isRequired) continue;
                    DestroyImmediate(b);
                    removed = true;
                }
                if (!removed) break;
            }

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var l in root.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
        }

        // ---------------------------------------------------------------------- key pickups --
        /// <summary>Called by Collectible when the player touches it.</summary>
        public void CollectKey()
        {
            if (state != LevelState.Playing) return;
            keysCollected++;
            OnKeysChanged?.Invoke(keysCollected, keysRequired);
        }

        // -------------------------------------------------------------------- win / fail -----
        /// <summary>Called by ExitPortal once the player has actually entered an unlocked door.</summary>
        public void ReportWin()
        {
            if (state != LevelState.Playing) return;
            state = LevelState.Won;
            OnStateChanged?.Invoke(state);

            // Drawn into the doorway rather than frozen on the spot - a ball that just stops at
            // the door reads as the game hanging.
            if (player)
                player.Celebrate(current && current.exit ? current.exit.MouthPosition
                                                         : player.transform.position);
            if (rotator) { rotator.CancelDrive(); rotator.RotationAllowed = false; }

            GameProgress.ReportCleared(index);
            OnLevelWon?.Invoke(current);

            pending = StartCoroutine(WinRoutine());
        }

        /// <summary>Called by Hazard, or by the player falling out of the world.</summary>
        public void ReportFail()
        {
            if (state != LevelState.Playing) return;
            state = LevelState.Failed;
            failsOnLevel++;
            OnStateChanged?.Invoke(state);

            if (player) player.Die();          // the pop; Kill() has already fired if needed
            if (rotator) rotator.CancelDrive();
            OnLevelFailed?.Invoke(current);

            pending = StartCoroutine(FailRoutine());
        }

        void HandlePlayerDied() => ReportFail();

        IEnumerator WinRoutine()
        {
            yield return new WaitForSeconds(winCelebrateTime);
            pending = null;
            Next();                                   // the glide to the next level
        }

        bool hitStopActive;

        /// <summary>
        /// Stop whatever win/fail routine is in flight. If that routine was mid hit-stop, put the
        /// clock back: a restart tapped within those 90 ms used to leave the entire game running
        /// at 12 % speed, which the test suite found by crawling into a timeout.
        /// </summary>
        void CancelPending()
        {
            if (pending != null) { StopCoroutine(pending); pending = null; }
            if (hitStopActive) { Time.timeScale = 1f; hitStopActive = false; }
            travelling = false;
        }

        IEnumerator FailRoutine()
        {
            bool fell = player && player.Fell;
            if (!fell && hitStopSeconds > 0f)
            {
                // A blink of slow motion on the frame of death - the classic hit-stop. Realtime, so
                // it is the same length whatever the time scale is doing. Not for falls: there is
                // nothing on screen to freeze.
                hitStopActive = true;
                Time.timeScale = hitStopScale;
                yield return new WaitForSecondsRealtime(hitStopSeconds);
                if (hitStopActive && Mathf.Approximately(Time.timeScale, hitStopScale)) Time.timeScale = 1f;
                hitStopActive = false;
            }
            yield return new WaitForSeconds(fell ? fallRestartDelay : failRestartDelay);
            pending = null;
            Restart();
        }

        void SetState(LevelState s)
        {
            if (state == s) return;
            state = s;
            OnStateChanged?.Invoke(state);
        }

#if UNITY_EDITOR
        /// <summary>Used by the build tooling to fill the list without touching the scene by hand.</summary>
        public void EditorSetLevels(LevelDefinition[] defs) { levels = defs; }
#endif
    }
}
