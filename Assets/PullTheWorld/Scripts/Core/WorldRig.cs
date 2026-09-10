using System;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// THE core system. The camera and the player never move - this moves the world instead.
    ///
    /// World pose is described by exactly two values, so it is trivial to reason about, author,
    /// test and rewind:
    ///   focus : the point in WORLD-LOCAL space that is currently sitting on the player anchor.
    ///   spin  : degrees rotated about the camera view axis, so a rotation always reads on screen
    ///           as a clean spin around the player AND always changes the direction of gravity
    ///           relative to the level.
    ///
    /// Applied pose:  rot = AngleAxis(spin, spinAxis);  pos = anchor - rot * focus;
    /// which guarantees the pivot of every rotation is exactly the player feet.
    ///
    /// PHYSICS ARCHITECTURE
    /// --------------------
    /// Moving a transform full of Rigidbodies is normally a recipe for jitter. This avoids it:
    ///  * WorldRoot carries ONE kinematic Rigidbody, so every static level collider below it
    ///    becomes a single compound PhysX actor - cheap to teleport, no broadphase churn.
    ///  * Dynamic props (rocks / crates) are separate actors parented under WorldRoot, so a world
    ///    move rigidly carries them. Uniform gravity is translation invariant, so a pure drag is a
    ///    perfect symmetry of the simulation: nothing relative changes, so nothing can jitter or
    ///    tunnel, because there is zero relative motion to resolve.
    ///  * A rotation is a rigid transform too, EXCEPT that gravity does not rotate with it. That
    ///    single asymmetry is the entire gravity mechanic, and it falls out of real physics.
    ///  * Velocities are rotated by the same delta so momentum stays continuous in the world frame.
    ///  * All of it happens in FixedUpdate, before the solver runs.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WorldRig : MonoBehaviour
    {
        public static WorldRig Instance { get; private set; }

        [Header("Scene references")]
        [SerializeField] Transform worldRoot;
        [SerializeField] Transform playerAnchor;
        [SerializeField] Camera viewCamera;

        [Header("Drag feel")]
        [Tooltip("How fast the world catches up to your finger. Higher = tighter, less weight.")]
        [SerializeField, Range(0.5f, 12f)] float dragFrequency = 4.2f;
        [Tooltip("1 = no overshoot. Slightly below 1 gives the world a little mass.")]
        [SerializeField, Range(0.3f, 1.2f)] float dragDamping = 0.78f;
        [Tooltip("How much of your flick speed carries on after you let go.")]
        [SerializeField, Range(0f, 1f)] float releaseInertia = 0.55f;
        [Tooltip("Half-life of the post-release glide, in seconds.")]
        [SerializeField, Range(0.02f, 1f)] float inertiaHalfLife = 0.16f;
        [Tooltip("How far past the level bounds the world can be pulled before it rubber-bands.")]
        [SerializeField, Range(0f, 6f)] float overscroll = 1.6f;

        [Header("Rotation feel")]
        [SerializeField, Range(0.5f, 12f)] float spinFrequency = 3.0f;
        [SerializeField, Range(0.3f, 1.2f)] float spinDamping = 0.72f;
        [Tooltip("Degrees the spin snaps to when you let go. 0 disables snapping.")]
        [SerializeField] float spinSnapStep = 15f;
        [SerializeField, Range(0f, 1f)] float spinReleaseInertia = 0.35f;
        [SerializeField, Range(0.02f, 1f)] float spinInertiaHalfLife = 0.14f;

        [Header("Shake")]
        [Tooltip("Impacts shake the WORLD, never the camera. The camera is sacred.")]
        [SerializeField, Range(0f, 1f)] float shakeHalfLife = 0.12f;
        [SerializeField, Range(0f, 2f)] float shakeScale = 1f;
        [SerializeField, Range(0f, 1f)] float shakeMaxOffset = 0.22f;

        [Header("Player vs world")]
        [Tooltip("The world cannot be pulled through the player. This is what makes walls matter: " +
                 "the player is a fixed pin and the level has to be steered around them.")]
        [SerializeField] bool blockOnPlayer = true;
        [Tooltip("Capsule representing the player body. Should start ABOVE foot height so the " +
                 "floor they stand on never counts as a blocker.")]
        [SerializeField] Collider playerBlocker;
        [SerializeField, Range(1, 4)] int blockIterations = 2;
        [Tooltip("Safety clamp so a fast rotation can never fling the world.")]
        [SerializeField] float maxCorrectionPerStep = 0.35f;

        [Header("Level intro")]
        [Tooltip("The world starts offset by this (in level space) and springs into place, so a " +
                 "level arrives with weight instead of just popping into existence. Kept small: " +
                 "the ground constraint clamps the world to the island, so a large offset would " +
                 "just be clipped away on the first step. Most of the intro is the spin.")]
        [SerializeField] Vector3 introFocusOffset = new Vector3(-0.3f, 0f, -0.45f);
        [SerializeField] float introSpin = 8f;

        [Header("Drag lean")]
        [Tooltip("The world tips slightly as it is hauled around, like a heavy tray held at the " +
                 "player's feet. This is the single strongest cue that the WORLD is the object in " +
                 "motion rather than the camera - a camera pan cannot tip the scene.")]
        [SerializeField, Range(0f, 12f)] float dragLeanDegrees = 3.2f;
        [SerializeField] float leanSpeedRef = 8f;
        [SerializeField] float leanResponse = 9f;

        [Header("Ground under the player")]
        [Tooltip("The player cannot be left standing over empty space. Without this, gaps between " +
                 "islands mean nothing and every level can be solved by cutting across the void.")]
        [SerializeField] bool requireGroundUnderPlayer = true;

        [Header("Settling thresholds")]
        [SerializeField] float settleDistance = 0.02f;
        [SerializeField] float settleSpeed = 0.05f;
        [SerializeField] float settleAngle = 0.25f;
        [SerializeField] float settleAngularSpeed = 1.0f;

        // ---- state -------------------------------------------------------------------------
        Vector3 focus, focusVel, focusTarget, glideVel;
        float spin, spinVel, spinTarget, spinGlideVel;
        Vector3 spinAxis = Vector3.forward;
        Quaternion appliedRot = Quaternion.identity;

        Vector3 shakeOffset;
        float shakeAmount;
        float shakeSeed;

        bool grabbing;
        Vector3 grabLocal;
        bool spinning;

        Bounds focusBounds = new Bounds(Vector3.zero, Vector3.one * 60f);
        bool boundsEnabled;
        bool rotationAllowed = true;

        readonly List<Rigidbody> bodies = new List<Rigidbody>();

        // ---- events ------------------------------------------------------------------------
        public event Action<Vector3> OnGrabBegin;   // world-space grab point
        public event Action OnGrabEnd;
        public event Action<float> OnSpinSnapped;   // final angle in degrees

        // ---- public API ---------------------------------------------------------------------
        public Transform WorldRoot => worldRoot;
        public Transform Anchor => playerAnchor;
        public Camera ViewCamera => viewCamera;
        public Vector3 AnchorPos => playerAnchor ? playerAnchor.position : Vector3.zero;
        public Vector3 Focus => focus;
        public Vector3 FocusTarget => focusTarget;
        public float Spin => spin;
        public float SpinTarget => spinTarget;
        public Vector3 SpinAxis => spinAxis;
        public bool IsGrabbing => grabbing;
        public bool IsSpinning => spinning;
        public bool RotationAllowed { get => rotationAllowed; set => rotationAllowed = value; }

        /// <summary>Linear speed of the world in units/second. Drives dust, lean and audio.</summary>
        public float DragSpeed => focusVel.magnitude;
        public float SpinSpeed => Mathf.Abs(spinVel);
        /// <summary>Velocity of the world in world space (what the player perceives as motion).</summary>
        public Vector3 WorldVelocity => -(appliedRot * focusVel);
        /// <summary>Direction gravity points in the LEVEL local space. (0,-1,0) when upright.</summary>
        public Vector3 LocalGravityDirection => Quaternion.Inverse(appliedRot) * Vector3.down;
        /// <summary>How far the level is tilted away from upright, in degrees.</summary>
        public float TiltAngle => Vector3.Angle(appliedRot * Vector3.up, Vector3.up);

        public bool IsSettled =>
            !grabbing && !spinning &&
            (focus - focusTarget).sqrMagnitude < settleDistance * settleDistance &&
            focusVel.sqrMagnitude < settleSpeed * settleSpeed &&
            Mathf.Abs(spin - spinTarget) < settleAngle &&
            Mathf.Abs(spinVel) < settleAngularSpeed;

        void Awake()
        {
            Instance = this;
            if (!viewCamera) viewCamera = Camera.main;
            shakeSeed = UnityEngine.Random.value * 100f;
            CacheSpinAxis();
            Apply(true);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The camera never moves, so the spin axis only needs capturing once.</summary>
        public void CacheSpinAxis()
        {
            spinAxis = viewCamera ? viewCamera.transform.forward.normalized : Vector3.forward;
        }

        // ------------------------------------------------------------------ level plumbing --
        /// <summary>Called by LevelManager once a level prefab has been swapped in.</summary>
        public void BindLevel(Vector3 startFocus, float startSpin, Bounds bounds, bool useBounds,
                              bool allowRotation)
        {
            focusBounds = bounds;
            boundsEnabled = useBounds;
            rotationAllowed = allowRotation;
            shakeAmount = 0f;
            shakeOffset = Vector3.zero;
            grabbing = spinning = false;
            CacheSpinAxis();

            // Snap to an offset pose, then aim at the real one: the existing drag spring does the
            // whole intro animation for free, with exactly the weight the rest of the game has.
            SetPoseImmediate(startFocus + introFocusOffset, startSpin + introSpin);
            focusTarget = startFocus;
            spinTarget = startSpin;

            RefreshBodies();
            RebuildFloorMap();
            lastGroundedFocus = startFocus;
        }

        /// <summary>Re-scan the dynamic props under the world. Cheap, and only on level load.</summary>
        public void RefreshBodies()
        {
            bodies.Clear();
            if (!worldRoot) return;
            worldRoot.GetComponentsInChildren(true, bodies);
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var rb = bodies[i];
                if (rb == null || rb.isKinematic || rb.transform == worldRoot)
                {
                    bodies.RemoveAt(i);
                    continue;
                }
                // These get teleported every step, so engine interpolation must be off or it smears.
                rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        public int DynamicBodyCount => bodies.Count;

        // ------------------------------------------------------------------------- input ----
        /// <summary>Grab the world at a screen point. That spot then stays glued to the finger.</summary>
        public void BeginDrag(Vector2 screenPoint)
        {
            if (!worldRoot || !TryScreenToWorldPlane(screenPoint, out Vector3 hit)) return;
            grabbing = true;
            grabLocal = worldRoot.InverseTransformPoint(hit);
            glideVel = Vector3.zero;
            OnGrabBegin?.Invoke(hit);
        }

        public void UpdateDrag(Vector2 screenPoint)
        {
            if (!grabbing || !TryScreenToWorldPlane(screenPoint, out Vector3 hit)) return;
            Vector3 wanted = grabLocal + Quaternion.Inverse(appliedRot) * (AnchorPos - hit);
            focusTarget = ApplyRubberBand(wanted);
        }

        public void EndDrag()
        {
            if (!grabbing) return;
            grabbing = false;
            glideVel = focusVel * releaseInertia;
            OnGrabEnd?.Invoke();
        }

        public void BeginSpin()
        {
            if (!rotationAllowed) return;
            spinning = true;
            spinGlideVel = 0f;
        }

        /// <summary>Add a relative twist in degrees, from a two finger gesture or a key.</summary>
        public void AddSpin(float degrees)
        {
            if (!rotationAllowed) return;
            spinning = true;
            spinTarget += degrees;
        }

        public void EndSpin()
        {
            if (!spinning) return;
            spinning = false;
            if (spinSnapStep > 0.01f)
            {
                float projected = spinTarget + spinVel * spinReleaseInertia * spinInertiaHalfLife;
                spinTarget = Mathf.Round(projected / spinSnapStep) * spinSnapStep;
                spinGlideVel = 0f;
                OnSpinSnapped?.Invoke(spinTarget);
            }
            else
            {
                spinGlideVel = spinVel * spinReleaseInertia;
            }
        }

        /// <summary>Nudge the world. Used by impacts, landings and level transitions.</summary>
        public void AddShake(float strength)
        {
            shakeAmount = Mathf.Min(1.5f, shakeAmount + strength * shakeScale);
        }

        // ------------------------------------------------------------------------ integrate --
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (!grabbing && glideVel.sqrMagnitude > 1e-6f)
            {
                focusTarget = ApplyRubberBand(focusTarget + glideVel * dt);
                glideVel = Spring.Decay(glideVel, inertiaHalfLife, dt);
            }
            // Once the finger is off, the target returns inside the legal area and the spring
            // carries the world back with it. That is the rubber-band snap-back.
            if (boundsEnabled && !grabbing) focusTarget = ClampToBounds(focusTarget, 0f);

            if (!spinning && Mathf.Abs(spinGlideVel) > 1e-4f)
            {
                spinTarget += spinGlideVel * dt;
                spinGlideVel = Spring.Decay(spinGlideVel, spinInertiaHalfLife, dt);
            }

            Spring.Step(ref focus, ref focusVel, focusTarget, dragFrequency, dragDamping, dt);
            Spring.Step(ref spin, ref spinVel, spinTarget, spinFrequency, spinDamping, dt);

            ResolvePlayerBlocking();
            EnforceGround();

            if (shakeAmount > 0.0005f && viewCamera)
            {
                float t = Time.time * 38f + shakeSeed;
                float nx = Mathf.PerlinNoise(t, 0.3f) * 2f - 1f;
                float ny = Mathf.PerlinNoise(0.7f, t) * 2f - 1f;
                var ct = viewCamera.transform;
                shakeOffset = (ct.right * nx + ct.up * ny) * (shakeAmount * shakeMaxOffset);
                shakeAmount = Spring.Decay(shakeAmount, shakeHalfLife, dt);
            }
            else
            {
                shakeAmount = 0f;
                shakeOffset = Vector3.zero;
            }

            Apply(false);
        }

        void Apply(bool teleport)
        {
            if (!worldRoot) return;

            Quaternion prev = appliedRot;
            appliedRot = ComputeLean() * Quaternion.AngleAxis(spin, spinAxis);
            Vector3 pos = AnchorPos - appliedRot * focus + shakeOffset;

            worldRoot.SetPositionAndRotation(pos, appliedRot);
            Physics.SyncTransforms();

            if (teleport) return;

            // Rigid frame change: carry momentum with it so a falling prop keeps a continuous
            // trajectory relative to the level instead of snapping sideways mid-air.
            //
            // AND wake everything. This is essential, not an optimisation detail: PhysX puts
            // settled bodies to sleep, and a sleeping body ignores gravity entirely. Re-aiming
            // gravity by rotating the world therefore does nothing at all to a prop that has come
            // to rest - it just sits on the slope looking broken. Only rotation needs this;
            // a pure translation is a symmetry of uniform gravity, so bodies may keep sleeping
            // through a drag, which is also what keeps dragging cheap.
            Quaternion delta = appliedRot * Quaternion.Inverse(prev);
            if (Quaternion.Angle(delta, Quaternion.identity) > 0.0005f)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    var rb = bodies[i];
                    if (rb == null) continue;
                    rb.linearVelocity = delta * rb.linearVelocity;
                    rb.angularVelocity = delta * rb.angularVelocity;
                    if (rb.IsSleeping()) rb.WakeUp();
                }
            }
        }

        // --------------------------------------------------------------- player blocking ----
        readonly Collider[] blockBuffer = new Collider[16];
        bool blockedThisStep;

        /// <summary>True while the world is pressed up against the player and cannot move further.</summary>
        public bool IsBlocked => blockedThisStep;

        /// <summary>
        /// Push the world back out of the player. Solved as depenetration rather than by letting
        /// PhysX do it, because both bodies involved are kinematic and PhysX will not resolve
        /// kinematic-vs-kinematic overlap. Working in focus space keeps it exactly consistent
        /// with everything else the rig does.
        /// </summary>
        void ResolvePlayerBlocking()
        {
            blockedThisStep = false;
            if (!blockOnPlayer || !playerBlocker || !worldRoot) return;

            Quaternion rot = Quaternion.AngleAxis(spin, spinAxis);
            Quaternion invRot = Quaternion.Inverse(rot);

            for (int iter = 0; iter < blockIterations; iter++)
            {
                // Put the world at the candidate pose so the overlap queries read the truth.
                worldRoot.SetPositionAndRotation(AnchorPos - rot * focus + shakeOffset, rot);
                Physics.SyncTransforms();

                Vector3 push = ComputeSeparation();
                if (push.sqrMagnitude < 1e-8f) break;

                blockedThisStep = true;
                // World pose is P = anchor - R*focus, so shifting the world by -push (moving it
                // away from the player) is exactly focus += R^-1 * push.
                Vector3 localPush = invRot * push;
                focus += localPush;

                // Bleed off the velocity that is driving into the obstruction, otherwise the
                // spring and the solver fight each other and the world buzzes against the wall.
                Vector3 n = localPush.normalized;
                float into = Vector3.Dot(focusVel, n);
                if (into < 0f) focusVel -= n * into;
            }
        }

        Vector3 ComputeSeparation()
        {
            Bounds b = playerBlocker.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.02f,
                                               blockBuffer, Quaternion.identity, ~0,
                                               QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            var pt = playerBlocker.transform;

            for (int i = 0; i < n; i++)
            {
                var other = blockBuffer[i];
                if (!other || other == playerBlocker) continue;
                // Only static level geometry. Loose props are PhysX's job - the player has a real
                // collider, so rocks bump off them naturally.
                if (!other.transform.IsChildOf(worldRoot)) continue;
                var orb = other.attachedRigidbody;
                if (orb && !orb.isKinematic) continue;

                if (Physics.ComputePenetration(playerBlocker, pt.position, pt.rotation,
                                               other, other.transform.position, other.transform.rotation,
                                               out Vector3 dir, out float dist))
                {
                    // dir*dist is how the PLAYER would have to move to separate.
                    push += dir * dist;
                }
            }
            return Vector3.ClampMagnitude(push, maxCorrectionPerStep);
        }

        // ------------------------------------------------------------------------- lean ----
        Vector3 leanVec;   // axis * degrees, smoothed

        /// <summary>
        /// A small tip in the direction of travel, about an axis lying in the screen plane and
        /// perpendicular to the motion. Deliberately tiny (a few degrees): enough that the world
        /// reads as a solid object with mass being hauled about, not enough to disturb gravity
        /// (tan(3 deg) is far below any prop's friction, so nothing creeps because of it).
        /// </summary>
        Quaternion ComputeLean()
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-5f);
            Vector3 want = Vector3.zero;

            if (dragLeanDegrees > 0.01f && viewCamera)
            {
                var ct = viewCamera.transform;
                Vector3 v = WorldVelocity;
                float sx = Vector3.Dot(v, ct.right);
                float sy = Vector3.Dot(v, ct.up);
                Vector3 screenDir = ct.right * sx + ct.up * sy;

                if (screenDir.sqrMagnitude > 1e-6f)
                {
                    float amount = Mathf.Clamp01(new Vector2(sx, sy).magnitude /
                                                 Mathf.Max(0.01f, leanSpeedRef)) * dragLeanDegrees;
                    want = Vector3.Cross(ct.forward, screenDir.normalized) * amount;
                }
            }

            leanVec = Vector3.Lerp(leanVec, want, 1f - Mathf.Exp(-leanResponse * dt));
            float deg = leanVec.magnitude;
            return deg > 0.01f ? Quaternion.AngleAxis(deg, leanVec / deg) : Quaternion.identity;
        }

        // ----------------------------------------------------------------- ground under us --
        readonly HashSet<Vector2Int> floorCells = new HashSet<Vector2Int>();
        Vector3 lastGroundedFocus;

        /// <summary>
        /// Occupied ground cells in level space, rebuilt on load.
        ///
        /// A grid lookup rather than a downward raycast: it costs nothing, so the ground test can
        /// run many times per frame inside a search, and it stays exact while the world is tilted
        /// (the question is "which cell of the LEVEL am I over", which tilt does not change).
        /// </summary>
        void RebuildFloorMap()
        {
            floorCells.Clear();
            if (!worldRoot) return;

            var cols = worldRoot.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                if (c == null || c.isTrigger) continue;
                if (c.transform == worldRoot) continue;
                var orb = c.attachedRigidbody;
                if (orb && !orb.isKinematic) continue;      // loose props are not ground
                Vector3 lp = worldRoot.InverseTransformPoint(c.transform.position);
                floorCells.Add(new Vector2Int(Mathf.RoundToInt(lp.x), Mathf.RoundToInt(lp.z)));
            }
        }

        public int FloorCellCount => floorCells.Count;

        public bool IsGroundedAt(Vector3 f) =>
            floorCells.Count == 0 ||
            floorCells.Contains(new Vector2Int(Mathf.RoundToInt(f.x), Mathf.RoundToInt(f.z)));

        public bool IsGrounded => IsGroundedAt(focus);

        /// <summary>
        /// Walk the world back to the last spot that had ground under the player. Binary search
        /// along the segment gives a clean stop right at the lip of the island instead of a snap.
        /// </summary>
        void EnforceGround()
        {
            if (!requireGroundUnderPlayer || floorCells.Count == 0) return;

            if (IsGroundedAt(focus)) { lastGroundedFocus = focus; return; }
            if (!IsGroundedAt(lastGroundedFocus)) { lastGroundedFocus = focus; return; }

            Vector3 wanted = focus;

            // Walk back to the last legal spot along the path travelled.
            Vector3 a = lastGroundedFocus, b = wanted;
            for (int i = 0; i < 8; i++)
            {
                Vector3 m = (a + b) * 0.5f;
                if (IsGroundedAt(m)) a = m; else b = m;
            }

            // ...then SLIDE along the lip instead of stopping dead. Recover whichever single axis
            // of the rejected motion is still legal. Without this, running the world along the
            // edge of an island snags on every cell boundary and feels like the player is glued
            // to the rim; with it, you skim the edge the way a character controller would.
            Vector3 rejected = wanted - a;
            Vector3 slideX = a + new Vector3(rejected.x, 0f, 0f);
            Vector3 slideZ = a + new Vector3(0f, 0f, rejected.z);

            bool okX = Mathf.Abs(rejected.x) > 1e-4f && IsGroundedAt(slideX);
            bool okZ = Mathf.Abs(rejected.z) > 1e-4f && IsGroundedAt(slideZ);

            if (okX && okZ) a = Mathf.Abs(rejected.x) >= Mathf.Abs(rejected.z) ? slideX : slideZ;
            else if (okX) a = slideX;
            else if (okZ) a = slideZ;

            // Only bleed the velocity that is actually being refused, so the surviving axis keeps
            // its momentum and the motion stays fluid.
            Vector3 blocked = wanted - a;
            if (Mathf.Abs(blocked.x) > 1e-4f) focusVel.x = 0f;
            if (Mathf.Abs(blocked.z) > 1e-4f) focusVel.z = 0f;
            if (Mathf.Abs(blocked.x) > 1e-4f) glideVel.x = 0f;
            if (Mathf.Abs(blocked.z) > 1e-4f) glideVel.z = 0f;

            focus = a;
            lastGroundedFocus = a;
        }

        // ------------------------------------------------------------------------- helpers --
        Vector3 ApplyRubberBand(Vector3 wanted)
        {
            if (!boundsEnabled) return wanted;
            Vector3 hard = ClampToBounds(wanted, 0f);
            Vector3 excess = wanted - hard;
            return hard + new Vector3(SoftExcess(excess.x), SoftExcess(excess.y), SoftExcess(excess.z));
        }

        /// <summary>Asymptotic resistance: pulling harder yields progressively less, capped at overscroll.</summary>
        float SoftExcess(float e)
        {
            if (overscroll <= 0.0001f || Mathf.Abs(e) < 1e-5f) return 0f;
            float a = Mathf.Abs(e);
            return Mathf.Sign(e) * overscroll * (1f - 1f / (1f + a / overscroll));
        }

        Vector3 ClampToBounds(Vector3 v, float slack)
        {
            Vector3 min = focusBounds.min - Vector3.one * slack;
            Vector3 max = focusBounds.max + Vector3.one * slack;
            return new Vector3(
                Mathf.Clamp(v.x, min.x, max.x),
                Mathf.Clamp(v.y, min.y, max.y),
                Mathf.Clamp(v.z, min.z, max.z));
        }

        /// <summary>Screen point -> the horizontal plane the level lives on, through the anchor.</summary>
        public bool TryScreenToWorldPlane(Vector2 screenPoint, out Vector3 hit)
        {
            hit = default;
            if (!viewCamera) return false;
            var plane = new Plane(Vector3.up, AnchorPos);
            Ray ray = viewCamera.ScreenPointToRay(screenPoint);
            if (!plane.Raycast(ray, out float dist)) return false;
            hit = ray.GetPoint(dist);
            return true;
        }

        // ---------------------------------------------------------------- test / tool hooks --
        /// <summary>Instantly place the world with no spring. Used by level loads and tests.</summary>
        public void SetPoseImmediate(Vector3 newFocus, float newSpin)
        {
            focus = focusTarget = newFocus;
            spin = spinTarget = newSpin;
            focusVel = glideVel = Vector3.zero;
            spinVel = spinGlideVel = 0f;
            Apply(true);
        }

        /// <summary>Spring the world toward a pose. This is what an automated player does.</summary>
        public void SetPoseTarget(Vector3 newFocus, float newSpin)
        {
            focusTarget = boundsEnabled ? ClampToBounds(newFocus, 0f) : newFocus;
            spinTarget = newSpin;
        }
    }
}
