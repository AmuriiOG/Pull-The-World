using UnityEngine;
using UnityEngine.Events;

namespace PullTheWorld
{
    /// <summary>
    /// Held down by any heavy enough Pushable resting on it. Drives gates, hazards and doors
    /// through plain UnityEvents so a designer can rewire a puzzle entirely in the Inspector.
    /// </summary>
    public class PressurePlate : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] Transform padCenter;
        [SerializeField] Vector3 detectSize = new Vector3(0.85f, 0.7f, 0.85f);
        [SerializeField] float requiredMass = 0.5f;
        [Tooltip("Stops the plate chattering when a prop is settling on the edge.")]
        [SerializeField] float releaseDelay = 0.15f;

        [Header("Visuals")]
        [SerializeField] Transform pad;
        [SerializeField] float pressDepth = 0.09f;
        [SerializeField] float pressSpeed = 14f;
        [SerializeField] Renderer indicator;
        [SerializeField] Color offColor = new Color(0.85f, 0.35f, 0.30f);
        [SerializeField] Color onColor = new Color(0.42f, 0.86f, 0.45f);

        [Header("Events")]
        public UnityEvent onPressed;
        public UnityEvent onReleased;
        [Tooltip("Convenient for wiring straight into Gate.SetOpen / Hazard.SetArmed.")]
        public UnityEvent<bool> onChanged;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly Collider[] buffer = new Collider[8];
        MaterialPropertyBlock mpb;
        Vector3 padBaseLocalPos;
        bool pressed;
        float releaseTimer;
        float visual;

        public bool IsPressed => pressed;
        Transform Pad => padCenter ? padCenter : transform;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (pad) padBaseLocalPos = pad.localPosition;
            ApplyIndicator(0f);
        }

        void FixedUpdate()
        {
            bool occupied = Probe();

            if (occupied)
            {
                releaseTimer = releaseDelay;
                if (!pressed) SetPressed(true);
            }
            else if (pressed)
            {
                releaseTimer -= Time.fixedDeltaTime;
                if (releaseTimer <= 0f) SetPressed(false);
            }
        }

        bool Probe()
        {
            int n = Physics.OverlapBoxNonAlloc(
                Pad.position + Pad.up * (detectSize.y * 0.45f),
                detectSize * 0.5f, buffer, Pad.rotation, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                var c = buffer[i];
                if (!c) continue;
                var prop = c.GetComponentInParent<Pushable>();
                if (prop && prop.Mass >= requiredMass) return true;
            }
            return false;
        }

        void SetPressed(bool value)
        {
            pressed = value;
            PtwAudio.Play(value ? PtwSfx.PlateOn : PtwSfx.PlateOff);
            Haptics.Medium();
            if (WorldRig.Instance) WorldRig.Instance.AddShake(0.12f);
            if (value) onPressed?.Invoke(); else onReleased?.Invoke();
            onChanged?.Invoke(value);
        }

        void Update()
        {
            visual = Mathf.Lerp(visual, pressed ? 1f : 0f, 1f - Mathf.Exp(-pressSpeed * Time.deltaTime));
            if (pad) pad.localPosition = padBaseLocalPos - new Vector3(0f, pressDepth * visual, 0f);
            ApplyIndicator(visual);
        }

        void ApplyIndicator(float v)
        {
            if (!indicator) return;
            Color c = Color.Lerp(offColor, onColor, v);
            indicator.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(EmissionColorId, c * Mathf.Lerp(0.5f, 2.2f, v));
            indicator.SetPropertyBlock(mpb);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(Pad.position + Pad.up * (detectSize.y * 0.45f),
                                          Pad.rotation, Vector3.one);
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, detectSize);
        }
#endif
    }
}
