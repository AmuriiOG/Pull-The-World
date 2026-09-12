using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A slow vertical float for the small rocks that decorate the world between the levels. A
    /// translation only, and a tiny one: enough that the sky reads as alive, never enough to
    /// compete with the island that is turning or the level waiting in the distance.
    /// </summary>
    public class SkyBob : MonoBehaviour
    {
        [SerializeField] float amplitude = 0.15f;
        [SerializeField] float hz = 0.05f;

        Vector3 basePosition;
        float phase;

        void Awake()
        {
            basePosition = transform.localPosition;
            phase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
        }

        void Update()
        {
            float y = Mathf.Sin(Time.time * hz * Mathf.PI * 2f + phase) * amplitude;
            transform.localPosition = basePosition + Vector3.up * y;
        }
    }
}
