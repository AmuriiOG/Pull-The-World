using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PullTheWorld.Tests
{
    /// <summary>
    /// Drives the real game the way a player would - by asking the rotator for an angle and letting
    /// the spring, the solver and the physics do the rest - then asserts the promises v2 makes.
    ///
    /// The promises changed completely from v1, so these tests did too. v1 asserted that the player
    /// NEVER MOVES, which was its entire premise. v2 asserts nearly the opposite: that tilting the
    /// level does move the player, that gravity never changes while it happens, and that the player
    /// stays in the puzzle plane while doing it.
    ///
    /// Also writes real 1080x1920 captures to /Captures, which is how the art actually gets
    /// directed - see Grab().
    /// </summary>
    public class PtwPlayTests
    {
        WorldRotator rotator;
        LevelManager levels;
        PlayerBody player;
        Camera cam;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Game", LoadSceneMode.Single);
            yield return null;
            yield return null;

            rotator = WorldRotator.Instance;
            levels = LevelManager.Instance;
            player = PlayerBody.Instance;
            cam = Camera.main;

            Assert.IsNotNull(rotator, "WorldRotator missing from scene");
            Assert.IsNotNull(levels, "LevelManager missing from scene");
            Assert.IsNotNull(player, "PlayerBody missing from scene");
            Assert.IsNotNull(cam, "Main camera missing from scene");
            Assert.Greater(levels.LevelCount, 0, "No levels assigned");

            yield return null;
        }

        // ======================================================================= helpers =====
        IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator LoadLevel(int index)
        {
            levels.LoadLevel(index);
            yield return null;
            yield return null;
            // Let the ball drop in and settle before a test starts measuring anything.
            yield return Wait(0.9f);
        }

        /// <summary>
        /// Ask for an absolute angle and wait for the spring to arrive. Goes through the same
        /// BeginDrive/Drive/EndDrive path a finger uses, so a test cannot accidentally exercise a
        /// code path the game does not.
        /// </summary>
        IEnumerator RotateTo(float angle, float timeout = 3f)
        {
            rotator.BeginDrive();
            rotator.Drive(angle - rotator.AngleTarget);
            rotator.EndDrive(0f);

            float t = 0f;
            while (t < timeout)
            {
                if (Mathf.Abs(rotator.Angle - rotator.AngleTarget) < 0.35f) break;
                t += Time.deltaTime;
                yield return null;
            }
            yield return Wait(0.35f);
        }

        IEnumerator WaitUntil(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (t < timeout)
            {
                if (cond()) yield break;
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"PTW_DIAG timed out waiting for {what}");
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // ========================================================================= tests =====

        [UnityTest]
        public IEnumerator EveryLevelHasSpawnAndExit()
        {
            for (int i = 0; i < levels.LevelCount; i++)
            {
                yield return LoadLevel(i);
                var def = levels.Current;
                Assert.IsNotNull(def, $"Level {i + 1} failed to instantiate");
                Assert.IsNotNull(def.exit, $"Level {i + 1} has no ExitPortal");
                Assert.Greater(def.transform.childCount, 2,
                               $"Level {i + 1} looks empty - the ASCII map probably placed nothing");
            }
        }

        /// <summary>
        /// The one thing v2 inherited unchanged from v1: the camera is nailed down. A camera that
        /// drifts turns "the world is rotating" back into "I am moving", which is the exact
        /// ambiguity the redesign exists to remove.
        /// </summary>
        [UnityTest]
        public IEnumerator CameraNeverMoves()
        {
            yield return LoadLevel(0);

            Vector3 c0 = cam.transform.position;
            Quaternion cr0 = cam.transform.rotation;
            float ortho = cam.orthographicSize;

            yield return RotateTo(35f);
            rotator.AddShake(1f);
            yield return Wait(0.5f);
            yield return RotateTo(-35f);

            Assert.Less(Vector3.Distance(c0, cam.transform.position), 0.0005f,
                        "Camera moved - it must be completely fixed");
            Assert.Less(Quaternion.Angle(cr0, cam.transform.rotation), 0.05f, "Camera rotated");
            Assert.AreEqual(ortho, cam.orthographicSize, 0.0005f, "Camera zoomed");
        }

        /// <summary>
        /// Gravity is a constant. v1's mechanic was "rotation re-aims gravity"; v2's is "rotation
        /// moves the level while gravity stays put". If anything ever starts writing Physics.gravity
        /// again, the whole readability argument collapses and this test is the tripwire.
        /// </summary>
        [UnityTest]
        public IEnumerator GravityIsConstantAndPointsDown()
        {
            yield return LoadLevel(2);

            Vector3 g0 = Physics.gravity;
            Assert.Less(g0.y, -1f, "Gravity should point down the screen");
            Assert.AreEqual(0f, g0.x, 0.0001f, "Gravity has a sideways component");
            Assert.AreEqual(0f, g0.z, 0.0001f, "Gravity has a depth component");

            yield return RotateTo(40f);
            yield return RotateTo(-40f);

            Assert.AreEqual(g0, Physics.gravity, "Something re-aimed gravity during play");
        }

        [UnityTest]
        public IEnumerator RotatingTheWorldActuallyRotatesIt()
        {
            yield return LoadLevel(0);

            float before = rotator.WorldRoot.rotation.eulerAngles.z;
            yield return RotateTo(30f);
            float after = rotator.WorldRoot.rotation.eulerAngles.z;

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(before, after)), 20f,
                           "World root barely rotated for a 30 degree request");
        }

        /// <summary>The core mechanic, asserted as directly as it can be.</summary>
        [UnityTest]
        public IEnumerator TiltingTheWorldMovesThePlayer()
        {
            yield return LoadLevel(0);

            Vector3 start = player.transform.position;
            yield return RotateTo(28f);
            yield return Wait(1.4f);          // let gravity do the work

            float moved = Vector3.Distance(start, player.transform.position);
            Assert.Greater(moved, 0.6f,
                           "Player did not move when the level was tilted - the entire mechanic " +
                           "is that gravity carries them once the floor is no longer level");
        }

        /// <summary>
        /// The ball is constrained to XY. Without this it can be nudged out of the puzzle plane by
        /// a bad contact and end up visually in front of or behind the level it is standing on.
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerStaysInThePuzzlePlane()
        {
            yield return LoadLevel(3);

            for (float a = -40f; a <= 40f; a += 20f)
            {
                yield return RotateTo(a, 2f);
                Assert.Less(Mathf.Abs(player.transform.position.z), 0.05f,
                            $"Player left the XY plane at {a} degrees (z={player.transform.position.z})");
            }
        }

        [UnityTest]
        public IEnumerator PlayerLandsAndSettlesOnLoad()
        {
            yield return LoadLevel(0);
            yield return Wait(1.2f);

            Assert.IsTrue(player.IsAlive, "Player died just from spawning");
            Assert.IsTrue(IsFinite(player.transform.position), "Player position is NaN");
            Assert.Less(player.Speed, 2.5f,
                        "Player never settled after spawning - it is probably falling through the floor");
        }

        [UnityTest]
        public IEnumerator RestartResetsThePlayer()
        {
            yield return LoadLevel(0);
            yield return RotateTo(35f);
            yield return Wait(1f);

            levels.Restart();
            yield return null;
            yield return null;

            // Read the spawn point AFTER the restart, not before. It is expressed in level-local
            // space, and a restart re-instantiates the level at its start angle - so a spawn point
            // sampled while the level was still tilted 35 degrees resolves to a different world
            // position than the one the player is correctly put back at.
            Vector3 spawn = levels.Current.WorldSpawnPoint;

            Assert.Less(Vector3.Distance(player.transform.position, spawn), 0.6f,
                        "Restart did not put the player back at the spawn point");
            Assert.AreEqual(LevelState.Playing, levels.State, "Restart did not resume play");
        }

        /// <summary>An angle limit is what stops early levels being solved by one panicked spin.</summary>
        [UnityTest]
        public IEnumerator AngleLimitIsRespected()
        {
            yield return LoadLevel(0);
            float limit = levels.Current.angleLimit;
            if (limit <= 0f) Assert.Ignore("Level 1 has no angle limit set");

            yield return RotateTo(limit + 180f, 3f);
            Assert.LessOrEqual(Mathf.Abs(rotator.AngleTarget), limit + 0.5f,
                               "Rotation went past the level's angle limit");
        }

        /// <summary>
        /// Spin every level hard in both directions and assert nothing detonates. This is the test
        /// that catches the failure mode this architecture is most exposed to: a compound kinematic
        /// body being rotated into dynamic bodies at speed.
        /// </summary>
        [UnityTest]
        public IEnumerator PhysicsNeverExplodes()
        {
            for (int i = 0; i < levels.LevelCount; i++)
            {
                yield return LoadLevel(i);

                float limit = levels.Current.angleLimit;
                float sweep = limit > 0f ? limit : 120f;

                yield return RotateTo(sweep, 2.5f);
                yield return RotateTo(-sweep, 2.5f);
                yield return RotateTo(0f, 2.5f);

                Assert.IsTrue(IsFinite(player.transform.position),
                              $"Level {i + 1}: player position went non-finite");
                Assert.Less(player.transform.position.magnitude, 200f,
                            $"Level {i + 1}: player was launched out of the world");

                foreach (var rb in DynamicRegistry.Bodies)
                {
                    if (!rb) continue;
                    Assert.IsTrue(IsFinite(rb.position),
                                  $"Level {i + 1}: {rb.name} position went non-finite");
                    Assert.IsTrue(IsFinite(rb.linearVelocity),
                                  $"Level {i + 1}: {rb.name} velocity went non-finite");
                    Assert.Less(rb.linearVelocity.magnitude, 80f,
                                $"Level {i + 1}: {rb.name} reached an absurd speed");
                }
            }
        }

        /// <summary>Falling off the island has to end the run, or a lost ball hangs the level.</summary>
        [UnityTest]
        public IEnumerator FallingOutOfTheWorldFails()
        {
            yield return LoadLevel(0);

            // Teleport below everything rather than trying to physically roll off an edge, which
            // would make the test a level-design assertion instead of a rules assertion.
            player.Body.position = new Vector3(0f, -60f, 0f);
            yield return WaitUntil(() => !player.IsAlive || levels.State != LevelState.Playing,
                                   2f, "the fall to register as a failure");

            Assert.IsTrue(!player.IsAlive || levels.State == LevelState.Failed,
                          "Player fell out of the world and the level did not fail");
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
                yield return Wait(0.6f);              // let VFX warm up
                yield return Grab(Path.Combine(dir, $"level_{i + 1:00}_start.png"));
            }

            // A tilted shot, because the rotation mechanic is the thing worth looking at.
            yield return LoadLevel(0);
            yield return RotateTo(32f, 3f);
            yield return Wait(0.5f);
            yield return Grab(Path.Combine(dir, "level_01_tilted.png"));

            yield return LoadLevel(4);
            yield return RotateTo(45f, 3f);
            yield return Wait(0.8f);
            yield return Grab(Path.Combine(dir, "level_05_tilted.png"));

            Debug.Log("PTW_CAPTURES_WRITTEN: " + dir);
        }

        /// <summary>
        /// Captures the three UI screens. Worth having in the same automated pass as the levels:
        /// they are as much of the deliverable as the gameplay is, and they are the part most
        /// likely to be quietly broken by a layout change nobody re-renders.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureUiScreens()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);

            // The scene opens on the main menu, over the editor preview island.
            yield return Wait(0.9f);
            yield return Grab(Path.Combine(dir, "ui_01_main_menu.png"));

            var settingsBtn = FindButton("SettingsButton");
            Assert.IsNotNull(settingsBtn, "Main menu has no SettingsButton");
            settingsBtn.onClick.Invoke();
            yield return Wait(0.7f);
            yield return Grab(Path.Combine(dir, "ui_02_settings.png"));

            var closeBtn = FindButton("CloseButton");
            Assert.IsNotNull(closeBtn, "Settings panel has no CloseButton");
            closeBtn.onClick.Invoke();
            yield return Wait(0.5f);

            // Win the level outright rather than trying to solve it - this is a UI capture, and
            // making it depend on a puzzle being solvable would make it fail for the wrong reason.
            yield return LoadLevel(0);
            levels.ReportWin();
            yield return Wait(1.5f);
            yield return Grab(Path.Combine(dir, "ui_03_level_complete.png"));

            Debug.Log("PTW_UI_CAPTURES_WRITTEN: " + dir);
        }

        static UnityEngine.UI.Button FindButton(string name)
        {
            // Hidden panels are deactivated by UiPanel, so inactive objects have to be included.
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var b in all)
                if (b.name == name) return b;
            return null;
        }

        const int ShotWidth = 1080;
        const int ShotHeight = 1920;

        /// <summary>
        /// Renders the real camera to a RenderTexture and writes a PNG.
        ///
        /// Deliberately NOT ScreenCapture + WaitForEndOfFrame: WaitForEndOfFrame never fires under
        /// -batchmode, so that approach hangs the run forever rather than failing. Rendering
        /// explicitly also pins an exact 1080x1920 portrait frame regardless of the host window,
        /// which is what makes the shots comparable to each other and to the reference sheet.
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
            var rigCam = cam.GetComponent<PlaneCameraRig>();

            try
            {
                cam.targetTexture = rt;
                // Re-frame for the capture aspect, otherwise the composition is the host window's.
                if (rigCam) rigCam.Apply();

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
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (rigCam) rigCam.Apply();
            }
        }
    }
}
