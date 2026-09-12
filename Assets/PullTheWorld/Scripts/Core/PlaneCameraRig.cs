using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Places the one and only camera, frames whichever level is live, and - the one time it ever
    /// moves - pushes forward through the world to the next one.
    ///
    /// Yaw is zero and the camera looks straight down +Z at the XY puzzle plane: the mechanic is a
    /// rotation about world Z, and a yawed camera would turn that clean screen-plane spin into a
    /// skewed tumble. What survives from the old isometric look is the PITCH, and it earns more of
    /// the frame than it looks like it should: downward tilt is what reveals the top faces of the
    /// blocks, and the top face is where the grass is. Do not push it much past 20.
    ///
    /// The lens is a NARROW perspective, not an orthographic one. The world is laid out in depth
    /// now (see LevelManager.SlotFor): the level you are playing stands 70-odd units in front of
    /// the lens and the next two stand further down the same line of sight, so they have to get
    /// smaller with distance or they would simply sit on top of each other. At 18 degrees the
    /// island itself is still as good as flat - a block a unit deep changes size by under two
    /// percent front to back - and the turntable gesture is still read off the pivot's screen
    /// position, so nothing about the mechanic's legibility is spent. What the perspective buys is
    /// real parallax on the one move the camera makes.
    ///
    /// Framing is by DISTANCE at a fixed focal length: the level's worst-case extents (computed by
    /// the generator across its rotation range) decide how far back the camera stands so they
    /// fit. A level that can only tip 40 degrees is framed much tighter than one that spins
    /// freely, which is the difference between an island that fills the screen and one that sits
    /// in the middle of it looking like a model.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-250)]
    public class PlaneCameraRig : MonoBehaviour
    {
        [Header("Lens")]
        [Tooltip("Downward tilt. Small on purpose - see the class comment. Above ~20 degrees the " +
                 "rotation stops reading as a flat spin.")]
        [SerializeField, Range(0f, 26f)] float pitch = 20f;
        [Tooltip("Vertical field of view. Narrow, so the live island stays a readable diagram of " +
                 "itself while the levels behind it still recede.")]
        [SerializeField, Range(8f, 40f)] float fieldOfView = 18f;

        [Header("Framing")]
        [Tooltip("What the camera is centred on. Left empty it uses `focus`.")]
        [SerializeField] Transform lookTarget;
        [Tooltip("World point framed at centreViewportPoint when there is no lookTarget: the active " +
                 "level's pivot. LevelManager snaps it on a load and pushes it forward on a win.")]
        [SerializeField] Vector3 focus;
        [Tooltip("Minimum world units visible horizontally at the focus plane. On a portrait phone " +
                 "this is normally the binding constraint, because a level's rotation extents are " +
                 "about square and the screen is not.")]
        [SerializeField] float minViewWidth = 11f;
        [Tooltip("Minimum world units visible vertically at the focus plane.")]
        [SerializeField] float minViewHeight = 19f;
        [Tooltip("Where the level pivot sits on screen. Below centre so the sky above the island " +
                 "is free for the next two levels standing in the distance, like the mockup's " +
                 "far islands, while the tilt cue keeps its room at the bottom.")]
        [SerializeField] Vector2 centreViewportPoint = new Vector2(0.5f, 0.46f);

        [Header("Travel")]
        [Tooltip("How far the camera rises over the middle of a push, so it clears the finished " +
                 "island rather than skimming its portal. Small - the move is forward, not up.")]
        [SerializeField] float travelLift = 4f;
        [Tooltip("The lens widens by this fraction over the middle of a push and settles back on " +
                 "arrival: a touch of wide-angle exaggerates the rush of the world going past.")]
        [SerializeField, Range(0f, 0.4f)] float travelWiden = 0.10f;

        [Header("Clipping")]
        [SerializeField] float nearClip = 0.05f;
        [Tooltip("Far enough for the level after next and the mountain ridges behind it.")]
        [SerializeField] float farClip = 700f;

        Camera cam;
        float lastAspect = -1f;

        public Camera Cam => cam ? cam : (cam = GetComponent<Camera>());

        void OnEnable() { cam = GetComponent<Camera>(); Apply(); }
        void OnValidate() { cam = GetComponent<Camera>(); Apply(); }

        // There is deliberately no "kick" here any more. Earlier builds zoomed the lens a few
        // percent on level load, on a win and on arrival and let a spring settle it; every one of
        // those read as a small pop exactly at the moments that must feel continuous - the first
        // frame of a level, the last frame of the push. The camera now does nothing it is not
        // asked to do: it stands, or it travels, and the travel ends in the standing state.

        // ---- travel: the one time this camera moves ------------------------------------------
        // Between levels the camera pushes FORWARD, from its stand in front of the finished island
        // to its stand in front of the next one, which was already there in the distance. It eases
        // in and out, lifts a little over the middle so it sails over the finished island rather
        // than through its portal, and widens the lens a touch at speed. The finished island
        // swells and slides out under the bottom of the frame; the next grows from a distant
        // silhouette into the level. During play the camera is bolted down exactly as before.
        bool travelling;
        float travelT, travelSeconds;
        Vector3 travelFrom, travelTo;
        Vector2 extFrom, extTo;
        float lift, widen;

        public bool IsTravelling => travelling;
        public Vector3 Focus => focus;
        /// <summary>0 while idle, 0..1 through a push. Read by SkyParallax for the travel parallax.</summary>
        public static float TravelProgress { get; private set; }

        /// <summary>Cut straight to a framing: level load, restart, menu.</summary>
        public void SnapTo(Vector3 worldFocus, Vector2 extents)
        {
            travelling = false;
            TravelProgress = 0f;
            lift = 0f;
            widen = 0f;
            SkyParallax.TravelOffset = Vector2.zero;
            SkyParallax.DriftBoost = 1f;
            focus = worldFocus;
            FrameExtents(extents);
        }

        /// <summary>The framing extents currently in force.</summary>
        public Vector2 Extents => new Vector2(minViewWidth, minViewHeight);

        /// <summary>
        /// Push to a framing over <paramref name="seconds"/>. Poll <see cref="IsTravelling"/>. A
        /// travel that stays on the same focus (the menu easing into a level's framing) is a pure
        /// zoom: no lift, no lens widening - those belong to a real journey through the world.
        /// </summary>
        public void TravelTo(Vector3 worldFocus, Vector2 extents, float seconds)
        {
            travelFrom = focus;
            travelTo = worldFocus;
            extFrom = new Vector2(minViewWidth, minViewHeight);
            extTo = new Vector2(Mathf.Max(1f, extents.x), Mathf.Max(1f, extents.y));
            travelSeconds = Mathf.Max(0.05f, seconds);
            travelT = 0f;
            inPlace = (travelTo - travelFrom).sqrMagnitude < 0.01f;
            travelling = true;
        }

        bool inPlace;

        void Update()
        {
            if (travelling && Application.isPlaying)
            {
                travelT += Time.deltaTime / travelSeconds;
                float u = Mathf.Clamp01(travelT);
                // Smootherstep: zero velocity AND zero acceleration at both ends, so the last frame
                // of the push is, to the pixel, the standing frame that gameplay continues from.
                // The lift and the widening ride on a bump built from the same curve (up over the
                // first half, back down over the second), so they too arrive with zero velocity.
                // A plain sin(pi u) was the one thing still moving at the handoff: it is zero at
                // u = 1 but its slope is not, so the camera was still descending from the lift at
                // several units a second when the push stopped - a small, definite snap.
                float e = Smoother(u);
                float bump = inPlace ? 0f : Smoother(u < 0.5f ? 2f * u : 2f * (1f - u));
                focus = Vector3.Lerp(travelFrom, travelTo, e);
                Vector2 ext = Vector2.Lerp(extFrom, extTo, e);
                minViewWidth = ext.x;
                minViewHeight = ext.y;
                lift = travelLift * bump;
                widen = travelWiden * bump;
                TravelProgress = u;
                // The foreground clouds sink out of the way and hurry sideways as the camera
                // sails past them; the ridges, welded to the lens, barely notice.
                SkyParallax.TravelOffset = new Vector2(0f, -bump * 2.5f);
                SkyParallax.DriftBoost = 1f + 3f * bump;
                if (u >= 1f)
                {
                    travelling = false;
                    TravelProgress = 0f;
                    SkyParallax.TravelOffset = Vector2.zero;
                    SkyParallax.DriftBoost = 1f;
                    focus = travelTo;
                    minViewWidth = extTo.x;
                    minViewHeight = extTo.y;
                    lift = 0f;
                    widen = 0f;
                }
                Apply();
                return;
            }

            // Otherwise only recomputes on a real resolution change, so this is free at runtime
            // and, more to the point, the camera cannot drift by itself.
            if (!Mathf.Approximately(lastAspect, CurrentAspect())) Apply();
        }

        /// <summary>6x^5 - 15x^4 + 10x^3: zero first AND second derivative at both ends.</summary>
        static float Smoother(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }

        float CurrentAspect()
        {
            if (cam && cam.pixelHeight > 0) return cam.pixelWidth / (float)cam.pixelHeight;
            return Screen.height > 0 ? Screen.width / (float)Screen.height : 1080f / 1920f;
        }

        /// <summary>Frame a specific level's worst-case rotation extents, called on every level load.</summary>
        public void FrameExtents(Vector2 extents)
        {
            minViewWidth = Mathf.Max(1f, extents.x);
            minViewHeight = Mathf.Max(1f, extents.y);
            Apply();
        }

        /// <summary>
        /// How far in front of the lens a level framed with <paramref name="extents"/> stands.
        /// LevelManager uses it to lay the levels out so each one's successors land where the
        /// mockup's far islands are. Portrait is assumed (the width constraint binds), so the
        /// layout does not depend on the device's exact shape.
        /// </summary>
        public float StandDistance(Vector2 extents)
        {
            float tanHalf = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float forHeight = Mathf.Max(1f, extents.y) * 0.5f / tanHalf;
            float forWidth = Mathf.Max(1f, extents.x) * 0.5f / (tanHalf * (9f / 16f));
            return Mathf.Max(forHeight, forWidth);
        }

        /// <summary>The camera's forward and up in the world for the fixed pitch. Shared with the layout.</summary>
        public Quaternion Attitude => Quaternion.Euler(pitch, 0f, 0f);

        public void Apply()
        {
            if (!cam) cam = GetComponent<Camera>();
            if (!cam) return;

            float aspect = CurrentAspect();
            lastAspect = aspect;

            cam.orthographic = false;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;

            // The stand distance comes from the BASE lens, so the travel widening is a pure zoom:
            // the camera does not move and the pivot stays put on screen.
            float tanHalf = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float forHeight = minViewHeight * 0.5f / tanHalf;
            float forWidth = minViewWidth * 0.5f / (tanHalf * Mathf.Max(0.01f, aspect));
            float distance = Mathf.Max(forHeight, forWidth);
            cam.fieldOfView = Mathf.Clamp(fieldOfView * (1f + widen), 2f, 120f);

            // Yaw stays 0. Roll stays 0. Only pitch.
            Quaternion rot = Attitude;
            transform.rotation = rot;

            Vector3 target = lookTarget ? lookTarget.position : focus;
            float viewH = 2f * distance * tanHalf;
            float viewW = viewH * aspect;

            Vector3 offset = -(rot * Vector3.right) * ((centreViewportPoint.x - 0.5f) * viewW)
                             - (rot * Vector3.up) * ((centreViewportPoint.y - 0.5f) * viewH);

            transform.position = target - (rot * Vector3.forward) * distance + offset + Vector3.up * lift;

            // The backdrop and every sky layer are welded to the camera and sized from its frustum,
            // so they have to be refitted in the same breath as the framing. See ScreenFillQuad.Fit.
            // (The capture path renders to a portrait texture from a window that may be a different
            // shape; without this the layers were fitted to the window and landed half off the shot.)
            if (!backdrop) backdrop = GetComponentInChildren<ScreenFillQuad>(true);
            if (backdrop) backdrop.Fit();
            if (skyLayers == null || skyLayers.Length == 0) skyLayers = GetComponentsInChildren<SkyLayer>(true);
            foreach (var layer in skyLayers) if (layer) layer.Fit();
        }

        ScreenFillQuad backdrop;
        SkyLayer[] skyLayers;
    }
}
