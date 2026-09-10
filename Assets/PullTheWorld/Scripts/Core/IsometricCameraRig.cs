using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Places the one and only camera. It is orthographic and it never moves for the whole game.
    ///
    /// Orthographic matters for more than looks: with no perspective divide, one screen pixel maps
    /// to a constant world distance, so a drag can be a perfect 1:1 "the spot under my finger stays
    /// under my finger". That is the difference between grabbing the world and scrolling it.
    ///
    /// Framing adapts to the device aspect so a tall phone and a short one both get a sensible
    /// composition, with the player parked slightly below centre to leave room for the HUD.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-250)]
    public class IsometricCameraRig : MonoBehaviour
    {
        [Header("Angle")]
        [Tooltip("Measured off the reference sheet's LEVEL 1 panel, which is far more top-down " +
                 "than a standard 30-degree iso: you see big top faces and only a sliver of side. " +
                 "Pushed as high as it can go before the door arch starts to look squashed.")]
        [SerializeField] float pitch = 46f;
        [SerializeField] float yaw = 45f;
        [SerializeField] float distance = 40f;

        [Header("Framing")]
        [SerializeField] Transform lookTarget;
        [Tooltip("Minimum world units visible horizontally. On a portrait phone this is what " +
                 "actually sets the zoom, so it is the main composition dial.")]
        [SerializeField] float minViewWidth = 8.2f;
        [Tooltip("Minimum world units visible vertically.")]
        [SerializeField] float minViewHeight = 11.5f;
        [Tooltip("Where the player sits on screen. Slightly below centre leaves room for the " +
                 "level to extend upward, which is where the door usually is.")]
        [SerializeField] Vector2 anchorViewportPoint = new Vector2(0.5f, 0.42f);

        [Header("Clipping")]
        [SerializeField] float nearClip = 0.05f;
        [SerializeField] float farClip = 120f;

        Camera cam;
        float lastAspect = -1f;

        void OnEnable() { cam = GetComponent<Camera>(); ApplyFraming(); }
        void OnValidate() { cam = GetComponent<Camera>(); ApplyFraming(); }

        void Update()
        {
            // Only recomputes on an actual resolution change, so this is free at runtime.
            if (!Mathf.Approximately(lastAspect, CurrentAspect())) ApplyFraming();
        }

        float CurrentAspect()
        {
            if (cam && cam.pixelHeight > 0) return cam.pixelWidth / (float)cam.pixelHeight;
            return Screen.height > 0 ? Screen.width / (float)Screen.height : 0.5625f;
        }

        public void ApplyFraming()
        {
            if (!cam) cam = GetComponent<Camera>();
            if (!cam) return;

            float aspect = CurrentAspect();
            lastAspect = aspect;

            cam.orthographic = true;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;

            // Satisfy BOTH minimums, whichever is the binding constraint on this device.
            float sizeForWidth = minViewWidth / (2f * Mathf.Max(0.01f, aspect));
            float sizeForHeight = minViewHeight * 0.5f;
            float orthoSize = Mathf.Max(sizeForWidth, sizeForHeight);
            cam.orthographicSize = orthoSize;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;

            Vector3 target = lookTarget ? lookTarget.position : Vector3.zero;
            float viewH = orthoSize * 2f;
            float viewW = viewH * aspect;

            // Shift the camera so the anchor lands on the requested viewport point.
            Vector3 offset = -(rot * Vector3.right) * ((anchorViewportPoint.x - 0.5f) * viewW)
                             - (rot * Vector3.up) * ((anchorViewportPoint.y - 0.5f) * viewH);

            transform.position = target - (rot * Vector3.forward) * distance + offset;

            // WorldRig derives its rotation axis from the camera, so keep it in step.
            if (Application.isPlaying && WorldRig.Instance) WorldRig.Instance.CacheSpinAxis();
        }
    }
}
