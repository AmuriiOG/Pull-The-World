using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Kicks up motes as the world slides past the player. The particle system simulates in WORLD
    /// space, so every mote is immediately left behind by the moving ground - which is precisely
    /// the read we want: the terrain is travelling, the viewer is not.
    /// </summary>
    public class WorldMotionDust : MonoBehaviour
    {
        [SerializeField] WorldRig rig;
        [SerializeField] ParticleSystem dust;

        [Header("Emission")]
        [Tooltip("World speed at which the effect reaches full strength.")]
        [SerializeField] float fullSpeed = 7f;
        [SerializeField] float minSpeed = 0.9f;
        [SerializeField] float maxParticlesPerSecond = 34f;
        [SerializeField] float spawnRadius = 2.6f;
        [SerializeField] float spawnHeight = 0.15f;

        [Header("Motion")]
        [Tooltip("How much of the world velocity each mote inherits before being left behind.")]
        [SerializeField, Range(0f, 1f)] float inheritVelocity = 0.55f;
        [SerializeField] float lift = 0.5f;

        float carry;

        void Awake()
        {
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();
            if (!dust) dust = GetComponent<ParticleSystem>();
        }

        void Update()
        {
            if (!rig || !dust) return;

            Vector3 worldVel = rig.WorldVelocity;
            float speed = worldVel.magnitude;
            if (speed < minSpeed) { carry = 0f; return; }

            float strength = Mathf.Clamp01((speed - minSpeed) / Mathf.Max(0.01f, fullSpeed - minSpeed));
            carry += strength * maxParticlesPerSecond * Time.deltaTime;

            int count = Mathf.FloorToInt(carry);
            if (count <= 0) return;
            carry -= count;

            Vector3 origin = rig.AnchorPos;
            for (int i = 0; i < count; i++)
            {
                Vector2 disc = Random.insideUnitCircle * spawnRadius;
                var ep = new ParticleSystem.EmitParams
                {
                    position = origin + new Vector3(disc.x, Random.Range(-0.1f, spawnHeight), disc.y),
                    velocity = worldVel * inheritVelocity + Vector3.up * Random.Range(0f, lift),
                    startSize = Random.Range(0.045f, 0.11f) * Mathf.Lerp(0.7f, 1.3f, strength),
                    startLifetime = Random.Range(0.32f, 0.72f),
                    applyShapeToPosition = false
                };
                dust.Emit(ep, 1);
            }
        }
    }
}
