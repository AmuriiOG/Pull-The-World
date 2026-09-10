using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Distant scenery that acts as a FIXED reference frame.
    ///
    /// Both factors default to zero, and that is the whole point. Background that slides at a
    /// fraction of the foreground is precisely the signature of a moving CAMERA over a static
    /// world - that is what parallax means. If the world is the thing moving and the camera is
    /// bolted down, the backdrop must not move at all. Anything non-zero here actively tells the
    /// player they are the one travelling, which is the exact opposite of this game's premise.
    ///
    /// The factors are left exposed only so the effect can be dialled in deliberately for a
    /// specific shot; treat non-zero as a special case, not a default.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ParallaxLayer : MonoBehaviour
    {
        [SerializeField] WorldRig rig;
        [Tooltip("0 = pinned in place (correct for this game), 1 = moves exactly with the world.")]
        [SerializeField, Range(0f, 1f)] float translateFactor;
        [Tooltip("How much of the world spin this layer copies. Should be 0.")]
        [SerializeField, Range(0f, 1f)] float spinFactor;
        [Tooltip("Gentle idle drift so the backdrop never feels dead.")]
        [SerializeField] float driftAmplitude = 0.08f;
        [SerializeField] float driftSpeed = 0.09f;

        Vector3 basePos;
        Quaternion baseRot;
        float seed;

        void Awake()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
            seed = Random.value * 100f;
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();
        }

        void LateUpdate()
        {
            if (!rig || !rig.WorldRoot) return;

            // How far the world has slid away from the anchor this frame.
            Vector3 worldOffset = rig.WorldRoot.position - rig.AnchorPos;

            float t = Time.time * driftSpeed + seed;
            Vector3 drift = new Vector3(Mathf.PerlinNoise(t, 0.2f) - 0.5f,
                                        Mathf.PerlinNoise(0.6f, t) - 0.5f,
                                        0f) * (driftAmplitude * 2f);

            transform.position = basePos + worldOffset * translateFactor + drift;
            transform.rotation = spinFactor > 0.001f
                ? Quaternion.AngleAxis(rig.Spin * spinFactor, rig.SpinAxis) * baseRot
                : baseRot;
        }
    }
}
