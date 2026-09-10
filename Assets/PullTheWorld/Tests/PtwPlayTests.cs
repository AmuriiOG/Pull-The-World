using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PullTheWorld.Tests
{
    /// <summary>
    /// Drives the real game the way a player would - by setting world pose targets and letting the
    /// springs, the solver and the physics do the rest - then asserts the promises the design makes.
    /// Also writes real Game View captures to /Captures so the art can be compared against the
    /// reference sheet rather than guessed at.
    /// </summary>
    public class PtwPlayTests
    {
        WorldRig rig;
        LevelManager levels;
        PlayerAnchor player;
        Camera cam;

        const float Settle = 0.02f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Game", LoadSceneMode.Single);
            yield return null;
            yield return null;

            rig = WorldRig.Instance;
            levels = LevelManager.Instance;
            player = Object.FindFirstObjectByType<PlayerAnchor>();
            cam = Camera.main;

            Assert.IsNotNull(rig, "WorldRig missing from scene");
            Assert.IsNotNull(levels, "LevelManager missing from scene");
            Assert.IsNotNull(player, "PlayerAnchor missing from scene");
            Assert.IsNotNull(cam, "Main camera missing from scene");
            Assert.Greater(levels.LevelCount, 0, "No levels assigned");

            yield return null;
        }

        // ===================================================================== utilities =====
        IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        /// <summary>Spring the world toward a pose and wait until it has effectively arrived.</summary>
        IEnumerator DriveTo(Vector3 focus, float spin, float timeout = 6f)
        {
            rig.SetPoseTarget(focus, spin);
            float t = 0f;
            while (t < timeout)
            {
                t += Time.deltaTime;
                if ((rig.Focus - rig.FocusTarget).magnitude < 0.06f &&
                    Mathf.Abs(rig.Spin - rig.SpinTarget) < 0.6f &&
                    rig.DragSpeed < 0.35f)
                    yield break;
                yield return null;
            }
        }

        IEnumerator LoadLevel(int index)
        {
            levels.LoadLevel(index);
            yield return null;
            yield return null;
            // Let the level intro spring settle so the drag-lean has decayed to zero.
            yield return Wait(0.8f);
        }

        IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (t < timeout)
            {
                if (cond()) yield break;
                t += Time.deltaTime;
                yield return null;
            }
            Assert.Fail($"Timed out after {timeout}s waiting for: {what}");
        }

        /// <summary>Watch a physics prop and report where it actually went. Debugging beats guessing.</summary>
        IEnumerator LogProp(string label, float seconds, System.Func<bool> stopWhen)
        {
            var prop = FindInLevel<Pushable>();
            var plate = FindInLevel<PressurePlate>();
            float t = 0f, next = 0f;
            while (t < seconds)
            {
                if (stopWhen != null && stopWhen()) { Debug.Log($"PTW_DIAG {label}: condition met at {t:F2}s"); yield break; }
                if (t >= next && prop)
                {
                    Vector3 lp = prop.transform.localPosition;
                    Debug.Log($"PTW_DIAG {label} t={t:F1} local=({lp.x:F2},{lp.y:F2},{lp.z:F2}) " +
                              $"vel={prop.Body.linearVelocity.magnitude:F2} resting={prop.IsResting} " +
                              $"plate={(plate ? plate.IsPressed.ToString() : "none")} " +
                              $"localG={rig.LocalGravityDirection} spin={rig.Spin:F1}");
                    next += 1f;
                }
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"PTW_DIAG {label}: TIMED OUT after {seconds}s");
        }

        T FindInLevel<T>() where T : Component =>
            levels.Current ? levels.Current.GetComponentInChildren<T>(true) : null;

        /// <summary>
        /// Walk the world one grid cell at a time. Since the player must always have ground
        /// beneath them, a solution is a genuine connected path across the islands - jumping
        /// straight to the door would simply be clamped at the first gap.
        /// </summary>
        IEnumerator DrivePath(params Vector2[] cells)
        {
            foreach (var c in cells)
                yield return DriveTo(new Vector3(c.x, 0f, c.y), rig.Spin, 3f);
        }

        static Vector2 C(float x, float z) => new Vector2(x, z);

        IEnumerator SolveLevel(int index)
        {
            yield return LoadLevel(index);

            switch (index)
            {
                case 0: // L1 - pure drag, up the staircase island
                    yield return DrivePath(C(0, 1), C(1, 1), C(1, 2), C(2, 2), C(2, 3), C(3, 3), C(3, 4));
                    break;

                case 1: // L2 - the wall forces the long way round to the right
                    yield return DrivePath(C(1, 0), C(2, 0), C(3, 0), C(3, 1), C(3, 2), C(3, 3),
                                           C(3, 4), C(2, 4), C(1, 4), C(0, 4), C(0, 5));
                    break;

                case 2: // L3 - tip the boulder down its trough onto the plate to unlock the door
                {
                    yield return DriveTo(rig.Focus, -45f, 3f);
                    var plate = FindInLevel<PressurePlate>();
                    Assert.IsNotNull(plate, "L3 has no pressure plate");
                    yield return LogProp("L3 boulder", 9f, () => plate.IsPressed);
                    Assert.IsTrue(plate.IsPressed, "L3 boulder never reached the plate");
                    yield return DriveTo(rig.Focus, 0f, 3f);
                    yield return DrivePath(C(1, 0), C(1, 1), C(1, 2), C(2, 2), C(3, 2), C(4, 2),
                                           C(5, 2), C(5, 3), C(5, 4), C(5, 5), C(5, 6), C(4, 6),
                                           C(4, 7), C(3, 7), C(3, 8));
                    break;
                }

                case 3: // L4 - the fire holds the only crossing; drop the boulder on it
                {
                    yield return DriveTo(rig.Focus, -45f, 3f);
                    var fire = FindInLevel<Hazard>();
                    Assert.IsNotNull(fire, "L4 has no hazard");
                    yield return LogProp("L4 boulder", 9f, () => fire.Smothered);
                    Assert.IsTrue(fire.Smothered, "L4 fire was never smothered");
                    yield return DriveTo(rig.Focus, 0f, 3f);
                    yield return DrivePath(C(0, 1), C(1, 1), C(1, 2), C(1, 3), C(1, 4), C(1, 5),
                                           C(1, 6), C(1, 7));
                    break;
                }

                case 4: // L5 - boulder opens the gate, then round past the spikes
                {
                    yield return DriveTo(rig.Focus, -45f, 3f);
                    var plate = FindInLevel<PressurePlate>();
                    Assert.IsNotNull(plate, "L5 has no pressure plate");
                    yield return LogProp("L5 boulder", 9f, () => plate.IsPressed);
                    Assert.IsTrue(plate.IsPressed, "L5 boulder never reached the plate");
                    var gate = FindInLevel<MovingGate>();
                    yield return WaitUntil(() => gate == null || gate.IsOpen, 4f, "L5 gate to open");
                    yield return DriveTo(rig.Focus, 0f, 3f);
                    yield return DrivePath(C(1, 0), C(2, 0), C(3, 0), C(4, 0), C(4, 1), C(4, 2),
                                           C(4, 3), C(4, 4), C(4, 5), C(3, 5), C(2, 5), C(1, 5),
                                           C(1, 6), C(1, 7), C(1, 8));
                    break;
                }
            }

            yield return WaitUntil(() => levels.State == LevelState.Won, 4f,
                                   $"level {index + 1} to report a win");
        }

        /// <summary>The gaps between islands have to be real obstacles, not decoration.</summary>
        [UnityTest]
        public IEnumerator PlayerCannotWalkOnAir()
        {
            yield return LoadLevel(0);
            Assert.Greater(rig.FloorCellCount, 0, "Level 1 built no floor map");
            Assert.IsTrue(rig.IsGrounded, "Player should start on solid ground");

            // Haul the world towards a patch of pure void, well inside the drag bounds but with
            // no floor anywhere near it. The player must refuse to leave the island.
            var voidTarget = new Vector3(4f, 0f, -2f);
            yield return DriveTo(voidTarget, 0f, 4f);

            Assert.IsTrue(rig.IsGrounded, "Player ended up hovering over empty space");
            Assert.Greater(Vector3.Distance(rig.Focus, voidTarget), 1f,
                           "World was dragged out over open space - gaps are not obstacles");
        }

        // ======================================================================== tests ======

        [UnityTest] public IEnumerator Level1_IsCompletable() { yield return SolveLevel(0); }
        [UnityTest] public IEnumerator Level2_IsCompletable() { yield return SolveLevel(1); }
        [UnityTest] public IEnumerator Level3_IsCompletable() { yield return SolveLevel(2); }
        [UnityTest] public IEnumerator Level4_IsCompletable() { yield return SolveLevel(3); }
        [UnityTest] public IEnumerator Level5_IsCompletable() { yield return SolveLevel(4); }

        /// <summary>The core promise of the game, asserted literally.</summary>
        [UnityTest]
        public IEnumerator PlayerAndCameraNeverMove()
        {
            yield return LoadLevel(0);

            Vector3 p0 = player.transform.position;
            Quaternion pr0 = player.transform.rotation;
            Vector3 c0 = cam.transform.position;
            Quaternion cr0 = cam.transform.rotation;
            float ortho = cam.orthographicSize;

            yield return DriveTo(new Vector3(2.5f, 0f, 3f), 0f);
            rig.RotationAllowed = true;
            yield return DriveTo(new Vector3(-1f, 0f, 1f), 60f);
            rig.AddShake(1f);
            yield return Wait(0.5f);
            yield return DriveTo(new Vector3(1f, 0f, 2f), -30f);

            Assert.Less(Vector3.Distance(p0, player.transform.position), 0.0005f,
                        "Player anchor moved - the whole premise is that it never does");
            Assert.Less(Quaternion.Angle(pr0, player.transform.rotation), 0.05f,
                        "Player anchor rotated");
            Assert.Less(Vector3.Distance(c0, cam.transform.position), 0.0005f,
                        "Camera moved - it must be completely fixed");
            Assert.Less(Quaternion.Angle(cr0, cam.transform.rotation), 0.05f, "Camera rotated");
            Assert.AreEqual(ortho, cam.orthographicSize, 0.0005f, "Camera zoomed");
        }

        [UnityTest]
        public IEnumerator WorldActuallyMovesWhenDragged()
        {
            yield return LoadLevel(0);
            Vector3 before = rig.WorldRoot.position;
            yield return DriveTo(new Vector3(3f, 0f, 4f), 0f);
            Assert.Greater(Vector3.Distance(before, rig.WorldRoot.position), 2f,
                           "World root barely moved for a large drag");
        }

        /// <summary>Rotation must re-aim gravity relative to the level, not just spin the picture.</summary>
        [UnityTest]
        public IEnumerator RotationReaimsGravityInLevelSpace()
        {
            yield return LoadLevel(2);
            Vector3 upright = rig.LocalGravityDirection;
            Assert.Less(Vector3.Angle(upright, Vector3.down), 1.5f,
                        "Level should start with gravity straight down in its own space");

            yield return DriveTo(rig.Focus, -45f, 4f);
            Vector3 tilted = rig.LocalGravityDirection;

            Assert.Greater(Vector3.Angle(upright, tilted), 20f,
                           "Spinning the world did not change gravity relative to the level");
            Assert.Greater(tilted.x, 0.2f,
                           "A negative spin should tip downhill towards +X in level space");
            Assert.Greater(rig.TiltAngle, 20f, "World should read as visibly tilted");
        }

        [UnityTest]
        public IEnumerator RestartResetsTheLevel()
        {
            yield return LoadLevel(2);
            var boulderBefore = FindInLevel<Pushable>();
            Assert.IsNotNull(boulderBefore);
            Vector3 startLocal = boulderBefore.transform.localPosition;

            yield return DriveTo(rig.Focus, -45f, 3f);
            yield return Wait(1.5f);

            levels.Restart();
            yield return null; yield return null;
            // Long enough for the level intro spring to settle back onto the start pose.
            yield return Wait(1.6f);

            Assert.AreEqual(LevelState.Playing, levels.State, "Restart should return to Playing");
            var boulderAfter = FindInLevel<Pushable>();
            Assert.IsNotNull(boulderAfter);
            Assert.Less(Vector3.Distance(startLocal, boulderAfter.transform.localPosition), 0.15f,
                        "Boulder should be back at its authored spot after restart");
            Assert.Less(rig.Focus.magnitude, 0.15f, "World should be back at the start pose");
            Assert.Less(Mathf.Abs(rig.Spin), 1f, "Spin should be reset");
        }

        [UnityTest]
        public IEnumerator PhysicsStaysStableUnderViolentInput()
        {
            yield return LoadLevel(2);

            // Thrash the world: fast drags and full rotations, the worst a player could do.
            for (int i = 0; i < 6; i++)
            {
                yield return DriveTo(new Vector3(Random.Range(-3f, 4f), 0f, Random.Range(0f, 8f)),
                                     Random.Range(-90f, 90f), 1.2f);
            }
            yield return Wait(0.6f);

            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                Vector3 p = rb.position;
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z),
                               $"{rb.name} position went NaN");
                Assert.Less(rb.linearVelocity.magnitude, 90f,
                            $"{rb.name} velocity exploded ({rb.linearVelocity.magnitude:F1} m/s)");
                Assert.Less(Vector3.Distance(p, rig.AnchorPos), 250f, $"{rb.name} flew away");
            }
        }

        [UnityTest]
        public IEnumerator ProgressionAdvancesAfterAWin()
        {
            yield return SolveLevel(0);
            int before = levels.CurrentIndex;
            yield return WaitUntil(() => levels.CurrentIndex != before, 4f, "auto-advance to level 2");
            Assert.AreEqual(before + 1, levels.CurrentIndex);
            Assert.AreEqual(LevelState.Playing, levels.State);
        }

        /// <summary>Level 2 is only a puzzle if the wall genuinely stops the world.</summary>
        [UnityTest]
        public IEnumerator WallsBlockTheWorld()
        {
            yield return LoadLevel(1);
            // Aim straight through the wall at the door.
            yield return DriveTo(new Vector3(0f, 0f, 5f), 0f, 4f);
            Assert.Less(rig.Focus.z, 2.2f,
                        "The world was dragged straight through a solid wall");
            Assert.AreNotEqual(LevelState.Won, levels.State,
                               "Level 2 was solved without going around the wall");
        }

        // ====================================================================== captures =====
        [UnityTest]
        public IEnumerator CaptureAllLevels()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);

            for (int i = 0; i < levels.LevelCount; i++)
            {
                yield return LoadLevel(i);
                yield return Wait(0.9f);              // let idle animation and VFX warm up
                yield return Grab(Path.Combine(dir, $"level_{i + 1:00}_start.png"));
            }

            // A tilted shot, because the rotation mechanic is the thing worth showing.
            yield return LoadLevel(2);
            yield return DriveTo(rig.Focus, -45f, 4f);
            yield return Wait(1.2f);
            yield return Grab(Path.Combine(dir, "level_03_tilted.png"));

            // Mid-drag on level 1, to show the world in motion.
            yield return LoadLevel(0);
            rig.SetPoseTarget(new Vector3(3f, 0f, 4f), 0f);
            yield return Wait(0.28f);
            yield return Grab(Path.Combine(dir, "level_01_dragging.png"));

            Debug.Log("PTW_CAPTURES_WRITTEN: " + dir);
        }

        const int ShotWidth = 1080;
        const int ShotHeight = 1920;

        /// <summary>
        /// Renders the real camera to a RenderTexture and writes a PNG.
        ///
        /// Deliberately NOT ScreenCapture + WaitForEndOfFrame: WaitForEndOfFrame never fires under
        /// -batchmode, so that approach hangs the run forever rather than failing. Rendering
        /// explicitly also lets us pin an exact 1080x1920 portrait frame regardless of the host
        /// window, which is what makes the shots comparable to the reference sheet.
        /// </summary>
        IEnumerator Grab(string path)
        {
            yield return null;   // let one full frame of animation/VFX advance

            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1
            };
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            try
            {
                cam.targetTexture = rt;
                // Re-frame for the capture aspect, otherwise the composition is the host window's.
                var rigCam = cam.GetComponent<IsometricCameraRig>();
                if (rigCam) rigCam.ApplyFraming();

                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
                tex.Apply(false);

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"PTW_CAPTURE {Path.GetFileName(path)} {ShotWidth}x{ShotHeight}");
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
                var rigCam = cam.GetComponent<IsometricCameraRig>();
                if (rigCam) rigCam.ApplyFraming();
            }
        }
    }
}
