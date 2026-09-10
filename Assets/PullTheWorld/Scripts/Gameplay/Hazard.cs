using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Fire, spikes, anything that ends the run on contact.
    ///
    /// Can be smothered by dropping a heavy prop on it, which is the "rocks are tools" lesson, and
    /// fire can additionally be doused by water. Like ExitPortal this uses explicit overlap queries
    /// rather than physics triggers, because the whole static level is a single compound collider
    /// and trigger callbacks do not reliably arrive on this GameObject.
    /// </summary>
    public class Hazard : MonoBehaviour
    {
        [Header("Danger zone")]
        [SerializeField] Transform zoneCenter;
        [Tooltip("Added to the player radius. Keep it tight - an unfair hitbox on a physics puzzle " +
                 "reads as the game cheating rather than as the player misjudging.")]
        [SerializeField] float killRadius = 0.42f;

        [Header("Smothering")]
        [Tooltip("Dropping a heavy prop on this puts it out for good.")]
        [SerializeField] bool canBeSmothered = true;
        [SerializeField] float smotherMass = 0.5f;
        [SerializeField] Vector3 smotherBoxSize = new Vector3(0.95f, 1.1f, 0.95f);

        [Header("Water")]
        [Tooltip("Fire goes out when water reaches it. Spikes do not care.")]
        [SerializeField] bool dousedByWater = true;

        [Header("Visuals")]
        [SerializeField] GameObject activeVisuals;
        [SerializeField] GameObject spentVisuals;
        [SerializeField] ParticleSystem flameVfx;
        [SerializeField] ParticleSystem smotherVfx;
        [SerializeField] Light hazardLight;

        [Header("State")]
        [SerializeField] bool armed = true;

        readonly Collider[] overlapBuffer = new Collider[12];
        bool smothered;
        float lightBase;
        float t;

        public bool Armed => armed && !smothered;
        public bool Smothered => smothered;
        public bool DousedByWater => dousedByWater;
        Transform Zone => zoneCenter ? zoneCenter : transform;

        void Awake()
        {
            if (hazardLight) lightBase = hazardLight.intensity;
            ApplyVisualState();
        }

        /// <summary>Wired from a pressure plate or a script to turn the hazard on and off.</summary>
        public void SetArmed(bool value)
        {
            if (armed == value) return;
            armed = value;
            ApplyVisualState();
        }

        void Update()
        {
            t += Time.deltaTime;
            if (!Armed) return;

            if (canBeSmothered && CheckSmothered())
            {
                Smother();
                return;
            }

            var lm = LevelManager.Instance;
            var player = PlayerBody.Instance;
            if (lm == null || !lm.IsPlaying || player == null || !player.IsAlive) return;

            float d = Vector3.Distance(player.transform.position, Zone.position);
            if (d <= killRadius + player.Radius)
            {
                PtwAudio.Play(PtwSfx.Fail);
                Haptics.Play(HapticKind.Fail);
                if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.5f);
                player.Kill();
            }
        }

        /// <summary>
        /// Anything heavy sitting in the box above this puts it out. Mass rather than a tag so a
        /// boulder, a crate and an enemy corpse all work without being enumerated.
        /// </summary>
        bool CheckSmothered()
        {
            int n = Physics.OverlapBoxNonAlloc(Zone.position + Zone.up * 0.35f,
                                               smotherBoxSize * 0.5f, overlapBuffer,
                                               Zone.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = overlapBuffer[i].attachedRigidbody;
                if (!rb || rb.isKinematic) continue;
                // The player is not a fire extinguisher.
                if (PlayerBody.Instance && rb == PlayerBody.Instance.Body) continue;
                if (rb.mass >= smotherMass) return true;
            }
            return false;
        }

        /// <summary>Called by WaterVolume when water overlaps this hazard.</summary>
        public void Douse()
        {
            if (!dousedByWater || smothered) return;
            Smother();
        }

        void Smother()
        {
            if (smothered) return;
            smothered = true;
            if (smotherVfx) smotherVfx.Play();
            PtwAudio.Play(PtwSfx.Smother);
            Haptics.Play(HapticKind.Break);
            ApplyVisualState();
        }

        void ApplyVisualState()
        {
            bool on = Armed;
            if (activeVisuals) activeVisuals.SetActive(on);
            if (spentVisuals) spentVisuals.SetActive(!on);

            if (flameVfx)
            {
                var em = flameVfx.emission;
                em.enabled = on;
                if (!on) flameVfx.Clear();
            }
            if (hazardLight) hazardLight.enabled = on;
        }

        void LateUpdate()
        {
            // Fire light flickers so it never reads as a static emissive decal.
            if (!hazardLight || !Armed) return;
            float flicker = 1f + Mathf.Sin(t * 11.3f) * 0.09f + Mathf.Sin(t * 23.7f) * 0.05f;
            hazardLight.intensity = lightBase * flicker;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(Zone.position, killRadius);
            Gizmos.matrix = Matrix4x4.TRS(Zone.position + Zone.up * 0.35f, Zone.rotation, Vector3.one);
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, smotherBoxSize);
        }
#endif
    }
}
