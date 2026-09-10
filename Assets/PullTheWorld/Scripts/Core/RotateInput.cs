using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace PullTheWorld
{
    /// <summary>
    /// Turns a finger into a rotation request and does nothing else. Keeping gesture recognition
    /// out of WorldRotator means the feel can be retuned without touching the input, and the input
    /// can be swapped or driven by a test without touching the feel.
    ///
    /// The gesture is a TURNTABLE GRAB: whatever angle your finger sweeps around the centre of the
    /// level, the level turns by. One finger, no modes, no buttons. It is worth doing this properly
    /// rather than mapping horizontal drag straight to degrees, because the turntable version means
    /// the spot under your finger stays under your finger - the world feels grabbed rather than
    /// scrolled, and a circular gesture, a horizontal swipe and a vertical swipe at the edge all
    /// just work without being special-cased.
    ///
    /// The one place it breaks down is near the pivot, where a tiny finger movement sweeps a huge
    /// angle and the rotation goes wild. Inside <see cref="minPivotRadius"/> pixels it falls back
    /// to plain horizontal drag, signed to match the way the turntable would have moved for a grip
    /// below centre (the common one).
    ///
    /// Sign convention, since it is easy to get backwards: screen pixels are y-up, so an
    /// anticlockwise finger sweep gives a positive atan2 delta. Unity rotates X toward Y for a
    /// positive angle about +Z, and the camera looks down +Z with up = +Y, so a positive Z angle
    /// also reads as anticlockwise on screen. The two agree and no inversion is needed.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RotateInput : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] WorldRotator rotator;

        [Header("Turntable")]
        [Tooltip("Degrees of world rotation per degree of finger sweep. 1 = the world tracks your " +
                 "finger exactly, which is the whole point. Lower it only to make a level heavier.")]
        [SerializeField] float gain = 1f;
        [Tooltip("Inside this many pixels of the pivot the sweep angle gets too noisy to use, so " +
                 "the gesture falls back to horizontal drag.")]
        [SerializeField] float minPivotRadius = 110f;
        [Tooltip("Degrees per pixel for the near-the-pivot fallback.")]
        [SerializeField] float fallbackGain = 0.32f;
        [Tooltip("Ignore the first fraction of a degree so a resting thumb does not creep the world.")]
        [SerializeField] float deadzone = 0.15f;
        [Tooltip("Flip if rotation feels backwards on a device.")]
        [SerializeField] bool invert;

        [Header("Editor mouse")]
        [SerializeField] float keyRotateSpeed = 95f;

        [Header("Fling")]
        [Tooltip("Seconds of sweep history used to estimate release speed. Short enough to feel " +
                 "like the flick you just did, long enough not to be one noisy frame.")]
        [SerializeField] float flingWindow = 0.09f;

        [Header("Enable / disable")]
        [SerializeField] bool inputEnabled = true;

        // gesture state
        bool dragging;
        int fingerId = -1;
        Vector2 lastPos;
        Vector2 pivot;
        bool usingFallback;

        // fling estimation
        float velEstimate;      // degrees/sec, smoothed
        float lastDelta;

        public bool InputEnabled
        {
            get => inputEnabled;
            set { if (!value) Cancel(); inputEnabled = value; }
        }

        void Awake()
        {
            if (!rotator) rotator = WorldRotator.Instance
                ? WorldRotator.Instance
                : FindFirstObjectByType<WorldRotator>();
        }

        void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        void OnDisable() => Cancel();

        void Cancel()
        {
            if (dragging && rotator) rotator.EndDrive(0f);
            dragging = false;
            fingerId = -1;
            velEstimate = 0f;
        }

        void Update()
        {
            if (!rotator) return;

            if (!inputEnabled || !rotator.RotationAllowed)
            {
                if (dragging) Cancel();
                return;
            }

            pivot = PivotScreenPoint();

            if (Application.isMobilePlatform || ETouch.activeTouches.Count > 0) StepTouch();
            else StepMouse();

            StepKeyboard();
        }

        /// <summary>Screen position of the level centre, which is what the finger sweeps around.</summary>
        Vector2 PivotScreenPoint()
        {
            var cam = rotator.ViewCamera;
            if (!cam) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 p = cam.WorldToScreenPoint(rotator.WorldRoot.position);
            return new Vector2(p.x, p.y);
        }

        // -------------------------------------------------------------------------- touch ---
        void StepTouch()
        {
            var touches = ETouch.activeTouches;

            if (!dragging)
            {
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    if (t.phase != UnityEngine.InputSystem.TouchPhase.Began) continue;
                    if (OverUI(t.screenPosition)) continue;
                    Begin(t.touchId, t.screenPosition);
                    break;
                }
                return;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t.touchId != fingerId) continue;

                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        Move(t.screenPosition);
                        return;
                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        End();
                        return;
                }
            }

            // The finger vanished without an Ended phase (happens on focus loss).
            End();
        }

        // -------------------------------------------------------------------------- mouse ---
        void StepMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pos = mouse.position.ReadValue();

            if (!dragging)
            {
                if (mouse.leftButton.wasPressedThisFrame && !OverUI(pos)) Begin(-1, pos);
                return;
            }

            if (mouse.leftButton.isPressed) Move(pos);
            else End();
        }

        void StepKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null || dragging) return;

            float dir = 0f;
            if (kb.qKey.isPressed) dir += 1f;
            if (kb.eKey.isPressed) dir -= 1f;
            if (Mathf.Approximately(dir, 0f)) return;

            // Keyboard nudges bypass the drag state machine but still go through the same spring,
            // so a held key and a slow drag produce identical motion.
            rotator.BeginDrive();
            rotator.Drive(dir * keyRotateSpeed * Time.deltaTime);
            rotator.EndDrive(0f);
        }

        // ------------------------------------------------------------------ gesture core ----
        void Begin(int finger, Vector2 pos)
        {
            dragging = true;
            fingerId = finger;
            lastPos = pos;
            velEstimate = 0f;
            lastDelta = 0f;
            usingFallback = (pos - pivot).magnitude < minPivotRadius;
            rotator.BeginDrive();
        }

        void Move(Vector2 pos)
        {
            float delta = MeasureDelta(lastPos, pos);
            lastPos = pos;

            if (Mathf.Abs(delta) < deadzone) delta = 0f;
            if (invert) delta = -delta;
            delta *= gain;

            rotator.Drive(delta);

            // Exponential window on degrees/sec so the fling reads as the flick you just did.
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            float instant = delta / dt;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, flingWindow));
            velEstimate = Mathf.Lerp(velEstimate, instant, k);
            lastDelta = delta;
        }

        /// <summary>
        /// Swept angle around the pivot, or horizontal drag when the finger is too close to it.
        /// The mode is latched per-gesture rather than re-tested every frame: a finger that
        /// wanders across the radius threshold mid-drag would otherwise change the control law
        /// underneath the player.
        /// </summary>
        float MeasureDelta(Vector2 from, Vector2 to)
        {
            if (usingFallback) return (to.x - from.x) * fallbackGain;

            Vector2 a = from - pivot;
            Vector2 b = to - pivot;
            if (a.sqrMagnitude < 1f || b.sqrMagnitude < 1f) return 0f;

            // Signed angle in screen space, anticlockwise positive.
            return Mathf.Atan2(a.x * b.y - a.y * b.x, a.x * b.x + a.y * b.y) * Mathf.Rad2Deg;
        }

        void End()
        {
            dragging = false;
            fingerId = -1;
            // If the finger was held still at the end, do not fling - players expect a stop.
            float fling = Mathf.Abs(lastDelta) > 1e-4f ? velEstimate : 0f;
            rotator.EndDrive(fling);
            velEstimate = 0f;
        }

        static bool OverUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            return hits.Count > 0;
        }
    }
}
