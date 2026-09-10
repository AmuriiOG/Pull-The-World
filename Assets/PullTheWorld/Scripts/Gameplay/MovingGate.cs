using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A block that slides out of the way when a plate is pressed.
    /// Carries its own kinematic Rigidbody so it is a separate PhysX actor rather than part of
    /// the world compound - that way it can move independently without forcing a compound rebuild.
    /// </summary>
    public class MovingGate : MonoBehaviour
    {
        [Header("Travel (local space)")]
        [SerializeField] Vector3 closedOffset = Vector3.zero;
        [SerializeField] Vector3 openOffset = new Vector3(0f, -1.05f, 0f);
        [SerializeField] float travelSpeed = 3.2f;
        [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("State")]
        [SerializeField] bool open;

        [Header("Feel")]
        [SerializeField] ParticleSystem moveVfx;
        [SerializeField] float shakeOnArrive = 0.18f;

        Vector3 basePos;
        float t;          // 0 = closed, 1 = open
        bool moving;

        public bool IsOpen => open;
        public float Openness => t;

        void Awake()
        {
            basePos = transform.localPosition;
            t = open ? 1f : 0f;
            Apply();
        }

        /// <summary>Hook a PressurePlate.onChanged straight into this.</summary>
        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            moving = true;
            if (moveVfx) moveVfx.Play();
            PtwAudio.Play(value ? PtwSfx.Unlock : PtwSfx.PlateOff);
            Haptics.Light();
        }

        public void Toggle() => SetOpen(!open);

        void Update()
        {
            if (!moving) return;

            float target = open ? 1f : 0f;
            t = Mathf.MoveTowards(t, target, travelSpeed * Time.deltaTime);
            Apply();

            if (Mathf.Approximately(t, target))
            {
                moving = false;
                if (WorldRig.Instance) WorldRig.Instance.AddShake(shakeOnArrive);
                if (moveVfx) moveVfx.Stop();
            }
        }

        void Apply()
        {
            float e = ease.Evaluate(Mathf.Clamp01(t));
            transform.localPosition = basePos + Vector3.Lerp(closedOffset, openOffset, e);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 root = Application.isPlaying ? basePos : transform.localPosition;
            Transform p = transform.parent;
            Vector3 a = p ? p.TransformPoint(root + closedOffset) : root + closedOffset;
            Vector3 b = p ? p.TransformPoint(root + openOffset) : root + openOffset;
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(b, 0.12f);
        }
#endif
    }
}
