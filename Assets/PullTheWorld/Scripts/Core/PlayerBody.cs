using System;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The character, v2: a real dynamic body that gravity actually moves.
    ///
    /// v1's player was a kinematic pin that never moved by design. That is gone. The player is now
    /// the thing the puzzle is about, and everything here exists to make "I tilted the floor and it
    /// rolled" read instantly and feel good.
    ///
    /// Why a sphere collider under a character mesh
    /// -------------------------------------------
    /// A rolling sphere is the only shape that behaves predictably on this geometry, and v1 already
    /// learned it the hard way: a box sits at PhysX's default friction angle on a slope and catches
    /// on the seams between floor tiles, so crates were abandoned as puzzle pieces and boulders used
    /// instead. A capsule that stays upright has the same problem plus a tipping failure mode. So
    /// the collider is a sphere, the body rolls, and rolling is also the most readable possible
    /// response to a tilt - you can see the rotation, so you can see the gravity.
    ///
    /// The body is locked to the XY plane (Z position and X/Y rotation frozen) so it can only ever
    /// roll about Z. That keeps it in the puzzle plane no matter how badly a collision goes, and it
    /// means the visible spin is always exactly the roll.
    ///
    /// The player is deliberately NOT a child of the rotating level root. It lives in world space,
    /// gravity pulls it straight down the screen, and the level turns around it. Parenting it would
    /// re-create the v1 confusion, where the player and the world move together and neither reads
    /// as the thing in motion.
    ///
    /// Endings
    /// -------
    /// A win and a death both used to just stop the ball, and both read as the game hanging.
    /// Now a win draws the ball into the doorway and shrinks it away, and a death pops it. Both are
    /// short, both run on the visual only, and both switch the body kinematic first so physics
    /// cannot argue with the animation.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class PlayerBody : MonoBehaviour
    {
        public static PlayerBody Instance { get; private set; }

        enum Pose { Live, Celebrating, Dying, Gone }

        [Header("References")]
        [Tooltip("Visual only. Squash and stretch happen here so the collider stays a clean sphere.")]
        [SerializeField] Transform visual;
        [SerializeField] Transform contactShadow;

        [Header("Body")]
        [SerializeField] float mass = 1f;
        [Tooltip("Rolling resistance. Too low and the ball never settles; too high and a gentle " +
                 "tilt does nothing and the level feels unresponsive.")]
        [SerializeField, Range(0f, 2f)] float angularDamping = 0.42f;
        [SerializeField, Range(0f, 2f)] float linearDamping = 0.02f;
        [Tooltip("Hard ceiling on speed. Without it a long drop builds up enough energy to punch " +
                 "through a wall and to overshoot every target on the way down.")]
        [SerializeField] float maxSpeed = 15f;

        [Header("Feel")]
        [Tooltip("Impact speed that produces a full squash. Below this it scales down smoothly.")]
        [SerializeField] float impactReference = 7f;
        [SerializeField, Range(0f, 0.6f)] float maxSquash = 0.34f;
        [SerializeField] float squashRecover = 11f;
        [Tooltip("Impacts softer than this are ignored entirely, otherwise resting contact chatter " +
                 "fires a squash and a haptic every frame.")]
        [SerializeField] float minImpactSpeed = 1.6f;

        [Header("Endings")]
        [Tooltip("Seconds the ball takes to be drawn into the door and vanish on a win.")]
        [SerializeField] float celebrateSeconds = 0.55f;
        [Tooltip("Seconds for the death pop: a quick swell, then gone.")]
        [SerializeField] float dieSeconds = 0.32f;
        [SerializeField] float dieSwell = 1.35f;

        [Header("Water")]
        [Tooltip("How hard water pushes the ball up, in gravities when fully submerged. Above 1 " +
                 "floats: the ball bobs across a pool at surface level while rocks sink through it.")]
        [SerializeField] float buoyancy = 1.6f;

        [Header("Death")]
        [Tooltip("Distance from the level centre past which the player counts as having fallen off. " +
                 "Generous enough to allow a real fall to be seen before the level resets.")]
        [SerializeField] float fallRadius = 26f;

        [Header("Grounding")]
        [Tooltip("Extra distance below the sphere used for the grounded test. Drives dust and the " +
                 "rolling audio loop.")]
        [SerializeField] float groundProbe = 0.12f;
        [SerializeField] LayerMask groundMask = ~0;

        Rigidbody body;
        SphereCollider sphere;
        Vector3 visualBaseScale;
        float squash;
        bool alive = true;
        float aliveTime;

        Pose pose = Pose.Live;
        float poseT;
        Vector3 poseStart, poseTarget;

        /// <summary>Impact strength 0..1. Dust, haptics and audio all hang off this.</summary>
        public event Action<float, Vector3> OnImpact;
        public event Action OnDied;

        public Rigidbody Body => body;
        public float Buoyancy => buoyancy;
        public float Radius => sphere ? sphere.radius * transform.lossyScale.x : 0.35f;
        public bool IsAlive => alive;
        public bool IsGrounded { get; private set; }
        /// <summary>Speed along the plane, used by audio and dust.</summary>
        public float Speed => body ? body.linearVelocity.magnitude : 0f;
        /// <summary>True while a win or death animation owns the body.</summary>
        public bool IsAnimatingEnding => pose != Pose.Live;

        void Awake()
        {
            Instance = this;
            body = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();

            body.mass = mass;
            body.angularDamping = angularDamping;
            body.linearDamping = linearDamping;
            body.useGravity = true;
            // PhysX spin ceiling defaults to 7 rad/s, which would force a 0.35 m ball to slide above
            // 2.4 m/s. Enough to roll at maxSpeed, with headroom for the level turning under it.
            body.maxAngularVelocity = 1.5f * maxSpeed / Mathf.Max(0.05f, Radius);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // The level can sweep into the player fast on a hard spin, and the player can fall
            // fast. Continuous dynamic is the only mode that reliably survives both.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Lock to the puzzle plane: roll about Z only, never leave XY.
            body.constraints = RigidbodyConstraints.FreezePositionZ
                             | RigidbodyConstraints.FreezeRotationX
                             | RigidbodyConstraints.FreezeRotationY;

            if (visual) visualBaseScale = visual.localScale;
            DynamicRegistry.Register(body);
        }

        void OnDestroy()
        {
            DynamicRegistry.Unregister(body);
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------------ lifecycle --
        /// <summary>Drop the player in at a fresh spawn point. Called on load and restart.</summary>
        public void Spawn(Vector3 worldPosition)
        {
            alive = true;
            aliveTime = 0f;
            squash = 0f;
            pose = Pose.Live;
            poseT = 0f;

            body.isKinematic = false;
            transform.position = worldPosition;
            transform.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            if (visual)
            {
                visual.gameObject.SetActive(true);
                visual.localScale = visualBaseScale;
                visual.localRotation = Quaternion.identity;
            }
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Stop simulating but stay visible. Kept for anything that wants a plain freeze; the win
        /// path uses <see cref="Celebrate"/> instead.
        /// </summary>
        public void Freeze()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        /// <summary>Win: the ball is drawn into the doorway and shrinks away.</summary>
        public void Celebrate(Vector3 towards)
        {
            if (pose != Pose.Live) return;
            Freeze();
            pose = Pose.Celebrating;
            poseT = 0f;
            poseStart = transform.position;
            // Stay in the puzzle plane; the door mouth is authored at z = 0 anyway.
            poseTarget = new Vector3(towards.x, towards.y, transform.position.z);
        }

        /// <summary>Kill without a visual - the rules layer. Fires OnDied once.</summary>
        public void Kill()
        {
            if (!alive) return;
            alive = false;
            OnDied?.Invoke();
        }

        /// <summary>Death: a quick swell and pop. Safe to call after Kill or instead of it.</summary>
        public void Die()
        {
            if (pose != Pose.Live) return;
            if (alive) Kill();
            Freeze();
            pose = Pose.Dying;
            poseT = 0f;
        }

        // -------------------------------------------------------------------------- stepping --
        void FixedUpdate()
        {
            if (pose != Pose.Live) return;
            aliveTime += Time.fixedDeltaTime;

            // Clamp speed rather than lowering gravity: gravity has to stay heavy so the response
            // to a tilt is immediate, but terminal velocity has to stay sane.
            // Clamp the ball's OWN motion, not the velocity it rides the turning level with -
            // that part is exact (see WorldRotator.CoRotate) and cutting it makes the ball lag.
            Vector3 ride = WorldRotator.Instance ? WorldRotator.Instance.CarriedVelocity(body) : Vector3.zero;
            var v = body.linearVelocity - ride;
            float sqr = v.sqrMagnitude;
            if (sqr > maxSpeed * maxSpeed)
                body.linearVelocity = ride + v * (maxSpeed / Mathf.Sqrt(sqr));

            ProbeGround();

            // Falling off the edge is a legitimate way to lose, and the only one that needs a
            // distance check rather than a collision.
            if (alive && transform.position.sqrMagnitude > fallRadius * fallRadius)
                Kill();
        }

        void ProbeGround()
        {
            float r = sphere.radius * Mathf.Abs(transform.lossyScale.x);
            IsGrounded = Physics.SphereCast(transform.position, r * 0.92f, Vector3.down,
                                            out _, groundProbe + r * 0.08f,
                                            groundMask, QueryTriggerInteraction.Ignore);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-squashRecover * dt));

            switch (pose)
            {
                case Pose.Celebrating: StepCelebrate(dt); break;
                case Pose.Dying: StepDie(dt); break;
                case Pose.Gone: break;
                default: ApplyVisual(); break;
            }
        }

        void StepCelebrate(float dt)
        {
            poseT += dt / Mathf.Max(0.05f, celebrateSeconds);
            float u = Mathf.Clamp01(poseT);
            float e = 1f - (1f - u) * (1f - u);              // ease-out: fast start, gentle arrive

            transform.position = Vector3.Lerp(poseStart, poseTarget, e);
            if (visual)
            {
                // A little extra spin on the way in reads as being pulled through.
                visual.Rotate(0f, 0f, 540f * dt, Space.Self);
                visual.localScale = visualBaseScale * (1f - e);
            }
            if (u >= 1f) Vanish();
        }

        void StepDie(float dt)
        {
            poseT += dt / Mathf.Max(0.05f, dieSeconds);
            float u = Mathf.Clamp01(poseT);
            // Swell up quickly, then collapse to nothing: sin gives the swell, (1-u) the collapse.
            float s = (1f + (dieSwell - 1f) * Mathf.Sin(u * Mathf.PI)) * (1f - u * u);
            if (visual) visual.localScale = visualBaseScale * Mathf.Max(0f, s);
            if (u >= 1f) Vanish();
        }

        void Vanish()
        {
            pose = Pose.Gone;
            if (visual) visual.gameObject.SetActive(false);
        }

        /// <summary>
        /// The impact reaction is a UNIFORM scale pulse, not a directional squash, and that is a
        /// deliberate constraint rather than laziness.
        ///
        /// The visual is a child of the Rigidbody, so it inherits the roll - it is spinning
        /// constantly, and that spin IS the feedback that gravity is working. A classic squash
        /// flattens local Y, but local Y is tumbling, so the flatten direction would be whatever
        /// the roll happened to be at the moment of impact: the ball would squash sideways on one
        /// landing and diagonally on the next. Transform.localScale cannot express a scale along
        /// an arbitrary world axis, so a correct directional squash would need either a
        /// counter-rotating parent (which cancels the roll) or a per-frame mesh deform.
        ///
        /// A uniform pulse is rotation-invariant, so it is always right, and on a sphere it reads
        /// as an impact perfectly well.
        /// </summary>
        void ApplyVisual()
        {
            if (!visual) return;

            visual.localScale = visualBaseScale * (1f - squash);

            if (contactShadow)
            {
                // Shadow shrinks with height off the ground so a fall reads as a fall. Also
                // counter-rotated to stay flat, since it too inherits the roll.
                contactShadow.rotation = Quaternion.identity;
                float lift = IsGrounded ? 0f : 0.35f;
                contactShadow.localScale = Vector3.one * Mathf.Max(0.25f, 1f - lift - squash * 0.4f);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!alive || pose != Pose.Live) return;

            float speed = c.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            float strength = Mathf.Clamp01(speed / Mathf.Max(0.01f, impactReference));
            squash = Mathf.Min(maxSquash, squash + strength * maxSquash);

            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            OnImpact?.Invoke(strength, point);
        }
    }
}
