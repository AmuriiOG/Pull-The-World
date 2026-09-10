using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A shallow pool sunk into the floor. Three jobs, all cheap:
    ///
    ///  * FLOATS things. Anything dynamic inside the volume gets an upward force scaled by how
    ///    deep it sits and by its own buoyancy (PlayerBody floats, rocks sink), plus drag. The
    ///    ball bobs across a pool at surface level; a rock settles onto the bed.
    ///  * POURS when the world is tilted. Past a threshold the pool drains from whichever lip is
    ///    downhill - droplets, a sound, the surface visibly sloping in the shader - and anything
    ///    burning just downhill of that lip is put out. This is the water-versus-fire mechanic:
    ///    tip the pool over the flame. Water is finite, so tipping the wrong way wastes it.
    ///  * DOUSES fire it actually touches, immediately.
    ///
    /// There is no fluid simulation here and there should not be; a real one would cost more
    /// than the rest of the game on a phone. Like every other gameplay volume in the project it
    /// uses explicit overlap queries rather than triggers, because the level is one compound
    /// collider and trigger callbacks do not reliably arrive on this object.
    ///
    /// The pool has NO collider of its own, so the ball can enter it. The solid pool bed under it
    /// is a separate half-height block placed by the level generator.
    /// </summary>
    public class WaterVolume : MonoBehaviour
    {
        [Header("Volume (local space, origin at the bed)")]
        [Tooltip("Width of the pool along the floor.")]
        [SerializeField] float width = 0.96f;
        [Tooltip("Depth from bed to full surface. Half a cell: the bed block fills the other half.")]
        [SerializeField] float depth = 0.5f;
        [SerializeField] float thickness = 0.9f;

        [Header("Buoyancy")]
        [Tooltip("Upward acceleration in gravities on a fully submerged body of buoyancy 1.")]
        [SerializeField] float buoyancyStrength = 1.15f;
        [Tooltip("Velocity damping per second while submerged. Water should feel thick.")]
        [SerializeField] float drag = 3.2f;
        [SerializeField] float angularDrag = 2.5f;

        [Header("Pouring")]
        [Tooltip("World tilt (degrees) below which the pool is calm.")]
        [SerializeField] float tiltThreshold = 22f;
        [Tooltip("Tilt at which it drains as fast as it will.")]
        [SerializeField] float fullTilt = 55f;
        [Tooltip("Seconds to empty a full pool at full tilt.")]
        [SerializeField] float drainSeconds = 2.4f;
        [SerializeField] float maxDropletsPerSecond = 80f;
        [SerializeField] float dropletSpeed = 2.4f;
        [Tooltip("How far past the downhill lip the pour reaches things to put out.")]
        [SerializeField] float douseReach = 1.5f;

        [Header("References")]
        [SerializeField] Renderer surface;
        [SerializeField] Transform surfaceTransform;
        [SerializeField] Transform body;
        [SerializeField] ParticleSystem pourVfx;
        [SerializeField] ParticleSystem splashVfx;

        static readonly int TiltId = Shader.PropertyToID("_Tilt");
        static readonly int TileOffsetId = Shader.PropertyToID("_TileOffset");

        readonly Collider[] overlap = new Collider[16];
        readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();
        readonly HashSet<Rigidbody> insideNow = new HashSet<Rigidbody>();

        MaterialPropertyBlock mpb;
        Vector4 tileOffset;
        float fill = 1f;
        float tilt01;
        float carry;
        float soundTimer;
        Vector3 bodyBaseScale;

        public float Fill => fill;
        public float Depth => depth;
        public bool IsPouring => fill > 0.01f && tilt01 > 0.02f;
        /// <summary>Centre of the water surface in world space, at the current fill.</summary>
        public Vector3 SurfaceWorldPoint => transform.TransformPoint(new Vector3(0f, depth * fill, 0f));
        public Vector3 BedWorldPoint => transform.position;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (!surface) surface = GetComponentInChildren<Renderer>();
            if (body) bodyBaseScale = body.localScale;

            // Level-local offset so waves run continuously across a multi-cell pool instead of
            // repeating per cell and printing a grid. See the shader.
            Vector3 lp = transform.localPosition;
            tileOffset = new Vector4(lp.x, lp.z, 0f, 0f);

            ApplyVisual();
        }

        // -------------------------------------------------------------------- physics -------
        void FixedUpdate()
        {
            if (fill <= 0.001f) { inside.Clear(); return; }

            float dt = Time.fixedDeltaTime;
            float h = depth * fill;

            Vector3 centre = transform.TransformPoint(new Vector3(0f, h * 0.5f, 0f));
            Vector3 half = new Vector3(width * 0.5f, h * 0.5f, thickness * 0.5f);
            int n = Physics.OverlapBoxNonAlloc(centre, half, overlap, transform.rotation, ~0,
                                               QueryTriggerInteraction.Ignore);

            insideNow.Clear();
            Vector3 surfaceWorld = SurfaceWorldPoint;
            Vector3 up = Vector3.up;

            for (int i = 0; i < n; i++)
            {
                var rb = overlap[i].attachedRigidbody;
                if (!rb || rb.isKinematic) continue;
                insideNow.Add(rb);

                // Depth below the surface along WORLD up. The pool tilts with the level, but
                // gravity does not, so buoyancy has to be reckoned against real down.
                float below = Vector3.Dot(surfaceWorld - rb.worldCenterOfMass, up);
                float radius = BodyRadius(overlap[i]);
                float submerged = Mathf.Clamp01((below + radius) / Mathf.Max(0.01f, radius * 2f));
                if (submerged <= 0f) continue;

                float buoyancy = BuoyancyOf(rb);
                rb.AddForce(up * (Physics.gravity.magnitude * buoyancyStrength * buoyancy * submerged),
                            ForceMode.Acceleration);

                float k = Mathf.Clamp01(drag * submerged * dt);
                rb.linearVelocity *= 1f - k;
                rb.angularVelocity *= 1f - Mathf.Clamp01(angularDrag * submerged * dt);

                if (!inside.Contains(rb)) Splash(rb);
            }

            inside.Clear();
            foreach (var rb in insideNow) inside.Add(rb);

            DouseTouchingHazards(centre, half);
        }

        static float BodyRadius(Collider c)
        {
            if (c is SphereCollider s) return s.radius * Mathf.Abs(c.transform.lossyScale.x);
            var e = c.bounds.extents;
            return Mathf.Max(0.1f, Mathf.Min(e.x, e.y));
        }

        static float BuoyancyOf(Rigidbody rb)
        {
            var pb = rb.GetComponent<PlayerBody>();
            if (pb) return pb.Buoyancy;
            var dp = rb.GetComponent<DynamicProp>();
            return dp ? dp.Buoyancy : 0.5f;
        }

        void Splash(Rigidbody rb)
        {
            if (splashVfx)
            {
                splashVfx.transform.position = new Vector3(rb.worldCenterOfMass.x, SurfaceWorldPoint.y, 0f);
                splashVfx.Emit(10);
            }
            PtwAudio.Play(PtwSfx.Impact, 0.5f, 0.7f);
            Haptics.Play(HapticKind.Pickup);
        }

        void DouseTouchingHazards(Vector3 centre, Vector3 half)
        {
            // TIGHT - slightly inside the actual water, no padding. A 0.15 pad reached into the
            // neighbouring cell and put out level 20's fire at load, before the player had done
            // anything; PouringDousesTheFire caught it. Fire in the next cell is the pour's job.
            int n = Physics.OverlapBoxNonAlloc(centre, half * 0.92f, overlap,
                                               transform.rotation, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hz = overlap[i].GetComponentInParent<Hazard>();
                if (hz && hz.Armed) hz.Douse();
            }
        }

        // --------------------------------------------------------------------- pouring ------
        void Update()
        {
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            float target = fill > 0.001f ? Mathf.Clamp01(Mathf.InverseLerp(tiltThreshold, fullTilt, tilt)) : 0f;
            tilt01 = Mathf.Lerp(tilt01, target, 1f - Mathf.Exp(-8f * Time.deltaTime));

            if (tilt01 > 0.02f && fill > 0.001f)
            {
                fill = Mathf.Max(0f, fill - tilt01 * Time.deltaTime / Mathf.Max(0.1f, drainSeconds));
                Pour(tilt01);
            }
            else carry = 0f;

            ApplyVisual();
        }

        void Pour(float strength)
        {
            // Downhill along the pool surface: gravity with the surface normal projected out.
            Vector3 nrm = transform.up;
            Vector3 down = Vector3.down;
            Vector3 downhill = down - Vector3.Dot(down, nrm) * nrm;
            if (downhill.sqrMagnitude < 1e-5f) return;
            downhill.Normalize();

            Vector3 lip = SurfaceWorldPoint + downhill * (width * 0.5f);

            // Anything burning just past the lip goes out. This is how the pool is a tool.
            Vector3 reachCentre = lip + downhill * (douseReach * 0.5f) + down * 0.25f;
            Vector3 reachHalf = new Vector3(douseReach * 0.5f, 0.7f, thickness * 0.5f);
            Quaternion reachRot = Quaternion.LookRotation(Vector3.forward, nrm);
            int n = Physics.OverlapBoxNonAlloc(reachCentre, reachHalf, overlap, reachRot, ~0,
                                               QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var hz = overlap[i].GetComponentInParent<Hazard>();
                if (hz && hz.Armed) hz.Douse();
            }

            if (!pourVfx) return;
            carry += strength * maxDropletsPerSecond * Time.deltaTime;
            int count = Mathf.FloorToInt(carry);
            if (count <= 0) return;
            carry -= count;

            Vector3 tangent = Vector3.Cross(nrm, downhill).normalized;
            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = lip + tangent * Random.Range(-0.25f, 0.25f) * thickness,
                    velocity = down * (dropletSpeed * Random.Range(0.7f, 1.3f)) +
                               downhill * Random.Range(0.4f, 1.2f),
                    startSize = Random.Range(0.045f, 0.11f),
                    startLifetime = Random.Range(0.45f, 1.0f),
                    applyShapeToPosition = false
                };
                pourVfx.Emit(ep, 1);
            }

            soundTimer -= Time.deltaTime;
            if (soundTimer <= 0f)
            {
                soundTimer = 0.35f;
                PtwAudio.Play(PtwSfx.Smother, 0.35f * strength, Random.Range(1.4f, 1.8f));
            }
        }

        // ---------------------------------------------------------------------- visual ------
        void ApplyVisual()
        {
            float h = depth * fill;

            if (surfaceTransform)
            {
                surfaceTransform.localPosition = new Vector3(0f, h, 0f);
                surfaceTransform.gameObject.SetActive(fill > 0.02f);
            }
            if (body)
            {
                body.localScale = new Vector3(bodyBaseScale.x, bodyBaseScale.y * fill, bodyBaseScale.z);
                body.gameObject.SetActive(fill > 0.02f);
            }
            if (surface)
            {
                surface.GetPropertyBlock(mpb);
                mpb.SetFloat(TiltId, tilt01);
                mpb.SetVector(TileOffsetId, tileOffset);
                surface.SetPropertyBlock(mpb);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, depth * 0.5f, 0f), new Vector3(width, depth, thickness));
        }
#endif
    }
}
