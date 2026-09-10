using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Charm for the ball's face, visual only: it blinks now and then, and its eyes go wide while
    /// it is in the air. Neither touches the body - the face is a child of the rolling visual and
    /// this only scales it - so the physics feel stays exactly as signed off.
    /// </summary>
    public class BallFace : MonoBehaviour
    {
        [SerializeField] PlayerBody player;
        [Tooltip("The face mesh under the visual. Found by name if left empty.")]
        [SerializeField] Transform face;
        [Tooltip("Seconds between blinks, picked at random in this range.")]
        [SerializeField] Vector2 blinkEvery = new Vector2(2.2f, 4.8f);
        [SerializeField] float blinkSeconds = 0.09f;
        [Tooltip("Eye height multiplier while airborne. Wide eyes sell the drop.")]
        [SerializeField] float airborneEyes = 1.3f;

        Vector3 baseScale;
        float nextBlink, blinkT, wide = 1f;

        void Awake()
        {
            if (!player) player = GetComponentInParent<PlayerBody>();
            if (!face && player) face = player.transform.Find("Visual/Face");
            if (face) baseScale = face.localScale;
            Schedule();
        }

        void Schedule() => nextBlink = Random.Range(blinkEvery.x, blinkEvery.y);

        void Update()
        {
            if (!face) return;
            float dt = Time.deltaTime;

            nextBlink -= dt;
            if (nextBlink <= 0f) { blinkT = blinkSeconds * 2f; Schedule(); }

            // Lid: closes over blinkSeconds, opens over the next.
            float lid = 1f;
            if (blinkT > 0f)
            {
                blinkT -= dt;
                float k = Mathf.Clamp01(blinkT / (blinkSeconds * 2f));
                lid = Mathf.Lerp(0.08f, 1f, Mathf.Abs(k * 2f - 1f));
            }

            bool airborne = player && player.IsAlive && !player.IsAnimatingEnding && !player.IsGrounded;
            wide = Mathf.Lerp(wide, airborne ? airborneEyes : 1f, 1f - Mathf.Exp(-14f * dt));

            face.localScale = new Vector3(baseScale.x * Mathf.Lerp(1f, 1.08f, wide - 1f),
                                          baseScale.y * lid * wide,
                                          baseScale.z);
        }
    }
}
