using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// What the onboarding should teach on a level, if anything. One enum rather than a pile of
    /// bools so a level can only ever be teaching one thing at a time - which is also the design
    /// rule: introduce exactly one idea per level.
    /// </summary>
    public enum TeachHint
    {
        None = 0,
        /// <summary>Animated finger arcing around the island. Level 1 only.</summary>
        Rotate,
        /// <summary>Pulse the hazard so "do not touch that" lands before it kills you.</summary>
        AvoidHazard,
        /// <summary>Pulse the loose rock so it reads as a tool rather than scenery.</summary>
        RockIsATool,
        /// <summary>Pulse plate then gate, so the causal link is visible before it is needed.</summary>
        PlateOpensGate,
        /// <summary>Pulse the key then the door.</summary>
        CollectKey,
        /// <summary>Pulse the moving platform so its rhythm gets watched before it is ridden.</summary>
        Timing,
    }

    /// <summary>
    /// Sits on the root of every level prefab and is the whole authoring contract for a level:
    /// drop blocks and props under the prefab, mark the spawn point, set the hint, done.
    ///
    /// v1 stored a start focus and drag bounds because the world translated. v2 does not translate
    /// at all, so all of that is gone; what a level needs now is where the player drops in, how far
    /// the world may be turned, and what the level is trying to teach.
    /// </summary>
    public class LevelDefinition : MonoBehaviour
    {
        [Header("Identity")]
        public int number = 1;
        public string title = "Tip It Over";

        [Header("Start pose")]
        [Tooltip("Degrees the level starts rotated. Non-zero is a cheap way to make a level read " +
                 "as already precarious.")]
        public float startAngle;

        [Tooltip("Where the player drops in, in this prefab local space. Marked by a child named " +
                 "Spawn if there is one, otherwise this value is used directly.")]
        public Vector3 spawnPoint = new Vector3(0f, 2f, 0f);

        [Header("Rules")]
        [Tooltip("Off only if a level is meant to be watched rather than played.")]
        public bool allowRotation = true;

        [Tooltip("0 = the world can be spun freely. Otherwise the world cannot be turned further " +
                 "than this many degrees from its start, which is how the first levels stay " +
                 "readable instead of being solved by a panicked full spin.")]
        public float angleLimit;

        [Tooltip("Keys that must be collected before the door will open. 0 = the door is always " +
                 "open. Anything higher requires that many Collectibles in the level.")]
        public int requiredKeys;

        [Header("Framing")]
        [Tooltip("World units the camera must show for this level, worked out by the generator as " +
                 "the worst case over the level's whole rotation range - not just its upright " +
                 "bounding box, because a tall island becomes a wide one when it is turned. " +
                 "Framing per level is what makes every island fill the screen by the same amount.")]
        public Vector2 viewExtents = new Vector2(12f, 12f);

        [Header("Onboarding")]
        [Tooltip("Taught with a visual hint, no text, and dismissed as soon as the player does it.")]
        public TeachHint teach = TeachHint.None;

        [Header("Wiring (auto-filled if left empty)")]
        public ExitPortal exit;
        public Transform spawnMarker;

        /// <summary>World-space spawn position, honouring the level current rotation.</summary>
        public Vector3 WorldSpawnPoint =>
            spawnMarker ? spawnMarker.position : transform.TransformPoint(spawnPoint);

        void Reset() => EnsureWiring();

        public void EnsureWiring()
        {
            if (!exit) exit = GetComponentInChildren<ExitPortal>(true);
            if (!spawnMarker)
            {
                var t = transform.Find("Spawn");
                if (t) spawnMarker = t;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(WorldSpawnPoint, 0.35f);
        }
#endif
    }
}
