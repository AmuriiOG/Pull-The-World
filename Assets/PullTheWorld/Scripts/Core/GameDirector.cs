using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// App level setup in one obvious place: frame rate, physics tuning, orientation.
    /// Everything here is exposed so the whole prototype can be retuned from one Inspector.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class GameDirector : MonoBehaviour
    {
        [Header("Performance")]
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] bool disableVSync = true;
        [SerializeField] bool neverSleep = true;

        [Header("Physics")]
        [Tooltip("Heavier than real gravity - props settle fast, which reads better on a phone.")]
        [SerializeField] float gravity = 22f;
        [Tooltip("60Hz physics matches the display, so teleported props never look steppy.")]
        [SerializeField] float fixedTimeStep = 1f / 60f;
        [SerializeField] int solverIterations = 8;
        [SerializeField] int solverVelocityIterations = 2;

        [Header("Orientation")]
        [SerializeField] bool lockPortrait = true;

        void Awake()
        {
            if (disableVSync) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            if (neverSleep) Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Physics.gravity = new Vector3(0f, -Mathf.Abs(gravity), 0f);
            Time.fixedDeltaTime = fixedTimeStep;
            Physics.defaultSolverIterations = solverIterations;
            Physics.defaultSolverVelocityIterations = solverVelocityIterations;
            // We drive every transform ourselves and sync explicitly, so leave this off.
            Physics.autoSyncTransforms = false;

            if (lockPortrait && Application.isMobilePlatform)
            {
                Screen.orientation = ScreenOrientation.Portrait;
            }
        }
    }
}
