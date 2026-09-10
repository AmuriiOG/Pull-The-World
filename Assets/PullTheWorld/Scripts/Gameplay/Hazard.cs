using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Fire / spikes / anything that ends the run if it reaches the player.
    /// Can be smothered by dropping a heavy prop on it, which is the Level 4 lesson.
    /// Like ExitPortal this uses explicit overlap queries rather than physics triggers, because
    /// the whole static world is a single compound collider.
    /// </summary>
    public class Hazard : MonoBehaviour
    {
        [Header("Danger zone")]
        [SerializeField] Transform zoneCenter;
        [SerializeField] Vector3 zoneSize = new Vector3(0.9f, 0.9f, 0.9f);
        [Tooltip("How close the player anchor has to get before this kills.")]
        [SerializeField] float killRadius = 0.55f;

        [Header("Smothering")]
        [Tooltip("Dropping a heavy prop on this puts it out for good.")]
        [SerializeField] bool canBeSmothered = true;
        [SerializeField] float smotherMass = 0.5f;
        [SerializeField] Vector3 smotherBoxSize = new Vector3(0.95f, 1.1f, 0.95f);

        [Header("Visuals")]
        [SerializeField] GameObject activeVisuals;
        [SerializeField] ParticleSystem flameVfx;
        [SerializeField] ParticleSystem smotherVfx;
        [SerializeField] Light hazardLight;

        [Header("State")]
        [SerializeField] bool armed = true;

        readonly Collider[] overlapBuffer = new Collider[8];
        bool smothered;
        float lightBase;
        float t;

        public bool Armed => armed && !smothered;
        public bool Smothered => smothered;
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

            var rig = WorldRig.Instance;
            var lm = LevelManager.Instance;
            if (rig == null || lm == null || !lm.IsPlaying) return;

            if (Vector3.Distance(rig.AnchorPos, Zone.position) <= killRadius)
            {
                PtwAudio.Play(PtwSfx.Fail);
                rig.AddShake(0.6f);
                var player = FindFirstObjectByType<PlayerAnchor>();
                if (player) player.Stumble();
                lm.ReportFail();
            }

            if (hazardLight)
            {
                hazardLight.intensity = lightBase * (0.82f + Mathf.PerlinNoise(t * 6f, 0f) * 0.36f);
            }
        }

        bool CheckSmothered()
        {
            int n = Physics.OverlapBoxNonAlloc(
                Zone.position + Zone.up * (smotherBoxSize.y * 0.4f),
                smotherBoxSize * 0.5f, overlapBuffer, Zone.rotation, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var c = overlapBuffer[i];
                if (!c) continue;
                var prop = c.GetComponentInParent<Pushable>();
                if (prop && prop.Mass >= smotherMass) return true;
            }
            return false;
        }

        void Smother()
        {
            smothered = true;
            if (smotherVfx) smotherVfx.Play();
            PtwAudio.Play(PtwSfx.Smother);
            if (WorldRig.Instance) WorldRig.Instance.AddShake(0.22f);
            ApplyVisualState();
        }

        void ApplyVisualState()
        {
            bool on = Armed;
            if (activeVisuals) activeVisuals.SetActive(on);
            if (hazardLight) hazardLight.enabled = on;
            if (flameVfx)
            {
                var em = flameVfx.emission;
                em.enabled = on;
                if (!on) flameVfx.Clear();
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(Zone.position, killRadius);
            Gizmos.matrix = Matrix4x4.TRS(Zone.position + Zone.up * (smotherBoxSize.y * 0.4f),
                                          Zone.rotation, Vector3.one);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, smotherBoxSize);
        }
#endif
    }
}
