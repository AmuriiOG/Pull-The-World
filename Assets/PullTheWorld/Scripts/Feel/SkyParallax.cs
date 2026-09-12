using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The one shared shift every <see cref="SkyLayer"/> parallaxes from. Sits on the camera.
    ///
    /// The camera never moves (that is the whole v2 argument - see PlaneCameraRig), so an
    /// orthographic backdrop has no parallax of its own. This fakes a little: as the level tips,
    /// the sky slides a touch the way the orb is about to roll, and as the orb travels away from
    /// the pivot the sky leans the other way, like a camera that would love to follow it but is
    /// bolted down. Near layers take the whole offset, far ridges a tenth of it, so the layers
    /// separate in depth. Everything here is a small TRANSLATION - nothing in the sky ever
    /// rotates, because a rotating backdrop is exactly what would make the island's spin
    /// ambiguous again.
    ///
    /// Static <see cref="Offset"/> so the layers need no wiring; zero whenever this is disabled
    /// or absent, so the layers fall back to being pinned.
    /// </summary>
    [DefaultExecutionOrder(-240)]
    public class SkyParallax : MonoBehaviour
    {
        public static Vector2 Offset { get; private set; }
        /// <summary>
        /// World-space vector by which a layer with parallax 1 lags the lens during a push, set by
        /// PlaneCameraRig every travel frame: the camera's displacement from the point it would be
        /// at had it left later. Zero at rest and at the end of a push, with zero velocity there.
        /// </summary>
        public static Vector3 TravelLagWorld;

        [Tooltip("World units the nearest layer (parallax 1) slides sideways at a quarter turn.")]
        [SerializeField] float tiltShift = 1.1f;
        [Tooltip("World units the nearest layer sinks as the level tips over.")]
        [SerializeField] float tiltSink = 0.30f;
        [Tooltip("Fraction of the orb's distance from the pivot the nearest layer follows, inverted.")]
        [SerializeField, Range(0f, 0.3f)] float follow = 0.06f;
        [Tooltip("Cap on the follow shift, so a long level cannot drag the sky off its ridges.")]
        [SerializeField] float followClamp = 0.8f;
        [Tooltip("Higher is snappier. The sky should lag the island by a beat, like weight.")]
        [SerializeField] float smoothing = 3.2f;

        Vector2 current;

        void OnEnable() { current = Vector2.zero; Offset = Vector2.zero; TravelLagWorld = Vector3.zero; }
        void OnDisable() { Offset = Vector2.zero; TravelLagWorld = Vector3.zero; }

        void Update()
        {
            if (!Application.isPlaying) { Offset = Vector2.zero; return; }

            Vector2 target = Vector2.zero;

            var rot = WorldRotator.Instance;
            if (rot)
            {
                // Positive angle turns the island anti-clockwise on screen: its right side rises,
                // the orb rolls left, and the sky leans right to meet it.
                float a = rot.Angle * Mathf.Deg2Rad;
                target.x += Mathf.Sin(a) * tiltShift;
                target.y -= (1f - Mathf.Cos(a)) * tiltSink;
            }

            var player = PlayerBody.Instance;
            if (player && player.IsAlive)
            {
                // Relative to the live level's pivot: levels stand at very different world
                // heights now, and a world-space read pinned the sky to the clamp on every level
                // past the first.
                Vector3 p = player.transform.position - LevelManager.PivotOrOrigin;
                target.x -= Mathf.Clamp(p.x * follow, -followClamp, followClamp);
                target.y -= Mathf.Clamp(p.y * follow * 0.5f, -followClamp, followClamp);
            }

            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            current = Vector2.Lerp(current, target, k);
            Offset = current;
        }
    }
}
