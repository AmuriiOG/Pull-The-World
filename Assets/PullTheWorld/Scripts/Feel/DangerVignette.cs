using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PullTheWorld
{
    /// <summary>
    /// The screen edges darken and redden, with a slow heartbeat, while an enemy is hunting the
    /// player. Reads <see cref="Enemy.CurrentThreat"/> - the strongest alert enemy's closeness -
    /// and drives the post-process vignette on top of its authored values. Works on the Volume's
    /// runtime profile copy, so nothing here ever dirties the asset.
    /// </summary>
    public class DangerVignette : MonoBehaviour
    {
        [SerializeField] Volume volume;
        [Tooltip("Vignette intensity added at full threat.")]
        [SerializeField] float extraIntensity = 0.16f;
        [SerializeField] Color dangerColor = new Color(0.32f, 0.02f, 0.04f);
        [SerializeField] float heartbeatHz = 1.3f;

        Vignette vignette;
        float baseIntensity, threat;
        Color baseColor;

        void Start()
        {
            if (!volume) volume = FindFirstObjectByType<Volume>();
            if (volume && volume.profile && volume.profile.TryGet(out vignette))
            {
                baseIntensity = vignette.intensity.value;
                baseColor = vignette.color.value;
            }
        }

        void Update()
        {
            if (vignette == null) return;

            float want = Enemy.CurrentThreat;
            // Comes on faster than it goes: being noticed is sudden, calming down is not.
            threat = Mathf.MoveTowards(threat, want, Time.unscaledDeltaTime * (want > threat ? 2.5f : 1.1f));

            float beat = 0.7f + 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * heartbeatHz * Mathf.PI)), 3f);
            vignette.intensity.value = baseIntensity + extraIntensity * threat * beat;
            vignette.color.value = Color.Lerp(baseColor, dangerColor, threat * 0.8f);
        }
    }
}
