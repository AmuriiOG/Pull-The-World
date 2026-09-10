using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A crate that blocks the way until something heavy hits it hard enough.
    ///
    /// The threshold is mass AND speed, and the mass floor is set just above the player's, so the
    /// ball can lean on it all day and nothing happens: only a rock arriving with real momentum
    /// breaks it. That is the whole lesson - the rock is the tool, you are not.
    ///
    /// Detection is an explicit query in FixedUpdate, not a collision callback. The crate is a child
    /// collider of the level's compound kinematic body, and a rock rolling along the floor is
    /// ALREADY touching that body when it reaches the crate - PhysX reports the new contact as a
    /// Stay on the same actor pair, so OnCollisionEnter on the rock never fires (that is exactly how
    /// the first version failed its test). Hazard and PressurePlate query for the same reason.
    /// </summary>
    public class Breakable : MonoBehaviour
    {
        [Header("Threshold")]
        [Tooltip("Approach speed, along the line to the crate, needed to break it. The mass floor " +
                 "below is what keeps the player out; this only has to rule out a slow lean. A rock " +
                 "two cells uphill on a 40-degree tilt arrives at about 3.4.")]
        [SerializeField] float breakSpeed = 3.0f;
        [Tooltip("Minimum mass of the impactor. The player is 1.0 and must not qualify.")]
        [SerializeField] float breakMass = 1.2f;
        [Tooltip("How close a fast body has to be, beyond what it will cover this step, to count as " +
                 "hitting. Absorbs contact offset and interpolation.")]
        [SerializeField] float contactSlack = 0.06f;

        [Header("References")]
        [SerializeField] GameObject intact;
        [SerializeField] Collider blocker;
        [SerializeField] ParticleSystem debrisVfx;

        bool broken;

        public bool Broken => broken;

        void Awake()
        {
            if (!blocker) blocker = GetComponentInChildren<Collider>();
            if (!intact && blocker) intact = blocker.gameObject;
        }

        void FixedUpdate()
        {
            if (broken || !blocker) return;

            // The level turns, so the crate moves too; judge the approach relative to it.
            var levelBody = blocker.attachedRigidbody;
            Vector3 crateVel = levelBody ? levelBody.GetPointVelocity(blocker.bounds.center) : Vector3.zero;

            var bodies = DynamicRegistry.Bodies;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var rb = bodies[i];
                if (!rb || rb.mass < breakMass) continue;

                Vector3 com = rb.worldCenterOfMass;
                Vector3 onCrate = blocker.ClosestPoint(com);
                Vector3 toCrate = onCrate - com;
                float dist = toCrate.magnitude;
                if (dist < 1e-4f) continue;                     // centre inside the box: not a hit, a glitch

                float approach = Vector3.Dot(rb.linearVelocity - crateVel, toCrate / dist);
                if (approach < breakSpeed) continue;

                // Gap between the two surfaces, so it works for any collider shape the tool has.
                var col = rb.GetComponentInChildren<Collider>();
                if (!col) continue;
                float gap = Vector3.Distance(onCrate, col.ClosestPoint(onCrate));
                if (gap > approach * Time.fixedDeltaTime + contactSlack) continue;

                Break();
                return;
            }
        }

        /// <summary>Direct hit report (e.g. from a body's own collision callback). Returns true if it broke.</summary>
        public bool TryBreak(float relativeSpeed, float mass)
        {
            if (broken || mass < breakMass || relativeSpeed < breakSpeed) return false;
            Break();
            return true;
        }

        void Break()
        {
            broken = true;
            if (blocker) blocker.enabled = false;
            if (intact) intact.SetActive(false);
            if (debrisVfx)
            {
                debrisVfx.transform.SetParent(null, true);   // outlive the crate
                debrisVfx.Emit(20);
                Destroy(debrisVfx.gameObject, 2.5f);
            }
            PtwAudio.Play(PtwSfx.Smother, 1f, 0.9f);
            Haptics.Play(HapticKind.Break);
            if (WorldRotator.Instance) WorldRotator.Instance.AddShake(0.55f);
        }
    }
}
