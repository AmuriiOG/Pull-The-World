using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The soundtrack: one short, tuneful music-box loop per chapter, synthesised in the
    /// background on first use, crossfaded on chapter change, gated by the Music setting.
    ///
    /// Everything is AUTHORED: a written eight-bar melody over a I-V-vi-IV progression, a harp
    /// arpeggio under it, a plucked bass on the strong beats and a very quiet pad. Nothing is
    /// random and nothing is noise-based. The earlier version - a slow drone pad with random
    /// sparkles, a wind-noise bed underneath and a glass tick on every 18 degrees of rotation -
    /// was rejected in three rounds ("I don't like it", "sounds like water", "a weird sound when
    /// I tilt"). A music box is the instrument the theme's glass-bell sound effects already
    /// imply, and a melody the player can hum is what "music that matches the theme" means for
    /// a pastel hyper-casual game.
    ///
    /// Synthesis rules, because every one of these was a bug once:
    ///  * every partial is sin(2*pi*f*t) with a CONSTANT f - never multiply a varying frequency
    ///    by t (that was the out-of-tune vibrato);
    ///  * notes that ring past the end of the loop wrap round into its start, so the seam is
    ///    genuinely seamless rather than faded;
    ///  * no noise, no random hits, no per-sample modulation.
    ///
    /// Chapters are built a few thousand samples per frame so nothing hitches, chapter one first;
    /// the menu is silent for about a second and the music then fades in. Slots for authored
    /// loops exist (<see cref="overrideLoops"/>): drop a clip in per chapter and it replaces the
    /// synthesised one. "Pull The World/Render Music To WAV" writes the loops to Captures/ so they
    /// can be auditioned outside the game.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public class PtwMusic : MonoBehaviour
    {
        public static PtwMusic Instance { get; private set; }

        [SerializeField, Range(0f, 1f)] float volume = 0.30f;
        [Tooltip("Multiplier while the game is paused (settings open).")]
        [SerializeField, Range(0f, 1f)] float duckVolume = 0.35f;
        [Tooltip("Seconds for a full fade, both the on/off fade and the chapter crossfade.")]
        [SerializeField] float fadeSeconds = 1.6f;
        [Tooltip("Optional authored loops, one per chapter. Empty slots use the synthesised loop.")]
        [SerializeField] AudioClip[] overrideLoops = new AudioClip[0];

        AudioSource a, b, live;                  // two sources so chapters crossfade
        readonly Dictionary<int, AudioClip> loops = new Dictionary<int, AudioClip>();
        LevelManager hooked;
        SkyTheme sky;
        int chapter = -1;
        static bool ducked;

        /// <summary>True when the setting is on and a chapter has been chosen.</summary>
        public bool WantsToPlay => GameProgress.MusicOn && chapter >= 0;
        public float LiveVolume => live ? live.volume : 0f;
        public int Chapter => chapter;
        public static int ChapterCount => songs.Length;
        public static int SampleRate => Rate;
        public static string SongName(int c) => songs[Mathf.Clamp(c, 0, songs.Length - 1)].name;

        /// <summary>Pause ducking. Static so UI code need not care whether music exists.</summary>
        public static void SetDucked(bool on) => ducked = on;

        void Awake()
        {
            Instance = this;
            a = MakeSource();
            b = MakeSource();
            live = a;
        }

        AudioSource MakeSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.reverbZoneMix = 0f;
            s.volume = 0f;
            s.priority = 0;                      // never stolen by a burst of impacts
            return s;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unhook();
        }

        void Start()
        {
            sky = FindFirstObjectByType<SkyTheme>();
            PlayChapter(0);                      // the menu sits in chapter one's mood
            StartCoroutine(Pregenerate());
        }

        void Update()
        {
            Hook();
            if (chapter >= 0) TryStart();

            float target = WantsToPlay ? volume * (ducked ? duckVolume : 1f) : 0f;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds);   // keeps fading while paused
            Fade(live, target, step);
            var other = live == a ? b : a;
            Fade(other, 0f, step);
            if (other.clip && other.volume <= 0f) { other.Stop(); other.clip = null; }
        }

        static void Fade(AudioSource s, float target, float step)
        {
            if (!s.clip) return;
            s.volume = Mathf.MoveTowards(s.volume, target, step);
        }

        // ------------------------------------------------------------------- chapters -------
        void Hook()
        {
            if (hooked) return;
            var lm = LevelManager.Instance;
            if (!lm) return;
            hooked = lm;
            hooked.OnLevelLoaded += HandleLevelLoaded;
            if (lm.Current) HandleLevelLoaded(lm.Current);
        }

        void Unhook()
        {
            if (hooked) hooked.OnLevelLoaded -= HandleLevelLoaded;
            hooked = null;
        }

        void HandleLevelLoaded(LevelDefinition def)
        {
            if (!hooked) return;
            int count = Mathf.Max(1, hooked.LevelCount);
            int c = sky ? sky.ChapterFor(hooked.CurrentIndex, count)
                        : Mathf.Clamp(hooked.CurrentIndex * songs.Length / count, 0, songs.Length - 1);
            PlayChapter(c);
        }

        void PlayChapter(int c)
        {
            if (c == chapter) return;
            chapter = c;
            TryStart();
        }

        /// <summary>Puts the chapter's loop on the idle source once it exists; a no-op until then.</summary>
        void TryStart()
        {
            var clip = Loop(chapter);
            if (!clip || live.clip == clip) return;
            var next = live == a ? b : a;
            next.clip = clip;
            next.volume = 0f;
            next.Play();
            live = next;
        }

        AudioClip Loop(int c)
        {
            if (c < overrideLoops.Length && overrideLoops[c]) return overrideLoops[c];
            return loops.TryGetValue(c, out var clip) ? clip : null;
        }

        IEnumerator Pregenerate()
        {
            // Chapter one first - it is what the menu plays - then the rest, all in the background.
            for (int c = 0; c < songs.Length; c++)
            {
                if (loops.ContainsKey(c)) continue;
                float[] data = null;
                yield return Synthesise(c, 24576, d => data = d);
                var clip = AudioClip.Create($"ptw_music_{c}", data.Length, 1, Rate, false);
                clip.SetData(data, 0);
                loops[c] = clip;
            }
        }

        /// <summary>The whole loop, rendered synchronously. For the WAV export and for tests.</summary>
        public static float[] Render(int chapterIndex)
        {
            float[] data = null;
            var it = Synthesise(chapterIndex, int.MaxValue, d => data = d);
            while (it.MoveNext()) { }
            return data;
        }

        // ------------------------------------------------------------------ the songs -------
        const int Rate = 44100;

        struct Song
        {
            public string name;
            public float bpm;
            public int[][] chords;                        // one MIDI triad per bar, low to high
            public (float beat, int midi, float len)[] melody;   // beat from the loop start
        }

        // Three chapters, one tune each, same family: I-V-vi-IV over eight bars, a music-box
        // melody that rises through the first half and walks back down to the tonic at the end so
        // the loop closes on itself. Dawn is C, Morning D (a shade brighter), Golden Hour F (a
        // shade warmer). MIDI: C4 = 60, C5 = 72.
        static readonly Song[] songs =
        {
            new Song
            {
                name = "Dawn", bpm = 92f,
                chords = new[]
                {
                    new[] { 60, 64, 67 }, new[] { 55, 59, 62 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 },
                    new[] { 60, 64, 67 }, new[] { 55, 59, 62 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 },
                },
                melody = new (float, int, float)[]
                {
                    ( 0f, 76, 1f), ( 1f, 79, 1f), ( 2f, 84, 1.5f), ( 3.5f, 79, 0.5f),
                    ( 4f, 83, 1f), ( 5f, 81, 0.5f), ( 5.5f, 79, 0.5f), ( 6f, 74, 2f),
                    ( 8f, 72, 1f), ( 9f, 76, 1f), (10f, 81, 1.5f), (11.5f, 76, 0.5f),
                    (12f, 77, 1f), (13f, 81, 1f), (14f, 79, 0.5f), (14.5f, 77, 0.5f), (15f, 76, 1f),
                    (16f, 76, 1f), (17f, 79, 1f), (18f, 84, 1f), (19f, 86, 1f),
                    (20f, 83, 1f), (21f, 86, 1f), (22f, 83, 1f), (23f, 79, 1f),
                    (24f, 81, 1f), (25f, 84, 1f), (26f, 76, 1f), (27f, 79, 1f),
                    (28f, 77, 1f), (29f, 76, 1f), (30f, 74, 1f), (31f, 72, 1f),
                },
            },
            new Song
            {
                name = "Morning", bpm = 96f,
                chords = new[]
                {
                    new[] { 62, 66, 69 }, new[] { 57, 61, 64 }, new[] { 59, 62, 66 }, new[] { 55, 59, 62 },
                    new[] { 62, 66, 69 }, new[] { 57, 61, 64 }, new[] { 59, 62, 66 }, new[] { 55, 59, 62 },
                },
                melody = new (float, int, float)[]
                {
                    ( 0f, 78, 1f), ( 1f, 81, 1f), ( 2f, 86, 1.5f), ( 3.5f, 81, 0.5f),
                    ( 4f, 85, 0.5f), ( 4.5f, 83, 0.5f), ( 5f, 81, 1f), ( 6f, 76, 2f),
                    ( 8f, 74, 1f), ( 9f, 78, 1f), (10f, 83, 1.5f), (11.5f, 78, 0.5f),
                    (12f, 79, 1f), (13f, 83, 1f), (14f, 81, 0.5f), (14.5f, 79, 0.5f), (15f, 78, 1f),
                    (16f, 78, 0.5f), (16.5f, 81, 0.5f), (17f, 86, 1f), (18f, 88, 1f), (19f, 86, 1f),
                    (20f, 85, 1f), (21f, 88, 1f), (22f, 85, 1f), (23f, 81, 1f),
                    (24f, 83, 1f), (25f, 86, 1f), (26f, 78, 1f), (27f, 81, 1f),
                    (28f, 79, 1f), (29f, 78, 1f), (30f, 76, 1f), (31f, 74, 1f),
                },
            },
            new Song
            {
                name = "GoldenHour", bpm = 84f,
                chords = new[]
                {
                    new[] { 65, 69, 72 }, new[] { 60, 64, 67 }, new[] { 62, 65, 69 }, new[] { 58, 62, 65 },
                    new[] { 65, 69, 72 }, new[] { 60, 64, 67 }, new[] { 62, 65, 69 }, new[] { 58, 62, 65 },
                },
                melody = new (float, int, float)[]
                {
                    ( 0f, 81, 1f), ( 1f, 84, 1f), ( 2f, 86, 1.5f), ( 3.5f, 84, 0.5f),
                    ( 4f, 88, 0.5f), ( 4.5f, 86, 0.5f), ( 5f, 84, 1f), ( 6f, 79, 2f),
                    ( 8f, 77, 1f), ( 9f, 81, 1f), (10f, 86, 1.5f), (11.5f, 81, 0.5f),
                    (12f, 82, 1f), (13f, 86, 1f), (14f, 84, 0.5f), (14.5f, 82, 0.5f), (15f, 81, 1f),
                    (16f, 81, 1f), (17f, 84, 1f), (18f, 89, 1f), (19f, 86, 1f),
                    (20f, 88, 1f), (21f, 84, 1f), (22f, 79, 1f), (23f, 84, 1f),
                    (24f, 86, 1f), (25f, 84, 1f), (26f, 81, 1f), (27f, 77, 1f),
                    (28f, 82, 1f), (29f, 81, 1f), (30f, 79, 1f), (31f, 77, 1f),
                },
            },
        };

        enum Timbre { MusicBox, Harp, Bass }

        struct Note
        {
            public float start, hz, gain, decay, hold;
            public Timbre timbre;
        }

        static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        /// <summary>
        /// Renders one chapter's loop. Yields after roughly <paramref name="samplesPerStep"/>
        /// samples of work so it can run as a coroutine; pass int.MaxValue to run straight through.
        /// </summary>
        static IEnumerator Synthesise(int chapterIndex, int samplesPerStep, Action<float[]> done)
        {
            var song = songs[Mathf.Clamp(chapterIndex, 0, songs.Length - 1)];
            float beat = 60f / song.bpm;
            int bars = song.chords.Length;
            float barLen = 4f * beat;
            int n = Mathf.RoundToInt(bars * barLen * Rate);
            var data = new float[n];
            bool stepping = samplesPerStep < int.MaxValue;

            // ---- score ----
            var notes = new List<Note>();
            foreach (var (b, midi, _) in song.melody)
                notes.Add(new Note { start = b * beat, hz = Hz(midi), gain = 0.26f, decay = 2.4f, hold = 2.2f, timbre = Timbre.MusicBox });

            for (int bar = 0; bar < bars; bar++)
            {
                var ch = song.chords[bar];
                // Harp: root, fifth, third, fifth - the classic music-box left hand, in eighths.
                int[] pattern = { ch[0], ch[2], ch[1], ch[2], ch[0], ch[2], ch[1], ch[2] };
                for (int e = 0; e < 8; e++)
                    notes.Add(new Note
                    {
                        start = bar * barLen + e * 0.5f * beat, hz = Hz(pattern[e]),
                        gain = e % 4 == 0 ? 0.10f : 0.07f, decay = 4.5f, hold = 1.3f, timbre = Timbre.Harp,
                    });
                // Bass: the root an octave down, on one and three.
                for (int k = 0; k < 2; k++)
                    notes.Add(new Note
                    {
                        start = bar * barLen + k * 2f * beat, hz = Hz(ch[0] - 12),
                        gain = 0.22f, decay = 1.6f, hold = 2f * beat, timbre = Timbre.Bass,
                    });
            }

            // ---- struck notes, wrapping round the loop end ----
            int budget = samplesPerStep;
            foreach (var note in notes)
            {
                int s0 = Mathf.RoundToInt(note.start * Rate);
                int count = Mathf.CeilToInt(note.hold * Rate);
                float w = 2f * Mathf.PI * note.hz;
                for (int j = 0; j < count; j++)
                {
                    float tau = j / (float)Rate;
                    float env = Mathf.Exp(-note.decay * tau) * (1f - Mathf.Exp(-tau * 900f))
                              * Mathf.Clamp01((note.hold - tau) / 0.25f);
                    float ph = w * tau;
                    float v;
                    switch (note.timbre)
                    {
                        case Timbre.MusicBox:
                            // A struck steel tooth: fundamental plus two inharmonic partials that
                            // die faster than it does. That is what makes it a music box.
                            v = Mathf.Sin(ph)
                              + 0.18f * Mathf.Sin(2.756f * ph) * Mathf.Exp(-3f * tau)
                              + 0.06f * Mathf.Sin(5.404f * ph) * Mathf.Exp(-6f * tau);
                            break;
                        case Timbre.Harp:
                            v = Mathf.Sin(ph) + 0.30f * Mathf.Sin(2f * ph) * Mathf.Exp(-2f * tau) + 0.08f * Mathf.Sin(3f * ph);
                            break;
                        default:
                            v = Mathf.Sin(ph) + 0.20f * Mathf.Sin(2f * ph);
                            break;
                    }
                    data[(s0 + j) % n] += v * env * note.gain;
                }
                if (stepping && (budget -= count) <= 0) { budget = samplesPerStep; yield return null; }
            }

            // ---- pad: the triad, very quiet, swelling in and out within each bar ----
            var padHz = new float[bars][];
            for (int bar = 0; bar < bars; bar++)
            {
                padHz[bar] = new float[3];
                for (int k = 0; k < 3; k++) padHz[bar][k] = Hz(song.chords[bar][k]);
            }
            for (int i0 = 0; i0 < n; i0 += stepping ? samplesPerStep : n)
            {
                int end = Mathf.Min(n, i0 + (stepping ? samplesPerStep : n));
                for (int i = i0; i < end; i++)
                {
                    float t = i / (float)Rate;
                    int bar = Mathf.Min(bars - 1, (int)(t / barLen));
                    float lt = t - bar * barLen;
                    float env = Mathf.SmoothStep(0f, 1f, lt / 0.35f) * Mathf.SmoothStep(0f, 1f, (barLen - lt) / 0.45f);
                    float pad = 0f;
                    var hz = padHz[bar];
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = 2f * Mathf.PI * hz[k] * t;
                        pad += Mathf.Sin(ph) + 0.12f * Mathf.Sin(2f * ph) + 0.5f * Mathf.Sin(ph * 1.003f);
                    }
                    data[i] += pad * env * 0.035f;
                }
                if (stepping && end < n) yield return null;
            }

            // ---- normalise ----
            float peak = 0.001f;
            for (int k = 0; k < n; k++) peak = Mathf.Max(peak, Mathf.Abs(data[k]));
            float norm = 0.8f / peak;
            for (int k = 0; k < n; k++) data[k] *= norm;

            done(data);
        }
    }
}
