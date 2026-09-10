using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Goes on anything with a dynamic Rigidbody that the puzzle depends on: boulders, crates,
    /// keys, enemies, breakables. Two jobs, both small.
    ///
    /// 1. Registers with <see cref="DynamicRegistry"/> so WorldRotator can wake sleeping bodies
    ///    when the level turns, without a scene search inside FixedUpdate.
    /// 2. Clamps speed, for exactly the same reason PlayerBody does - and it turns out props need
    ///    it more than the player does. The level is one big KINEMATIC compound body being rotated
    ///    into things, and when a fast spin sweeps a floor into a resting rock, PhysX resolves that
    ///    penetration with an impulse that has no upper bound. The PhysicsNeverExplodes test caught
    ///    a boulder on level 10 leaving at over 80 m/s, which fires it through a wall and out of the
    ///    world. Clamping here is much better than slowing the rotation down, because the rotation
    ///    speed is the feel of the game.
    ///
    /// This lives in its own file, and it has to. It was originally declared alongside the registry
    /// in DynamicRegistry.cs, which compiles perfectly happily and then fails at runtime in a way
    /// that is genuinely hard to read: Unity resolves a MonoBehaviour's script asset by FILE NAME,
    /// so a behaviour in a file named after something else serializes with m_Script: {fileID: 0}.
    /// The symptom was every prefab containing a boulder refusing to save, with only
    /// "You are trying to save a Prefab with a missing script" to go on.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DynamicProp : MonoBehaviour
    {
        [Tooltip("Hard ceiling on linear speed. Generous enough that a real fall is unaffected, " +
                 "low enough that a solver impulse cannot fire the prop out of the level.")]
        [SerializeField] float maxSpeed = 18f;
        [Tooltip("Hard ceiling on spin, in radians per second. A rock spinning faster than this " +
                 "reads as broken rather than as fast.")]
        [SerializeField] float maxAngularSpeed = 30f;
        [Tooltip("How hard water pushes this up, in gravities when fully submerged. Below 1 sinks. " +
                 "Rocks sink; that is what lets a rock sit on a plate under a pool.")]
        [SerializeField] float buoyancy = 0.55f;

        public float Buoyancy => buoyancy;

        [Header("Impact feel")]
        [Tooltip("Impact speed for a full squash and a full dust burst.")]
        [SerializeField] float impactReference = 6f;
        [SerializeField] float minImpactSpeed = 1.8f;
        [Tooltip("Optional. Found by name (Visual / ImpactVfx) if left empty.")]
        [SerializeField] Punch visualPunch;
        [SerializeField] ParticleSystem impactVfx;

        Rigidbody rb;

        void Awake()
        {
            if (!visualPunch)
            {
                var v = transform.Find("Visual");
                if (v) visualPunch = v.GetComponent<Punch>();
            }
            if (!impactVfx)
            {
                var fx = transform.Find("ImpactVfx");
                if (fx) impactVfx = fx.GetComponent<ParticleSystem>();
            }
        }

        void OnEnable()
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            DynamicRegistry.Register(rb);
        }

        /// <summary>
        /// A rock that lands with a squash, a puff and a thud reads as heavy. One that stops dead
        /// reads as a placeholder. v1's Pushable did this and it was lost in the rewrite.
        /// </summary>
        void OnCollisionEnter(Collision c)
        {
            float speed = c.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;
            float strength = Mathf.Clamp01(speed / Mathf.Max(0.01f, impactReference));

            if (visualPunch) visualPunch.Hit(strength * 0.8f);
            if (impactVfx && c.contactCount > 0)
            {
                impactVfx.transform.position = c.GetContact(0).point;
                impactVfx.Emit(Mathf.Max(1, Mathf.RoundToInt(6f * strength)));
            }
            PtwAudio.Play(PtwSfx.Impact, Mathf.Lerp(0.15f, 0.6f, strength), Mathf.Lerp(0.8f, 0.6f, strength));
            if (strength > 0.5f && WorldRotator.Instance) WorldRotator.Instance.AddShake(0.25f * strength);
        }

        void OnDisable() => DynamicRegistry.Unregister(rb);

        void FixedUpdate()
        {
            if (!rb || rb.isKinematic) return;

            var v = rb.linearVelocity;
            float sqr = v.sqrMagnitude;
            if (sqr > maxSpeed * maxSpeed)
                rb.linearVelocity = v * (maxSpeed / Mathf.Sqrt(sqr));

            var w = rb.angularVelocity;
            float wSqr = w.sqrMagnitude;
            if (wSqr > maxAngularSpeed * maxAngularSpeed)
                rb.angularVelocity = w * (maxAngularSpeed / Mathf.Sqrt(wSqr));
        }
    }
}
