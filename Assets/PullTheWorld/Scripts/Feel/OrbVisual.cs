using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Brings the glass orb to life. Visual only: reads the body, never writes to it.
    ///
    ///  * Keeps the visual UPRIGHT. The body rolls; a glass sphere rolling is invisible, and the
    ///    star, ring and halo must not tumble. (PlayerBody's win spin is left alone.)
    ///  * Breathes while idle: a slow pulse on the shader, the halo, the core and the ring.
    ///  * Reacts to speed: rim and halo brighten, the ring spins faster, sparkles come thicker.
    ///  * Flashes on impact via PlayerBody.OnImpact, with a sparkle burst.
    ///  * Whispers: an occasional tiny chime while idle, so it feels alive even standing still.
    /// </summary>
    public class OrbVisual : MonoBehaviour
    {
        [SerializeField] PlayerBody player;
        [SerializeField] Transform visual;
        [SerializeField] Renderer glass;
        [SerializeField] Transform core;
        [SerializeField] Renderer coreRenderer;
        [SerializeField] Transform ring;
        [Tooltip("The small glint riding on the orbit ring. A child of the ring, so it orbits; kept facing the camera here.")]
        [SerializeField] Transform bead;
        [SerializeField] Transform halo;
        [SerializeField] Renderer haloRenderer;
        [SerializeField] ParticleSystem sparkles;
        [SerializeField] Light orbLight;

        [Header("Feel")]
        [SerializeField] float breatheHz = 0.55f;
        [SerializeField] float ringSpinIdle = 28f;          // deg/s
        [SerializeField] float ringSpinFast = 160f;
        [SerializeField] float fullSpeed = 9f;              // speed at which the glow is at its brightest
        [SerializeField] Color haloColor = new Color(0.78f, 0.98f, 0.93f);   // mint-white: a soft cool tint on the grass, not a green flare
        [SerializeField] Color coreColor = Color.white;
        [SerializeField] Vector2 chimeEvery = new Vector2(3.5f, 7f);

        static readonly int PulseId = Shader.PropertyToID("_Pulse");
        static readonly int BoostId = Shader.PropertyToID("_Boost");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock glassBlock, haloBlock, coreBlock;
        float t, boost, ringAngle, chimeTimer;
        Vector3 coreBase, haloBase, ringBase;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerBody>();
            glassBlock = new MaterialPropertyBlock();
            haloBlock = new MaterialPropertyBlock();
            coreBlock = new MaterialPropertyBlock();
            if (core) coreBase = core.localScale;
            if (halo) haloBase = halo.localScale;
            if (ring) ringBase = ring.localScale;
            t = Random.value * 10f;
            chimeTimer = Random.Range(chimeEvery.x, chimeEvery.y);
        }

        void OnEnable() { if (player) player.OnImpact += HandleImpact; }
        void OnDisable() { if (player) player.OnImpact -= HandleImpact; }

        void HandleImpact(float strength, Vector3 point)
        {
            boost = Mathf.Max(boost, 0.25f + strength * 0.55f);
            if (sparkles) sparkles.Emit(Mathf.RoundToInt(3 + strength * 8f));
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            t += dt;
            boost = Mathf.Max(0f, boost - dt * 1.8f);

            bool ending = player && player.IsAnimatingEnding;
            float speed = player ? player.Speed : 0f;
            float k = Mathf.Clamp01(speed / Mathf.Max(0.1f, fullSpeed));

            // Upright. PlayerBody spins the visual on a win; let it.
            if (visual && !ending) visual.rotation = Quaternion.identity;

            float breathe = 0.5f + 0.5f * Mathf.Sin(t * breatheHz * Mathf.PI * 2f);
            // The speed glow is a hint, not a headlight. At k*0.9 plus an impact boost of up to 1.5
            // the additive core, halo and rim all crossed the bloom threshold together and the orb
            // was a white flare whenever it moved; the mockup's orb at speed is still a pale glass
            // ball with a brighter rim.
            float glow = boost + k * 0.35f;

            if (glass)
            {
                glass.GetPropertyBlock(glassBlock);
                glassBlock.SetFloat(PulseId, breathe);
                glassBlock.SetFloat(BoostId, glow);
                glass.SetPropertyBlock(glassBlock);
            }

            if (core)
            {
                float s = 1f + breathe * 0.12f + glow * 0.2f;
                core.localScale = coreBase * s;
                core.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.7f) * 8f);
                if (coreRenderer)
                {
                    coreRenderer.GetPropertyBlock(coreBlock);
                    coreBlock.SetColor(BaseColorId, coreColor * (0.80f + breathe * 0.15f + glow * 0.25f));
                    coreRenderer.SetPropertyBlock(coreBlock);
                }
            }

            if (ring)
            {
                ringAngle += Mathf.Lerp(ringSpinIdle, ringSpinFast, k) * dt;
                // Tilted orbit that slowly precesses, like the mockup's swept ring.
                ring.localRotation = Quaternion.Euler(64f + Mathf.Sin(t * 0.4f) * 8f, ringAngle, 18f);
                ring.localScale = ringBase * (1f + breathe * 0.04f + glow * 0.08f);
                if (bead) bead.rotation = Quaternion.identity;   // a billboard glint, wherever the ring has carried it
            }

            if (halo)
            {
                halo.localScale = haloBase * (1f + breathe * 0.08f + glow * 0.2f);
                if (haloRenderer)
                {
                    haloRenderer.GetPropertyBlock(haloBlock);
                    var c = haloColor;
                    c.a = 0.12f + breathe * 0.06f + glow * 0.16f;
                    haloBlock.SetColor(BaseColorId, c);
                    haloRenderer.SetPropertyBlock(haloBlock);
                }
            }

            if (orbLight) orbLight.intensity = 0.30f + breathe * 0.10f + glow * 0.35f;

            if (sparkles)
            {
                var em = sparkles.emission;
                em.rateOverTime = 4f + k * 8f;
            }

            // The idle whisper: a tiny chime now and then, only when still and alive.
            if (player && player.IsAlive && !ending && speed < 0.5f)
            {
                chimeTimer -= dt;
                if (chimeTimer <= 0f)
                {
                    chimeTimer = Random.Range(chimeEvery.x, chimeEvery.y);
                    PtwAudio.Play(PtwSfx.Sparkle, 0.35f, Random.Range(0.9f, 1.3f));
                    if (sparkles) sparkles.Emit(3);
                }
            }
        }
    }
}
