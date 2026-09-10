using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A pool of water that reacts to the world being tipped.
    ///
    /// There is no fluid simulation here and there shouldn't be - a real one would cost more than
    /// the rest of the game put together on a phone. What sells it is the two things the eye
    /// actually checks: the surface visibly drops as the world tilts, and water pours off whichever
    /// edge is currently downhill. The pour direction is derived from real gravity against the
    /// tile's own normal, so it stays correct at any rotation.
    /// </summary>
    public class WaterVolume : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Renderer surface;
        [SerializeField] ParticleSystem pourVfx;
        [SerializeField] WorldRig rig;

        [Header("Tilt response")]
        [Tooltip("Below this tilt the pool is calm.")]
        [SerializeField] float tiltThreshold = 7f;
        [Tooltip("Tilt at which the pool is draining as hard as it will.")]
        [SerializeField] float fullTilt = 38f;

        [Header("Pour")]
        [SerializeField] float maxDropletsPerSecond = 90f;
        [SerializeField] float dropletSpeed = 2.6f;
        [SerializeField] float edgeInset = 0.42f;
        [SerializeField] float spread = 0.34f;

        [Header("Audio")]
        [SerializeField] float pourSoundInterval = 0.35f;

        static readonly int TiltId = Shader.PropertyToID("_Tilt");
        static readonly int TileOffsetId = Shader.PropertyToID("_TileOffset");

        MaterialPropertyBlock mpb;
        Vector4 tileOffset;
        float carry;
        float soundTimer;
        float tilt01;

        /// <summary>0 when calm, 1 when pouring at full tilt. Handy for designers to hook into.</summary>
        public float Tilt01 => tilt01;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (!surface) surface = GetComponentInChildren<Renderer>();
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();

            // Where this tile sits inside the level. The shader adds it to object space so waves
            // and glints run continuously across a multi-tile pool instead of repeating per tile
            // and printing an obvious grid. Level-local, not world, so the pattern does not swim
            // when the world is dragged.
            Vector3 lp = transform.localPosition;
            tileOffset = new Vector4(lp.x, lp.z, 0f, 0f);

            Apply(0f);
        }

        void Update()
        {
            if (!rig) { rig = WorldRig.Instance; if (!rig) return; }

            float tilt = rig.TiltAngle;
            float target = Mathf.Clamp01(Mathf.InverseLerp(tiltThreshold, fullTilt, tilt));
            tilt01 = Mathf.Lerp(tilt01, target, 1f - Mathf.Exp(-8f * Time.deltaTime));
            Apply(tilt01);

            if (tilt01 > 0.02f) Pour(tilt01);
            else carry = 0f;
        }

        void Apply(float t)
        {
            if (!surface) return;
            surface.GetPropertyBlock(mpb);
            mpb.SetFloat(TiltId, t);
            mpb.SetVector(TileOffsetId, tileOffset);
            surface.SetPropertyBlock(mpb);
        }

        void Pour(float strength)
        {
            if (!pourVfx) return;

            // Downhill along the tile surface: gravity with the surface normal projected out.
            Vector3 n = transform.up;
            Vector3 down = Vector3.down;
            Vector3 downhill = down - Vector3.Dot(down, n) * n;
            if (downhill.sqrMagnitude < 1e-5f) return;
            downhill.Normalize();

            Vector3 lip = transform.position + downhill * (edgeInset * transform.lossyScale.x);
            Vector3 tangent = Vector3.Cross(n, downhill).normalized;

            carry += strength * maxDropletsPerSecond * Time.deltaTime;
            int count = Mathf.FloorToInt(carry);
            if (count <= 0) return;
            carry -= count;

            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = lip + tangent * Random.Range(-spread, spread),
                    velocity = down * (dropletSpeed * Random.Range(0.7f, 1.3f)) +
                               downhill * Random.Range(0.2f, 0.9f),
                    startSize = Random.Range(0.045f, 0.12f),
                    startLifetime = Random.Range(0.5f, 1.1f),
                    applyShapeToPosition = false
                };
                pourVfx.Emit(ep, 1);
            }

            soundTimer -= Time.deltaTime;
            if (soundTimer <= 0f)
            {
                soundTimer = pourSoundInterval;
                PtwAudio.Play(PtwSfx.Smother, 0.35f * strength, Random.Range(1.4f, 1.8f));
            }
        }
    }
}
