using System;
using System.Collections;
using UnityEngine;

namespace PullTheWorld
{
    public enum LevelState { Playing, Won, Failed }

    /// <summary>
    /// Swaps level prefabs in and out under WorldRoot and owns the win / fail / restart flow.
    /// Adding a level is: make a prefab with a LevelDefinition on it, drop it in the list.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Content")]
        [Tooltip("Played in order. Add a prefab here and it is in the game.")]
        [SerializeField] LevelDefinition[] levels;
        [SerializeField] int startLevel;

        [Header("References")]
        [SerializeField] WorldRig rig;
        [SerializeField] Transform levelParent;

        [Header("Timing")]
        [SerializeField] float winCelebrateTime = 1.35f;
        [SerializeField] float failRestartDelay = 0.85f;
        [SerializeField] bool autoAdvance = true;

        LevelDefinition current;
        int index;
        LevelState state = LevelState.Playing;
        Coroutine pending;

        public event Action<LevelDefinition> OnLevelLoaded;
        public event Action<LevelDefinition> OnLevelWon;
        public event Action<LevelDefinition> OnLevelFailed;

        public LevelDefinition Current => current;
        public int CurrentIndex => index;
        public int LevelCount => levels != null ? levels.Length : 0;
        public LevelState State => state;
        public bool IsPlaying => state == LevelState.Playing;

        void Awake()
        {
            Instance = this;
            if (!rig) rig = FindFirstObjectByType<WorldRig>();
            if (!levelParent && rig) levelParent = rig.WorldRoot;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            ClearEditorPreview();
            if (LevelCount > 0) LoadLevel(Mathf.Clamp(startLevel, 0, LevelCount - 1));
        }

        /// <summary>
        /// The scene ships with Level 1 already placed under WorldRoot so that opening the scene
        /// shows the actual game instead of an empty void. That preview is thrown away the moment
        /// play starts, and the real level is instantiated in its place.
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

        public void LoadLevel(int i)
        {
            if (levels == null || levels.Length == 0) return;
            if (pending != null) { StopCoroutine(pending); pending = null; }

            index = Mathf.Clamp(i, 0, levels.Length - 1);

            if (current) Destroy(current.gameObject);

            current = Instantiate(levels[index], levelParent);
            current.transform.localPosition = Vector3.zero;
            current.transform.localRotation = Quaternion.identity;
            current.transform.localScale = Vector3.one;
            current.name = levels[index].name;
            current.EnsureWiring();

            state = LevelState.Playing;

            if (rig)
            {
                rig.BindLevel(current.startFocus, current.startSpin,
                              current.FocusBounds, current.useBounds, current.allowRotation);
            }

            OnLevelLoaded?.Invoke(current);
        }

        public void Restart() => LoadLevel(index);

        public void Next()
        {
            if (index + 1 < LevelCount) LoadLevel(index + 1);
            else LoadLevel(0); // loop the prototype rather than dead-ending a demo
        }

        /// <summary>Called by ExitPortal when the door has reached the player.</summary>
        public void ReportWin()
        {
            if (state != LevelState.Playing) return;
            state = LevelState.Won;
            OnLevelWon?.Invoke(current);
            if (autoAdvance) pending = StartCoroutine(WinRoutine());
        }

        /// <summary>Called by Hazard when the player has been reached by something nasty.</summary>
        public void ReportFail()
        {
            if (state != LevelState.Playing) return;
            state = LevelState.Failed;
            OnLevelFailed?.Invoke(current);
            pending = StartCoroutine(FailRoutine());
        }

        IEnumerator WinRoutine()
        {
            yield return new WaitForSeconds(winCelebrateTime);
            pending = null;
            Next();
        }

        IEnumerator FailRoutine()
        {
            yield return new WaitForSeconds(failRestartDelay);
            pending = null;
            Restart();
        }

#if UNITY_EDITOR
        /// <summary>Used by the build tooling to populate the list without touching the scene by hand.</summary>
        public void EditorSetLevels(LevelDefinition[] defs) { levels = defs; }
#endif
    }
}
