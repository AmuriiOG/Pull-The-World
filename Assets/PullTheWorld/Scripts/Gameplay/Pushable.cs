using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A dynamic prop the player can only influence indirectly, by re-aiming gravity.
    /// Handles its own impact juice and self-rescues if it falls out of the level so a
    /// mistimed rotation can never soft-lock a puzzle.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Pushable : MonoBehaviour
    {
        [Header("Impact feel")]
        [SerializeField] float minImpactSpeed = 1.6f;
        [SerializeField] float maxImpactSpeed = 9f;
        [SerializeField] float shakeScale = 0.35f;
        [SerializeField] float squashScale = 0.22f;
        [SerializeField] float squashRecover = 11f;
        [SerializeField] float impactCooldown = 0.09f;

        [Header("Player reaction")]
        [Tooltip("Impacts inside this radius make the character flinch.")]
        [SerializeField] float playerNoticeRadius = 3.2f;

        [Header("Safety net")]
        [Tooltip("If it ends up this far below the level origin, put it back.")]
        [SerializeField] float respawnBelow = -18f;
        [SerializeField] bool respawnEnabled = true;

        [Header("Visuals")]
        [SerializeField] Transform visual;
        [SerializeField] ParticleSystem impactVfx;

        Rigidbody rb;
        Vector3 startLocalPos;
        Quaternion startLocalRot;
        Vector3 visualBaseScale = Vector3.one;
        float squash;
        float lastImpactTime = -99f;

        public float Mass => rb ? rb.mass : 1f;
        public Rigidbody Body => rb;
        /// <summary>True while the prop is essentially still - used by puzzle checks and tests.</summary>
        public bool IsResting => rb && rb.linearVelocity.sqrMagnitude < 0.04f &&
                                 rb.angularVelocity.sqrMagnitude < 0.25f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            startLocalPos = transform.localPosition;
            startLocalRot = transform.localRotation;
            if (!visual && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual) visualBaseScale = visual.localScale;
        }

        void Update()
        {
            if (squash > 0.0005f)
            {
                squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-squashRecover * Time.deltaTime));
                if (visual)
                {
                    float s = 1f - squash;
                    float w = 1f / Mathf.Sqrt(Mathf.Max(0.05f, s));
                    visual.localScale = Vector3.Scale(visualBaseScale, new Vector3(w, s, w));
                }
            }

            if (respawnEnabled && transform.localPosition.y < respawnBelow) Respawn();
        }

        void OnCollisionEnter(Collision c)
        {
            if (Time.time - lastImpactTime < impactCooldown) return;

            float speed = c.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;
            lastImpactTime = Time.time;

            float strength = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed);

            squash = Mathf.Min(0.4f, squash + strength * squashScale);

            if (impactVfx)
            {
                var pt = c.GetContact(0);
                impactVfx.transform.position = pt.point;
                impactVfx.transform.rotation = Quaternion.LookRotation(pt.normal);
                impactVfx.Emit(Mathf.RoundToInt(Mathf.Lerp(3f, 14f, strength)));
            }

            var rig = WorldRig.Instance;
            if (rig)
            {
                float dist = Vector3.Distance(transform.position, rig.AnchorPos);
                float nearness = 1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, playerNoticeRadius));
                if (nearness > 0f)
                {
                    rig.AddShake(strength * shakeScale * nearness);
                    var player = FindFirstObjectByType<PlayerAnchor>();
                    if (player) player.React(strength * nearness);
                }
            }

            PtwAudio.Play(PtwSfx.Impact, Mathf.Lerp(0.25f, 1f, strength),
                          Mathf.Lerp(1.15f, 0.85f, strength));
            if (strength > 0.35f) Haptics.Light();
        }

        public void Respawn()
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.localPosition = startLocalPos;
            transform.localRotation = startLocalRot;
            Physics.SyncTransforms();
        }
    }
}
