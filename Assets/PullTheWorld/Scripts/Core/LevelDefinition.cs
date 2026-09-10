using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Sits on the root of every level prefab. This is the whole authoring contract for a level:
    /// drop blocks and props under the prefab, set the start pose and the hint, done.
    /// </summary>
    public class LevelDefinition : MonoBehaviour
    {
        [Header("Identity")]
        public int number = 1;
        public string title = "Move the World";
        [Tooltip("Shown once, low on screen, then fades. Keep it to a few words.")]
        [TextArea(2, 3)] public string hint = "Drag anywhere to pull the world";

        [Header("Start pose")]
        [Tooltip("The point of THIS prefab that starts sitting on the player anchor.")]
        public Vector3 startFocus = Vector3.zero;
        [Tooltip("Degrees of world spin the level starts at.")]
        public float startSpin;

        [Header("Rules")]
        [Tooltip("Level 1 and 2 teach dragging only, so rotation is locked off there.")]
        public bool allowRotation = true;

        [Tooltip("Show the two-finger twist tutorial. Set this on the FIRST level where rotation " +
                 "becomes available, otherwise the player has no way to discover the gesture.")]
        public bool teachRotation;

        [Header("Drag limits")]
        [Tooltip("Limits how far the world can be pulled, in this prefab local space.")]
        public bool useBounds = true;
        public Vector3 boundsCenter = Vector3.zero;
        public Vector3 boundsSize = new Vector3(14f, 1f, 14f);

        [Header("Wiring (auto-filled if left empty)")]
        public ExitPortal exit;

        public Bounds FocusBounds => new Bounds(boundsCenter, boundsSize);

        void Reset()
        {
            exit = GetComponentInChildren<ExitPortal>();
        }

        public void EnsureWiring()
        {
            if (!exit) exit = GetComponentInChildren<ExitPortal>(true);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireCube(boundsCenter, boundsSize);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(startFocus, 0.35f);
        }
#endif
    }
}
