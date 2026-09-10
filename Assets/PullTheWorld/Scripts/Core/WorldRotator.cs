using System;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The entire core mechanic, v2: the level rotates, gravity does not.
    ///
    /// v1 pinned the player and slid the whole world around underneath them. It was technically
    /// pretty and consistently confusing - a fixed camera over a moving world and a moving camera
    /// over a fixed world produce the same image, so players read it as "I am walking around".
    /// v2 inverts it into something a player understands in about one second:
    ///
    ///     rotate the level  -> gravity now points somewhere else relative to the level
    ///                       -> the character slides, rolls and falls
    ///                       -> steer them into the door
    ///
    /// Gravity is a plain constant world-down vector and is never touched. The ONLY thing that
    /// moves is this level root's Z rotation. That is what makes the mechanic legible: "down" is
    /// always down the screen, exactly where a player's intuition already puts it.
    ///
    /// Geometry contract
    /// -----------------
    /// The puzzle lives in the world XY plane and this root spins about world Z. The camera looks
    /// down -Z with only a slight pitch, so a Z rotation reads on screen as a clean spin about the
    /// level centre, and world-down reads as screen-down. Levels are authored as a 2D grid in XY
    /// (see PtwLevels) with about a unit of depth in Z purely so the blocks look like solid
    /// low-poly objects rather than sprites.
    ///
    /// Physics contract
    /// ----------------
    /// The root carries one KINEMATIC Rigidbody and every static level collider is a child of it,
    /// so PhysX treats the whole level as a single compound actor: one thing to move, no broadphase
    /// churn, and no chance of the level colliders fighting each other.
    ///
    /// The rotation is applied with Rigidbody.MoveRotation inside FixedUpdate rather than by
    /// writing transform.rotation. This matters more than it looks. MoveRotation gives PhysX a real
    /// angular velocity for the actor, so contacts against it are solved with the correct relative
    /// velocity and a crate resting on a tilting floor gets carried and shoved properly. Teleporting
    /// the transform instead lets the level pass through resting objects, which reads as the world
    /// scooping out from under them - a bug v1 only avoided because it never rotated while
    /// anything was touching it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WorldRotator : MonoBehaviour
    {
        public static WorldRotator Instance { get; private set; }

        [Header("References")]
        [Tooltip("The transform that actually spins. Every level prefab is instantiated under it.")]
        [SerializeField] Transform worldRoot;
        [SerializeField] Camera viewCamera;

        [Header("Feel")]
        [Tooltip("How hard the world chases the angle your finger asked for. Higher is snappier " +
                 "and less floaty; too high and the sense of weight disappears.")]
        [SerializeField, Range(0.5f, 14f)] float frequency = 4.2f;
        [Tooltip("1 = no overshoot. Slightly under 1 gives the world some mass and a small settle " +
                 "wobble, which is most of why turning it feels satisfying.")]
        [SerializeField, Range(0.3f, 1.2f)] float damping = 0.78f;

        [Header("Release inertia")]
        [Tooltip("Fraction of your fling speed the world keeps when you let go. 0 stops dead.")]
        [SerializeField, Range(0f, 1f)] float releaseInertia = 0.42f;
        [Tooltip("Seconds for that fling to lose half its speed.")]
        [SerializeField, Range(0.02f, 1.5f)] float inertiaHalfLife = 0.38f;
        [Tooltip("Fling speed is clamped so a fast flick cannot spin the level into a blur.")]
        [SerializeField] float maxFlingSpeed = 320f;

        [Header("Limits")]
        [Tooltip("0 = free spin. Otherwise the level cannot be turned further than this from " +
                 "upright, which stops the first levels being solved by accident.")]
        [SerializeField] float angleLimit;
        [Tooltip("Degrees to snap to on release. 0 = fully continuous, which is what makes gravity " +
                 "feel analogue instead of stepped. Off by default on purpose.")]
        [SerializeField] float snapStep;

        [Header("Waking the world")]
        [Tooltip("Rotating the level does not change gravity, so a body that has gone to sleep " +
                 "will happily hang on a wall that is no longer under it. Everything dynamic gets " +
                 "woken whenever the level is actually turning.")]
        [SerializeField] float wakeAngularSpeed = 1.5f;

        [Header("Audio / haptic ticks")]
        [Tooltip("Fire a tick every N degrees so rotation has a physical ratchet to it.")]
        [SerializeField] float tickEvery = 18f;

        [Header("Shake")]
        [Tooltip("Impacts shake the WORLD, not the camera. The camera never moves in this game, " +
                 "and a brief angular jitter of the level reads as the impact having real weight " +
                 "while staying inside the fiction.")]
        [SerializeField] float shakeDegrees = 1.5f;
        [SerializeField] float shakeHalfLife = 0.09f;
        [SerializeField] float shakeFrequency = 38f;

        Rigidbody body;
        float angle;            // where the level actually is
        float angleTarget;      // where input has asked it to be
        float angleVel;         // spring velocity
        float flingVel;         // decaying post-release spin
        float tickAccum;
        bool driving;           // a finger is currently on the world
        bool rotationAllowed = true;
        float shake;            // 0..1 envelope
        float shakePhase;

        /// <summary>Fires each time another <see cref="tickEvery"/> degrees has passed.</summary>
        public event Action OnRotationTick;
        public event Action OnDriveBegin;
        public event Action<float> OnDriveEnd;   // settled angle

        public Transform WorldRoot => worldRoot ? worldRoot : transform;
        public Camera ViewCamera => viewCamera;
        public float Angle => angle;
        public float AngleTarget => angleTarget;
        /// <summary>Degrees per second the level is turning. Drives dust, audio and shake.</summary>
        public float AngularSpeed => Mathf.Abs(angleVel) + Mathf.Abs(flingVel);
        public bool IsDriving => driving;
        public bool RotationAllowed { get => rotationAllowed; set => rotationAllowed = value; }

        /// <summary>
        /// Which way "down" points in the level own space. The whole puzzle is a function of this
        /// one vector, so it is worth exposing: hints, arrows and the tutorial all read it.
        /// </summary>
        public Vector3 LocalDownDirection =>
            Quaternion.Inverse(WorldRoot.rotation) * Physics.gravity.normalized;

        void Awake()
        {
            Instance = this;
            if (!worldRoot) worldRoot = transform;
            if (!viewCamera) viewCamera = Camera.main;

            body = worldRoot.GetComponent<Rigidbody>();
            if (!body) body = worldRoot.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            // Continuous-speculative lets the tilting floor sweep against fast-falling props
            // instead of letting a boulder tunnel through a wall on a hard spin.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ level binding ---
        /// <summary>Called by LevelManager once the new level prefab is parented under the root.</summary>
        public void BindLevel(float startAngle, bool allowRotation, float limit)
        {
            rotationAllowed = allowRotation;
            angleLimit = Mathf.Max(0f, limit);

            // Start a few degrees off and let the spring settle home. The level arrives with a
            // little swing instead of snapping into place, and it demonstrates the one verb the
            // game has before the player has touched anything. Small enough that the ball does
            // not go anywhere on a flat floor.
            angle = startAngle + introKick;
            angleTarget = startAngle;
            angleVel = flingVel = 0f;
            tickAccum = 0f;
            driving = false;

            ApplyImmediate();
        }

        [Header("Intro")]
        [Tooltip("Degrees the level starts tilted before springing to its start angle on load.")]
        [SerializeField] float introKick = 4f;

        /// <summary>Teleport with no spring settle. Used on level load and restart.</summary>
        public void ApplyImmediate()
        {
            WorldRoot.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            if (body) body.position = WorldRoot.position;
            // GameDirector turns autoSyncTransforms off, so a direct transform write has to be
            // pushed into PhysX by hand or the first frame of queries uses the stale pose.
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------------- input ----
        public void BeginDrive()
        {
            if (!rotationAllowed) return;
            driving = true;
            flingVel = 0f;
            OnDriveBegin?.Invoke();
        }

        /// <summary>Ask for another <paramref name="delta"/> degrees. Called every drag frame.</summary>
        public void Drive(float delta)
        {
            if (!rotationAllowed || !driving) return;
            angleTarget = ClampAngle(angleTarget + delta);
        }

        /// <param name="flingDegreesPerSecond">Signed spin speed at the moment of release.</param>
        public void EndDrive(float flingDegreesPerSecond)
        {
            if (!driving) return;
            driving = false;

            flingVel = Mathf.Clamp(flingDegreesPerSecond * releaseInertia,
                                   -maxFlingSpeed, maxFlingSpeed);

            if (snapStep > 0f)
            {
                angleTarget = ClampAngle(Mathf.Round(angleTarget / snapStep) * snapStep);
                flingVel = 0f;
            }

            OnDriveEnd?.Invoke(angleTarget);
        }

        /// <summary>Cancels any in-flight drag, e.g. when the pause menu opens.</summary>
        public void CancelDrive()
        {
            driving = false;
            flingVel = 0f;
            angleTarget = angle;
        }

        float ClampAngle(float a) => angleLimit > 0f ? Mathf.Clamp(a, -angleLimit, angleLimit) : a;

        // ----------------------------------------------------------------------- stepping ---
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Post-release fling keeps pushing the target, then bleeds out.
            if (!driving && Mathf.Abs(flingVel) > 0.01f)
            {
                angleTarget = ClampAngle(angleTarget + flingVel * dt);
                flingVel = Spring.Decay(flingVel, inertiaHalfLife, dt);

                // Hitting a limit should kill the fling rather than grind against it.
                if (angleLimit > 0f && Mathf.Abs(angleTarget) >= angleLimit - 0.001f) flingVel = 0f;
            }

            float before = angle;
            Spring.Step(ref angle, ref angleVel, angleTarget, frequency, damping, dt);

            float moved = angle - before;
            ApplyRotation();
            EmitTicks(moved);
            WakeDynamicBodies(Mathf.Abs(moved) / Mathf.Max(1e-5f, dt));
        }

        /// <summary>Add an impact kick. 0..1, and they accumulate rather than replace.</summary>
        public void AddShake(float strength)
        {
            shake = Mathf.Clamp01(shake + Mathf.Clamp01(strength));
        }

        /// <summary>
        /// MoveRotation rather than a transform write - see the class comment. This is the line
        /// that makes props resting on the floor get carried instead of passed through.
        /// </summary>
        void ApplyRotation()
        {
            float dt = Time.fixedDeltaTime;

            // Shake rides on top of the settled angle and is deliberately NOT fed back into
            // angleTarget, so an impact never permanently moves the puzzle.
            float offset = 0f;
            if (shake > 0.0001f)
            {
                shakePhase += dt * shakeFrequency;
                offset = Mathf.Sin(shakePhase) * shakeDegrees * shake * shake;
                shake = Spring.Decay(shake, shakeHalfLife, dt);
            }

            var target = Quaternion.AngleAxis(angle + offset, Vector3.forward);
            if (body) body.MoveRotation(target);
            else WorldRoot.rotation = target;
        }

        void EmitTicks(float movedDegrees)
        {
            if (tickEvery <= 0f || OnRotationTick == null) return;
            tickAccum += Mathf.Abs(movedDegrees);
            while (tickAccum >= tickEvery)
            {
                tickAccum -= tickEvery;
                OnRotationTick.Invoke();
            }
        }

        /// <summary>
        /// A settled Rigidbody is taken out of the solver, and because gravity itself never changes
        /// there is nothing to re-trigger it when the floor rotates away from under it. Without
        /// this, props stay glued to walls that are no longer beneath them.
        /// </summary>
        void WakeDynamicBodies(float degreesPerSecond)
        {
            if (degreesPerSecond < wakeAngularSpeed) return;

            var bodies = DynamicRegistry.Bodies;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var rb = bodies[i];
                if (!rb) { bodies.RemoveAt(i); continue; }
                if (rb.IsSleeping()) rb.WakeUp();
            }
        }
    }
}
