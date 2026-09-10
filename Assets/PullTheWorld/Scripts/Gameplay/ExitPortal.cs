using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The objective. Deliberately does NOT use a physics trigger: the world is one big compound
    /// collider, so trigger callbacks would land on the wrong GameObject. A plain distance test to
    /// the anchor is cheaper, fully deterministic and trivial to assert in a test.
    /// </summary>
    public class ExitPortal : MonoBehaviour
    {
        [Header("Reach")]
        [Tooltip("The exact spot that has to arrive at the player - the doorway threshold.")]
        [SerializeField] Transform mouth;
        [SerializeField] float reachRadius = 0.85f;
        [Tooltip("Distance at which the door starts visibly reacting to the player.")]
        [SerializeField] float noticeRadius = 4.5f;

        [Header("Locking")]
        [Tooltip("A locked door ignores the player until something unlocks it (e.g. a pressure plate).")]
        [SerializeField] bool locked;

        [Header("Visuals")]
        [SerializeField] Transform glowQuad;
        [SerializeField] Renderer glowRenderer;
        [SerializeField] Light portalLight;
        [SerializeField] ParticleSystem idleVfx;
        [SerializeField] ParticleSystem arriveVfx;
        [SerializeField] Color openColor = new Color(1f, 0.78f, 0.35f);
        [SerializeField] Color lockedColor = new Color(0.45f, 0.52f, 0.60f);

        [Header("Feel")]
        [SerializeField] float basePulseSpeed = 1.4f;
        [SerializeField] float basePulseAmount = 0.05f;
        [SerializeField] float baseLightIntensity = 2.2f;

        // PTW/PortalEnergy properties. Kept as ids so the per-frame update allocates nothing.
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        MaterialPropertyBlock mpb;
        Vector3 glowBaseScale = Vector3.one;
        bool consumed;
        float proximity;
        float t;

        public Vector3 MouthPosition => mouth ? mouth.position : transform.position;
        public bool Locked => locked;
        /// <summary>0 when far away, 1 when the player is right at the threshold.</summary>
        public float Proximity => proximity;
        public bool Reached => consumed;

        void Awake()
        {
            if (!mouth) mouth = transform;
            if (glowQuad) glowBaseScale = glowQuad.localScale;
            mpb = new MaterialPropertyBlock();
            ApplyGlow(0f);
        }

        public void SetLocked(bool value)
        {
            if (locked == value) return;
            locked = value;
            if (!locked && arriveVfx) arriveVfx.Play();
        }

        void Update()
        {
            t += Time.deltaTime;

            var rig = WorldRig.Instance;
            var lm = LevelManager.Instance;
            if (rig == null) return;

            float d = Vector3.Distance(rig.AnchorPos, MouthPosition);
            proximity = 1f - Mathf.Clamp01(Mathf.Max(0f, d - reachRadius) / Mathf.Max(0.01f, noticeRadius));

            if (!consumed && !locked && d <= reachRadius && lm != null && lm.IsPlaying)
            {
                consumed = true;
                if (arriveVfx) arriveVfx.Play();
                PtwAudio.Play(PtwSfx.Win);
                rig.AddShake(0.35f);
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
            float intensity = (locked ? 0.35f : 1f) * pulse * excite;

            if (glowRenderer)
            {
                glowRenderer.GetPropertyBlock(mpb);
                // Locked reads as a cold, dim field; unlocked is a hot warm core that blooms.
                mpb.SetColor(CoreColorId, locked ? c : Color.Lerp(c, Color.white, 0.55f));
                mpb.SetColor(EdgeColorId, c);
                mpb.SetFloat(IntensityId, (locked ? 0.55f : 2.3f) * pulse * excite);
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
