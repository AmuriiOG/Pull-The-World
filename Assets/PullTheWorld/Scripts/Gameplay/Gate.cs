using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A heavy slab filling a doorway that retracts when a plate is pressed.
    ///
    /// The slab is an ordinary child collider of the level's compound body and is animated by local
    /// position. That is fine for a gate specifically: it only ever needs to block or not block, and
    /// it retracts INTO the floor, so it never has to carry anything and never needs real contact
    /// velocity. (A moving platform does need that, which is why MovingPlatform is built
    /// completely differently.)
    /// </summary>
    public class Gate : MonoBehaviour
    {
        [Header("Motion")]
        [SerializeField] Transform slab;
        [Tooltip("How far the slab travels, in level space. Should be at least its own height so " +
                 "it disappears completely rather than leaving a lip to catch on.")]
        [SerializeField] float travel = 2f;
        [Tooltip("Direction of travel in LOCAL space. Down is the default: it sinks into the floor.")]
        [SerializeField] Vector3 localDirection = Vector3.down;
        [SerializeField] float speed = 4.5f;

        [Header("State")]
        [SerializeField] bool open;

        [Header("Visuals")]
        [SerializeField] Renderer thresholdRenderer;
        [SerializeField] Color thresholdLit = new Color(0.53f, 0.87f, 0.99f);

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock mpb;
        Vector3 slabClosedLocalPos;
        float t;            // 0 = closed, 1 = open
        bool lastOpen;

        public bool IsOpen => open;
        /// <summary>0 closed, 1 fully retracted. Useful for a test rather than polling a pose.</summary>
        public float Openness => t;

        void Awake()
        {
            if (!slab) slab = transform;
            slabClosedLocalPos = slab.localPosition;
            mpb = new MaterialPropertyBlock();
            t = open ? 1f : 0f;
            lastOpen = open;
            Apply();
        }

        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            PtwAudio.Play(PtwSfx.Unlock, 0.8f, open ? 1f : 0.8f);
            Haptics.Play(HapticKind.Gate);
            // A heavy slab starting to move should be felt: a flinch on the slab and a shake.
            if (slab) { var p = slab.GetComponent<Punch>(); if (p) p.Hit(0.5f); }
            if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.22f);
        }

        void Update()
        {
            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(t, target))
            {
                t = Mathf.MoveTowards(t, target, speed * Time.deltaTime);
                Apply();
            }
            else if (open != lastOpen)
            {
                lastOpen = open;
                Apply();
            }
        }

        void Apply()
        {
            if (slab)
                slab.localPosition = slabClosedLocalPos
                                   + localDirection.normalized * (travel * t);

            if (thresholdRenderer)
            {
                thresholdRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionColorId, thresholdLit * (t * 2.2f));
                thresholdRenderer.SetPropertyBlock(mpb);
            }
        }
    }
}
