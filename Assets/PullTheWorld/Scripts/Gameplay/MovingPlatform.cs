using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A ferry that slides back and forth along a track, so a level can ask for timing on top of
    /// rotation.
    ///
    /// Built very differently from Gate, and the difference is the whole point. A gate only has to
    /// block, so it can be a plain child collider of the level's compound body. A platform has to
    /// CARRY the player, and a child collider of a compound kinematic body has no velocity of its
    /// own as far as the solver is concerned - the player would sit on it and be pushed out by
    /// penetration resolution rather than carried, which reads as jitter and eventually flings
    /// them off.
    ///
    /// So the platform gets its OWN kinematic Rigidbody and is detached from the level hierarchy at
    /// startup. Every FixedUpdate it recomputes where it should be - the level's current rotation
    /// applied to its authored local pose plus the track offset - and gets there with MovePosition
    /// and MoveRotation. That gives PhysX a real velocity, so the player is carried properly, and
    /// it still tracks the rotating level exactly.
    ///
    /// It is authored as an ordinary child of the level prefab, so none of this is visible to level
    /// design.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MovingPlatform : MonoBehaviour
    {
        /// <summary>Live platforms. The onboarding hint needs to find one after it has detached.</summary>
        public static readonly List<MovingPlatform> Active = new List<MovingPlatform>(8);

        [Header("Track")]
        [Tooltip("Travel in LEVEL-LOCAL space, from the authored position. The platform ends up " +
                 "oscillating between here and here plus this.")]
        [SerializeField] Vector3 localTravel = new Vector3(3f, 0f, 0f);
        [Tooltip("Seconds for one full there-and-back cycle.")]
        [SerializeField] float period = 3.4f;
        [Tooltip("Shifts where in the cycle this platform starts, 0..1. Use it to stagger a pair.")]
        [SerializeField, Range(0f, 1f)] float phaseOffset;
        [Tooltip("On eases in and out at the ends, which is much easier to time than linear motion " +
                 "and looks mechanical rather than robotic.")]
        [SerializeField] bool smooth = true;

        [Header("Pausing")]
        [Tooltip("Seconds held still at each end. A beat of stillness is what makes a jump-on " +
                 "moment feel fair.")]
        [SerializeField] float dwell = 0.45f;

        Rigidbody body;
        Transform levelRoot;
        Vector3 localHome;
        Quaternion localHomeRot;
        float t;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var rotator = WorldRotator.Instance;
            levelRoot = rotator ? rotator.WorldRoot : transform.parent;

            // Record the authored pose relative to the rotating root, then leave the hierarchy so
            // the transform is ours alone to drive.
            if (levelRoot)
            {
                localHome = levelRoot.InverseTransformPoint(transform.position);
                localHomeRot = Quaternion.Inverse(levelRoot.rotation) * transform.rotation;
                transform.SetParent(null, true);
            }
            else
            {
                localHome = transform.position;
                localHomeRot = transform.rotation;
            }

            t = phaseOffset * Mathf.Max(0.01f, period);
        }

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        void OnDestroy() => Active.Remove(this);

        void FixedUpdate()
        {
            if (!levelRoot) return;

            float dt = Time.fixedDeltaTime;
            t += dt;

            float u = Progress();
            Vector3 localTarget = localHome + localTravel * u;

            body.MovePosition(levelRoot.TransformPoint(localTarget));
            body.MoveRotation(levelRoot.rotation * localHomeRot);
        }

        /// <summary>
        /// 0..1 position along the track. The dwell is implemented by stretching the cycle and
        /// clamping the ends, rather than with a state machine, so it stays a pure function of time
        /// and a restart cannot desync it.
        /// </summary>
        float Progress()
        {
            float p = Mathf.Max(0.01f, period);
            float d = Mathf.Clamp(dwell, 0f, p * 0.4f);
            float moveTime = Mathf.Max(0.01f, (p - d * 2f) * 0.5f);

            float phase = Mathf.Repeat(t, p);
            float u;

            if (phase < moveTime) u = phase / moveTime;                       // out
            else if (phase < moveTime + d) u = 1f;                            // dwell far
            else if (phase < moveTime * 2f + d) u = 1f - (phase - moveTime - d) / moveTime;
            else u = 0f;                                                      // dwell home

            return smooth ? Mathf.SmoothStep(0f, 1f, u) : u;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 a = transform.position;
            Vector3 b = a + (levelRoot ? levelRoot.TransformVector(localTravel)
                                       : transform.TransformVector(localTravel));
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(b, 0.2f);
        }
#endif
    }
}
