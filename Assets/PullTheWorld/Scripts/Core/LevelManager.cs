using System;
using System.Collections;
using UnityEngine;

namespace PullTheWorld
{
    public enum LevelState { Menu, Playing, Won, Failed }

    /// <summary>
    /// Swaps level prefabs in and out under the rotating world root and owns the
    /// win / fail / restart / advance flow. Adding a level is: make a prefab with a
    /// LevelDefinition on it, drop it in the list.
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

        [Header("Timing")]
        [Tooltip("How long the celebration runs before the level-complete panel appears.")]
        [SerializeField] float winCelebrateTime = 0.9f;
        [Tooltip("Death is instant but the reset is not, so the player sees what killed them.")]
        [SerializeField] float failRestartDelay = 0.75f;
        [Tooltip("Restart delay when the ball fell out of the world instead. There is nothing on " +
                 "screen to look at, so the pause that lets a death pop read is just a wait.")]
        [SerializeField] float fallRestartDelay = 0.2f;
        [Tooltip("Realtime seconds of slow motion on the frame of an on-screen death. Zero disables.")]
        [SerializeField] float hitStopSeconds = 0.09f;
        [SerializeField, Range(0.02f, 1f)] float hitStopScale = 0.12f;
        [Tooltip("Off means the level-complete panel waits for a tap instead of auto-advancing.")]
        [SerializeField] bool autoAdvance;

        LevelDefinition current;
        int index;
        LevelState state = LevelState.Menu;
        Coroutine pending;
        int keysCollected;
        int keysRequired;

        public event Action<LevelDefinition> OnLevelLoaded;
        public event Action<LevelDefinition> OnLevelWon;
        public event Action<LevelDefinition> OnLevelFailed;
        /// <summary>collected, required</summary>
        public event Action<int, int> OnKeysChanged;
        public event Action<LevelState> OnStateChanged;

        public LevelDefinition Current => current;
        public int CurrentIndex => index;
        /// <summary>Deaths on the current level since it was first entered. Drives the skip offer and "flawless".</summary>
        public int FailsOnLevel => failsOnLevel;
        int failsOnLevel;
        public int LevelCount => levels != null ? levels.Length : 0;
        public LevelState State => state;
        public bool IsPlaying => state == LevelState.Playing;
        public int KeysCollected => keysCollected;
        public int KeysRequired => keysRequired;
        public bool DoorUnlocked => keysCollected >= keysRequired;

        void Awake()
        {
            Instance = this;
            if (!rotator) rotator = FindFirstObjectByType<WorldRotator>();
            if (!player) player = FindFirstObjectByType<PlayerBody>();
            if (!cameraRig) cameraRig = FindFirstObjectByType<PlaneCameraRig>();
            if (!sky) sky = FindFirstObjectByType<SkyTheme>();
            if (!levelParent && rotator) levelParent = rotator.WorldRoot;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

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
            SetState(LevelState.Menu);
            if (rotator) rotator.IdleSway = true;
            if (cameraRig) cameraRig.FrameExtents(menuViewExtents);
            // The scene opens on the menu; UiRoot decides when to actually start a level.
            //
            // The editor preview level is deliberately NOT cleared here. v1 cleared it on Start
            // because it went straight into gameplay, but v2 opens on a main menu - and a menu
            // floating over an empty void looks broken. Leaving the preview island in place gives
            // the menu a real backdrop for free, and LoadLevel clears it the moment PLAY is
            // pressed.
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
        public void LoadLevel(int i)
        {
            if (levels == null || levels.Length == 0) return;
            CancelPending();

            int next = Mathf.Clamp(i, 0, levels.Length - 1);
            if (next != index || current == null) failsOnLevel = 0;   // a restart keeps the count
            index = next;

            // Clears both the previous level and the editor preview island, which are the same
            // kind of thing as far as the world root is concerned.
            ClearEditorPreview();
            current = null;
            DynamicRegistry.Prune();

            current = Instantiate(levels[index], levelParent);
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;
            current.transform.localScale = Vector3.one;
            current.name = levels[index].name;
            current.EnsureWiring();

            // Order matters - see the class comment.
            if (rotator)
            {
                rotator.IdleSway = false;
                rotator.BindLevel(current.startAngle, current.allowRotation, current.angleLimit);
            }

            // Framed before the player spawns so the first frame is already composed. The kick
            // is a small zoom that settles over the first half-second: the island arrives.
            if (cameraRig)
            {
                cameraRig.FrameExtents(current.viewExtents);
                cameraRig.Kick(1.07f);
            }
            if (sky) sky.Apply(sky.ChapterFor(index, LevelCount));

            keysRequired = Mathf.Max(0, current.requiredKeys);
            keysCollected = 0;
            OnKeysChanged?.Invoke(keysCollected, keysRequired);

            if (player) player.Spawn(current.WorldSpawnPoint);

            SetState(LevelState.Playing);
            OnLevelLoaded?.Invoke(current);
        }

        public void Restart() => LoadLevel(index);

        public void Next()
        {
            if (index + 1 < LevelCount) LoadLevel(index + 1);
            else ReturnToMenu();   // finished the slice; back to the menu rather than a dead end
        }

        public void ReturnToMenu()
        {
            CancelPending();
            if (current) { Destroy(current.gameObject); current = null; }
            DynamicRegistry.Prune();
            if (player) player.gameObject.SetActive(false);
            if (rotator) { rotator.CancelDrive(); rotator.IdleSway = true; }
            if (cameraRig) cameraRig.FrameExtents(menuViewExtents);
            if (sky) sky.Apply(0);
            SetState(LevelState.Menu);
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

            if (cameraRig) cameraRig.Kick(0.96f);        // lean in as the ball is drawn into the door
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
            if (autoAdvance) Next();
            // Otherwise UiRoot has shown the level-complete panel and waits for a tap.
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
