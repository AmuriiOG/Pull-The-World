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

        // The suite plays the real game against the REAL PlayerPrefs on whoever's machine runs
        // it. PhysicsNeverExplodes sweeps every level to its limits, which quite legitimately
        // rolls the ball into the door, and every one of those wins was being saved - a test run
        // left the developer's game at "LEVEL 18 OF 18". Snapshot progress before the first test
        // and put it back after the last.
        static int? suiteSavedProgress;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!suiteSavedProgress.HasValue) suiteSavedProgress = GameProgress.UnlockedIndex;

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

        /// <summary>
        /// Turn the level the way a finger does: a stream of small Drive() deltas at a plausible
        /// speed. A single Drive() step is a snap no finger produces; a test about how a rock
        /// behaves should turn the level the way a player would.
        /// </summary>
        IEnumerator DragTo(float angle, float degreesPerSecond = 110f, float timeout = 5f)
        {
            rotator.BeginDrive();
            float t = 0f;
            while (Mathf.Abs(rotator.AngleTarget - angle) > 0.01f && t < timeout)
            {
                float maxStep = degreesPerSecond * Time.deltaTime;
                rotator.Drive(Mathf.Clamp(angle - rotator.AngleTarget, -maxStep, maxStep));
                t += Time.deltaTime;
                yield return null;
            }
            rotator.EndDrive(0f);
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

        [UnityTest, Timeout(600000)]                        // grows with the level count
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
        /// Finishing a level is a journey, not a cut: the camera glides up the world to the next
        /// level, which was already standing in its slot, and only then does the orb drop in. The
        /// finished level stays behind as scenery with no physics left on it.
        /// </summary>
        [UnityTest]
        public IEnumerator PortalTravelsToTheNextLevel()
        {
            yield return LoadLevel(0);
            Vector3 camBefore = cam.transform.position;
            Assert.IsNotNull(GameObject.Find("Level_02 [scenery]"), "The next level is not standing in the world ahead");

            levels.ReportWin();
            yield return WaitUntil(() => levels.IsTravelling, 3f, "the camera to set off");
            Assert.IsFalse(levels.IsPlaying, "Control was handed back before the camera arrived");
            // Half way up: both islands in frame, the sky lagging. The one frame of the whole game
            // that no other capture shows.
            yield return Wait(0.85f);
            yield return Grab(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures")), "travel_01_to_02_mid.png"));
            yield return WaitUntil(() => levels.IsPlaying && !levels.IsTravelling, 6f, "the next level to start");

            Assert.AreEqual(1, levels.CurrentIndex, "Did not arrive at level 2");
            Assert.Greater(cam.transform.position.y - camBefore.y, 10f, "The camera did not glide up to the next level");
            Assert.IsTrue(player.IsAlive && player.gameObject.activeInHierarchy, "The orb did not drop into the next level");
            Assert.Less(Vector3.Distance(player.transform.position, levels.Current.WorldSpawnPoint), 3f,
                        "The orb did not spawn at the next level");

            var scenery = GameObject.Find("Level_01 [scenery]");
            Assert.IsNotNull(scenery, "The finished level did not stay behind as scenery");
            Assert.AreEqual(0, scenery.GetComponentsInChildren<Collider>(true).Length, "Scenery still has colliders");
            Assert.AreEqual(0, scenery.GetComponentsInChildren<MonoBehaviour>(true).Length, "Scenery still has behaviours");
            Assert.IsNotNull(GameObject.Find("Level_03 [scenery]"), "The level after next was not prepared");
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
        [UnityTest, Timeout(600000)]                        // grows with the level count
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
                Assert.Less((player.transform.position - levels.Pivot).magnitude, 200f,
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
            player.Body.position = levels.Pivot + new Vector3(0f, -60f, 0f);
            yield return WaitUntil(() => !player.IsAlive || levels.State != LevelState.Playing,
                                   2f, "the fall to register as a failure");

            Assert.IsTrue(!player.IsAlive || levels.State == LevelState.Failed,
                          "Player fell out of the world and the level did not fail");
        }

        /// <summary>
        /// Regression for a leak: MovingPlatform detaches from the level hierarchy in Awake (it has
        /// to, to carry the player), which means destroying the level did not destroy it. One
        /// platform per level load survived forever. It must now go when its owning level goes.
        /// </summary>
        [UnityTest]
        public IEnumerator PlatformsDoNotLeakAcrossLevels()
        {
            yield return LoadLevel(7);                      // level 8: has a ferry
            Assert.AreEqual(1, MovingPlatform.Active.Count, "Level 8 should have exactly one platform");

            yield return LoadLevel(0);                      // level 1: has none
            yield return null;
            yield return null;
            Assert.AreEqual(0, MovingPlatform.Active.Count,
                            "A platform survived its level being unloaded");
        }

        /// <summary>
        /// Opening settings during play has to STOP THE CLOCK, not just the input - otherwise the
        /// ball keeps rolling into hazards behind the overlay. Frame yields only in here: Wait()
        /// is scaled time and would spin forever while paused.
        /// </summary>
        [UnityTest]
        public IEnumerator SettingsActuallyPausesTheGame()
        {
            yield return LoadLevel(0);

            var pause = FindButton("PauseButton");
            Assert.IsNotNull(pause, "HUD has no PauseButton");
            pause.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Settings opened during play did not pause");

            var close = FindButton("CloseButton");
            Assert.IsNotNull(close, "Settings has no CloseButton");
            close.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Closing settings did not resume");
        }

        /// <summary>The two-tap restart: first tap arms, second wipes progress and goes to level 1.</summary>
        [OneTimeTearDown]
        public void RestoreProgress()
        {
            if (!suiteSavedProgress.HasValue) return;
            GameProgress.UnlockedIndex = suiteSavedProgress.Value;
            suiteSavedProgress = null;
        }

        /// <summary>The two-tap restart: first tap arms, second wipes progress and goes to level 1.</summary>
        [UnityTest]
        public IEnumerator RestartAllNeedsTwoTapsAndGoesToLevelOne()
        {
            yield return LoadLevel(4);                      // be on level 5 with some progress
            GameProgress.ReportCleared(3);
            Assert.GreaterOrEqual(GameProgress.UnlockedIndex, 4);

            FindButton("PauseButton").onClick.Invoke();
            yield return null;

            var restartAll = FindButton("RestartAllButton");
            Assert.IsNotNull(restartAll, "Settings has no RestartAllButton");

            restartAll.onClick.Invoke();                    // arm
            yield return null;
            Assert.GreaterOrEqual(GameProgress.UnlockedIndex, 4, "A single tap must not wipe progress");
            Assert.AreEqual(4, levels.CurrentIndex, "A single tap must not change level");

            restartAll.onClick.Invoke();                    // confirm
            yield return null;
            yield return null;
            Assert.AreEqual(0, GameProgress.UnlockedIndex, "Progress was not reset");
            Assert.AreEqual(0, levels.CurrentIndex, "Did not return to level 1");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Restart-all left the game paused");
        }

        /// <summary>
        /// Water floats the player. Drop the ball into level 19's pool and it must come to rest
        /// near the SURFACE, not on the bed - and still be alive, because water is not a hazard.
        /// </summary>
        [UnityTest]
        public IEnumerator WaterFloatsThePlayer()
        {
            yield return LoadLevel(18);                     // level 19: Wade Through
            var water = levels.Current.GetComponentInChildren<WaterVolume>();
            Assert.IsNotNull(water, "Level 19 has no WaterVolume");

            // Teleport above the pool and let it fall in.
            Vector3 surface = water.SurfaceWorldPoint;
            player.Body.position = surface + Vector3.up * 1.2f;
            player.Body.linearVelocity = Vector3.zero;
            yield return Wait(2.0f);

            Assert.IsTrue(player.IsAlive, "Water killed the player");
            float y = player.transform.position.y;
            Assert.Greater(y, water.BedWorldPoint.y + 0.2f,
                           $"Player sank to the bed (y={y:F2}, bed={water.BedWorldPoint.y:F2})");
            Assert.Less(y, surface.y + player.Radius + 0.35f,
                        $"Player is not in the water at all (y={y:F2}, surface={surface.y:F2})");
        }

        /// <summary>
        /// Water puts out fire when poured onto it. On level 20 the pool is immediately uphill of
        /// the flame; tip towards the door and the fire must be out within a couple of seconds.
        /// </summary>
        [UnityTest]
        public IEnumerator PouringDousesTheFire()
        {
            yield return LoadLevel(19);                     // level 20: Pour It Out
            var fire = levels.Current.GetComponentInChildren<Hazard>();
            var water = levels.Current.GetComponentInChildren<WaterVolume>();
            Assert.IsNotNull(fire, "Level 20 has no fire");
            Assert.IsNotNull(water, "Level 20 has no pool");
            Assert.IsTrue(fire.Armed, "Fire should start lit");

            // The player rolls too, and may die in the flame before the pour lands. Park it out of
            // the way so this stays a test of the water rule rather than of the race.
            player.Freeze();
            player.Body.position = levels.Pivot + new Vector3(0f, 12f, 0f);   // levels stand in slots up the world

            yield return RotateTo(-50f, 3f);                // door side down
            yield return WaitUntil(() => fire.Smothered, 3f, "the fire to be doused");

            Assert.IsTrue(fire.Smothered, "Pouring the pool onto the fire did not put it out");
            Assert.Less(water.Fill, 0.95f, "The pool did not drain while pouring");
        }

        /// <summary>Plates are for rocks. The ball sitting on one must do nothing.</summary>
        [UnityTest]
        public IEnumerator PlayerCannotPressThePlate()
        {
            yield return LoadLevel(4);                      // level 5: Hold It Down
            var plate = levels.Current.GetComponentInChildren<PressurePlate>();
            Assert.IsNotNull(plate, "Level 5 has no plate");

            player.Body.position = plate.transform.position + Vector3.up * 0.6f;
            player.Body.linearVelocity = Vector3.zero;
            yield return Wait(1.5f);

            Assert.IsFalse(plate.Pressed, "The player pressed a plate - plates must need a rock");
        }

        [UnityTest]
        public IEnumerator RockPressesThePlate()
        {
            yield return LoadLevel(4);
            var plate = levels.Current.GetComponentInChildren<PressurePlate>();
            var rock = FirstRock();
            Assert.IsNotNull(rock, "Level 5 has no rock");

            rock.position = plate.transform.position + Vector3.up * 0.6f;
            rock.linearVelocity = Vector3.zero;
            yield return Wait(1.5f);

            Assert.IsTrue(plate.Pressed, "A rock resting on the plate did not press it");
        }

        [UnityTest]
        public IEnumerator EnemyKillsOnContact()
        {
            yield return LoadLevel(20);                     // level 21: Bowl It Over
            var enemy = levels.Current.GetComponentInChildren<Enemy>();
            Assert.IsNotNull(enemy, "Level 21 has no enemy");

            player.Body.position = enemy.transform.position + Vector3.up * 0.75f;
            player.Body.linearVelocity = Vector3.zero;
            yield return WaitUntil(() => !player.IsAlive, 2f, "the enemy to kill the player");

            Assert.IsFalse(player.IsAlive, "Touching an enemy did not kill the player");
        }

        /// <summary>
        /// A ball rolling along a flat row of blocks must not register landings. The level is a
        /// compound body of one collider per block, so every seam raises OnCollisionEnter;
        /// measured by relative-velocity MAGNITUDE that was a 3-6 m/s "impact" about five times a
        /// second (a thud, a shake, a flash - the "weird knocking under the music"). Measured along
        /// the contact normal the same seams are under 2 m/s and ignored. Before the fix this run
        /// logged six impacts; a couple are still legitimate (the ball hops a block edge as the
        /// level tips) at strength 0.2-0.45, so the bound is on LOUD landings (0.5+), not on count.
        /// </summary>
        [UnityTest]
        public IEnumerator RollingDoesNotThud()
        {
            yield return LoadLevel(0);
            yield return Wait(0.5f);
            var hits = new System.Collections.Generic.List<float>();
            void Count(float strength, Vector3 _) => hits.Add(strength);
            player.OnImpact += Count;
            yield return DragTo(-22f);
            yield return Wait(1.6f);
            player.OnImpact -= Count;
            Debug.Log("PTW_DIAG roll impacts: " + string.Join(", ", hits.ConvertAll(h => h.ToString("F2"))));
            // Count the LOUD ones. With the magnitude measure every seam was a 0.5-0.9 hit (five or
            // six of them here); with the closing-speed measure the genuine block-edge hops come in
            // at 0.2-0.45 and are nearly silent (volume is squared). How many small hops occur
            // varies with frame pacing, so the bound is on strength, not on count.
            int loud = hits.FindAll(h => h >= 0.5f).Count;
            Assert.LessOrEqual(loud, 1, "Rolling along the grass row registered " + loud + " hard landings of " + hits.Count);
        }

        /// <summary>The intended solution to level 21: tip towards the door, the rock bowls it.</summary>
        [UnityTest]
        public IEnumerator RockCrushesTheEnemy()
        {
            // Park the orb BEFORE the level settles, not after. Left at its spawn through the
            // settle, the enemy (four cells away, inside alertRange) hunts it and rolls up against
            // the rock; when the level then tips, rock and enemy roll downhill already touching -
            // one gentle OnCollisionEnter at the start, no impact at the wall, no crush. Whether
            // the enemy got that far in 0.9 s came down to frame pacing (it did in the Editor, not
            // headless). Parked, the enemy sleeps where the level put it, reaches the wall first,
            // and the rock arrives at full speed.
            levels.LoadLevel(20);
            yield return null;
            yield return null;
            var enemy = levels.Current.GetComponentInChildren<Enemy>();
            Assert.IsNotNull(enemy);
            var rock = FirstRock();
            ParkPlayer();
            yield return Wait(1.2f);

            // Sampled the whole way, like RockBreaksTheCrate: a failure should say where the rock
            // and the enemy went, not just that nothing died.
            var trail = new System.Text.StringBuilder();
            float t = 0f, nextSample = 0f;
            rotator.BeginDrive();
            rotator.Drive(-50f - rotator.AngleTarget);      // door side down
            rotator.EndDrive(0f);
            while (t < 7f && enemy && enemy.IsAlive)
            {
                if (t >= nextSample)
                {
                    nextSample += 0.1f;
                    var rl = rock ? rotator.WorldRoot.InverseTransformPoint(rock.position) : Vector3.zero;
                    var el = rotator.WorldRoot.InverseTransformPoint(enemy.transform.position);
                    float rv = rock ? rock.linearVelocity.magnitude : 0f;
                    trail.Append($"[{t:F1}s a={rotator.Angle:F0}/{rotator.AngleTarget:F0} R=({rl.x:F2},{rl.y:F2}) v={rv:F1} E=({el.x:F2},{el.y:F2})] ");
                }
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"PTW_DIAG crush enemyAlive={(enemy ? enemy.IsAlive : false)} after {t:F2}s; trail " + trail);

            Assert.IsTrue(enemy == null || !enemy.IsAlive, "The rock did not kill the enemy");
        }

        [UnityTest]
        public IEnumerator RockBreaksTheCrate()
        {
            yield return LoadLevel(21);                     // level 22: Break Through
            var crate = levels.Current.GetComponentInChildren<Breakable>();
            Assert.IsNotNull(crate, "Level 22 has no breakable");
            var rock = FirstRock();
            Assert.IsNotNull(rock, "Level 22 has no rock");
            ParkPlayer();

            // The drag is inlined (same finger-speed policy as DragTo) so the WHOLE run-up is
            // sampled: a failure here should show where the rock went, not just that it didn't break.
            var trail = new System.Text.StringBuilder();
            float peak = 0f, t = 0f, nextSample = 0f;
            bool released = false;
            rotator.BeginDrive();
            while (t < 5.5f && !crate.Broken)
            {
                if (!released)
                {
                    float maxStep = 110f * Time.deltaTime;
                    rotator.Drive(Mathf.Clamp(-55f - rotator.AngleTarget, -maxStep, maxStep));
                    if (Mathf.Abs(rotator.AngleTarget + 55f) < 0.01f) { rotator.EndDrive(0f); released = true; }
                }
                if (rock)
                {
                    peak = Mathf.Max(peak, rock.linearVelocity.magnitude);
                    if (t >= nextSample)
                    {
                        nextSample += 0.1f;
                        var lp = rotator.WorldRoot.InverseTransformPoint(rock.position);
                        trail.Append($"[{t:F1}s a={rotator.Angle:F0} L=({lp.x:F2},{lp.y:F2}) v={rock.linearVelocity.magnitude:F1}] ");
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"PTW_DIAG crate broken={crate.Broken} after {t:F2}s, rock peak speed {peak:F2} m/s, " +
                      $"rock at {(rock ? rock.position : Vector3.zero)}, crate at {crate.transform.position}");
            Debug.Log("PTW_DIAG trail " + trail);

            Assert.IsTrue(crate.Broken, $"The rock did not break the crate (peak rock speed {peak:F2} m/s)");
        }

        /// <summary>Same tilt, no rock: the ball alone must NOT break it.</summary>
        [UnityTest]
        public IEnumerator PlayerCannotBreakTheCrate()
        {
            yield return LoadLevel(21);
            var crate = levels.Current.GetComponentInChildren<Breakable>();
            var rock = FirstRock();
            if (rock) UnityEngine.Object.Destroy(rock.gameObject);
            yield return null;

            yield return DragTo(-55f);
            yield return Wait(3f);

            Assert.IsFalse(crate.Broken, "The ball broke a crate - only a heavy rock may");
        }

        /// <summary>
        /// A rock that leaves the world comes back to its start cell. Without this a wild fling
        /// would soft-lock every plate and crate level until the player thinks to restart.
        /// </summary>
        [UnityTest]
        public IEnumerator LostRockComesBack()
        {
            yield return LoadLevel(21);
            var rock = FirstRock();
            Assert.IsNotNull(rock, "Level 22 has no rock");
            Vector3 home = rock.position;

            rock.position = levels.Pivot + new Vector3(0f, -60f, 0f);      // well past the fall radius
            yield return WaitUntil(() => rock.position.y > -20f, 2f, "the rock to respawn");

            Assert.Less(Vector3.Distance(rock.position, home), 1.5f,
                        "The lost rock did not come back to its start cell");
        }

        /// <summary>
        /// Falling out must restart fast. The ball is doomed the moment it is below the level's
        /// framing box; waiting for a far radius left seconds of empty screen.
        /// </summary>
        [UnityTest]
        public IEnumerator FallingOffRestartsQuickly()
        {
            yield return LoadLevel(0);
            float below = -(levels.Current.viewExtents.y + 2f);
            player.Body.position = new Vector3(0f, below, 0f);

            yield return Wait(0.7f);

            Assert.IsTrue(player.IsAlive, "The level did not restart within 0.7 s of the ball leaving the world");
            Assert.Less(Vector3.Distance(player.transform.position, levels.Current.WorldSpawnPoint), 2.5f,
                        "The ball was not back at spawn after the fall restart");
        }

        /// <summary>
        /// A restart tapped during the death hit-stop must put the clock back. The first build left
        /// the whole game at 12 % speed, which the suite found by crawling into a timeout.
        /// </summary>
        [UnityTest]
        public IEnumerator RestartDuringHitStopRestoresTime()
        {
            yield return LoadLevel(0);
            levels.ReportFail();                            // on-screen death: hit-stop begins
            yield return null;
            Assert.Less(Time.timeScale, 0.5f, "Hit-stop did not slow the clock");

            levels.Restart();                               // cancels the pending fail routine
            yield return null;
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "Restart during hit-stop left the clock slow");
            yield return Wait(0.3f);
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "Clock drifted after the restart");
        }

        /// <summary>A body set down on a spring pad is thrown along the pad's up.</summary>
        [UnityTest]
        public IEnumerator BouncePadLaunchesTheBall()
        {
            yield return LoadLevel(23);                     // level 24: Spring Step
            var pad = levels.Current.GetComponentInChildren<BouncePad>();
            Assert.IsNotNull(pad, "Level 24 has no spring pad");

            player.Body.position = pad.transform.position + pad.transform.up * 0.5f;
            player.Body.linearVelocity = Vector3.zero;
            for (int i = 0; i < 4; i++) yield return new WaitForFixedUpdate();

            float up = Vector3.Dot(player.Body.linearVelocity, pad.transform.up);
            Assert.Greater(up, 5f, $"The pad did not throw the ball (up speed {up:F2} m/s)");
        }

        /// <summary>Three deaths on one level and the HUD offers the rewarded skip; a fresh level withdraws it.</summary>
        [UnityTest]
        public IEnumerator SkipIsOfferedAfterRepeatedFails()
        {
            yield return LoadLevel(0);
            var ui = UiRoot.Instance;
            var ads = PullTheWorld.Ads.AdsManager.Instance;
            Assert.IsNotNull(ui, "No UiRoot");
            Assert.IsNotNull(ads, "No AdsManager");
            Assert.IsFalse(ui.SkipOffered, "Skip offered before any failure");

            for (int i = 0; i < ads.SkipAfterFails; i++)
            {
                levels.ReportFail();
                yield return Wait(1.4f);                    // death pause + restart + settle
            }

            Assert.AreEqual(ads.SkipAfterFails, levels.FailsOnLevel, "Fail count did not survive restarts");
            Assert.IsTrue(ui.SkipOffered, "Skip was not offered after repeated failures");

            yield return LoadLevel(1);
            Assert.IsFalse(ui.SkipOffered, "Skip offer survived into a new level");
        }

        /// <summary>The interstitial cadence: never early, never on its own, never too often.</summary>
        [UnityTest]
        public IEnumerator InterstitialCadenceIsConservative()
        {
            var ads = PullTheWorld.Ads.AdsManager.Instance;
            Assert.IsNotNull(ads, "No AdsManager");
            bool removed = PullTheWorld.Ads.AdsManager.AdsRemoved;
            PullTheWorld.Ads.AdsManager.AdsRemoved = false;
            try
            {
                Assert.IsFalse(ads.IsInterstitialDue(2, 10, 1000f), "Interstitial due in the tutorial levels");
                Assert.IsFalse(ads.IsInterstitialDue(10, 1, 1000f), "Interstitial due after a single win");
                Assert.IsFalse(ads.IsInterstitialDue(10, 3, 10f), "Interstitial due seconds after the last one");
                Assert.IsTrue(ads.IsInterstitialDue(10, 3, 1000f), "Interstitial not due when every rule is met");
                PullTheWorld.Ads.AdsManager.AdsRemoved = true;
                Assert.IsFalse(ads.IsInterstitialDue(10, 3, 1000f), "Interstitial due with ads removed");
            }
            finally
            {
                PullTheWorld.Ads.AdsManager.AdsRemoved = removed;
            }
            yield return null;
        }

        /// <summary>The soundtrack obeys the Music toggle: it fades out when off and back in when on.</summary>
        [UnityTest]
        public IEnumerator MusicFollowsTheSetting()
        {
            var music = PtwMusic.Instance;
            Assert.IsNotNull(music, "No PtwMusic in the scene");
            bool was = GameProgress.MusicOn;
            try
            {
                GameProgress.MusicOn = false;
                yield return Wait(2.2f);
                Assert.IsFalse(music.WantsToPlay, "Music still wants to play with the setting off");
                Assert.Less(music.LiveVolume, 0.01f, "Music did not fade out when switched off");

                GameProgress.MusicOn = true;
                yield return Wait(2.2f);
                Assert.IsTrue(music.WantsToPlay, "Music does not want to play with the setting on");
                Assert.Greater(music.LiveVolume, 0.05f, "Music did not come back when switched on");
            }
            finally
            {
                GameProgress.MusicOn = was;
            }
        }

        /// <summary>First boulder/crate in the level, excluding enemies (which also carry DynamicProp).</summary>
        Rigidbody FirstRock()
        {
            foreach (var p in levels.Current.GetComponentsInChildren<DynamicProp>(true))
                if (!p.GetComponent<Enemy>()) return p.GetComponent<Rigidbody>();
            return null;
        }

        /// <summary>Freeze the player far away so a physics test is about the rule, not the race.</summary>
        void ParkPlayer()
        {
            player.Freeze();
            player.Body.position = levels.Pivot + new Vector3(0f, 12f, 0f);   // above the ACTIVE level, wherever it stands
        }

        // ------------------------------------------------------------- capture encoding -----
        /// <summary>Linear float pixels to an 8-bit sRGB PNG, the way a phone screen would show them.</summary>
        static byte[] ToPng(Color[] px, int width, int height)
        {
            var t = new Texture2D(width, height, TextureFormat.RGB24, false);
            var c8 = new Color32[px.Length];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                c8[i] = linear ? c.gamma : c;
            }
            t.SetPixels32(c8); t.Apply(false);
            var bytes = t.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(t);
            return bytes;
        }

        // ====================================================================== captures =====
        [UnityTest, Timeout(600000)]                        // grows with the level count
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

            // The level picker.
            var levelsBtn = FindButton("LevelsButton");
            Assert.IsNotNull(levelsBtn, "Main menu has no LevelsButton");
            levelsBtn.onClick.Invoke();
            yield return Wait(1.0f);
            yield return Grab(Path.Combine(dir, "ui_02_level_select.png"));
            var closeLevels = FindButton("CloseLevelsButton");
            Assert.IsNotNull(closeLevels, "Level select has no CloseLevelsButton");
            closeLevels.onClick.Invoke();
            yield return Wait(0.5f);

            // Settings from the menu, with one switch turned off so both track states are on
            // show. The player's real setting is put back afterwards.
            var settingsBtn = FindButton("SettingsButton");
            Assert.IsNotNull(settingsBtn, "Main menu has no SettingsButton");
            settingsBtn.onClick.Invoke();
            yield return Wait(0.8f);
            var music = FindToggle("MusicToggle");
            Assert.IsNotNull(music, "Settings has no MusicToggle");
            bool musicWasOn = music.isOn;
            music.isOn = false;
            yield return Wait(0.8f);
            yield return Grab(Path.Combine(dir, "ui_03_settings.png"));
            music.isOn = musicWasOn;

            var closeBtn = FindButton("CloseButton");
            Assert.IsNotNull(closeBtn, "Settings panel has no CloseButton");
            closeBtn.onClick.Invoke();
            yield return Wait(0.5f);

            // The HUD over level 1, with its tilt cue showing. The cue fades on the return leg of
            // each sweep, so wait for a moment when it is fully up rather than a fixed time.
            yield return LoadLevel(0);
            yield return WaitForHint();
            yield return Grab(Path.Combine(dir, "ui_04_hud.png"));

            // Settings over a paused level. The clock is stopped, so wait on real time.
            var pauseBtn = FindButton("PauseButton");
            Assert.IsNotNull(pauseBtn, "HUD has no PauseButton");
            pauseBtn.onClick.Invoke();
            yield return WaitRealtime(1.0f);
            yield return Grab(Path.Combine(dir, "ui_05_settings_paused.png"));
            closeBtn.onClick.Invoke();
            yield return Wait(0.5f);

            // Win the level outright rather than trying to solve it - this is a UI capture, and
            // making it depend on a puzzle being solvable would make it fail for the wrong reason.
            levels.ReportWin();
            yield return Wait(1.5f);
            yield return Grab(Path.Combine(dir, "ui_06_level_complete.png"));

            Debug.Log("PTW_UI_CAPTURES_WRITTEN: " + dir);
        }

        IEnumerator WaitRealtime(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// <summary>Wait until the onboarding tilt cue is fully visible, or give up after a few seconds.</summary>
        IEnumerator WaitForHint()
        {
            CanvasGroup hint = null;
            foreach (var g in UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (g.name == "RotateHint") { hint = g; break; }
            float t = 0f;
            while (t < 5f && (hint == null || hint.alpha < 0.97f))
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// The menu, settings and HUD at other aspect ratios: a tall 19.5:9 phone and a 4:3
        /// tablet. The canvas lays itself out for the render target it is drawn into, so one
        /// run covers every size (batch mode's own Screen is a fixed 640x480 and is not used).
        /// Half resolution: these are layout checks, and a full-size 4:3 float readback at the
        /// end of the whole suite stalled the batch process for minutes on a low-memory machine.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureUiAtScreenSize()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(dir);
            var sizes = new[] { (540, 1170), (768, 1024) };

            yield return Wait(0.9f);
            foreach (var (w, h) in sizes)
                yield return Grab(Path.Combine(dir, $"aspect_{w}x{h}_menu.png"), w, h);

            FindButton("SettingsButton").onClick.Invoke();
            yield return Wait(1.0f);
            foreach (var (w, h) in sizes)
                yield return Grab(Path.Combine(dir, $"aspect_{w}x{h}_settings.png"), w, h);
            FindButton("CloseButton").onClick.Invoke();
            yield return Wait(0.5f);

            yield return LoadLevel(0);
            yield return WaitForHint();
            foreach (var (w, h) in sizes)
                yield return Grab(Path.Combine(dir, $"aspect_{w}x{h}_hud.png"), w, h);

            Debug.Log("PTW_ASPECT_CAPTURES_WRITTEN");
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

        static UnityEngine.UI.Toggle FindToggle(string name)
        {
            var all = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Toggle>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in all)
                if (t.name == name) return t;
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
        /// <summary>
        /// Overwrite a capture, retrying briefly if another process (an IDE indexing the folder, a
        /// previewer) has it memory-mapped - Win32 error 1224. A locked screenshot must never fail
        /// the suite; if it stays locked the shot goes to a sidecar name instead.
        /// </summary>
        static void WritePng(string path, byte[] bytes)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try { File.WriteAllBytes(path, bytes); return; }
                catch (IOException) { System.Threading.Thread.Sleep(150); }
            }
            string alt = Path.Combine(Path.GetDirectoryName(path) ?? "",
                                      Path.GetFileNameWithoutExtension(path) + ".new.png");
            File.WriteAllBytes(alt, bytes);
            Debug.LogWarning($"PTW_CAPTURE_LOCKED {Path.GetFileName(path)} -> wrote {Path.GetFileName(alt)}");
        }

        IEnumerator Grab(string path) => Grab(path, ShotWidth, ShotHeight);

        IEnumerator Grab(string path, int width, int height)
        {
            yield return null;   // let one full frame of animation/VFX advance

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 1
            };
            // Read back as float and convert to sRGB by hand. The project renders in LINEAR colour
            // space and an HDR RenderTexture holds linear values; encoding those bytes straight to
            // PNG produced captures about 40% darker and muddier than the game looks on a phone,
            // and two rounds of art direction chased that artefact (paler sky, lighter mountains)
            // before it was caught. This is the one place the whole visual loop depends on.
            var tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
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
                tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                tex.Apply(false);

                WritePng(path, ToPng(tex.GetPixels(), width, height));
                Debug.Log($"PTW_CAPTURE {Path.GetFileName(path)} {width}x{height}");
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
