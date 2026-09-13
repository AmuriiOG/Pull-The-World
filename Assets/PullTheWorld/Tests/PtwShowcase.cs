using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PullTheWorld.Tests
{
    /// <summary>
    /// The showcase renderer: drives the real game through a shot list and writes what a trailer
    /// editor needs - a JPEG frame sequence at a fixed 30 fps plus the game's own sound effects
    /// as a WAV, per shot - and the high-resolution stills for the deck, the store and the hero
    /// images. Everything goes to PullTheWorld_Showcase/RawCaptures next to Assets; the ffmpeg
    /// assembly and the deck are built from there outside Unity.
    ///
    /// Rendering is deterministic: Time.captureFramerate pins the clock to 1/30 s per frame
    /// whatever the machine is doing, the camera is rendered by hand into a RenderTexture of the
    /// exact output size (so the phone framing is what gets captured, not the Editor window), and
    /// AudioRenderer takes the mix for exactly that frame's worth of samples. The music is muted
    /// while rendering so the bed can be laid under the cut as one continuous piece; the SFX are
    /// captured. The UI canvas is hidden for the clean cinematic shots and shown for the two
    /// stills that need it.
    /// </summary>
    public class PtwShowcase
    {
        WorldRotator rotator;
        LevelManager levels;
        PlayerBody player;
        Camera cam;
        PlaneCameraRig rig;
        GameObject ui;

        const int Fps = 30;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PullTheWorld_Showcase"));
        static string Raw => Path.Combine(Root, "RawCaptures");

        bool musicWas;
        int progressWas;

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
            rig = cam ? cam.GetComponent<PlaneCameraRig>() : null;
            ui = GameObject.Find("UI");
            Assert.IsNotNull(rotator); Assert.IsNotNull(levels); Assert.IsNotNull(player); Assert.IsNotNull(rig);

            progressWas = GameProgress.UnlockedIndex;
            musicWas = GameProgress.MusicOn;
            GameProgress.MusicOn = false;         // the bed is laid in the edit, continuous
            GameProgress.SoundOn = true;
            SetUi(false);
            // The real mouse and keyboard must not reach the rotator while a scripted drag runs:
            // a stray click over the Game view ends the drive and the scripted pull stalls (it did,
            // once, for a second and a half in the middle of a shot).
            var input = UnityEngine.Object.FindFirstObjectByType<RotateInput>();
            if (input) input.InputEnabled = false;
            Time.timeScale = 1f;
            Time.captureFramerate = Fps;
            // A render is not a correctness test: a stray logged error must not abort the shot list.
            LogAssert.ignoreFailingMessages = true;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureFramerate = 0;
            StopAudio();
            GameProgress.MusicOn = musicWas;
            GameProgress.UnlockedIndex = progressWas;
            yield return null;
        }

        void SetUi(bool on)
        {
            if (ui) ui.SetActive(on);
        }

        // ======================================================================= helpers =====
        IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator LoadLevel(int index, float settle = 1.0f)
        {
            levels.LoadLevel(index);
            yield return null;
            yield return null;
            yield return Wait(settle);
        }

        /// <summary>
        /// Turn the level the way a finger does: a stream of small steps at a plausible speed.
        /// Insists: rotation is switched on first, and if a step does not move the target (the
        /// drive was ended under us) the drive is begun again rather than the loop spinning.
        /// </summary>
        IEnumerator DragTo(float angle, float degreesPerSecond = 110f, float timeout = 5f)
        {
            rotator.RotationAllowed = true;
            rotator.BeginDrive();
            float t = 0f;
            while (Mathf.Abs(rotator.AngleTarget - angle) > 0.01f && t < timeout)
            {
                float maxStep = degreesPerSecond * Time.deltaTime;
                float before = rotator.AngleTarget;
                rotator.Drive(Mathf.Clamp(angle - rotator.AngleTarget, -maxStep, maxStep));
                if (Mathf.Approximately(before, rotator.AngleTarget)) { rotator.RotationAllowed = true; rotator.BeginDrive(); }
                t += Time.deltaTime;
                yield return null;
            }
            rotator.EndDrive(0f);
        }

        /// <summary>A sequence of drags with holds between them, run beside the capture loop.</summary>
        IEnumerator Choreo(params (float angle, float speed, float holdAfter)[] moves)
        {
            foreach (var (angle, speed, hold) in moves)
            {
                yield return DragTo(angle, speed);
                yield return Wait(hold);
            }
        }

        // ---------------------------------------------------------------- frame capture ----
        RenderTexture rt;
        Texture2D readback;
        Color32[] pixels;

        void EnsureTargets(int w, int h)
        {
            if (rt && (rt.width != w || rt.height != h)) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); rt = null; UnityEngine.Object.DestroyImmediate(readback); readback = null; }
            if (!rt)
            {
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 1 };
                readback = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                pixels = new Color32[w * h];
            }
        }

        /// <summary>Render the camera at exactly w x h and write a PNG or JPEG, sRGB-encoded like a phone screen.</summary>
        void Grab(string path, int w, int h, bool jpg)
        {
            EnsureTargets(w, h);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                rig.Apply();                            // re-frame for the capture aspect
                cam.Render();
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                readback.Apply(false);

                var px = readback.GetPixelData<Color>(0);
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    c = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                    pixels[i] = linear ? c.gamma : c;
                }
                var out8 = new Texture2D(w, h, TextureFormat.RGB24, false);
                out8.SetPixels32(pixels);
                out8.Apply(false);
                File.WriteAllBytes(path, jpg ? out8.EncodeToJPG(94) : out8.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(out8);
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                rig.Apply();
            }
        }

        // ---------------------------------------------------------------- audio capture ----
        List<float> audio;
        int channels;
        bool audioOn;

        static int ChannelCount(AudioSpeakerMode mode) => mode switch
        {
            AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
            AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, _ => 2,
        };

        void StartAudio()
        {
            if (audioOn) return;
            channels = ChannelCount(AudioSettings.GetConfiguration().speakerMode);
            audio = new List<float>(AudioSettings.outputSampleRate * channels * 20);
            AudioRenderer.Start();
            audioOn = true;
        }

        void PumpAudio()
        {
            if (!audioOn) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n <= 0) return;
            var buf = new NativeArray<float>(n * channels, Allocator.Temp);
            AudioRenderer.Render(buf);
            for (int i = 0; i < buf.Length; i++) audio.Add(buf[i]);
            buf.Dispose();
        }

        void StopAudio()
        {
            if (!audioOn) return;
            AudioRenderer.Stop();
            audioOn = false;
        }

        void WriteWav(string path)
        {
            int rate = AudioSettings.outputSampleRate;
            var data = audio ?? new List<float>();
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int bytes = data.Count * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * 2 * channels); w.Write((short)(2 * channels)); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var s in data) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }

        // ---------------------------------------------------------------------- a shot ------
        const int ClipW = 1080, ClipH = 1920;

        class Shot
        {
            public string name, dir;
            public int frames;
        }

        Shot BeginShot(string name)
        {
            var s = new Shot { name = name, dir = Path.Combine(Raw, "clips", name) };
            if (Directory.Exists(s.dir)) Directory.Delete(s.dir, true);
            Directory.CreateDirectory(s.dir);
            audio = new List<float>();
            StartAudio();
            return s;
        }

        /// <summary>Capture frames for <paramref name="seconds"/>, or until <paramref name="until"/> says stop (checked after a minimum).</summary>
        IEnumerator Roll(Shot s, float seconds, Func<bool> until = null, float minSeconds = 0f)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                PumpAudio();
                Grab(Path.Combine(s.dir, $"f_{s.frames:D4}.jpg"), ClipW, ClipH, jpg: true);
                s.frames++;
                t += Time.deltaTime;
                if (until != null && t >= minSeconds && until()) break;
            }
        }

        void EndShot(Shot s)
        {
            StopAudio();
            WriteWav(Path.Combine(s.dir, "sfx.wav"));
            File.WriteAllText(Path.Combine(s.dir, "shot.json"),
                $"{{ \"name\": \"{s.name}\", \"frames\": {s.frames}, \"fps\": {Fps}, \"seconds\": {s.frames / (float)Fps:F3} }}");
            Debug.Log($"PTW_SHOT {s.name} frames={s.frames} seconds={s.frames / (float)Fps:F2}");
        }

        Vector2 Ext(float e) => new Vector2(e, e);

        // ================================================================== the trailer ======
        /// <summary>
        /// The trailer's shots, in cut order, each its own frame sequence and SFX track under
        /// RawCaptures/clips. The edit (transitions, logo, text, music bed) is done in ffmpeg from
        /// these; see the assembly script in the showcase folder.
        /// </summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator RenderClips()
        {
            Shot s;

            // 01 HOOK - tight on the first island, a pull, the orb rolls for the door.
            yield return LoadLevel(0, 1.2f);
            rig.SnapTo(levels.Pivot, Ext(9.2f));
            s = BeginShot("01_hook");
            yield return Roll(s, 0.5f);
            levels.StartCoroutine(Choreo((-24f, 70f, 0f)));
            yield return Roll(s, 3.2f);
            EndShot(s);

            // 02 TITLE - the world, wide and still, for the logo.
            yield return LoadLevel(0, 1.4f);
            rig.SnapTo(levels.Pivot, Ext(20f));
            s = BeginShot("02_title");
            rig.TravelTo(levels.Pivot, Ext(18.5f), 3.6f);           // a breath of push-in, in place
            yield return Roll(s, 3.6f);
            EndShot(s);

            // 03 CINEMATIC - the dolly in from the wide world to the playing frame.
            rig.SnapTo(levels.Pivot, Ext(18.5f));
            s = BeginShot("03_cinematic");
            rig.TravelTo(levels.Pivot, levels.Current.viewExtents, 4.2f);
            yield return Roll(s, 4.4f);
            EndShot(s);

            // 04 MECHANIC + PORTAL + PUSH - one continuous take: the pull, the roll, the door, the
            //    journey to the next island, the orb dropping in.
            yield return LoadLevel(0, 1.0f);
            s = BeginShot("04_mechanic_travel");
            yield return Roll(s, 0.7f);
            levels.StartCoroutine(Choreo((-18f, 55f, 0f)));
            yield return Roll(s, 14f, () => levels.CurrentIndex == 1 && levels.IsPlaying, 3f);
            yield return Roll(s, 1.8f);
            EndShot(s);

            // 05-13 MONTAGE - one situation each, ~3.6 s, all with the finger's speed of pull.
            yield return Montage("05_spikes", 2, 0.4f, (12f, 60f, 0.6f), (-34f, 90f, 0f));
            yield return Montage("06_gem", 6, 0.4f, (28f, 80f, 1.1f), (-36f, 110f, 0f));
            yield return Montage("07_water", 18, 0.4f, (-24f, 70f, 0f));
            yield return Montage("08_rock", 21, 0.3f, (-36f, 90f, 0f));
            yield return Montage("09_spring", 23, 0.3f, (-32f, 90f, 0f));
            yield return Montage("10_enemies", 28, 0.3f, (-32f, 90f, 0f));
            yield return Montage("11_golden", 34, 1.6f, (-26f, 70f, 0f));
            yield return Montage("12_dropin", 40, 0.4f, (-34f, 90f, 0f));
            yield return Montage("13_finale", 49, 0.4f, (-30f, 80f, 0f));

            // 14 PARALLAX - the push between two golden-hour islands, the sky sliding past in depth.
            yield return LoadLevel(34, 2.2f);
            s = BeginShot("14_parallax_travel");
            yield return Roll(s, 0.4f);
            levels.ReportWin();
            yield return Roll(s, 9f, () => levels.CurrentIndex == 35 && levels.IsPlaying, 2f);
            yield return Roll(s, 1.2f);
            EndShot(s);

            // 15 END CARD - the first world again, wide, a slow drift in behind the title.
            yield return LoadLevel(0, 1.4f);
            rig.SnapTo(levels.Pivot, Ext(21f));
            s = BeginShot("15_endcard");
            rig.TravelTo(levels.Pivot, Ext(19f), 5f);
            yield return Roll(s, 5f);
            EndShot(s);
        }

        /// <summary>Just the montage shots (05-13), for re-rendering them without the rest.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator RenderMontage()
        {
            yield return Montage("05_spikes", 2, 0.4f, (12f, 60f, 0.6f), (-34f, 90f, 0f));
            yield return Montage("06_gem", 6, 0.4f, (28f, 80f, 1.1f), (-36f, 110f, 0f));
            yield return Montage("07_water", 18, 0.4f, (-24f, 70f, 0f));
            yield return Montage("08_rock", 21, 0.3f, (-36f, 90f, 0f));
            yield return Montage("09_spring", 23, 0.3f, (-32f, 90f, 0f));
            yield return Montage("10_enemies", 28, 0.3f, (-32f, 90f, 0f));
            yield return Montage("11_golden", 34, 1.6f, (-26f, 70f, 0f));
            yield return Montage("12_dropin", 40, 0.4f, (-34f, 90f, 0f));
            yield return Montage("13_finale", 49, 0.4f, (-30f, 80f, 0f));
        }

        IEnumerator Montage(string name, int level, float lead, params (float angle, float speed, float holdAfter)[] moves)
        {
            yield return LoadLevel(level, 1.0f);
            var s = BeginShot(name);
            yield return Roll(s, lead);
            levels.StartCoroutine(Choreo(moves));
            yield return Roll(s, 3.6f - lead);
            EndShot(s);
        }

        // ==================================================================== the stills ======
        const int StillW = 1440, StillH = 2560;
        const int WideW = 3840, WideH = 2160;

        void Still(string name, int w = StillW, int h = StillH)
        {
            string dir = Path.Combine(Raw, "stills");
            Directory.CreateDirectory(dir);
            Grab(Path.Combine(dir, name + ".png"), w, h, jpg: false);
            Debug.Log($"PTW_STILL {name} {w}x{h}");
        }

        /// <summary>
        /// The marketing stills and hero bases: portrait at 1440 x 2560, the heroes also at
        /// 3840 x 2160 (the camera frames by height in landscape and the painted sky spreads to fill
        /// the width, which is what makes the cover work). The three UI shots reload the scene so
        /// the menu comes up the way a player sees it.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator RenderStills()
        {
            // Level one: clean, tight and tilted, wide.
            yield return LoadLevel(0, 1.4f);
            Still("l01_clean");
            Still("hero_l01_wide_landscape", WideW, WideH);
            rig.SnapTo(levels.Pivot, Ext(16.5f));
            Still("hero_l01_wider_landscape", WideW, WideH);      // room under the island for the title
            rig.SnapTo(levels.Pivot, Ext(19f));
            Still("l01_wide");
            rig.SnapTo(levels.Pivot, Ext(9.2f));
            yield return DragTo(-22f, 70f);
            yield return Wait(0.55f);
            Still("l01_tilted_close");
            Still("hero_l01_tilted_landscape", WideW, WideH);

            // The push, mid-way.
            yield return LoadLevel(0, 1.0f);
            yield return DragTo(-18f, 55f);
            float guard = 0f;
            while (!levels.IsTravelling && guard < 8f) { guard += Time.deltaTime; yield return null; }
            yield return Wait(1.05f);
            Still("l01_to_l02_push");

            // Situations.
            yield return LoadLevel(2, 1.0f);
            yield return DragTo(12f, 60f);
            yield return Wait(0.5f);
            Still("l03_spikes");

            yield return LoadLevel(6, 1.0f);
            yield return DragTo(28f, 80f);
            yield return Wait(0.6f);
            Still("l07_gem");

            yield return LoadLevel(18, 1.0f);
            yield return DragTo(-24f, 70f);
            yield return Wait(1.5f);
            Still("l19_water");

            yield return LoadLevel(21, 1.0f);
            yield return DragTo(-36f, 90f);
            yield return Wait(1.2f);
            Still("l22_rock");

            yield return LoadLevel(23, 1.0f);
            yield return DragTo(-32f, 90f);
            yield return Wait(1.3f);
            Still("l24_spring");

            yield return LoadLevel(28, 1.0f);
            yield return DragTo(-32f, 90f);
            yield return Wait(1.4f);
            Still("l29_enemies");

            yield return LoadLevel(34, 2.2f);
            Still("l35_golden");
            Still("hero_l35_golden_landscape", WideW, WideH);
            yield return DragTo(-26f, 70f);
            yield return Wait(1.2f);
            Still("l35_golden_tilted");

            yield return LoadLevel(40, 1.2f);
            Still("l41_dropin");

            yield return LoadLevel(49, 1.2f);
            Still("l50_finale");

            // With the UI: a fresh scene so the menu is exactly what a player sees (at the start of
            // the game, not wherever this machine's save happens to be), then the HUD, then the
            // level-complete card. TearDown puts the real progress back.
            GameProgress.UnlockedIndex = 0;
            Time.captureFramerate = 0;
            SceneManager.LoadScene("Game", LoadSceneMode.Single);
            yield return null; yield return null;
            levels = LevelManager.Instance; player = PlayerBody.Instance; cam = Camera.main; rig = cam.GetComponent<PlaneCameraRig>();
            rotator = WorldRotator.Instance; ui = GameObject.Find("UI");
            Time.captureFramerate = Fps;
            yield return Wait(1.6f);
            Still("ui_menu");

            yield return LoadLevel(0, 1.9f);
            Still("ui_hud");

            yield return DragTo(-18f, 55f);
            guard = 0f;
            while (!(levels.State == LevelState.Won) && guard < 8f) { guard += Time.deltaTime; yield return null; }
            yield return Wait(0.6f);
            Still("ui_level_complete");
        }

        // ======================================================================== probe =======
        /// <summary>
        /// A short sanity pass before the long render: does a right-to-left drag on level one
        /// roll the orb into the door, does AudioRenderer deliver a mix, and what does a native
        /// landscape frame look like. Writes to RawCaptures/probe.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Probe()
        {
            string dir = Path.Combine(Raw, "probe");
            Directory.CreateDirectory(dir);

            yield return LoadLevel(0, 1.2f);
            Grab(Path.Combine(dir, "l01_clean_1080.png"), 1080, 1920, false);
            Grab(Path.Combine(dir, "l01_landscape_2560.png"), 2560, 1440, false);
            rig.SnapTo(levels.Pivot, Ext(8.5f));
            Grab(Path.Combine(dir, "l01_tight.png"), 1080, 1920, false);
            rig.SnapTo(levels.Pivot, Ext(19f));
            Grab(Path.Combine(dir, "l01_wide.png"), 1080, 1920, false);
            rig.SnapTo(levels.Pivot, levels.Current.viewExtents);

            var shot = BeginShot("probe_l01");
            var drive = levels.StartCoroutine(Choreo((-30f, 90f, 0f)));
            float t0 = Time.time; bool won = false; float tWin = -1f;
            levels.OnLevelWon += _ => { won = true; tWin = Time.time - t0; };
            yield return Roll(shot, 9f, () => levels.CurrentIndex == 1 && levels.IsPlaying, 2f);
            EndShot(shot);
            Debug.Log($"PTW_PROBE won={won} tWin={tWin:F2} index={levels.CurrentIndex} playing={levels.IsPlaying} frames={shot.frames} audioSamples={audio.Count} rate={AudioSettings.outputSampleRate} ch={channels}");
            float peak = 0f; foreach (var v in audio) peak = Mathf.Max(peak, Mathf.Abs(v));
            Debug.Log($"PTW_PROBE audioPeak={peak:F3}");
        }
    }
}
