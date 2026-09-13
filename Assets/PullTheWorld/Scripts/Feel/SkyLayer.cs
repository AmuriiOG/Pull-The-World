using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// One painted layer of the sky - a mountain ridge or a cloud bank - on a quad that is a child
    /// of the camera, at its own distance down the line of sight. Placed by where its painted
    /// CONTENT should land on screen (viewport fractions) and sized by how tall that content
    /// should be as a fraction of the frame, measured at the layer's own distance; so the same
    /// composition holds on every level's framing and on every screen shape, and the quad's
    /// transparent margins never enter into it.
    ///
    /// It never turns with the world - the sky is the still reference the turning island is read
    /// against - but it is not welded to the screen either. It moves in three small ways, each
    /// scaled by <see cref="parallax"/>, which stands in for depth (far ridge ~0.05, foreground
    /// cloud ~0.35):
    ///
    ///  * PUSH. While the camera travels to the next level it lags behind the lens by its share
    ///    of <see cref="SkyParallax.TravelLagWorld"/> and catches up as the camera settles, so a
    ///    near cloud swells and sinks past while a far ridge barely stirs - the depth of the
    ///    scene, read off the one move the camera makes. Zero at rest, zero velocity at the end.
    ///  * TILT and FOLLOW. A fraction of <see cref="SkyParallax.Offset"/>: as the level tips and
    ///    the orb travels, the sky leans a little, near layers most.
    ///  * SWAY. Clouds drift a hair sideways on a slow sine, each with its own phase, so the sky
    ///    is alive without the composition ever wandering.
    /// </summary>
    [ExecuteAlways]
    public class SkyLayer : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;
        [Tooltip("Distance down the line of sight. Everything behind the level after next is 170+.")]
        [SerializeField] float distance = 300f;

        [Header("Placement (of the painted content)")]
        [Tooltip("Where the anchor lands, as viewport fractions.")]
        [SerializeField] Vector2 viewportPos = new Vector2(0.5f, 0.5f);
        [Tooltip("The canvas point that is placed at viewportPos, as fractions with y from the TOP: " +
                 "a ridge's peak, a cloud's top.")]
        [SerializeField] Vector2 anchor = new Vector2(0.5f, 0.5f);
        [Tooltip("Height of the content as a fraction of the frame height.")]
        [SerializeField] float heightFraction = 0.1f;
        [Tooltip("Canvas width / height of the sprite.")]
        [SerializeField] float aspect = 2f;
        [Tooltip("Content size within the canvas, as fractions.")]
        [SerializeField] Vector2 contentSize = new Vector2(0.9f, 0.6f);

        [Header("Motion")]
        [Tooltip("Share of the sky's parallax this layer takes: the push lag, the tilt lean. " +
                 "Far ridge ~0.05, mid cloud ~0.2, foreground cloud ~0.35.")]
        [SerializeField, Range(0f, 1f)] float parallax = 0.1f;
        [Tooltip("Sideways sway amplitude as a fraction of the frame width. Zero for ridges.")]
        [SerializeField] float swayAmplitude;
        [SerializeField] float swayPeriod = 60f;

        float swayPhase;

        void OnEnable()
        {
            if (!targetCamera) targetCamera = GetComponentInParent<Camera>();
            // Spread the clouds' sway out so they do not breathe as one.
            swayPhase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
            Fit();
        }

        void LateUpdate() => Fit();

        public void Fit()
        {
            if (!targetCamera) return;
            float aspectScreen = targetCamera.pixelHeight > 0
                ? targetCamera.pixelWidth / (float)targetCamera.pixelHeight
                : 0.5625f;

            // Half the frame at this layer's distance, before any push lag.
            float halfH = targetCamera.orthographic
                ? targetCamera.orthographicSize
                : distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * aspectScreen;

            // The quad from the content: tall enough that the painted part is heightFraction of
            // the frame, wide by the canvas aspect, and placed so the ANCHOR sits at viewportPos.
            float quadH = heightFraction / Mathf.Max(0.05f, contentSize.y) * 2f * halfH;
            float quadW = quadH * aspect;
            float x = (viewportPos.x - 0.5f) * 2f * halfW - (anchor.x - 0.5f) * quadW;
            float y = (viewportPos.y - 0.5f) * 2f * halfH + (anchor.y - 0.5f) * quadH;

            // Tilt / follow lean, specified for a ~20-unit frame and scaled with this frame.
            float shiftScale = halfH / 10f;
            Vector2 lean = SkyParallax.Offset * parallax * shiftScale;
            x += lean.x;
            y += lean.y;

            // Sway.
            if (Application.isPlaying && swayAmplitude > 0f && swayPeriod > 0.1f)
                x += Mathf.Sin(Time.time * (Mathf.PI * 2f / swayPeriod) + swayPhase) * swayAmplitude * 2f * halfW;

            // Push lag: the layer stays behind while the lens moves on, by its share. In camera
            // space that is closer and lower, which is what things do when you fly over them.
            // The forward part is capped so a near cloud can never reach the lens.
            Vector3 lag = Vector3.zero;
            if (SkyParallax.TravelLagWorld.sqrMagnitude > 1e-8f)
            {
                lag = targetCamera.transform.InverseTransformDirection(SkyParallax.TravelLagWorld) * parallax;
                lag.z = Mathf.Min(lag.z, distance * 0.45f);
            }

            transform.localRotation = Quaternion.identity;
            transform.localPosition = new Vector3(x - lag.x, y - lag.y, distance - lag.z);
            transform.localScale = new Vector3(quadW, quadH, 1f);
        }
    }
}
