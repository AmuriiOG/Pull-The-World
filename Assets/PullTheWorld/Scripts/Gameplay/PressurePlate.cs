using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A slab that reacts to weight sitting on it: the player, or a rock you have delivered there.
    ///
    /// Drives any number of gates, doors and hazards. Whether it LATCHES is the whole difficulty
    /// dial: a latching plate means "get a rock here once", a non-latching one means "keep
    /// something here", which is a much harder ask when the floor keeps tilting.
    ///
    /// Weight is tested with an explicit overlap box rather than triggers, for the same
    /// compound-collider reason as the rest of the gameplay layer.
    /// </summary>
    public class PressurePlate : MonoBehaviour
    {
        [Header("Trigger volume")]
        [SerializeField] Transform zoneCenter;
        [SerializeField] Vector3 zoneSize = new Vector3(0.9f, 0.7f, 0.9f);
        [Tooltip("Minimum Rigidbody mass that counts. The player is always heavy enough.")]
        [SerializeField] float requiredMass = 0.35f;
        [Tooltip("Off means only props press it, so a level can require a rock specifically.")]
        [SerializeField] bool playerCanPress = true;

        [Header("Latching")]
        [Tooltip("On means the plate stays pressed forever once it has been pressed once.")]
        [SerializeField] bool latching = true;

        [Header("Targets")]
        [Tooltip("Gates that open while this is pressed.")]
        [SerializeField] Gate[] gates;
        [Tooltip("Doors unlocked while this is pressed.")]
        [SerializeField] ExitPortal[] unlocks;
        [Tooltip("Hazards switched OFF while this is pressed.")]
        [SerializeField] Hazard[] disarms;

        [Header("Visuals")]
        [SerializeField] Transform slab;
        [Tooltip("How far the slab sinks when pressed. Small - it just has to read as flush.")]
        [SerializeField] float sinkDepth = 0.07f;
        [SerializeField] float sinkSpeed = 12f;
        [SerializeField] Renderer inlayRenderer;
        [SerializeField] Color inlayOff = new Color(0.10f, 0.16f, 0.20f);
        [SerializeField] Color inlayOn = new Color(0.53f, 0.87f, 0.99f);
        [SerializeField] float inlayOnEmission = 2.4f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly Collider[] overlapBuffer = new Collider[12];
        MaterialPropertyBlock mpb;
        Vector3 slabBaseLocalPos;
        bool pressed;
        bool latched;
        float sink;

        public bool Pressed => pressed || latched;
        Transform Zone => zoneCenter ? zoneCenter : transform;

        void Awake()
        {
            if (slab) slabBaseLocalPos = slab.localPosition;
            mpb = new MaterialPropertyBlock();
            ApplyIndicator();
            PushTargets();
        }

        void Update()
        {
            bool now = latched || CheckWeight();

            if (now != pressed)
            {
                pressed = now;
                if (pressed && latching) latched = true;

                PtwAudio.Play(pressed ? PtwSfx.PlateOn : PtwSfx.PlateOff);
                Haptics.Play(HapticKind.Plate);
                ApplyIndicator();
                PushTargets();
            }

            // Sink animation runs every frame regardless, so it stays smooth across a latch.
            float target = Pressed ? 1f : 0f;
            sink = Mathf.Lerp(sink, target, 1f - Mathf.Exp(-sinkSpeed * Time.deltaTime));
            if (slab) slab.localPosition = slabBaseLocalPos - Vector3.up * (sink * sinkDepth);
        }

        bool CheckWeight()
        {
            int n = Physics.OverlapBoxNonAlloc(Zone.position, zoneSize * 0.5f, overlapBuffer,
                                               Zone.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = overlapBuffer[i].attachedRigidbody;
                if (!rb || rb.isKinematic) continue;

                bool isPlayer = PlayerBody.Instance && rb == PlayerBody.Instance.Body;
                if (isPlayer) { if (playerCanPress) return true; continue; }

                if (rb.mass >= requiredMass) return true;
            }
            return false;
        }

        void PushTargets()
        {
            bool on = Pressed;

            if (gates != null)
                foreach (var g in gates) if (g) g.SetOpen(on);

            if (unlocks != null)
                foreach (var d in unlocks) if (d) d.SetLocked(!on);

            if (disarms != null)
                foreach (var h in disarms) if (h) h.SetArmed(!on);
        }

        void ApplyIndicator()
        {
            if (!inlayRenderer) return;
            bool on = Pressed;
            inlayRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, on ? inlayOn : inlayOff);
            mpb.SetColor(EmissionColorId, on ? inlayOn * inlayOnEmission : Color.black);
            inlayRenderer.SetPropertyBlock(mpb);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(Zone.position, Zone.rotation, Vector3.one);
            Gizmos.color = new Color(0.53f, 0.87f, 0.99f, 0.55f);
            Gizmos.DrawWireCube(Vector3.zero, zoneSize);
        }
#endif
    }
}
