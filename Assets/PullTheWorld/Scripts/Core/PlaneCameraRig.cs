using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Places the one and only camera and then leaves it alone forever.
    ///
    /// v1 used a 46/45 isometric rig. v2 cannot: the mechanic is a rotation about world Z, and a
    /// yawed camera turns that clean screen-plane spin into a skewed tumble, which is precisely the
    /// ambiguity the whole redesign exists to remove. So yaw is zero and the camera looks straight
    /// down +Z at the XY puzzle plane.
    ///
    /// What survives from the isometric look is the PITCH, and it earns more of the frame than it
    /// looks like it should. Downward tilt is what reveals the top faces of the blocks - and the
    /// top face is where the GRASS is. At 13 degrees the first captures were a wall of grey stone
    /// sides with a thin green line along the top; at 20 the same island reads as green. That is
    /// the trade to understand here: pitch buys colour, and it costs fidelity on the rotation
    /// reading as a flat spin (cos 20 is 0.94, still invisible; cos 40 would not be). Do not push
    /// it much past this.
    ///
    /// Orthographic is non-negotiable for two reasons. With no perspective divide the turntable
    /// gesture is a true 1:1 grab at every radius, and every 1x1 block is the same size on screen
    /// wherever it sits, so a level reads as a readable diagram of itself.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-250)]
    public class PlaneCameraRig : MonoBehaviour
    {
        [Header("Angle")]
        [Tooltip("Downward tilt. Small on purpose - see the class comment. Above ~20 degrees the " +
                 "rotation stops reading as a flat spin.")]
        [SerializeField, Range(0f, 26f)] float pitch = 20f;
        [SerializeField] float distance = 34f;

        [Header("Framing")]
        [Tooltip("What the camera is centred on. Left empty it uses the world origin, which is " +
                 "where every level pivots.")]
        [SerializeField] Transform lookTarget;
        [Tooltip("Minimum world units visible horizontally. On a portrait phone the height " +
                 "constraint normally wins, so this is the safety net for a wide screen.")]
        [SerializeField] float minViewWidth = 11f;
        [Tooltip("Minimum world units visible vertically. This is the real composition dial in " +
                 "portrait: it decides how much empty margin surrounds the island.")]
        [SerializeField] float minViewHeight = 19f;
        [Tooltip("Where the level centre sits on screen. Slightly above centre leaves room for " +
                 "the bottom HUD without pushing the island under it.")]
        [SerializeField] Vector2 centreViewportPoint = new Vector2(0.5f, 0.54f);

        [Header("Clipping")]
        [SerializeField] float nearClip = 0.05f;
        [SerializeField] float farClip = 120f;

        Camera cam;
        float lastAspect = -1f;

        public Camera Cam => cam ? cam : (cam = GetComponent<Camera>());

        void OnEnable() { cam = GetComponent<Camera>(); Apply(); }
        void OnValidate() { cam = GetComponent<Camera>(); Apply(); }

        float zoom = 1f, zoomVel;

        /// <summary>
        /// A momentary zoom that springs back to 1 over about half a second. Above 1 on level load
        /// (the island arrives), below 1 on a win (lean in). Visual only; framing math is unchanged.
        /// </summary>
        public void Kick(float scale)
        {
            zoom = Mathf.Clamp(scale, 0.7f, 1.4f);
            zoomVel = 0f;
        }

        void Update()
        {
            bool settling = Mathf.Abs(zoom - 1f) > 0.0005f || Mathf.Abs(zoomVel) > 0.0005f;
            if (settling) Spring.Step(ref zoom, ref zoomVel, 1f, 2.4f, 0.85f, Time.unscaledDeltaTime);

            // Otherwise only recomputes on a real resolution change, so this is free at runtime.
            if (settling || !Mathf.Approximately(lastAspect, CurrentAspect())) Apply();
        }

        float CurrentAspect()
        {
            if (cam && cam.pixelHeight > 0) return cam.pixelWidth / (float)cam.pixelHeight;
            return Screen.height > 0 ? Screen.width / (float)Screen.height : 1080f / 1920f;
        }

        /// <summary>
        /// Frame a specific level, called on every level load.
        ///
        /// One global framing does not work here, and the reason is the mechanic. A level is spun
        /// through arbitrary angles, so the box it occupies on screen changes constantly - an 11x6
        /// island becomes 6x11 at ninety degrees. The generator therefore hands over the worst-case
        /// extents across each level's own rotation range (see PtwLevels), and the camera simply
        /// obeys. A level that can only tip 40 degrees gets framed much tighter than one that can
        /// be spun freely, which is the difference between an island that fills the screen and one
        /// that sits in the middle of it looking like a model.
        /// </summary>
        public void FrameExtents(Vector2 extents)
        {
            minViewWidth = Mathf.Max(1f, extents.x);
            minViewHeight = Mathf.Max(1f, extents.y);
            Apply();
        }

        public void Apply()
        {
            if (!cam) cam = GetComponent<Camera>();
            if (!cam) return;

            float aspect = CurrentAspect();
            lastAspect = aspect;

            cam.orthographic = true;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;

            // Satisfy whichever minimum is the binding constraint on this device.
            float sizeForWidth = minViewWidth / (2f * Mathf.Max(0.01f, aspect));
            float sizeForHeight = minViewHeight * 0.5f;
            float orthoSize = Mathf.Max(sizeForWidth, sizeForHeight) * zoom;
            cam.orthographicSize = orthoSize;

            // Yaw stays 0. Roll stays 0. Only pitch.
            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            transform.rotation = rot;

            Vector3 target = lookTarget ? lookTarget.position : Vector3.zero;
            float viewH = orthoSize * 2f;
            float viewW = viewH * aspect;

            Vector3 offset = -(rot * Vector3.right) * ((centreViewportPoint.x - 0.5f) * viewW)
                             - (rot * Vector3.up) * ((centreViewportPoint.y - 0.5f) * viewH);

            transform.position = target - (rot * Vector3.forward) * distance + offset;

            // The backdrop and every sky layer are welded to the camera and sized from its frustum,
            // so they have to be refitted in the same breath as the framing. See ScreenFillQuad.Fit.
            // (The capture path renders to a portrait texture from a window that may be a different
            // shape; without this the layers were fitted to the window and the far islets landed
            // half off the shot.)
            if (!backdrop) backdrop = GetComponentInChildren<ScreenFillQuad>(true);
            if (backdrop) backdrop.Fit();
            if (skyLayers == null || skyLayers.Length == 0) skyLayers = GetComponentsInChildren<SkyLayer>(true);
            foreach (var layer in skyLayers) if (layer) layer.Fit();
        }

        ScreenFillQuad backdrop;
        SkyLayer[] skyLayers;
    }
}
