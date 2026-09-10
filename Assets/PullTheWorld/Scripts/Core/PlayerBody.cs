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
    /// </summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class PlayerBody : MonoBehaviour
    {
        public static PlayerBody Instance { get; private set; }

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
        float squash, squashVel;
        bool alive = true;
        bool frozen;
        float aliveTime;

        /// <summary>Impact strength 0..1. Dust, haptics and audio all hang off this.</summary>
        public event Action<float, Vector3> OnImpact;
        public event Action OnDied;

        public Rigidbody Body => body;
        public float Radius => sphere ? sphere.radius * transform.lossyScale.x : 0.35f;
        public bool IsAlive => alive;
        public bool IsGrounded { get; private set; }
        /// <summary>Speed along the plane, used by audio and dust.</summary>
        public float Speed => body ? body.linearVelocity.magnitude : 0f;

        void Awake()
        {
            Instance = this;
            body = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();

            body.mass = mass;
            body.angularDamping = angularDamping;
            body.linearDamping = linearDamping;
            body.useGravity = true;
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
            frozen = false;
            aliveTime = 0f;
            squash = squashVel = 0f;

            body.isKinematic = false;
            transform.position = worldPosition;
            transform.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            if (visual)
            {
                visual.localScale = visualBaseScale;
                visual.localRotation = Quaternion.identity;
            }
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Stop simulating but stay visible. Used for the win pose, so the player does not roll
        /// back out of the door while the celebration plays.
        /// </summary>
        public void Freeze()
        {
            frozen = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        public void Kill()
        {
            if (!alive) return;
            alive = false;
            OnDied?.Invoke();
        }

        // -------------------------------------------------------------------------- stepping --
        void FixedUpdate()
        {
            if (frozen) return;
            aliveTime += Time.fixedDeltaTime;

            // Clamp speed rather than lowering gravity: gravity has to stay heavy so the response
            // to a tilt is immediate, but terminal velocity has to stay sane.
            var v = body.linearVelocity;
            float sqr = v.sqrMagnitude;
            if (sqr > maxSpeed * maxSpeed)
                body.linearVelocity = v * (maxSpeed / Mathf.Sqrt(sqr));

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
            ApplyVisual();
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
            if (!alive || frozen) return;

            float speed = c.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;

            float strength = Mathf.Clamp01(speed / Mathf.Max(0.01f, impactReference));
            squash = Mathf.Min(maxSquash, squash + strength * maxSquash);

            Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            OnImpact?.Invoke(strength, point);
        }
    }
}
