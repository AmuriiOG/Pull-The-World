using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A spring pad. Anything that lands on it - the ball, a rock, an enemy - is thrown along the
    /// pad's up axis, which is the LEVEL's up, so a tilted level throws sideways. That is the whole
    /// toy: aim the throw by tilting, then let go.
    ///
    /// Detection is an explicit zone check, like PressurePlate and Hazard, for the same reason:
    /// the pad is a child collider of the level's compound body and collision callbacks on it are
    /// unreliable. The launch sets the body's velocity along the pad normal and leaves the rest,
    /// so a rolling approach turns into an arc rather than a pop straight up. A short per-body
    /// cooldown stops the zone re-firing while the body is still inside it.
    ///
    /// The player's own physics settings are untouched: this is a level object acting on the
    /// ball, exactly like the portal that draws it in.
    /// </summary>
    public class BouncePad : MonoBehaviour
    {
        [Header("Throw")]
        [Tooltip("Speed along the pad's up axis after a bounce. 11 m/s under 24 m/s^2 gravity is a " +
                 "2.5 m throw - clears a two-block ledge, not a three-block one.")]
        [SerializeField] float launchSpeed = 11f;
        [Tooltip("Bodies already moving away from the pad faster than this are left alone.")]
        [SerializeField] float maxApproachSpeed = 1.5f;
        [SerializeField] float cooldown = 0.35f;

        [Header("Zone (pad-local)")]
        [SerializeField] Vector3 zoneCenter = new Vector3(0f, 0.45f, 0f);
        [SerializeField] Vector3 zoneHalf = new Vector3(0.42f, 0.42f, 0.5f);

        [Header("References")]
        [SerializeField] Punch visualPunch;
        [SerializeField] ParticleSystem puffVfx;

        readonly Dictionary<Rigidbody, float> lastLaunch = new Dictionary<Rigidbody, float>();

        public float LaunchSpeed => launchSpeed;

        void Awake()
        {
            if (!visualPunch) visualPunch = GetComponentInChildren<Punch>();
        }

        void FixedUpdate()
        {
            var player = PlayerBody.Instance;
            if (player && player.IsAlive && !player.IsAnimatingEnding && player.Body && !player.Body.isKinematic)
                Consider(player.Body);

            var bodies = DynamicRegistry.Bodies;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var rb = bodies[i];
                if (!rb || rb.isKinematic) continue;
                if (player && rb == player.Body) continue;       // already handled
                Consider(rb);
            }
        }

        void Consider(Rigidbody rb)
        {
            Vector3 local = transform.InverseTransformPoint(rb.position) - zoneCenter;
            if (Mathf.Abs(local.x) > zoneHalf.x || Mathf.Abs(local.y) > zoneHalf.y || Mathf.Abs(local.z) > zoneHalf.z)
                return;

            Vector3 up = transform.up;
            if (Vector3.Dot(rb.linearVelocity, up) > maxApproachSpeed) return;

            float now = Time.time;
            if (lastLaunch.TryGetValue(rb, out float t) && now - t < cooldown) return;
            lastLaunch[rb] = now;

            Vector3 v = rb.linearVelocity;
            v -= Vector3.Dot(v, up) * up;
            v += up * launchSpeed;
            rb.linearVelocity = v;

            if (visualPunch) visualPunch.Hit(1f);
            if (puffVfx) { puffVfx.transform.position = rb.position; puffVfx.Emit(8); }
            PtwAudio.Play(PtwSfx.Bounce, 0.9f, Random.Range(0.95f, 1.08f));
            Haptics.Play(HapticKind.Impact);
            if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.12f);
        }

        void OnDisable() => lastLaunch.Clear();

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(zoneCenter, zoneHalf * 2f);
        }
#endif
    }
}
