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

        Rigidbody rb;

        void OnEnable()
        {
            if (!rb) rb = GetComponent<Rigidbody>();
            DynamicRegistry.Register(rb);
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
