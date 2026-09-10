using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The one thing that never moves. The transform itself is pinned every frame; all the life
    /// (breathing, bracing, squash, celebration) happens on a child so the promise stays literal
    /// and testable: this transform is byte-for-byte where it started.
    ///
    /// The bracing lean is the single most important piece of feel here. Leaning AGAINST the world
    /// velocity is what sells "I am pulling this thing towards me" rather than "I am being carried".
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class PlayerAnchor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform body;
        [SerializeField] Transform ring;
        [SerializeField] Transform contactShadow;
        [SerializeField] WorldRig rig;

        [Header("Idle")]
        [SerializeField] float breatheHeight = 0.035f;
        [SerializeField] float breatheSpeed = 1.6f;
        [SerializeField] float idleSwaySpeed = 0.7f;
        [SerializeField] float idleSwayAngle = 2.2f;

        [Header("Bracing against the world")]
        [Tooltip("Degrees of lean at full drag speed. Subtle beats obvious.")]
        [SerializeField, Range(0f, 25f)] float maxLean = 9f;
        [SerializeField] float leanSpeedRef = 9f;
        [SerializeField] float leanResponse = 7f;

        [Header("Anchor ring")]
        [SerializeField] float ringSpinSpeed = 22f;
        [SerializeField] float ringPulseScale = 0.06f;
        [SerializeField] float ringPulseSpeed = 1.9f;
        [SerializeField] float grabPulse = 0.16f;

        [Header("Reactions")]
        [SerializeField] float squashRecover = 9f;
        [SerializeField] float hopHeight = 0.55f;

        Vector3 pinnedPos;
        Quaternion pinnedRot;
        Vector3 bodyBaseLocalPos;
        Vector3 ringBaseScale;

        Vector2 lean, leanVel;
        float squash, squashVel;
        float pulse, pulseVel;
        float hop, hopVel;
        float celebrateTimer;
        float t;

        public bool IsCelebrating => celebrateTimer > 0f;

        void Awake()
        {
            pinnedPos = transform.position;
            pinnedRot = transform.rotation;
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();
            if (body) bodyBaseLocalPos = body.localPosition;
            if (ring) ringBaseScale = ring.localScale;
            t = Random.value * 10f;
        }

        void OnEnable()
        {
            if (!rig) rig = WorldRig.Instance;
            if (rig != null)
            {
                rig.OnGrabBegin += HandleGrabBegin;
                rig.OnSpinSnapped += HandleSpinSnapped;
            }
        }

        void OnDisable()
        {
            if (rig != null)
            {
                rig.OnGrabBegin -= HandleGrabBegin;
                rig.OnSpinSnapped -= HandleSpinSnapped;
            }
        }

        void HandleGrabBegin(Vector3 _) { pulse += grabPulse; }
        void HandleSpinSnapped(float _) { pulse += grabPulse * 0.8f; React(0.25f); }

        /// <summary>Called when something lands or bumps nearby. 0..1.</summary>
        public void React(float strength)
        {
            squash = Mathf.Min(0.35f, squash + strength * 0.3f);
            pulse += strength * 0.12f;
        }

        public void Celebrate()
        {
            celebrateTimer = 1.35f;
            hopVel += hopHeight * 7f;
            pulse += 0.4f;
        }

        public void Stumble()
        {
            squash = 0.3f;
            lean += new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 0.8f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            t += dt;

            // 1. The promise: this transform does not move. Ever.
            transform.SetPositionAndRotation(pinnedPos, pinnedRot);

            // 2. Bracing lean, driven by how fast the world is being pulled.
            Vector2 wantLean = Vector2.zero;
            if (rig && rig.ViewCamera)
            {
                Vector3 v = rig.WorldVelocity;
                var cam = rig.ViewCamera.transform;
                // Project world motion onto the screen axes so the lean always reads correctly.
                float sx = Vector3.Dot(v, cam.right);
                float sy = Vector3.Dot(v, cam.up);
                wantLean = new Vector2(sx, sy) / Mathf.Max(0.01f, leanSpeedRef);
                wantLean = Vector2.ClampMagnitude(wantLean, 1f);
            }
            lean = Vector2.Lerp(lean, wantLean, 1f - Mathf.Exp(-leanResponse * dt));

            squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-squashRecover * dt));
            pulse = Mathf.Lerp(pulse, 0f, 1f - Mathf.Exp(-6f * dt));

            // Hop uses a tiny ballistic sim so the celebration has real weight.
            if (hop > 0f || hopVel > 0f)
            {
                hopVel -= 22f * dt;
                hop += hopVel * dt;
                if (hop <= 0f) { hop = 0f; hopVel = 0f; squash = 0.28f; }
            }

            if (celebrateTimer > 0f) celebrateTimer -= dt;

            ApplyBody(dt);
            ApplyRing(dt);
        }

        void ApplyBody(float dt)
        {
            if (!body) return;

            float breathe = Mathf.Sin(t * breatheSpeed * Mathf.PI * 2f) * breatheHeight;
            float sway = Mathf.Sin(t * idleSwaySpeed * Mathf.PI * 2f) * idleSwayAngle;

            body.localPosition = bodyBaseLocalPos + new Vector3(0f, breathe + hop, 0f);

            // Lean is authored in screen space, then converted back into a world-space tilt so it
            // always reads correctly no matter what angle the camera was set to.
            Vector3 leanAxis = Vector3.zero;
            if (rig && rig.ViewCamera)
            {
                var cam = rig.ViewCamera.transform;
                Vector3 screenDir = cam.right * lean.x + cam.up * lean.y;
                // Tilt away from the direction of travel: spin about the axis perpendicular to it.
                leanAxis = Vector3.Cross(Vector3.up, screenDir);
            }

            float leanAmount = Mathf.Clamp01(lean.magnitude) * maxLean;
            Quaternion leanRot = leanAxis.sqrMagnitude > 1e-5f
                ? Quaternion.AngleAxis(leanAmount, leanAxis.normalized)
                : Quaternion.identity;

            float spinCelebrate = celebrateTimer > 0f ? (1.35f - celebrateTimer) * 420f : 0f;
            body.localRotation = leanRot * Quaternion.Euler(0f, sway + spinCelebrate, 0f);

            float s = 1f - squash;
            body.localScale = new Vector3(1f / Mathf.Sqrt(Mathf.Max(0.05f, s)), s,
                                          1f / Mathf.Sqrt(Mathf.Max(0.05f, s)));
        }

        void ApplyRing(float dt)
        {
            if (ring)
            {
                ring.Rotate(Vector3.up, ringSpinSpeed * dt, Space.Self);
                float p = 1f + Mathf.Sin(t * ringPulseSpeed * Mathf.PI * 2f) * ringPulseScale + pulse;
                ring.localScale = new Vector3(ringBaseScale.x * p, ringBaseScale.y, ringBaseScale.z * p);
            }
            if (contactShadow)
            {
                float s = 1f - squash * 0.5f + hop * -0.4f;
                contactShadow.localScale = Vector3.one * Mathf.Max(0.2f, s);
            }
        }
    }
}
