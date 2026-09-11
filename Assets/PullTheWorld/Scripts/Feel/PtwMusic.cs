using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// The soundtrack: one slow ambient loop per chapter, synthesised on first use, crossfaded on
    /// chapter change, gated by the Music setting.
    ///
    /// Synthesised for the same reason every sound effect is: the project ships with real audio
    /// and zero licensed assets, and a loop built from the chapter's own chord chart cannot clash
    /// with its mood. Each loop is four chords of four beats - a soft detuned pad carrying the
    /// chord, a plucked arpeggio an octave up, a sub bass on each chord change, and a few sparse
    /// pentatonic sparkles placed by a seeded RNG so the loop is fixed but does not sound gridded.
    /// The pad releases fully before each chord ends and the arpeggio rests on the last eighth, so
    /// the loop seam is quiet and the clip simply loops.
    ///
    /// Chapter one's loop is built synchronously at boot (it is what the menu plays); the others
    /// are built a few thousand samples per frame in the background so a chapter change never
    /// hitches. Slots for authored loops exist (<see cref="overrideLoops"/>): drop a clip in per
    /// chapter and it replaces the synthesised one.
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
        AudioSource ambience;                    // wind and air, under the Sound setting
        [SerializeField, Range(0f, 1f)] float ambienceVolume = 0.30f;
        readonly Dictionary<int, AudioClip> loops = new Dictionary<int, AudioClip>();
        LevelManager hooked;
        SkyTheme sky;
        int chapter = -1;
        static bool ducked;

        /// <summary>True when the setting is on and a chapter has been chosen.</summary>
        public bool WantsToPlay => GameProgress.MusicOn && chapter >= 0;
        public float LiveVolume => live ? live.volume : 0f;
        public int Chapter => chapter;

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

            // Environmental ambience: a soft wind that follows the SOUND setting, not the music one,
            // so the world still breathes with the music off.
            ambience = MakeSource();
            var wind = PtwAudio.Loop(PtwLoop.Ambience);
            if (wind) { ambience.clip = wind; ambience.Play(); }
        }

        void Update()
        {
            Hook();
            float target = WantsToPlay ? volume * (ducked ? duckVolume : 1f) : 0f;
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds);   // keeps fading while paused
            Fade(live, target, step);
            var other = live == a ? b : a;
            Fade(other, 0f, step);
            if (other.clip && other.volume <= 0f) { other.Stop(); other.clip = null; }

            if (ambience && ambience.clip)
                ambience.volume = Mathf.MoveTowards(ambience.volume,
                    GameProgress.SoundOn ? ambienceVolume * (ducked ? duckVolume : 1f) : 0f, step);
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
                        : Mathf.Clamp(hooked.CurrentIndex * charts.Length / count, 0, charts.Length - 1);
            PlayChapter(c);
        }

        void PlayChapter(int c)
        {
            if (c == chapter) return;
            chapter = c;
            var clip = Loop(c);
            if (!clip) return;
            var next = live == a ? b : a;
            next.clip = clip;
            next.volume = 0f;
            next.Play();
            live = next;
        }

        AudioClip Loop(int c)
        {
            if (c < overrideLoops.Length && overrideLoops[c]) return overrideLoops[c];
            if (loops.TryGetValue(c, out var clip) && clip) return clip;

            var it = Synthesise(c, int.MaxValue, made => clip = made);
            while (it.MoveNext()) { }
            loops[c] = clip;
            return clip;
        }

        IEnumerator Pregenerate()
        {
            for (int c = 0; c < charts.Length; c++)
            {
                if (loops.ContainsKey(c)) continue;
                AudioClip made = null;
                yield return Synthesise(c, 16384, k => made = k);
                if (made && !loops.ContainsKey(c)) loops[c] = made;
            }
        }

        // ------------------------------------------------------------------ synthesis -------
        const int Rate = 44100;

        struct Chart
        {
            public float bpm;
            public float detune;      // pad chorus width, as a fraction of pitch
            public int seed;
            public float[][] chords;  // four chords, three notes each, Hz
            public float[] sparkle;   // pentatonic pool for the sparse high notes, Hz
        }

        // One chart per chapter, all in the pastel theme's voice: slow, major, airy. Four-note
        // chords (a seventh or a ninth on top) so the pad shimmers rather than blocks; the
        // arpeggio only ever walks the lower three. Dawn is C, morning D, golden hour F.
        static readonly Chart[] charts =
        {
            new Chart
            {
                bpm = 54f, detune = 0.0030f, seed = 11,
                chords = new[]
                {
                    new[] { 261.63f, 329.63f, 392.00f, 587.33f },   // C add9
                    new[] { 220.00f, 261.63f, 329.63f, 392.00f },   // Am7
                    new[] { 174.61f, 220.00f, 261.63f, 329.63f },   // Fmaj7
                    new[] { 196.00f, 246.94f, 293.66f, 440.00f },   // G add9
                },
                sparkle = new[] { 523.25f, 587.33f, 659.25f, 783.99f, 880f, 1046.5f },
            },
            new Chart
            {
                bpm = 52f, detune = 0.0034f, seed = 23,
                chords = new[]
                {
                    new[] { 293.66f, 369.99f, 440.00f, 554.37f },   // Dmaj7
                    new[] { 196.00f, 246.94f, 293.66f, 329.63f },   // G add9
                    new[] { 220.00f, 277.18f, 329.63f, 415.30f },   // Amaj7
                    new[] { 246.94f, 293.66f, 369.99f, 440.00f },   // Bm7
                },
                sparkle = new[] { 587.33f, 659.25f, 739.99f, 880f, 987.77f, 1174.66f },
            },
            new Chart
            {
                bpm = 50f, detune = 0.0030f, seed = 37,
                chords = new[]
                {
                    new[] { 174.61f, 220.00f, 261.63f, 392.00f },   // F add9
                    new[] { 146.83f, 174.61f, 220.00f, 261.63f },   // Dm7
                    new[] { 233.08f, 293.66f, 349.23f, 440.00f },   // Bbmaj7
                    new[] { 261.63f, 329.63f, 392.00f, 587.33f },   // C add9
                },
                sparkle = new[] { 698.46f, 783.99f, 880f, 1046.5f, 1174.66f, 1396.91f },
            },
        };

        /// <summary>One struck note: a pluck, a bass hit or a sparkle.</summary>
        struct Hit
        {
            public float start, hz, gain, decay, bright, hold;
        }

        /// <summary>
        /// Builds one chapter's loop. Yields after every <paramref name="samplesPerStep"/> samples
        /// so it can run as a coroutine; pass int.MaxValue to run it straight through.
        /// </summary>
        IEnumerator Synthesise(int chapterIndex, int samplesPerStep, Action<AudioClip> done)
        {
            var ch = charts[Mathf.Clamp(chapterIndex, 0, charts.Length - 1)];
            float beat = 60f / ch.bpm, eighth = beat * 0.5f, chordLen = beat * 4f;
            int chordCount = ch.chords.Length;
            float loopLen = chordLen * chordCount;
            int n = Mathf.RoundToInt(loopLen * Rate);
            var data = new float[n];

            // Score the struck notes first; the pad is computed per sample below.
            var hits = new List<Hit>();
            int[] pattern = { 0, 1, 2, 1, 0, 2, 1, -1 };          // -1 rests, so bar ends breathe
            var rng = new System.Random(ch.seed);
            int eighths = Mathf.RoundToInt(loopLen / eighth);
            for (int k = 0; k < eighths; k++)
            {
                int c = Mathf.Min(chordCount - 1, (int)(k * eighth / chordLen));
                int p = pattern[k % pattern.Length];
                if (p >= 0)
                    hits.Add(new Hit
                    {
                        start = k * eighth, hz = ch.chords[c][p] * 2f,
                        gain = k % 4 == 0 ? 0.14f : 0.09f, decay = 2.4f, bright = 0.30f, hold = 2.0f,
                    });
                if (rng.NextDouble() < 0.12)
                    hits.Add(new Hit
                    {
                        start = k * eighth + eighth * 0.5f * (float)rng.NextDouble(),
                        hz = ch.sparkle[rng.Next(ch.sparkle.Length)] * (rng.NextDouble() < 0.3 ? 2f : 1f),
                        gain = 0.05f + 0.05f * (float)rng.NextDouble(), decay = 2.4f, bright = 0.3f, hold = 2f,
                    });
            }
            for (int c = 0; c < chordCount; c++)
                hits.Add(new Hit
                {
                    start = c * chordLen, hz = ch.chords[c][0] * 0.5f,
                    gain = 0.20f, decay = 0.5f, bright = 0.08f, hold = chordLen - 0.15f,
                });

            const float attack = 0.55f, release = 0.8f;
            int i = 0;
            while (i < n)
            {
                int i0 = i;
                int end = samplesPerStep >= n - i ? n : i + samplesPerStep;

                // Pad: four notes, each a fundamental, two soft partials and a detuned twin, under a
                // slow tremolo and a slight vibrato, with a per-chord attack/release envelope.
                //
                // The vibrato is applied to the PHASE, as the integral of the wobbling frequency:
                //   phase = 2*pi*f * (t - (d/w) * (cos(w t + c) - cos(c)))
                // An earlier version multiplied the frequency by (1 + d sin(w t)) and then by t.
                // That is not a vibrato: the instantaneous pitch of sin(f*(1+d sin(wt))*t) drifts
                // by f*d*w*t, which grows without bound - about +/-8% (more than a semitone) one
                // second into the loop and a full siren by the end. That was the "off" music.
                const float vibHz = 4.6f, vibDepth = 0.0028f;
                const float vibW = 2f * Mathf.PI * vibHz;
                for (; i < end; i++)
                {
                    float t = i / (float)Rate;
                    int c = Mathf.Min(chordCount - 1, (int)(t / chordLen));
                    float lt = t - c * chordLen;
                    float env = Mathf.SmoothStep(0f, 1f, lt / attack)
                              * Mathf.SmoothStep(0f, 1f, (chordLen - lt) / release);
                    float trem = 0.86f + 0.14f * Mathf.Sin(2f * Mathf.PI * 0.31f * t);
                    float vibT = t - (vibDepth / vibW) * (Mathf.Cos(vibW * t + c) - Mathf.Cos(c));
                    float pad = 0f;
                    var notes = ch.chords[c];
                    for (int k = 0; k < notes.Length; k++)
                    {
                        float ph = 2f * Mathf.PI * notes[k] * vibT;
                        pad += Mathf.Sin(ph) + 0.22f * Mathf.Sin(2f * ph + 0.4f) + 0.05f * Mathf.Sin(3f * ph)
                             + 0.60f * Mathf.Sin(ph * (1f + ch.detune));
                    }
                    data[i] = pad * env * trem * 0.11f;
                }

                // Struck notes overlapping this block.
                foreach (var h in hits)
                {
                    int s0 = Mathf.RoundToInt(h.start * Rate);
                    int s1 = Mathf.Min(n, s0 + Mathf.CeilToInt(h.hold * Rate));
                    int from = Mathf.Max(s0, i0), to = Mathf.Min(s1, end);
                    for (int j = from; j < to; j++)
                    {
                        float tau = (j - s0) / (float)Rate;
                        float ph = 2f * Mathf.PI * h.hz * tau;
                        float env = Mathf.Exp(-h.decay * tau) * (1f - Mathf.Exp(-tau * 240f))
                                  * Mathf.Clamp01((h.hold - tau) / 0.35f);
                        data[j] += (Mathf.Sin(ph) + h.bright * Mathf.Sin(2f * ph)) * env * h.gain;
                    }
                }

                if (i < n) yield return null;
            }

            // Normalise, and belt-and-braces fades at the seam so nothing can click.
            float peak = 0.001f;
            for (int k = 0; k < n; k++) peak = Mathf.Max(peak, Mathf.Abs(data[k]));
            float norm = 0.8f / peak;
            int fadeIn = Rate / 25, fadeOut = Rate / 4;
            for (int k = 0; k < n; k++)
            {
                float v = data[k] * norm;
                if (k < fadeIn) v *= k / (float)fadeIn;
                if (k >= n - fadeOut) v *= (n - 1 - k) / (float)fadeOut;
                data[k] = v;
            }

            var clip = AudioClip.Create($"ptw_music_{chapterIndex}", n, 1, Rate, false);
            clip.SetData(data, 0);
            done(clip);
        }
    }
}
