using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// App-level setup in one obvious place: frame rate, physics tuning, orientation. Everything is
    /// exposed so the whole game can be retuned from one Inspector.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class GameDirector : MonoBehaviour
    {
        [Header("Performance")]
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] bool disableVSync = true;
        [SerializeField] bool neverSleep = true;

        [Header("Physics")]
        [Tooltip("Heavier than real gravity on purpose. The whole game is judged on how quickly " +
                 "the character answers a tilt, and 9.81 feels like the level is underwater. " +
                 "Terminal velocity is capped on PlayerBody instead, so heavy gravity costs " +
                 "nothing in control.")]
        [SerializeField] float gravity = 24f;
        [Tooltip("60Hz physics matches the display, so the rotating level never looks steppy.")]
        [SerializeField] float fixedTimeStep = 1f / 60f;
        [SerializeField] int solverIterations = 10;
        [SerializeField] int solverVelocityIterations = 3;
        [Tooltip("Left on in v2. v1 drove every transform by hand and switched this off to avoid " +
                 "the cost; v2 moves the world with Rigidbody.MoveRotation instead, so PhysX is " +
                 "already in sync and leaving this on just removes a class of stale-query bug.")]
        [SerializeField] bool autoSyncTransforms = true;

        [Header("Orientation")]
        [SerializeField] bool lockPortrait = true;

        void Awake()
        {
            if (disableVSync) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            if (neverSleep) Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Straight down the screen, constant, never touched again. This single line is the
            // reason v2 is legible: the player never has to work out where down is.
            Physics.gravity = new Vector3(0f, -Mathf.Abs(gravity), 0f);

            Time.fixedDeltaTime = fixedTimeStep;
            Physics.defaultSolverIterations = solverIterations;
            Physics.defaultSolverVelocityIterations = solverVelocityIterations;
            Physics.autoSyncTransforms = autoSyncTransforms;

            // Ads live on the same systems object. Added here as well as by the scene generator so
            // a scene built before ads existed still gets the manager (and its fake provider).
            if (!GetComponent<Ads.AdsManager>()) gameObject.AddComponent<Ads.AdsManager>();

            if (lockPortrait && Application.isMobilePlatform)
                Screen.orientation = ScreenOrientation.Portrait;
        }
    }
}
