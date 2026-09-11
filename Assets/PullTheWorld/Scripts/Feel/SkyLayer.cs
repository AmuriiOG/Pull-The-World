using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A background layer welded to the camera: mountains, cloud puffs, far islets. Positioned by
    /// VIEWPORT fraction and sized to the frustum, so it lands in the same place on screen whatever
    /// the level's framing, and it never drifts with the world - the backdrop is the static
    /// reference the moving island is judged against. Clouds may drift slowly on their own axis;
    /// mountains never do.
    ///
    /// For a unit mesh (x -0.5..0.5, y 0..1): the base sits at <see cref="viewportBottom"/> and
    /// the top at <see cref="viewportTop"/>, width is the frustum width times <see cref="widthScale"/>.
    /// For a quad sprite (x,y -0.5..0.5) use <see cref="centred"/>: placed at (viewportX, viewportY)
    /// with an explicit world size.
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
        [SerializeField] Vector2 worldSize = new Vector2(6f, 3f);
        [Header("Drift")]
        [Tooltip("World units per second along x. Wraps at the frustum edge. Zero for mountains.")]
        [SerializeField] float driftSpeed;

        float driftX;

        void OnEnable() { if (!targetCamera) targetCamera = GetComponentInParent<Camera>(); Fit(); }

        void LateUpdate()
        {
            if (Application.isPlaying && Mathf.Abs(driftSpeed) > 0.0001f) driftX += driftSpeed * Time.deltaTime;
            Fit();
        }

        public void Fit()
        {
            if (!targetCamera || !targetCamera.orthographic) return;
            float aspect = targetCamera.pixelHeight > 0
                ? targetCamera.pixelWidth / (float)targetCamera.pixelHeight
                : 0.5625f;
            float halfH = targetCamera.orthographicSize;
            float halfW = halfH * aspect;

            transform.localRotation = Quaternion.identity;
            if (centred)
            {
                float wrapW = halfW * 2f + worldSize.x;
                float x = (viewportPos.x - 0.5f) * 2f * halfW + driftX;
                x = Mathf.Repeat(x + wrapW * 0.5f, wrapW) - wrapW * 0.5f;
                float y = (viewportPos.y - 0.5f) * 2f * halfH;
                transform.localPosition = new Vector3(x, y, distance);
                transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
            }
            else
            {
                float bottom = (viewportBottom - 0.5f) * 2f * halfH;
                float top = (viewportTop - 0.5f) * 2f * halfH;
                transform.localPosition = new Vector3(driftX, bottom, distance);
                transform.localScale = new Vector3(halfW * 2f * widthScale, Mathf.Max(0.01f, top - bottom), 1f);
            }
        }
    }
}
