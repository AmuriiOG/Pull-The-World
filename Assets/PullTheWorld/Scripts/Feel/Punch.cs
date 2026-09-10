using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A scale punch with overshoot: call <see cref="Hit"/> and the object swells and rings back
    /// to rest on a damped spring. This is most of what "juicy" means in practice, and it needs no
    /// tween library - the project already has an unconditionally stable spring integrator.
    ///
    /// Works on world transforms and RectTransforms alike. Runs on unscaled time so UI still
    /// punches while the game is paused. Do NOT put this on anything that already drives its own
    /// localScale (PlayerBody's visual, WaterVolume's body); the two would fight.
    /// </summary>
    public class Punch : MonoBehaviour
    {
        [Tooltip("Oscillations per second of the ring-down. Higher is snappier.")]
        [SerializeField] float frequency = 3.4f;
        [Tooltip("Under 1 overshoots; that overshoot IS the effect.")]
        [SerializeField, Range(0.1f, 1f)] float damping = 0.42f;
        [Tooltip("Scale amplitude a Hit(1) produces, roughly.")]
        [SerializeField] float scalePerStrength = 0.22f;
        [Tooltip("Cap so stacked hits cannot balloon the object.")]
        [SerializeField] float maxAmplitude = 0.5f;

        Vector3 baseScale;
        float amount, velocity;
        bool baseCaptured;

        void Awake() => CaptureBase();

        void CaptureBase()
        {
            if (baseCaptured) return;
            baseScale = transform.localScale;
            baseCaptured = true;
        }

        /// <summary>Kick the spring. Strength 1 is a full punch; small taps stack up.</summary>
        public void Hit(float strength = 1f)
        {
            CaptureBase();
            // Convert a scale amplitude into a spring velocity kick: v = A * omega.
            float omega = 2f * Mathf.PI * frequency;
            velocity += strength * scalePerStrength * omega;
        }

        /// <summary>Re-read the rest scale if something else legitimately resized the object.</summary>
        public void Rebase()
        {
            baseCaptured = false;
            amount = velocity = 0f;
            CaptureBase();
        }

        void LateUpdate()
        {
            if (Mathf.Abs(amount) < 0.0005f && Mathf.Abs(velocity) < 0.0005f)
            {
                if (amount != 0f) { amount = 0f; transform.localScale = baseScale; }
                return;
            }

            Spring.Step(ref amount, ref velocity, 0f, frequency, damping, Time.unscaledDeltaTime);
            amount = Mathf.Clamp(amount, -maxAmplitude, maxAmplitude);
            transform.localScale = baseScale * (1f + amount);
        }
    }
}
