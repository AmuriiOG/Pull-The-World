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

        [Header("Lost overboard")]
        [Tooltip("Distance from the world origin beyond which the prop counts as gone. Same as the player's.")]
        [SerializeField] float fallRadius = 26f;
        [Tooltip("Put the prop back at its start cell when it leaves the world. On for puzzle tools - a " +
                 "lost rock would soft-lock a plate or crate level - and off for enemies, which die instead.")]
        [SerializeField] bool respawnIfLost = true;

        [Header("Impact feel")]
        [Tooltip("Impact speed for a full squash and a full dust burst.")]
        [SerializeField] float impactReference = 6f;
        [SerializeField] float minImpactSpeed = 1.8f;
        [Tooltip("Optional. Found by name (Visual / ImpactVfx) if left empty.")]
        [SerializeField] Punch visualPunch;
        [SerializeField] ParticleSystem impactVfx;

        Rigidbody rb;
        Transform spawnParent;      // the level root, so the start cell turns with the level
        Vector3 spawnLocal;

        void Awake()
        {
            spawnParent = transform.parent;
            spawnLocal = transform.localPosition;
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
            // PhysX's default spin ceiling is 7 rad/s: a 0.34 m ball would have to SLIDE above
            // 2.4 m/s. Our own clamp is the ceiling; let the body actually roll up to it.
            rb.maxAngularVelocity = maxAngularSpeed;
            DynamicRegistry.Register(rb);
        }

        /// <summary>
        /// The squeeze. Enter alone missed a whole class of kills: a rock and an enemy rolling
        /// downhill together touch once, gently (Enter, under crush speed), and from then on only
        /// Stay fires - so when the enemy hit a wall and the rock slammed into it at 11 m/s,
        /// nothing noticed and level 21 jammed with the rock parked against a live enemy. The
        /// contact impulse sees the slam: divided by the rock's mass it is the speed this contact
        /// took off the rock in one step, ~9 m/s for the wall slam, well under 1 m/s for a rock
        /// resting on or rolling beside an enemy (m*g*dt is 0.7 N s here). Enemy.Crush applies
        /// its own mass and speed thresholds, so this is the same rule from a second sensor.
        /// </summary>
        void OnCollisionStay(Collision c)
        {
            var enemy = c.collider.GetComponentInParent<Enemy>();
            if (!enemy || enemy.gameObject == gameObject) return;
            if (!rb) rb = GetComponent<Rigidbody>();
            float takenOff = c.impulse.magnitude / Mathf.Max(0.01f, rb.mass);
            enemy.Crush(takenOff, rb.mass);
        }

        /// <summary>
        /// A rock that lands with a squash, a puff and a thud reads as heavy. One that stops dead
        /// reads as a placeholder. v1's Pushable did this and it was lost in the rewrite.
        /// </summary>
        void OnCollisionEnter(Collision c)
        {
            // Closing speed along the normal (see Impacts): a rock rolling along a row of blocks
            // used to thud at every seam.
            float speed = Impacts.ClosingSpeed(c);

            // The rock is the tool. Rock-vs-enemy is its own rigidbody pair so Enter fires here
            // reliably. (Breakables are NOT reported from here: they sit on the level's compound
            // body, which the rolling rock already touches, so Enter never comes - Breakable
            // detects the hit itself.)
            if (!rb) rb = GetComponent<Rigidbody>();
            var enemy = c.collider.GetComponentInParent<Enemy>();
            if (enemy && enemy.gameObject != gameObject) enemy.Crush(speed, rb.mass);

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

            if (respawnIfLost && (rb.position - LevelManager.PivotOrOrigin).sqrMagnitude > fallRadius * fallRadius)
            {
                Respawn();
                return;
            }

            // Clamp the prop's OWN motion. The velocity it rides the level with is exact and must
            // not be cut, or the prop lags the floor on a fast turn.
            Vector3 ride = WorldRotator.Instance ? WorldRotator.Instance.CarriedVelocity(rb) : Vector3.zero;
            var v = rb.linearVelocity - ride;
            float sqr = v.sqrMagnitude;
            if (sqr > maxSpeed * maxSpeed)
                rb.linearVelocity = ride + v * (maxSpeed / Mathf.Sqrt(sqr));

            var w = rb.angularVelocity;
            float wSqr = w.sqrMagnitude;
            if (wSqr > maxAngularSpeed * maxAngularSpeed)
                rb.angularVelocity = w * (maxAngularSpeed / Mathf.Sqrt(wSqr));
        }

        /// <summary>
        /// Back to the start cell, in the level's CURRENT orientation, so it lands on the island
        /// however the player has turned it. A hard fling can hop a rock clean off the world; without
        /// this, the plate and crate levels would sit soft-locked until a manual restart.
        /// </summary>
        void Respawn()
        {
            Vector3 p = spawnParent ? spawnParent.TransformPoint(spawnLocal) : spawnLocal;
            rb.position = p;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (WorldRotator.Instance) WorldRotator.Instance.ForgetBody(rb);
            transform.position = p;             // don't let the interpolated transform lag a frame
            if (impactVfx)
            {
                impactVfx.transform.position = p;
                impactVfx.Emit(10);
            }
            if (visualPunch) visualPunch.Hit(0.6f);
        }
    }
}
