using UnityEngine;
using UnityEngine.UI;

namespace PullTheWorld
{
    /// <summary>
    /// A real sliding switch on top of a plain uGUI Toggle: the knob travels from the left end of
    /// the track to the right, and the sage "on" track cross-fades over the clay "off" one.
    ///
    /// A bare Toggle can only show or hide one graphic, which is why the first settings panel lit
    /// the whole track green instead of moving anything. This reads the Toggle's state every
    /// frame rather than subscribing to onValueChanged, so it also follows values that UiRoot
    /// pushes in silently when the panel opens. Unscaled time: settings is open while paused.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class UiSwitch : MonoBehaviour
    {
        [SerializeField] Toggle toggle;
        [Tooltip("Drawn on top of the off track; faded in as the switch turns on.")]
        [SerializeField] Image trackOn;
        [SerializeField] RectTransform knob;
        [Tooltip("Knob centre offset from the track centre at either end, in canvas units.")]
        [SerializeField] float travel = 54f;
        [Tooltip("Higher is snappier.")]
        [SerializeField] float speed = 18f;

        float t = -1f;   // 0 = off, 1 = on; negative until first read

        void Awake()
        {
            if (!toggle) toggle = GetComponent<Toggle>();
        }

        void OnEnable() => Snap();

        /// <summary>Jump to the current state with no slide. Used when the panel (re)appears.</summary>
        public void Snap()
        {
            if (!toggle) return;
            t = toggle.isOn ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            if (!toggle) return;
            float target = toggle.isOn ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;

            t = Mathf.Lerp(t, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(t - target) < 0.003f) t = target;
            Apply();
        }

        void Apply()
        {
            float e = Mathf.SmoothStep(0f, 1f, t);
            if (knob)
            {
                var p = knob.anchoredPosition;
                p.x = Mathf.Lerp(-travel, travel, e);
                knob.anchoredPosition = p;
            }
            if (trackOn)
            {
                var c = trackOn.color;
                c.a = e;
                trackOn.color = c;
            }
        }
    }
}
