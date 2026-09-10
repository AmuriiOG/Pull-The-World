using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Implicit (unconditionally stable) damped spring integration.
    /// Used for every "weighted" motion in the game so the feel is consistent:
    /// world drag, world rotation, camera-less shake decay and UI pops.
    /// </summary>
    public static class Spring
    {
        /// <param name="frequency">Oscillations per second. Higher = snappier.</param>
        /// <param name="damping">1 = critically damped (no overshoot), &lt;1 = springy overshoot.</param>
        public static void Step(ref Vector3 value, ref Vector3 velocity, Vector3 target,
                                float frequency, float damping, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * Mathf.Max(0.0001f, frequency);
            float f = 1f + 2f * dt * damping * omega;
            float oo = omega * omega;
            float hoo = dt * oo;
            float hhoo = dt * hoo;
            float detInv = 1f / (f + hhoo);
            Vector3 detX = f * value + dt * velocity + hhoo * target;
            Vector3 detV = velocity + hoo * (target - value);
            value = detX * detInv;
            velocity = detV * detInv;
        }

        public static void Step(ref float value, ref float velocity, float target,
                                float frequency, float damping, float dt)
        {
            if (dt <= 0f) return;
            float omega = 2f * Mathf.PI * Mathf.Max(0.0001f, frequency);
            float f = 1f + 2f * dt * damping * omega;
            float oo = omega * omega;
            float hoo = dt * oo;
            float hhoo = dt * hoo;
            float detInv = 1f / (f + hhoo);
            float detX = f * value + dt * velocity + hhoo * target;
            float detV = velocity + hoo * (target - value);
            value = detX * detInv;
            velocity = detV * detInv;
        }

        /// <summary>Frame-rate independent exponential decay toward zero.</summary>
        public static float Decay(float value, float halfLife, float dt)
        {
            if (halfLife <= 0f) return 0f;
            return value * Mathf.Pow(0.5f, dt / halfLife);
        }

        public static Vector3 Decay(Vector3 value, float halfLife, float dt)
        {
            if (halfLife <= 0f) return Vector3.zero;
            return value * Mathf.Pow(0.5f, dt / halfLife);
        }
    }
}
