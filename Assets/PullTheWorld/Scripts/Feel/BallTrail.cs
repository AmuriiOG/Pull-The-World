using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A soft streak behind the ball once it is really moving. Visual only - it reads the body's
    /// speed and never writes to it. Lives on a non-rolling child at the ball's centre, so the
    /// roll does not wobble the trail, and clears itself on a teleport (spawn, respawn) so a
    /// restart never draws a line from where the ball died to where it starts.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class BallTrail : MonoBehaviour
    {
        [SerializeField] PlayerBody player;
        [Tooltip("Speed at which the trail starts to show.")]
        [SerializeField] float minSpeed = 4.5f;
        [Tooltip("Speed at which it is at full width.")]
        [SerializeField] float fullSpeed = 11f;

        TrailRenderer trail;
        Vector3 lastPos;
        bool wasOn;

        void Awake()
        {
            trail = GetComponent<TrailRenderer>();
            if (!player) player = GetComponentInParent<PlayerBody>();
            trail.emitting = false;
            lastPos = transform.position;
        }

        void LateUpdate()
        {
            if (!player) { trail.emitting = false; return; }

            // A jump of more than a few metres in one frame is a teleport, not motion.
            if ((transform.position - lastPos).sqrMagnitude > 16f) trail.Clear();
            lastPos = transform.position;

            float speed = player.Speed;
            bool on = player.IsAlive && !player.IsAnimatingEnding && speed > minSpeed;
            if (!on && wasOn) trail.Clear();
            wasOn = on;

            trail.emitting = on;
            trail.widthMultiplier = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(minSpeed, fullSpeed, speed));
        }
    }
}
