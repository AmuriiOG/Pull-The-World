using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The objective: a glowing doorway the player has to be delivered into by gravity.
    ///
    /// Deliberately does NOT use a physics trigger. The whole static level is one compound collider
    /// hanging off a single kinematic Rigidbody, so trigger callbacks arrive on whichever
    /// GameObject PhysX felt like reporting rather than on this door. An explicit distance test
    /// against the player is cheaper, fully deterministic, and trivial to assert in a test. v1
    /// learned this the hard way and the reasoning still holds.
    ///
    /// Locking is how the key and pressure-plate levels work: a locked door reads cold and dim and
    /// ignores the player entirely until the level says otherwise.
    /// </summary>
    public class ExitPortal : MonoBehaviour
    {
        [Header("Reach")]
        [Tooltip("The doorway threshold - the exact spot the player has to arrive at.")]
        [SerializeField] Transform mouth;
        [Tooltip("Added to the player radius, so a fast roll through the doorway still registers.")]
        [SerializeField] float reachRadius = 0.62f;
        [Tooltip("Distance at which the door starts visibly reacting to the player approaching.")]
        [SerializeField] float noticeRadius = 4.5f;

        [Header("Locking")]
        [Tooltip("A locked door ignores the player until a plate or the last key unlocks it.")]
        [SerializeField] bool locked;
        [Tooltip("On means the door watches the level key count and unlocks itself when they are " +
                 "all collected. Off means something else drives SetLocked, e.g. a pressure plate.")]
        [SerializeField] bool unlockedByKeys = true;

        [Header("Visuals")]
        [SerializeField] Transform glowQuad;
        [SerializeField] Renderer glowRenderer;
        [SerializeField] Light portalLight;
        [SerializeField] ParticleSystem idleVfx;
        [SerializeField] ParticleSystem arriveVfx;
        [SerializeField] Color openColor = new Color(1f, 0.74f, 0.38f);      // amber-gold: the mockup interior is (254,200,123)
        [SerializeField] Color lockedColor = new Color(0.80f, 0.77f, 0.88f);  // pale lilac, dormant

        [Header("Feel")]
        [SerializeField] float basePulseSpeed = 1.4f;
        [SerializeField] float basePulseAmount = 0.05f;
        [SerializeField] float baseLightIntensity = 1.4f;

        // PTW/PortalEnergy properties, cached as ids so the per-frame update allocates nothing.
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        MaterialPropertyBlock mpb;
        Vector3 glowBaseScale = Vector3.one;
        bool consumed;
        float proximity;
        float t;
        float flash;                // the win flare, decaying

        public Vector3 MouthPosition => mouth ? mouth.position : transform.position;
        public bool Locked => locked;
        /// <summary>0 when far away, 1 when the player is right at the threshold.</summary>
        public float Proximity => proximity;
        public bool Reached => consumed;

        AudioSource hum;

        void Awake()
        {
            if (!mouth) mouth = transform;
            if (glowQuad) glowBaseScale = glowQuad.localScale;
            mpb = new MaterialPropertyBlock();

            // The portal hums, and the hum swells as you approach. The loop is synthesised once by
            // PtwAudio; in headless runs there is no audio instance and the source simply stays silent.
            hum = gameObject.AddComponent<AudioSource>();
            hum.loop = true;
            hum.playOnAwake = false;
            hum.spatialBlend = 0f;
            hum.volume = 0f;
            var clip = PtwAudio.Loop(PtwLoop.PortalHum);
            if (clip) { hum.clip = clip; hum.Play(); }

            ApplyGlow(0f);
        }

        /// <summary>The door swells with light as the camera leaves through it. Called by LevelManager on a win.</summary>
        public void Flare() => flash = Mathf.Max(flash, 1.6f);

        public void SetLocked(bool value)
        {
            if (locked == value) return;
            locked = value;
            if (!locked)
            {
                if (arriveVfx) arriveVfx.Play();
                PtwAudio.Play(PtwSfx.Unlock);
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            flash = Mathf.Max(0f, flash - Time.deltaTime * 1.6f);

            var lm = LevelManager.Instance;
            var player = PlayerBody.Instance;

            // Key-driven doors resolve their own locked state so a level does not need wiring.
            if (unlockedByKeys && lm != null && lm.KeysRequired > 0)
                SetLocked(!lm.DoorUnlocked);

            if (player == null || !player.IsAlive)
            {
                ApplyGlow(0f);
                return;
            }

            float d = Vector3.Distance(player.transform.position, MouthPosition);
            float hit = reachRadius + player.Radius;
            proximity = 1f - Mathf.Clamp01(Mathf.Max(0f, d - hit) / Mathf.Max(0.01f, noticeRadius));

            if (!consumed && !locked && d <= hit && lm != null && lm.IsPlaying)
            {
                consumed = true;
                if (arriveVfx) arriveVfx.Play();
                PtwAudio.Play(PtwSfx.PortalEnter, 0.9f);
                PtwAudio.Play(PtwSfx.Win);
                Haptics.Play(HapticKind.Win);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.35f);
                lm.ReportWin();
            }

            ApplyGlow(proximity);
        }

        void ApplyGlow(float p)
        {
            float pulse = 1f + Mathf.Sin(t * basePulseSpeed * Mathf.PI * 2f) * basePulseAmount;
            float excite = 1f + p * 0.9f;

            if (glowQuad) glowQuad.localScale = glowBaseScale * pulse * (1f + p * 0.06f);

            Color c = locked ? lockedColor : openColor;
            float intensity = (locked ? 0.35f : 1f) * pulse * excite * (1f + flash);

            if (glowRenderer)
            {
                glowRenderer.GetPropertyBlock(mpb);
                // Locked reads as a cold, dim field; unlocked is the mockup's gold. The fill is
                // alpha-blended (see PtwPortalEnergy.shader), so these ARE the colours that show:
                // _CoreColor is the field low down and round the core (mockup 253,231,184),
                // _EdgeColor the amber at the top (246,184,95). Kept under the bloom threshold;
                // only the shader's core disc blooms, and the spill is the halo's and light's job.
                mpb.SetColor(CoreColorId, locked ? c : Color.Lerp(c, Color.white, 0.60f));
                mpb.SetColor(EdgeColorId, c);
                mpb.SetFloat(IntensityId, (locked ? 0.55f : 1.0f) * pulse * (1f + p * 0.10f + flash * 0.35f));
                glowRenderer.SetPropertyBlock(mpb);
            }
            if (portalLight)
            {
                portalLight.color = c;
                portalLight.intensity = baseLightIntensity * intensity;
            }
            if (idleVfx)
            {
                var em = idleVfx.emission;
                em.rateOverTimeMultiplier = locked ? 0f : Mathf.Lerp(6f, 22f, p);
            }
            if (hum)
                hum.volume = !GameProgress.SoundOn ? 0f : locked ? 0.04f : 0.09f + p * 0.24f;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(MouthPosition, reachRadius);
        }
#endif
    }
}
