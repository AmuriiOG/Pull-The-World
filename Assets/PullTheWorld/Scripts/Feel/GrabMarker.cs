using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// A grip ring that sticks to the exact patch of terrain you grabbed.
    ///
    /// This is a perception fix, not decoration. A fixed camera over a moving world and a moving
    /// camera over a fixed world produce identical images; the brain resolves the ambiguity using
    /// whatever it can see that is definitely attached to something. Because this marker is
    /// parented to WorldRoot, it travels WITH the terrain while staying pinned under your finger -
    /// so the thing you are holding is unmistakably the world, not yourself.
    /// </summary>
    public class GrabMarker : MonoBehaviour
    {
        [SerializeField] WorldRig rig;
        [SerializeField] Transform visual;

        [Header("Feel")]
        [SerializeField] float fadeIn = 22f;
        [SerializeField] float fadeOut = 7f;
        [SerializeField] float popScale = 1.45f;
        [SerializeField] float baseScale = 1.15f;
        [SerializeField] float spin = 40f;

        Renderer rend;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        float alpha, target;
        Color tint = Color.white;

        void Awake()
        {
            if (!rig) rig = WorldRig.Instance ? WorldRig.Instance : FindFirstObjectByType<WorldRig>();
            if (!visual && transform.childCount > 0) visual = transform.GetChild(0);
            rend = visual ? visual.GetComponent<Renderer>() : null;
            mpb = new MaterialPropertyBlock();
            if (rend) tint = rend.sharedMaterial ? rend.sharedMaterial.GetColor(BaseColorId) : Color.white;
            Apply();
        }

        void OnEnable()
        {
            if (!rig) rig = WorldRig.Instance;
            if (rig != null) { rig.OnGrabBegin += HandleBegin; rig.OnGrabEnd += HandleEnd; }
        }

        void OnDisable()
        {
            if (rig != null) { rig.OnGrabBegin -= HandleBegin; rig.OnGrabEnd -= HandleEnd; }
        }

        void HandleBegin(Vector3 worldPoint)
        {
            // Setting a WORLD position on a child of WorldRoot pins it to that spot in the level.
            transform.position = worldPoint;
            transform.rotation = Quaternion.identity;
            target = 1f;
            if (visual) visual.localScale = Vector3.one * popScale;
        }

        void HandleEnd() => target = 0f;

        void Update()
        {
            float rate = target > alpha ? fadeIn : fadeOut;
            alpha = Mathf.Lerp(alpha, target, 1f - Mathf.Exp(-rate * Time.deltaTime));

            if (visual && alpha > 0.002f)
            {
                visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one * baseScale,
                                                 1f - Mathf.Exp(-12f * Time.deltaTime));
                visual.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
            }
            Apply();
        }

        void Apply()
        {
            if (!rend) return;
            rend.enabled = alpha > 0.004f;
            if (!rend.enabled) return;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(tint.r, tint.g, tint.b, tint.a * alpha));
            rend.SetPropertyBlock(mpb);
        }
    }
}
