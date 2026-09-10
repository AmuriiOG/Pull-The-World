using UnityEngine;
using UnityEngine.EventSystems;

namespace PullTheWorld
{
    /// <summary>
    /// Press feedback for every button: shrinks while the finger is down, then pops back with an
    /// overshoot on release. On a phone there is no hover, so this is the ONLY confirmation that a
    /// tap landed on the thing you meant - which is why it goes on every button, not just the
    /// important ones. Unscaled time, because settings is open while paused.
    /// </summary>
    public class UiButtonJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] float pressedScale = 0.92f;
        [SerializeField] float releaseOvershoot = 0.06f;
        [SerializeField] float frequency = 5f;
        [SerializeField, Range(0.1f, 1f)] float damping = 0.5f;

        Vector3 baseScale;
        float scale = 1f, velocity, target = 1f;
        bool down;

        void Awake() => baseScale = transform.localScale;

        public void OnPointerDown(PointerEventData e)
        {
            down = true;
            target = pressedScale;
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) { if (down) Release(); }

        void Release()
        {
            down = false;
            target = 1f;
            // Kick past rest so it pops, then the spring settles it.
            velocity += releaseOvershoot * 2f * Mathf.PI * frequency;
        }

        void Update()
        {
            if (Mathf.Abs(scale - target) < 0.0005f && Mathf.Abs(velocity) < 0.0005f) return;
            Spring.Step(ref scale, ref velocity, target, frequency, damping, Time.unscaledDeltaTime);
            transform.localScale = baseScale * scale;
        }
    }
}
