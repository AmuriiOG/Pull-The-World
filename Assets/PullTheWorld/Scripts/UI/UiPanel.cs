using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A screen or overlay that fades and pops in and out. Every panel in the game is one of these,
    /// so all the transitions match without any of them owning tween code.
    ///
    /// Runs on unscaled time on purpose: the settings overlay is allowed to appear while the game
    /// is paused, and a panel that animates on scaled time would freeze half-open.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPanel : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [Tooltip("Higher is snappier. Mobile UI wants this fast - anything under about 8 feels sluggish.")]
        [SerializeField] float speed = 13f;
        [Tooltip("Scale the panel springs up from. 1 disables the pop and just cross-fades.")]
        [SerializeField] float popFrom = 0.9f;
        [Tooltip("Start hidden without a frame of visible flicker.")]
        [SerializeField] bool startHidden = true;

        float alpha, target;

        public bool Visible => target > 0.5f;
        /// <summary>True once a hide has fully finished, so the object can be deactivated.</summary>
        public bool FullyHidden => target < 0.5f && alpha < 0.005f;

        void Awake()
        {
            if (!group) group = GetComponent<CanvasGroup>();
            if (startHidden) { alpha = target = 0f; }
            else { alpha = target = 1f; }
            Push();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            target = 1f;
        }

        public void Hide() => target = 0f;

        public void SetVisible(bool v) { if (v) Show(); else Hide(); }

        /// <summary>Snap with no animation. Used when the flow jumps, e.g. on a restart.</summary>
        public void SetImmediate(bool v)
        {
            target = alpha = v ? 1f : 0f;
            if (v) gameObject.SetActive(true);
            Push();
            if (!v) gameObject.SetActive(false);
        }

        void Update()
        {
            if (Mathf.Approximately(alpha, target))
            {
                // Deactivate once fully faded so hidden panels cost nothing and cannot be clicked.
                if (FullyHidden && gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }

            alpha = Mathf.Lerp(alpha, target,
                               1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(alpha - target) < 0.004f) alpha = target;
            Push();
        }

        void Push()
        {
            if (!group) return;
            group.alpha = alpha;
            group.interactable = target > 0.5f;
            group.blocksRaycasts = target > 0.5f;

            if (popFrom < 0.999f)
                transform.localScale = Vector3.one * Mathf.Lerp(popFrom, 1f, EaseOutBack(alpha));
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }
    }
}
