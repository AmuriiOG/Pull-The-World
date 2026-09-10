using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// All the reactive game feel in one place: dust, impact sound, haptics, world shake, and the
    /// ratchet tick while the world is being turned.
    ///
    /// Worth centralising rather than sprinkling through the gameplay scripts. Feel is tuned by
    /// comparing effects against each other - a dust puff that fires on a bump too small to be
    /// worth a haptic is the sort of mismatch you only notice when both thresholds are on screen
    /// together. It also means PlayerBody stays a physics object with no opinions about audio.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class ImpactFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerBody player;
        [SerializeField] WorldRotator rotator;
        [Tooltip("One-shot burst emitter. Moved to the contact point before each burst.")]
        [SerializeField] ParticleSystem dust;
        [Tooltip("Continuous emitter for a fast roll along the ground.")]
        [SerializeField] ParticleSystem rollDust;

        [Header("Impact thresholds")]
        [Tooltip("Below this normalised strength an impact produces nothing at all. Stops resting " +
                 "contact chatter from firing a puff and a buzz every frame.")]
        [SerializeField, Range(0f, 1f)] float minImpact = 0.18f;
        [Tooltip("Particles emitted at a full-strength impact.")]
        [SerializeField] int dustAtFullImpact = 7;
        [SerializeField] float shakeAtFullImpact = 0.55f;

        [Header("Rolling")]
        [Tooltip("Speed at which the rolling dust reaches full rate.")]
        [SerializeField] float rollSpeedReference = 6f;
        [SerializeField] float rollMaxRate = 8f;

        [Header("Rotation ticks")]
        [Tooltip("Ticks are the main reason turning the world feels physical rather than like " +
                 "dragging a slider. They must stay quiet and tiny.")]
        [SerializeField, Range(0f, 1f)] float tickVolume = 0.32f;

        void Awake()
        {
            if (!player) player = PlayerBody.Instance ? PlayerBody.Instance : FindFirstObjectByType<PlayerBody>();
            if (!rotator) rotator = WorldRotator.Instance ? WorldRotator.Instance : FindFirstObjectByType<WorldRotator>();
        }

        void OnEnable()
        {
            if (player != null) player.OnImpact += HandleImpact;
            if (rotator != null) rotator.OnRotationTick += HandleTick;
            var lm = LevelManager.Instance ? LevelManager.Instance : FindFirstObjectByType<LevelManager>();
            if (lm != null)
            {
                lm.OnLevelWon += HandleWon;
                lm.OnLevelFailed += HandleFailed;
                levelsHooked = lm;
            }
        }

        void OnDisable()
        {
            if (player != null) player.OnImpact -= HandleImpact;
            if (rotator != null) rotator.OnRotationTick -= HandleTick;
            if (levelsHooked != null)
            {
                levelsHooked.OnLevelWon -= HandleWon;
                levelsHooked.OnLevelFailed -= HandleFailed;
                levelsHooked = null;
            }
        }

        LevelManager levelsHooked;

        /// <summary>
        /// The two moments that most need to LAND. A win or a death that just stops the ball reads
        /// as a bug; a burst at the ball plus a proper shake reads as an event.
        /// </summary>
        void HandleWon(LevelDefinition _)
        {
            if (dust && player) { dust.transform.position = player.transform.position; dust.Emit(22); }
            if (rotator) rotator.AddShake(0.35f);
        }

        void HandleFailed(LevelDefinition _)
        {
            if (dust && player) { dust.transform.position = player.transform.position; dust.Emit(28); }
            if (rotator) rotator.AddShake(0.7f);
        }

        void HandleImpact(float strength, Vector3 point)
        {
            if (strength < minImpact) return;

            if (dust)
            {
                dust.transform.position = point;
                dust.Emit(Mathf.Max(1, Mathf.RoundToInt(dustAtFullImpact * strength)));
            }

            PtwAudio.Play(PtwSfx.Impact, Mathf.Lerp(0.25f, 1f, strength),
                          Mathf.Lerp(1.15f, 0.85f, strength));   // heavier hits sound lower
            Haptics.Impact(strength);
            if (rotator) rotator.AddShake(shakeAtFullImpact * strength);
        }

        void HandleTick()
        {
            PtwAudio.Play(PtwSfx.SpinTick, tickVolume, Random.Range(0.94f, 1.06f));
            Haptics.Play(HapticKind.RotateTick);
        }

        void Update()
        {
            if (!rollDust || player == null) return;

            // Dust only while actually skidding along the ground, not while airborne.
            var em = rollDust.emission;
            float t = player.IsGrounded && player.IsAlive
                ? Mathf.Clamp01(player.Speed / Mathf.Max(0.01f, rollSpeedReference))
                : 0f;
            em.rateOverTimeMultiplier = t * rollMaxRate;

            // Emit at the CONTACT point, not the centre of the ball. Spawning dust inside the ball
            // puts half of every puff behind it, which is what made the trail read as a solid
            // stripe rather than as grit being kicked up off the ground.
            if (player.IsAlive)
                rollDust.transform.position =
                    player.transform.position + Vector3.down * (player.Radius * 0.85f);
        }
    }
}
