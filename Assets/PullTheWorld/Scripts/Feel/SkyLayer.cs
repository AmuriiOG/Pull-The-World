using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A background layer welded to the camera: mountains, cloud puffs, far islets. Positioned by
    /// VIEWPORT fraction and sized to the frustum, so it lands in the same place on screen whatever
    /// the level's framing. It never moves with the world's rotation - the backdrop is still the
    /// reference the turning island is read against - but it is not frozen either:
    ///
    ///  * <see cref="driftSpeed"/>: clouds glide sideways on their own and wrap at the frustum
    ///    edge, the near ones faster than the far ones. Ridges do not drift (they cannot wrap).
    ///  * <see cref="parallax"/>: each layer takes a fraction of <see cref="SkyParallax.Offset"/>,
    ///    a small shared shift driven by the level's tilt and the orb's position. Far ridge ~0.1,
    ///    a cloud in front of the island ~1. It is a translation, never a rotation, so it adds
    ///    depth without re-creating the "which one is turning" ambiguity.
    ///  * <see cref="bobAmplitude"/>: a slow vertical float, for the islets.
    ///
    /// For a unit mesh (x -0.5..0.5, y 0..1): the base sits at <see cref="viewportBottom"/> and
    /// the top at <see cref="viewportTop"/>, width is the frustum width times <see cref="widthScale"/>.
    /// For a quad sprite (x,y -0.5..0.5) use <see cref="centred"/>: placed at (viewportX, viewportY)
    /// and sized as a fraction of the frame, so it looks the same however far back it sits.
    ///
    /// Works for a perspective camera as well as an orthographic one: the frame is measured at
    /// the layer's own distance, which is what lets the ridges stand hundreds of units back,
    /// behind the levels waiting in the distance, and still fill the same part of the screen.
    /// </summary>
    [ExecuteAlways]
    public class SkyLayer : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;
        [SerializeField] float distance = 60f;
        [Header("Strip (unit mesh, base at y 0)")]
        [SerializeField] float viewportBottom = 0f;
        [SerializeField] float viewportTop = 0.5f;
        [SerializeField] float widthScale = 1.6f;
        [Header("Centred sprite")]
        [SerializeField] bool centred;
        [SerializeField] Vector2 viewportPos = new Vector2(0.5f, 0.5f);
        [Tooltip("Width and height as fractions of the frame at this layer's distance.")]
        [SerializeField] Vector2 viewportSize = new Vector2(0.5f, 0.15f);
        [Header("Motion")]
        [Tooltip("World units per second along x. Wraps at the frustum edge. Zero for mountains.")]
        [SerializeField] float driftSpeed;
        [Tooltip("Share of the SkyParallax offset this layer takes. Far ridge ~0.1, mid clouds " +
                 "~0.3, a cloud in front of the island 1.")]
        [SerializeField, Range(0f, 1.5f)] float parallax;
        [Tooltip("Slow vertical float in world units (islets). Zero for everything else.")]
        [SerializeField] float bobAmplitude;
        [SerializeField] float bobHz = 0.07f;

        float driftX, bobPhase;

        void OnEnable()
        {
            if (!targetCamera) targetCamera = GetComponentInParent<Camera>();
            // Spread the islets' bobbing out so they do not rise and fall as one.
            bobPhase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
            Fit();
        }

        void LateUpdate()
        {
            if (Application.isPlaying && Mathf.Abs(driftSpeed) > 0.0001f) driftX += driftSpeed * SkyParallax.DriftBoost * Time.deltaTime;
            Fit();
        }

        public void Fit()
        {
            if (!targetCamera) return;
            float aspect = targetCamera.pixelHeight > 0
                ? targetCamera.pixelWidth / (float)targetCamera.pixelHeight
                : 0.5625f;
            // Half the frame at this layer's distance.
            float halfH = targetCamera.orthographic
                ? targetCamera.orthographicSize
                : distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * aspect;
            // The parallax shift is specified for a ~20-unit-tall frame; scale it with the frame
            // so a far layer moves the same fraction of the screen whatever its distance.
            float shiftScale = halfH / 10f;

            Vector2 shift = (SkyParallax.Offset + SkyParallax.TravelOffset) * parallax * shiftScale;
            float bob = Application.isPlaying && bobAmplitude > 0f
                ? Mathf.Sin(Time.time * bobHz * Mathf.PI * 2f + bobPhase) * bobAmplitude * shiftScale
                : 0f;

            transform.localRotation = Quaternion.identity;
            if (centred)
            {
                var size = new Vector2(viewportSize.x * 2f * halfW, viewportSize.y * 2f * halfH);
                float wrapW = halfW * 2f + size.x;
                float x = (viewportPos.x - 0.5f) * 2f * halfW + driftX * shiftScale + shift.x;
                x = Mathf.Repeat(x + wrapW * 0.5f, wrapW) - wrapW * 0.5f;
                float y = (viewportPos.y - 0.5f) * 2f * halfH + shift.y + bob;
                transform.localPosition = new Vector3(x, y, distance);
                transform.localScale = new Vector3(size.x, size.y, 1f);
            }
            else
            {
                float bottom = (viewportBottom - 0.5f) * 2f * halfH;
                float top = (viewportTop - 0.5f) * 2f * halfH;
                transform.localPosition = new Vector3(driftX * shiftScale + shift.x, bottom + shift.y, distance);
                transform.localScale = new Vector3(halfW * 2f * widthScale, Mathf.Max(0.01f, top - bottom), 1f);
            }
        }
    }
}
