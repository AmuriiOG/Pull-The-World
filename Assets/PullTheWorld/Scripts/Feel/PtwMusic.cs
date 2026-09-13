using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The soundtrack: one slow, atmospheric loop per chapter, synthesised in the background on
    /// first use, crossfaded on chapter change, gated by the Music setting.
    ///
    /// Everything is AUTHORED. Sixteen bars at a walking sixty-odd BPM (a minute a loop, so it
    /// does not wear), over lush ninth and suspended chords - Cmaj9, Am9, Fmaj9, Gsus2, Em7, Dm9 -
    /// voiced low and wide. Five layers, back to front: a warm PAD that holds each chord and
    /// crossfades into the next; a SUB BASS root under it; a soft FELT-KEY touch of the chord on
    /// the one and its upper voices on the three; a sparse GLASS-BELL melody of long notes in the
    /// middle register, pentatonic and unhurried; and a very quiet high SPARKLE every other bar.
    /// The struck voices are heard in a small ROOM (a handful of taps, no feedback) so they sit
    /// in a space instead of on top of the mix. Nothing is random and nothing is noise-based.
    ///
    /// History, so it is not repeated: the first version - a drone pad with random sparkles, a
    /// wind-noise bed and a glass tick on rotation - was rejected three times ("sounds like
    /// water", "a weird sound when I tilt"). The second - a bright music-box tune at 92 BPM over
    /// harp eighths - was tuneful but read as "childish / baby-like" against the painted world.
    /// This third one keeps the melody idea but drops the register, the tempo and the plink, and
    /// puts the weight on atmosphere: relaxing, a little magical, not a nursery.
    ///
    /// Synthesis rules, because every one of these was a bug once:
    ///  * every partial is sin(2*pi*f*t) with a CONSTANT f - never multiply a varying frequency
    ///    by t (that was the out-of-tune vibrato); the pad's warmth is a fixed detuned pair;
    ///  * anything that rings past the end of the loop wraps round into its start, so the seam
    ///    is genuinely seamless rather than faded - the pad's last chord IS its first;
    ///  * no noise, no random hits, no per-sample modulation.
    ///
    /// Chapters are built a few thousand samples per frame so nothing hitches, chapter one first;
    /// the menu is silent for a second or two and the music then fades in. Slots for authored
    /// loops exist (<see cref="overrideLoops"/>): drop a clip in per chapter and it replaces the
    /// synthesised one. "Pull The World/Render Music To WAV" writes the loops to Captures/ so they
    /// can be auditioned outside the game.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public class PtwMusic : MonoBehaviour
    {
        public static PtwMusic Instance { get; private set; }

        [SerializeField, Range(0f, 1f)] float volume = 0.28f;
        [Tooltip("Multiplier while the game is paused (settings open).")]
        [SerializeField, Range(0f, 1f)] float duckVolume = 0.35f;
        [Tooltip("Seconds for a full fade, both the on/off fade and the chapter crossfade.")]
        [SerializeField] float fadeSeconds = 2.2f;
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
                yield return Synthesise(c, 16384, d => data = d);
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
        // 22.05 kHz: nothing in this music lives above 2 kHz, and three one-minute loops at
        // 44.1 kHz would be 32 MB of clip on a phone (and twice the synthesis time).
        const int Rate = 22050;

        struct Song
        {
            public string name;
            public float bpm;
            public int transpose;                 // semitones, applied to everything
        }

        // One composition heard in three keys. Dawn is C at a slow walk; Morning sits a tone
        // higher and a shade quicker; Golden Hour a tone lower and slower still, warmer.
        static readonly Song[] songs =
        {
            new Song { name = "Dawn", bpm = 64f, transpose = 0 },
            new Song { name = "Morning", bpm = 67f, transpose = 2 },
            new Song { name = "GoldenHour", bpm = 60f, transpose = -2 },
        };

        // Voicings in C, MIDI, low to high (C3 = 48, C4 = 60). Wide, with the ninth on top or in
        // the middle so nothing sits in a plain triad.
        static readonly int[] Cmaj9 = { 48, 55, 59, 62, 64 };
        static readonly int[] Em7   = { 52, 55, 59, 62, 67 };
        static readonly int[] Fmaj9 = { 53, 57, 60, 64, 67 };
        static readonly int[] Gsus2 = { 55, 59, 62, 64, 69 };
        static readonly int[] Am9   = { 57, 60, 64, 67, 71 };
        static readonly int[] Dm9   = { 50, 53, 57, 60, 64 };

        // Sixteen bars: two bars a chord through the first half, floating; a bar a chord through
        // the second, a gentle lift that walks back home, so the loop closes on the chord it
        // opened with.
        static readonly int[][] progression =
        {
            Cmaj9, Cmaj9, Am9, Am9, Fmaj9, Fmaj9, Gsus2, Gsus2,
            Cmaj9, Em7, Fmaj9, Am9, Dm9, Gsus2, Cmaj9, Cmaj9,
        };

        // The melody: long notes, mostly pentatonic, G4 to G5, with rests. (beat from the loop
        // start, MIDI, length in beats.) It rises through the lift in bar nine and settles back
        // to E4 at the end, under the G4 it starts on.
        static readonly (float beat, int midi, float len)[] melody =
        {
            ( 0f, 67, 3f), ( 3f, 69, 1f),
            ( 4f, 72, 3.5f),
            ( 8f, 71, 2f), (10f, 67, 2f),
            (12f, 69, 4f),
            (16f, 72, 2f), (18f, 74, 2f),
            (20f, 76, 3f), (23f, 74, 1f),
            (24f, 71, 2f), (26f, 69, 2f),
            (28f, 67, 4f),
            (32f, 79, 2f), (34f, 76, 2f),
            (36f, 74, 3f),
            (40f, 72, 2f), (42f, 69, 2f),
            (44f, 71, 4f),
            (48f, 69, 2f), (50f, 65, 2f),
            (52f, 67, 3f), (55f, 69, 1f),
            (56f, 72, 4f),
            (60f, 67, 2f), (62f, 64, 2f),
        };

        // A very quiet high glint on the and-of-three every other bar, on a chord tone.
        static readonly (float beat, int midi)[] sparkles =
        {
            (3.5f, 91), (11.5f, 88), (19.5f, 91), (27.5f, 86),
            (35.5f, 93), (43.5f, 88), (51.5f, 89), (59.5f, 91),
        };

        // The room the struck voices are heard in: early reflections only, no feedback.
        static readonly (float seconds, float gain)[] room =
        {
            (0.071f, 0.28f), (0.131f, 0.22f), (0.197f, 0.17f), (0.283f, 0.13f),
            (0.359f, 0.10f), (0.449f, 0.075f), (0.557f, 0.055f),
        };

        enum Timbre { Bell, Keys, Sparkle }

        struct Note
        {
            public float start, hz, gain, decay, hold, attack;
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
            int tr = song.transpose;
            float beat = 60f / song.bpm;
            int bars = progression.Length;
            float barLen = 4f * beat;
            int n = Mathf.RoundToInt(bars * barLen * Rate);
            var data = new float[n];
            var struck = new float[n];
            bool stepping = samplesPerStep < int.MaxValue;
            int budget = samplesPerStep;

            // ---- score: the struck voices ----
            var notes = new List<Note>();
            foreach (var (b, midi, len) in melody)
                notes.Add(new Note { start = b * beat, hz = Hz(midi + tr), gain = 0.19f, decay = 1.4f,
                                     hold = len * beat + 1.8f, attack = 0.012f, timbre = Timbre.Bell });

            for (int bar = 0; bar < bars; bar++)
            {
                var ch = progression[bar];
                // Felt keys: the three middle voices on the one, softly; the top two on the three, softer.
                for (int k = 1; k <= 3; k++)
                    notes.Add(new Note { start = bar * barLen, hz = Hz(ch[k] + tr), gain = 0.05f, decay = 1.9f,
                                         hold = 2.2f * beat, attack = 0.006f, timbre = Timbre.Keys });
                for (int k = 3; k <= 4; k++)
                    notes.Add(new Note { start = bar * barLen + 2f * beat, hz = Hz(ch[k] + tr), gain = 0.028f, decay = 2.2f,
                                         hold = 1.8f * beat, attack = 0.006f, timbre = Timbre.Keys });
            }

            foreach (var (b, midi) in sparkles)
                notes.Add(new Note { start = b * beat, hz = Hz(midi + tr), gain = 0.045f, decay = 0.9f,
                                     hold = 3.5f, attack = 0.004f, timbre = Timbre.Sparkle });

            // ---- struck notes, wrapping round the loop end ----
            foreach (var note in notes)
            {
                int s0 = Mathf.RoundToInt(note.start * Rate);
                int count = Mathf.CeilToInt(note.hold * Rate);
                float w = 2f * Mathf.PI * note.hz;
                for (int j = 0; j < count; j++)
                {
                    float tau = j / (float)Rate;
                    float env = Mathf.Exp(-note.decay * tau) * (1f - Mathf.Exp(-tau / note.attack))
                              * Mathf.Clamp01((note.hold - tau) / 0.4f);
                    float ph = w * tau;
                    float v;
                    switch (note.timbre)
                    {
                        case Timbre.Bell:
                            // A soft glass bell: nearly all fundamental, a fading octave, and one
                            // faint stretched partial for the glint - not the steel tooth of a
                            // music box.
                            v = Mathf.Sin(ph)
                              + 0.22f * Mathf.Sin(2f * ph) * Mathf.Exp(-2.2f * tau)
                              + 0.05f * Mathf.Sin(3f * ph) * Mathf.Exp(-3f * tau)
                              + 0.035f * Mathf.Sin(4.16f * ph) * Mathf.Exp(-5f * tau);
                            break;
                        case Timbre.Keys:
                            // Felt-dampened keys: a rounder attack, harmonics that die quickly.
                            v = Mathf.Sin(ph)
                              + 0.35f * Mathf.Sin(2f * ph) * Mathf.Exp(-3f * tau)
                              + 0.12f * Mathf.Sin(3f * ph) * Mathf.Exp(-5f * tau)
                              + 0.04f * Mathf.Sin(4f * ph) * Mathf.Exp(-7f * tau);
                            break;
                        default:
                            // A shimmer: two partials a few cents apart beat slowly against each other.
                            v = Mathf.Sin(ph) + 0.5f * Mathf.Sin(2.004f * ph) + 0.15f * Mathf.Sin(3.01f * ph) * Mathf.Exp(-2f * tau);
                            break;
                    }
                    struck[(s0 + j) % n] += v * env * note.gain;
                }
                if (stepping && (budget -= count) <= 0) { budget = samplesPerStep; yield return null; }
            }

            // ---- the room: early reflections of the struck voices, wrapping like everything else ----
            foreach (var (seconds, gain) in room)
            {
                int d = Mathf.RoundToInt(seconds * Rate);
                for (int i = 0; i < n; i++) data[(i + d) % n] += struck[i] * gain;
                if (stepping) yield return null;
            }
            for (int i = 0; i < n; i++) data[i] += struck[i];

            // ---- pad and bass: each chord held for its span, crossfading into the next ----
            // Consecutive bars on the same voicing are one span. Spans fade in over 1.6 s centred
            // on their start and out the same way on their end, so neighbours cross at half
            // volume with no dip; the first and last spans are the same chord and wrap into each
            // other across the loop seam.
            var spans = new List<(int startBar, int endBar, int[] chord)>();
            for (int bar = 0; bar < bars; bar++)
            {
                if (spans.Count > 0 && spans[spans.Count - 1].chord == progression[bar])
                    spans[spans.Count - 1] = (spans[spans.Count - 1].startBar, bar + 1, progression[bar]);
                else spans.Add((bar, bar + 1, progression[bar]));
            }

            const float xfade = 0.8f;                                   // half the crossfade, seconds
            foreach (var (startBar, endBar, chord) in spans)
            {
                float t0 = startBar * barLen, t1 = endBar * barLen;
                int i0 = Mathf.FloorToInt((t0 - xfade) * Rate), i1 = Mathf.CeilToInt((t1 + xfade) * Rate);
                var hz = new float[chord.Length];
                for (int k = 0; k < chord.Length; k++) hz[k] = Hz(chord[k] + tr);
                float bassHz = Hz(chord[0] + tr - 12);

                for (int i = i0; i < i1; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.SmoothStep(0f, 1f, (t - (t0 - xfade)) / (2f * xfade))
                              * Mathf.SmoothStep(0f, 1f, ((t1 + xfade) - t) / (2f * xfade));
                    // A slow breath on the pad, a different phase per chord so it never pumps.
                    env *= 1f + 0.10f * Mathf.Sin(2f * Mathf.PI * 0.06f * t + startBar);

                    float pad = 0f;
                    for (int k = 0; k < hz.Length; k++)
                    {
                        float ph = 2f * Mathf.PI * hz[k] * t;
                        pad += Mathf.Sin(ph) + 0.85f * Mathf.Sin(ph * 1.0025f) + 0.10f * Mathf.Sin(2f * ph);
                    }
                    float bph = 2f * Mathf.PI * bassHz * t;
                    float bass = Mathf.Sin(bph) + 0.15f * Mathf.Sin(2f * bph);

                    int idx = ((i % n) + n) % n;
                    data[idx] += (pad * 0.024f + bass * 0.06f) * env;
                    if (stepping && ((i - i0) & (samplesPerStep - 1)) == samplesPerStep - 1) yield return null;
                }
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
