using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace PullTheWorld
{
    /// <summary>
    /// Translates raw pointers into world intent, and nothing else. Keeping gesture recognition
    /// out of WorldRig means the feel can be retuned without touching the input, and the input can
    /// be swapped (or driven by a test) without touching the feel.
    ///
    /// Mobile : one finger drags, two fingers twist.
    /// Editor : left mouse drags, right mouse drags to twist, Q / E twist by keyboard.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WorldInput : MonoBehaviour
    {
        [SerializeField] WorldRig rig;

        [Header("Twist")]
        [Tooltip("Degrees of world spin per degree of finger twist.")]
        [SerializeField] float twistGain = 1f;
        [Tooltip("Ignore the first few degrees so a two finger drag does not spin the world.")]
        [SerializeField] float twistDeadzone = 3.5f;
        [SerializeField] float mouseTwistGain = 0.32f;
        [SerializeField] float keyTwistSpeed = 110f;
        [Tooltip("Flip if twisting feels backwards on your device.")]
        [SerializeField] bool invertTwist;

        [Header("Feedback")]
        [Tooltip("Play a tick every N degrees of rotation.")]
        [SerializeField] float tickEvery = 15f;

        [Header("Enable / disable")]
        [SerializeField] bool inputEnabled = true;

        // gesture state
        bool dragging;
        int dragFingerId = -1;
        bool twisting;
        float lastTwistAngle;
        float twistAccum;
        float tickAccum;
        Vector2 lastMousePos;
        bool mouseTwisting;

        public bool InputEnabled { get => inputEnabled; set { if (!value) CancelAll(); inputEnabled = value; } }

        void Awake() { if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>(); }

        void OnEnable()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        void OnDisable()
        {
            CancelAll();
            if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
        }

        void CancelAll()
        {
            if (rig)
            {
                if (dragging) rig.EndDrag();
                if (twisting || mouseTwisting) rig.EndSpin();
            }
            dragging = twisting = mouseTwisting = false;
            dragFingerId = -1;
        }

        void Update()
        {
            if (!rig || !inputEnabled) return;

            bool handledByTouch = Touchscreen.current != null && ETouch.activeTouches.Count > 0;
            if (handledByTouch) HandleTouch();
            else HandleMouseAndKeys();
        }

        // ------------------------------------------------------------------------- touch ----
        void HandleTouch()
        {
            var touches = ETouch.activeTouches;

            if (touches.Count >= 2)
            {
                // Two fingers: twist. A drag in progress is handed off cleanly.
                if (dragging) { rig.EndDrag(); dragging = false; dragFingerId = -1; }

                Vector2 a = touches[0].screenPosition;
                Vector2 b = touches[1].screenPosition;
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

                if (!twisting)
                {
                    twisting = true;
                    twistAccum = 0f;
                    lastTwistAngle = angle;
                    rig.BeginSpin();
                }
                else
                {
                    float delta = Mathf.DeltaAngle(lastTwistAngle, angle);
                    lastTwistAngle = angle;
                    twistAccum += delta;

                    if (Mathf.Abs(twistAccum) > twistDeadzone)
                    {
                        // Screen angle grows counter-clockwise; world spin is clockwise-positive.
                        ApplySpin(-delta * twistGain);
                    }
                }
                return;
            }

            if (twisting) { rig.EndSpin(); twisting = false; }

            if (touches.Count == 1)
            {
                var t = touches[0];
                switch (t.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (IsOverUI(t.touchId)) break;
                        dragFingerId = t.touchId;
                        dragging = true;
                        rig.BeginDrag(t.screenPosition);
                        PtwAudio.Play(PtwSfx.Grab);
                        Haptics.Light();
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (dragging && t.touchId == dragFingerId) rig.UpdateDrag(t.screenPosition);
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        if (dragging && t.touchId == dragFingerId)
                        {
                            rig.EndDrag();
                            dragging = false;
                            dragFingerId = -1;
                            PtwAudio.Play(PtwSfx.Release, 0.6f);
                        }
                        break;
                }
            }
            else if (dragging)
            {
                rig.EndDrag();
                dragging = false;
                dragFingerId = -1;
            }
        }

        // ------------------------------------------------------------- mouse and keyboard ---
        void HandleMouseAndKeys()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();

                if (mouse.leftButton.wasPressedThisFrame && !IsOverUI(-1))
                {
                    dragging = true;
                    rig.BeginDrag(pos);
                    PtwAudio.Play(PtwSfx.Grab);
                    Haptics.Light();
                }
                else if (mouse.leftButton.isPressed && dragging)
                {
                    rig.UpdateDrag(pos);
                }
                else if (mouse.leftButton.wasReleasedThisFrame && dragging)
                {
                    dragging = false;
                    rig.EndDrag();
                    PtwAudio.Play(PtwSfx.Release, 0.6f);
                }

                if (mouse.rightButton.wasPressedThisFrame)
                {
                    mouseTwisting = true;
                    lastMousePos = pos;
                    rig.BeginSpin();
                }
                else if (mouse.rightButton.isPressed && mouseTwisting)
                {
                    // Dragging right turns the world clockwise, like spinning a wheel to the right.
                    ApplySpin((pos.x - lastMousePos.x) * mouseTwistGain);
                    lastMousePos = pos;
                }
                else if (mouse.rightButton.wasReleasedThisFrame && mouseTwisting)
                {
                    mouseTwisting = false;
                    rig.EndSpin();
                }
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            float dir = 0f;
            if (kb.qKey.isPressed) dir -= 1f;
            if (kb.eKey.isPressed) dir += 1f;

            if (dir != 0f)
            {
                if (!twisting) { twisting = true; rig.BeginSpin(); }
                ApplySpin(dir * keyTwistSpeed * Time.deltaTime);
            }
            else if (twisting)
            {
                twisting = false;
                rig.EndSpin();
            }

            if (kb.rKey.wasPressedThisFrame && LevelManager.Instance) LevelManager.Instance.Restart();
            if (kb.nKey.wasPressedThisFrame && LevelManager.Instance) LevelManager.Instance.Next();
        }

        void ApplySpin(float degrees)
        {
            if (invertTwist) degrees = -degrees;
            rig.AddSpin(degrees);
            tickAccum += Mathf.Abs(degrees);
            if (tickEvery > 0.01f && tickAccum >= tickEvery)
            {
                tickAccum = 0f;
                PtwAudio.Play(PtwSfx.SpinTick, 0.5f, Random.Range(0.95f, 1.08f));
                Haptics.Light();
            }
        }

        static bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }
    }
}
