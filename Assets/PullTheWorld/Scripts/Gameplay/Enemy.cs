using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A hostile rock. It obeys gravity exactly like a boulder - it rolls when you tilt - and it
    /// kills the player on contact. Two things make it an enemy rather than a hazard that moves:
    ///
    ///  * It CHASES, weakly. A small acceleration towards the player along the floor means you
    ///    cannot simply wait it out; downhill it comes fast, uphill it crawls. The force is small
    ///    enough that a tilt always wins, so gravity stays the verb.
    ///  * It can be KILLED: bowled over by a heavy prop arriving at speed (a rock is the intended
    ///    tool), or rolled into spikes or fire, which Hazard handles.
    ///
    /// Same explicit-distance kill as Hazard, for the same compound-collider reason. The crush
    /// check lives on the ROCK (DynamicProp.OnCollisionEnter), not here, because the dynamic body
    /// is the one whose collision callback reliably fires with the right other collider.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(DynamicProp))]
    public class Enemy : MonoBehaviour
    {
        [Header("Threat")]
        [Tooltip("Added to the player radius. Tight - an unfair enemy hitbox reads as cheating.")]
        [SerializeField] float killRadius = 0.40f;

        [Header("Chase")]
        [Tooltip("Acceleration towards the player along the floor, in m/s^2. Small on purpose.")]
        [SerializeField] float chaseAccel = 2.4f;
        [Tooltip("Beyond this it just sits and rolls with gravity.")]
        [SerializeField] float chaseRange = 6.5f;

        [Header("Dying")]
        [Tooltip("Relative impact speed from a heavy prop that kills it.")]
        [SerializeField] float crushSpeed = 3.0f;
        [Tooltip("Minimum mass of the thing hitting it. The player is 1.0 and must NOT count.")]
        [SerializeField] float crushMass = 1.1f;
        [SerializeField] float fallRadius = 26f;

        [Header("References")]
        [SerializeField] Transform visual;
        [SerializeField] ParticleSystem deathVfx;

        Rigidbody rb;
        Collider col;
        bool alive = true;
        float t;

        public bool IsAlive => alive;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            if (!visual) visual = transform.Find("Visual");
            t = Random.value * 10f;
        }

        void FixedUpdate()
        {
            if (!alive) return;

            if (transform.position.sqrMagnitude > fallRadius * fallRadius) { Die(quiet: true); return; }

            var player = PlayerBody.Instance;
            var rotator = WorldRotator.Instance;
            if (!player || !player.IsAlive || !rotator) return;

            // Chase along the level's floor direction only, never against gravity's axis - it is
            // a nudge, not a motor.
            Vector3 toPlayer = player.transform.position - transform.position;
            if (toPlayer.sqrMagnitude > chaseRange * chaseRange) return;
            Vector3 along = rotator.WorldRoot.right;
            float side = Mathf.Sign(Vector3.Dot(toPlayer, along));
            rb.AddForce(along * (side * chaseAccel), ForceMode.Acceleration);
        }

        void Update()
        {
            if (!alive) return;
            t += Time.deltaTime;

            // A slow menacing squash so it never reads as a rock that happens to be purple.
            if (visual)
            {
                float s = 1f + Mathf.Sin(t * 3.1f) * 0.045f;
                visual.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s));
            }

            var lm = LevelManager.Instance;
            var player = PlayerBody.Instance;
            if (lm == null || !lm.IsPlaying || player == null || !player.IsAlive) return;

            float d = Vector3.Distance(player.transform.position, transform.position);
            if (d <= killRadius + player.Radius)
            {
                PtwAudio.Play(PtwSfx.Fail);
                Haptics.Play(HapticKind.Fail);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.5f);
                player.Kill();
            }
        }

        /// <summary>Called by DynamicProp when a prop hits this. Heavy and fast kills it.</summary>
        public void Crush(float relativeSpeed, float mass)
        {
            if (!alive) return;
            if (mass < crushMass || relativeSpeed < crushSpeed) return;
            Die(quiet: false);
        }

        public void Die(bool quiet)
        {
            if (!alive) return;
            alive = false;

            if (!quiet)
            {
                if (deathVfx) { deathVfx.transform.SetParent(null, true); deathVfx.Emit(24); Destroy(deathVfx.gameObject, 2f); }
                PtwAudio.Play(PtwSfx.Smother, 0.9f, 0.75f);
                Haptics.Play(HapticKind.Break);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.45f);
            }

            // Stop being a thing: no collider, no physics, visual gone. Destroyed shortly after so
            // anything still holding a reference this frame does not hit a null.
            if (col) col.enabled = false;
            rb.isKinematic = true;
            if (visual) visual.gameObject.SetActive(false);
            Destroy(gameObject, 0.5f);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.5f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, killRadius);
        }
#endif
    }
}
