using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Keeps a quad exactly filling an orthographic camera's frustum.
    ///
    /// The backdrop is a child of the camera rather than a skybox or a UI layer, which means it is
    /// welded to the screen and provably cannot drift. That matters: the fixed backdrop is the
    /// reference against which the player reads the world as the thing that is moving.
    /// </summary>
    [ExecuteAlways]
    public class ScreenFillQuad : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;
        [Tooltip("How far in front of the camera to sit. Must be inside the far clip plane.")]
        [SerializeField] float distance = 100f;
        [Tooltip("A little overscan so no seam shows at any aspect ratio.")]
        [SerializeField] float padding = 1.06f;

        float lastSize = -1f, lastAspect = -1f;

        void OnEnable() { if (!targetCamera) targetCamera = GetComponentInParent<Camera>(); Fit(); }
        void LateUpdate() { Fit(); }

        void Fit()
        {
            if (!targetCamera || !targetCamera.orthographic) return;

            float aspect = targetCamera.pixelHeight > 0
                ? targetCamera.pixelWidth / (float)targetCamera.pixelHeight
                : 0.5625f;
            float size = targetCamera.orthographicSize;
            if (Mathf.Approximately(size, lastSize) && Mathf.Approximately(aspect, lastAspect)) return;
            lastSize = size; lastAspect = aspect;

            transform.localPosition = new Vector3(0f, 0f, distance);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(size * 2f * aspect * padding, size * 2f * padding, 1f);
        }
    }
}
