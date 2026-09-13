using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// Language-free onboarding. No tutorial text anywhere: a level teaches by pointing at the
    /// thing it wants you to look at, then getting out of the way the instant you act.
    ///
    /// Two devices only, because a hyper-casual player will not read a legend:
    ///
    ///  * ROTATE - a finger that sweeps an arc around the island, matching the actual gesture.
    ///    Retires as soon as the world has been turned a few degrees.
    ///  * POINT  - a pulsing ring parked on a world object (the hazard, the loose rock, the plate,
    ///    the key, the platform). Retires on a timer, or as soon as the player rotates, because by
    ///    then they are engaged and the ring is just clutter.
    ///
    /// Which one a level uses comes from LevelDefinition.teach, and a level may only ever teach one
    /// thing - see TeachHint.
    /// </summary>
    public class OnboardingHint : MonoBehaviour
    {
        [Header("Rotate hint")]
        [SerializeField] CanvasGroup rotateGroup;
        [Tooltip("The finger graphic that travels along the arc.")]
        [SerializeField] RectTransform rotateFinger;
        [Tooltip("Radius of the arc in canvas units. Should sit just outside the island.")]
        [SerializeField] float arcRadius = 235f;
        [Tooltip("Degrees the finger sweeps. A short confident arc reads better than a full circle.")]
        [SerializeField] float arcSweep = 62f;
        [SerializeField] float arcPeriod = 2.3f;
        [Tooltip("Degrees of world rotation after which the player clearly gets it.")]
        [SerializeField] float dismissAfterDegrees = 12f;

        [Header("Point hint")]
        [SerializeField] CanvasGroup pointGroup;
        [SerializeField] RectTransform pointRing;
        [SerializeField] float pointPulseSpeed = 1.7f;
        [SerializeField] float pointPulseAmount = 0.16f;
        [Tooltip("Hide the ring after this long even if the player has not moved.")]
        [SerializeField] float pointAutoHide = 6f;

        [Header("Refs")]
        [SerializeField] Camera worldCamera;

        TeachHint active = TeachHint.None;
        Transform target;               // world object the point ring follows
        float rotateAlpha, rotateWant;
        float pointAlpha, pointWant;
        float t;
        float startAngle;
        float pointTimer;

        public bool IsShowing => active != TeachHint.None;

        void Awake()
        {
            if (!worldCamera) worldCamera = Camera.main;
            Push();
        }

        /// <summary>Called by UiRoot when a level loads.</summary>
        public void Begin(LevelDefinition def)
        {
            Stop();
            if (def == null || def.teach == TeachHint.None) return;

            active = def.teach;
            startAngle = WorldRotator.Instance ? WorldRotator.Instance.AngleTarget : 0f;
            t = 0f;
            pointTimer = 0f;

            if (active == TeachHint.Rotate)
            {
                rotateWant = 1f;
                return;
            }

            target = FindTarget(def, active);
            pointWant = target ? 1f : 0f;
        }

        public void Stop()
        {
            active = TeachHint.None;
            target = null;
            rotateWant = 0f;
            pointWant = 0f;
        }

        /// <summary>
        /// Locates the object a hint should point at by searching the live level for the relevant
        /// component. Searching by type rather than by a wired reference means a level author only
        /// has to set the enum - there is nothing to forget to hook up.
        /// </summary>
        static Transform FindTarget(LevelDefinition def, TeachHint hint)
        {
            switch (hint)
            {
                case TeachHint.AvoidHazard:
                {
                    var h = def.GetComponentInChildren<Hazard>(true);
                    return h ? h.transform : null;
                }
                case TeachHint.PlateOpensGate:
                {
                    var p = def.GetComponentInChildren<PressurePlate>(true);
                    return p ? p.transform : null;
                }
                case TeachHint.CollectKey:
                {
                    var c = def.GetComponentInChildren<Collectible>(true);
                    return c ? c.transform : null;
                }
                case TeachHint.Timing:
                {
                    // Platforms detach from the level hierarchy in Awake (see MovingPlatform), so
                    // a GetComponentInChildren search here would always come back empty.
                    return MovingPlatform.Active.Count > 0
                        ? MovingPlatform.Active[0].transform
                        : null;
                }
                case TeachHint.RockIsATool:
                {
                    // The loose rock is the heaviest non-player dynamic body in the level.
                    var props = def.GetComponentsInChildren<DynamicProp>(true);
                    Transform best = null;
                    float bestMass = -1f;
                    foreach (var p in props)
                    {
                        var rb = p.GetComponent<Rigidbody>();
                        if (!rb || rb.isKinematic) continue;
                        if (rb.mass > bestMass) { bestMass = rb.mass; best = p.transform; }
                    }
                    return best;
                }
                default:
                    return null;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            float k = 1f - Mathf.Exp(-10f * dt);

            // Both hints retire once the player has clearly engaged with the mechanic.
            if (active != TeachHint.None && WorldRotator.Instance)
            {
                float turned = Mathf.Abs(WorldRotator.Instance.AngleTarget - startAngle);
                if (turned > dismissAfterDegrees) { rotateWant = 0f; pointWant = 0f; }
            }

            if (pointWant > 0.5f)
            {
                pointTimer += dt;
                if (pointTimer > pointAutoHide) pointWant = 0f;
            }

            rotateAlpha = Mathf.Lerp(rotateAlpha, rotateWant, k);
            pointAlpha = Mathf.Lerp(pointAlpha, pointWant, k);

            AnimateRotate();
            AnimatePoint();
            Push();
        }

        void AnimateRotate()
        {
            if (!rotateFinger || rotateAlpha < 0.01f) return;

            // Ease out and hold, so it reads as one deliberate swipe rather than a pendulum.
            float phase = (t / Mathf.Max(0.1f, arcPeriod)) % 1f;
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / 0.66f));
            // RIGHT to LEFT. A finger below the pivot dragged leftwards turns the island clockwise,
            // which drops its right side and rolls the orb to the door on the right - the first
            // level's first move. (It used to sweep the other way and taught the wrong turn.)
            float deg = Mathf.Lerp(arcSweep * 0.5f, -arcSweep * 0.5f, e);
            float rad = (deg - 90f) * Mathf.Deg2Rad;   // start below the island

            rotateFinger.anchoredPosition =
                new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * arcRadius;
            rotateFinger.localRotation = Quaternion.Euler(0f, 0f, deg);

            // Fade out on the return leg so the swipe direction stays unambiguous.
            float fade = Mathf.Clamp01(1f - Mathf.InverseLerp(0.66f, 1f, phase));
            if (rotateGroup) rotateGroup.alpha = rotateAlpha * fade;
        }

        void AnimatePoint()
        {
            if (!pointRing || pointAlpha < 0.01f || !target || !worldCamera) return;

            // Follow the target on screen - it is bolted to a level that is being rotated, so a
            // static ring would slide off it the moment the player starts playing.
            pointRing.position = worldCamera.WorldToScreenPoint(target.position);

            float pulse = 1f + Mathf.Sin(t * pointPulseSpeed * Mathf.PI * 2f) * pointPulseAmount;
            pointRing.localScale = Vector3.one * pulse;
        }

        void Push()
        {
            if (rotateGroup && rotateAlpha < 0.01f) rotateGroup.alpha = 0f;
            if (pointGroup) pointGroup.alpha = pointAlpha;
        }
    }
}
