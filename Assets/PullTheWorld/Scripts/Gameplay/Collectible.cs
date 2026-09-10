using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A key the player has to roll into before the door will open.
    ///
    /// Static by default and bolted to the level, so it rotates with the world and the puzzle is
    /// "get gravity to carry me over there". Tick <see cref="loose"/> and it becomes a dynamic prop
    /// that falls and rolls on its own, which turns the same object into a much harder puzzle -
    /// now you have to deliver the key to yourself.
    ///
    /// Same explicit-distance approach as the door and the hazards, for the same compound-collider
    /// reason.
    /// </summary>
    public class Collectible : MonoBehaviour
    {
        [Header("Pickup")]
        [Tooltip("Added to the player radius. Generous on purpose - missing a key you clearly " +
                 "touched is infuriating in a way that missing a door is not.")]
        [SerializeField] float pickupRadius = 0.5f;

        [Header("Behaviour")]
        [Tooltip("On makes this a physics object that falls and rolls with the level instead of " +
                 "being fixed to it.")]
        [SerializeField] bool loose;

        [Header("Visuals")]
        [SerializeField] Transform spinner;
        [SerializeField] float spinSpeed = 95f;
        [SerializeField] float bobHeight = 0.09f;
        [SerializeField] float bobSpeed = 1.5f;
        [SerializeField] ParticleSystem collectVfx;
        [SerializeField] GameObject visuals;

        bool collected;
        float t;
        Vector3 spinnerBaseLocalPos;

        public bool Collected => collected;
        public bool Loose => loose;

        void Awake()
        {
            if (spinner) spinnerBaseLocalPos = spinner.localPosition;
            t = Random.value * 10f;   // desync the bob between multiple keys
        }

        void Update()
        {
            if (collected) return;
            t += Time.deltaTime;

            AnimateIdle();

            var lm = LevelManager.Instance;
            var player = PlayerBody.Instance;
            if (lm == null || !lm.IsPlaying || player == null || !player.IsAlive) return;

            if (Vector3.Distance(player.transform.position, transform.position)
                <= pickupRadius + player.Radius)
                Collect();
        }

        /// <summary>
        /// The idle spin is authored about the LEVEL's up, not the world's, so a key still reads as
        /// mounted to its platform when the whole island is tilted 40 degrees.
        /// </summary>
        void AnimateIdle()
        {
            if (!spinner || loose) return;
            spinner.localRotation = Quaternion.Euler(0f, t * spinSpeed, 0f);
            spinner.localPosition = spinnerBaseLocalPos
                                  + Vector3.up * (Mathf.Sin(t * bobSpeed * Mathf.PI * 2f) * bobHeight);
        }

        void Collect()
        {
            collected = true;
            if (collectVfx)
            {
                // Detached so it outlives the visuals being hidden - but then it also outlives
                // the LEVEL, because it is no longer a child of anything that gets destroyed on
                // unload. The first build leaked one of these per key collected, forever. Give
                // it a hard lifetime instead.
                collectVfx.transform.SetParent(null, true);
                collectVfx.Play();
                Destroy(collectVfx.gameObject, 2.5f);
            }
            if (visuals) visuals.SetActive(false);

            PtwAudio.Play(PtwSfx.PlateOn, 1f, 1.35f);   // same chime, pitched up
            Haptics.Play(HapticKind.Pickup);
            if (UiRoot.Instance) UiRoot.Instance.FlyKey(transform.position);   // gem flies to the counter
            LevelManager.Instance.CollectKey();
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
#endif
    }
}
