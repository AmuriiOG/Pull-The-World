using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A hostile thing. It obeys gravity exactly like a boulder - it rolls when you tilt - and it
    /// kills the player on contact. It can be KILLED: bowled over by a heavy prop arriving at speed
    /// (a rock is the intended tool), or rolled into spikes or fire, which Hazard handles.
    ///
    /// What makes it scary rather than a purple rock, in order of how much each does:
    ///
    ///  * It NOTICES you. Inside alertRange the eyes flare from a dull ember to a hard pulsing
    ///    glare, the aura swells, it hisses and does a little startle hop. Calm to alert is a
    ///    visible, audible moment, and the player learns the range by being noticed.
    ///  * It LUNGES. Alert, grounded, close and roughly level with you, it coils for a beat and
    ///    springs: a short burst along the floor with a little lift. There is a cooldown, so it is
    ///    a threat you can time rather than a homing missile, and a tilt still always wins.
    ///  * It STARES. The body, face and aura are held world-upright and lean towards you while the
    ///    spike ring rolls with the rigidbody, so it reads as a creature gliding on a saw rather
    ///    than a tumbling ball with a face painted on.
    ///  * It BREATHES. Slow and deep when calm, fast and shallow when hunting; the aura, the eye
    ///    light and the smoke trail follow the same state.
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

        [Header("Noticing you")]
        [Tooltip("Inside this it wakes up: eyes flare, a hiss, a startle hop, and it starts to hunt.")]
        [SerializeField] float alertRange = 5.5f;
        [Tooltip("Outside this it loses interest again. Wider than alertRange so it cannot flicker.")]
        [SerializeField] float loseRange = 7.5f;
        [Tooltip("Vertical hop on noticing, m/s. A flinch that says 'it saw me'.")]
        [SerializeField] float startleHop = 1.7f;

        [Header("Hunting")]
        [Tooltip("Acceleration towards the player along the floor while alert, m/s^2. Still small " +
                 "enough that a tilt always wins - gravity stays the verb.")]
        [SerializeField] float chaseAccel = 3.2f;

        [Header("Lunge")]
        [SerializeField] float lungeRange = 2.5f;
        [Tooltip("How level with the enemy the player must be, in metres along the level's up axis.")]
        [SerializeField] float lungeLevelTolerance = 1.1f;
        [SerializeField] float lungeSpeed = 4.4f;
        [SerializeField] float lungeLift = 1.4f;
        [Tooltip("Seconds it coils before springing. The tell.")]
        [SerializeField] float lungeWindup = 0.18f;
        [SerializeField] float lungeCooldown = 2.0f;

        [Header("Dying")]
        [Tooltip("Relative impact speed from a heavy prop that kills it.")]
        [SerializeField] float crushSpeed = 3.0f;
        [Tooltip("Minimum mass of the thing hitting it. The player is 1.0 and must NOT count.")]
        [SerializeField] float crushMass = 1.1f;
        [SerializeField] float fallRadius = 26f;

        [Header("References")]
        [Tooltip("Body, face and aura. Held world-upright so the glare never rolls.")]
        [SerializeField] Transform visual;
        [Tooltip("The spike ring. Left alone so it rolls with the body.")]
        [SerializeField] Transform spikes;
        [SerializeField] Renderer faceRenderer;
        [SerializeField] Transform aura;
        [SerializeField] Renderer auraRenderer;
        [SerializeField] Light eyeLight;
        [SerializeField] ParticleSystem trail;
        [SerializeField] ParticleSystem deathVfx;

        [Header("Look")]
        [SerializeField] Color eyeColor = new Color(1f, 0.20f, 0.12f);
        [SerializeField] Color auraColor = new Color(1f, 0.10f, 0.18f);
        [Tooltip("Body scale relative to the collider. A little oversize so it carries on a phone.")]
        [SerializeField] float visualScale = 1.12f;

        // ---- screen-wide threat, read by DangerVignette -----------------------------------
        static float threat;
        static int threatFrame;

        /// <summary>0..1: how closely the most dangerous alert enemy is hunting the player. Zero when none is.</summary>
        public static float CurrentThreat => threatFrame >= Time.frameCount - 1 ? threat : 0f;

        static void ReportThreat(float v)
        {
            if (threatFrame != Time.frameCount) { threat = 0f; threatFrame = Time.frameCount; }
            threat = Mathf.Max(threat, v);
        }

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Rigidbody rb;
        Collider col;
        MaterialPropertyBlock faceBlock, auraBlock;
        bool alive = true, alert;
        float t, growlTimer, lungeTimer, windup, flash;
        Vector3 windupDir;

        public bool IsAlive => alive;
        public bool IsAlert => alert;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            if (!visual) visual = transform.Find("Visual");
            faceBlock = new MaterialPropertyBlock();
            auraBlock = new MaterialPropertyBlock();
            t = Random.value * 10f;
        }

        // ------------------------------------------------------------------- hunting --------
        void FixedUpdate()
        {
            if (!alive) return;
            if (transform.position.sqrMagnitude > fallRadius * fallRadius) { Die(quiet: true); return; }

            var player = PlayerBody.Instance;
            var rotator = WorldRotator.Instance;
            if (!player || !rotator) return;
            float dt = Time.fixedDeltaTime;
            lungeTimer -= dt;

            if (!player.IsAlive) { alert = false; windup = 0f; return; }

            Vector3 toPlayer = player.transform.position - transform.position;
            float d = toPlayer.magnitude;
            if (!alert && d < alertRange) Notice();
            else if (alert && d > loseRange) alert = false;
            if (!alert) return;

            // Chase along the level's floor direction only, never against gravity's axis - it is
            // a nudge, not a motor.
            Vector3 along = rotator.WorldRoot.right;
            float side = Mathf.Sign(Vector3.Dot(toPlayer, along));

            if (windup > 0f)
            {
                windup -= dt;
                if (windup <= 0f) Lunge();
                return;
            }

            rb.AddForce(along * (side * chaseAccel), ForceMode.Acceleration);

            float level = Mathf.Abs(Vector3.Dot(toPlayer, rotator.WorldRoot.up));
            if (lungeTimer <= 0f && d < lungeRange && level < lungeLevelTolerance && Grounded())
            {
                windup = lungeWindup;
                windupDir = along * side;
                PtwAudio.Play(PtwSfx.EnemySnarl, 0.9f, Random.Range(0.92f, 1.08f));
            }
        }

        void Notice()
        {
            alert = true;
            growlTimer = 0f;                                 // hiss right now
            flash = 1f;
            if (Grounded()) rb.linearVelocity += Vector3.up * startleHop;
            if (trail) trail.Emit(5);
        }

        void Lunge()
        {
            lungeTimer = lungeCooldown;
            rb.linearVelocity += windupDir * lungeSpeed + Vector3.up * lungeLift;
            flash = 1f;
            Haptics.Play(HapticKind.Impact);
            if (trail) trail.Emit(8);
        }

        bool Grounded()
        {
            float r = col is SphereCollider s ? s.radius * transform.lossyScale.x : 0.3f;
            // A sweep ignores anything it starts inside, i.e. this body's own collider.
            return Physics.SphereCast(transform.position, r * 0.85f, Vector3.down, out _,
                                      0.12f + r * 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------- presence -------
        void Update()
        {
            if (!alive) return;
            float dt = Time.deltaTime;
            t += dt;
            flash = Mathf.Max(0f, flash - dt * 2.2f);

            var player = PlayerBody.Instance;
            Vector3 toPlayer = player ? player.transform.position - transform.position : Vector3.right;

            // Breathing: slow and deep when calm, fast and shallow when hunting; coiled in the windup.
            float breathe = alert ? 1f + Mathf.Sin(t * 7.5f) * 0.05f : 1f + Mathf.Sin(t * 3.1f) * 0.045f;
            float s = breathe * (windup > 0f ? 0.84f : 1f);
            if (visual)
            {
                visual.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s)) * visualScale;
                // Upright, leaning towards you when hunting. The body rolls; the face does not.
                float lean = alert ? -Mathf.Sign(toPlayer.x) * 9f : 0f;
                visual.rotation = Quaternion.Euler(0f, 0f, lean);
            }

            // Eyes: dull ember when calm, hard pulsing glare when hunting, white-hot on a flinch.
            if (faceRenderer)
            {
                float glow = alert ? 4.2f + 2.4f * Mathf.Abs(Mathf.Sin(t * 9f))
                                   : 1.4f + 0.5f * Mathf.Sin(t * 2.3f);
                glow += flash * 6f;
                faceRenderer.GetPropertyBlock(faceBlock);
                faceBlock.SetColor(EmissionColor, Color.Lerp(eyeColor, Color.white, flash * 0.6f) * glow);
                faceRenderer.SetPropertyBlock(faceBlock);
            }

            if (aura)
            {
                float size = (alert ? 1.9f + 0.28f * Mathf.Sin(t * 7.1f) : 1.45f + 0.10f * Mathf.Sin(t * 2.6f))
                           + flash * 0.6f;
                aura.localScale = new Vector3(size, size, 1f);
                if (auraRenderer)
                {
                    float alpha = (alert ? 0.85f : 0.42f) + flash * 0.4f;
                    auraRenderer.GetPropertyBlock(auraBlock);
                    auraBlock.SetColor(BaseColor, new Color(auraColor.r, auraColor.g, auraColor.b, alpha));
                    auraRenderer.SetPropertyBlock(auraBlock);
                }
            }

            if (eyeLight)
                eyeLight.intensity = (alert ? 1.6f + 0.7f * Mathf.PerlinNoise(t * 11f, 0.3f) : 0.6f) + flash * 2f;

            if (trail)
            {
                var em = trail.emission;
                em.enabled = rb.linearVelocity.sqrMagnitude > 1.4f * 1.4f;
            }

            if (alert)
            {
                if (player && player.IsAlive) ReportThreat(Mathf.Clamp01(1.15f - toPlayer.magnitude / alertRange));
                growlTimer -= dt;
                if (growlTimer <= 0f)
                {
                    growlTimer = Random.Range(2.4f, 4.2f);
                    PtwAudio.Play(PtwSfx.EnemyAlert, 0.6f, Random.Range(0.85f, 1.1f));
                }
            }

            // The kill.
            var lm = LevelManager.Instance;
            if (lm == null || !lm.IsPlaying || player == null || !player.IsAlive) return;
            if (toPlayer.magnitude <= killRadius + player.Radius)
            {
                PtwAudio.Play(PtwSfx.EnemyBite);
                Haptics.Play(HapticKind.Fail);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.6f);
                flash = 1f;
                if (visual) visual.localScale *= 1.3f;       // the bite
                player.Kill();
            }
        }

        // ------------------------------------------------------------------- dying ----------
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
                if (deathVfx)
                {
                    deathVfx.transform.SetParent(null, true);
                    deathVfx.Emit(36);
                    Destroy(deathVfx.gameObject, 2f);
                }
                PtwAudio.Play(PtwSfx.EnemyDie, 0.9f, Random.Range(0.9f, 1.1f));
                Haptics.Play(HapticKind.Break);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.5f);
            }

            // Stop being a thing: no collider, no physics, visual gone. Destroyed shortly after so
            // anything still holding a reference this frame does not hit a null.
            if (col) col.enabled = false;
            rb.isKinematic = true;
            if (visual) visual.gameObject.SetActive(false);
            if (spikes) spikes.gameObject.SetActive(false);
            if (eyeLight) eyeLight.enabled = false;
            if (trail) { var em = trail.emission; em.enabled = false; }
            Destroy(gameObject, 0.5f);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, killRadius);
            Gizmos.color = new Color(0.9f, 0.5f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, alertRange);
        }
#endif
    }
}
