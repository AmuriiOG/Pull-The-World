using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A slow breathing scale, for the one thing on a screen you want the thumb to go to. Used on
    /// PLAY. Unscaled time, because menus can be up while the game is paused.
    /// </summary>
    public class UiPulse : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.2f)] float amplitude = 0.035f;
        [SerializeField] float period = 1.6f;

        Vector3 baseScale;
        float t;

        void Awake() { baseScale = transform.localScale; t = Random.value * period; }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float s = 1f + Mathf.Sin(t / Mathf.Max(0.1f, period) * Mathf.PI * 2f) * amplitude;
            transform.localScale = baseScale * s;
        }
    }
}
