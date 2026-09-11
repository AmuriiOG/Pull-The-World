using System;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    public enum PtwSfx
    {
        Grab, Release, Impact, Win, Fail, PlateOn, PlateOff, Smother, SpinTick, Unlock,
        EnemyAlert, EnemySnarl, EnemyBite, EnemyDie,
        Bounce,
        Sparkle, UiTap, PortalEnter,
    }

    /// <summary>Looping ambiences, synthesised once on demand and cached.</summary>
    public enum PtwLoop
    {
        /// <summary>The portal's warm hum: a slow-beating drone of three low partials.</summary>
        PortalHum,
        /// <summary>Wind and air: band-limited noise under a slow breathing envelope.</summary>
        Ambience,
    }

    /// <summary>
    /// Every sound in the prototype is synthesised at boot, so the project ships with real audio
    /// feedback and zero licensed assets. Each slot can be overridden with an authored clip in the
    /// Inspector - those are the "sound hooks": drop a wav in and it takes over immediately.
    /// Calls are no-ops when no instance exists, so tests and headless runs stay silent and safe.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PtwAudio : MonoBehaviour
    {
        [Serializable]
        public class Slot
        {
            public PtwSfx id;
            [Tooltip("Leave empty to use the built-in synthesised sound.")]
            public AudioClip overrideClip;
            [Range(0f, 1f)] public float volume = 0.7f;
        }

        [SerializeField] bool audioEnabled = true;
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.55f;
        [SerializeField, Range(2, 16)] int voices = 8;
        [SerializeField] List<Slot> slots = new List<Slot>();

        static PtwAudio instance;

        readonly Dictionary<PtwSfx, AudioClip> generated = new Dictionary<PtwSfx, AudioClip>();
        AudioSource[] pool;
        int next;

        void Awake()
        {
            instance = this;
            pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.reverbZoneMix = 0f;
                pool[i] = src;
            }
            BuildBank();

            // The soundtrack lives on the same object. Added here as well as by the scene generator
            // so a scene built before music existed still gets it.
            if (!GetComponent<PtwMusic>()) gameObject.AddComponent<PtwMusic>();
        }

        void OnDestroy() { if (instance == this) instance = null; }

        public static void Play(PtwSfx sfx, float volume = 1f, float pitch = 1f)
        {
            if (instance == null) return;
            instance.PlayInternal(sfx, volume, pitch);
        }

        /// <summary>A seamless ambience loop, built on first request. Null when audio is off.</summary>
        public static AudioClip Loop(PtwLoop kind)
        {
            if (instance == null) return null;
            if (!instance.loops.TryGetValue(kind, out var clip) || !clip)
            {
                clip = instance.BuildLoop(kind);
                instance.loops[kind] = clip;
            }
            return clip;
        }

        readonly Dictionary<PtwLoop, AudioClip> loops = new Dictionary<PtwLoop, AudioClip>();

        AudioClip BuildLoop(PtwLoop kind)
        {
            switch (kind)
            {
                case PtwLoop.PortalHum:
                {
                    // Integer numbers of cycles in the loop length keep the seam silent.
                    const float len = 2f;
                    return MakeLoop("ptw_portal_hum", len, t =>
                        (Sine(110f, t) * 0.5f + Sine(165f, t) * 0.3f + Sine(220f, t) * 0.18f + Sine(330f, t) * 0.08f)
                        * (0.75f + 0.25f * Sine(1.5f, t)) * 0.35f);
                }
                default:
                {
                    // Wind: low-passed noise breathing slowly, over a steady, very quiet 55 Hz
                    // floor. 4 s, crossfaded at the seam inside MakeLoop. (The floor used to pulse
                    // at 0.5 Hz, which on speakers was a soft bump every two seconds - one more
                    // thing that sounded like something knocking.)
                    const float len = 4f;
                    return MakeLoop("ptw_ambience", len, t =>
                        Noise(t) * (0.55f + 0.45f * Sine(0.25f, t)) * 0.16f
                        + Sine(55f, t) * 0.012f);
                }
            }
        }

        void PlayInternal(PtwSfx sfx, float volume, float pitch)
        {
            // GameProgress is the single gate for the player's Sound setting, checked here rather
            // than at each of the ~30 call sites.
            if (!audioEnabled || pool == null || !GameProgress.SoundOn) return;

            AudioClip clip = null;
            float slotVol = 0.7f;
            foreach (var s in slots)
            {
                if (s.id != sfx) continue;
                slotVol = s.volume;
                if (s.overrideClip) clip = s.overrideClip;
                break;
            }
            if (clip == null) generated.TryGetValue(sfx, out clip);
            if (clip == null) return;

            var src = pool[next];
            next = (next + 1) % pool.Length;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * slotVol * masterVolume);
            src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            src.Play();
        }

        // ------------------------------------------------------------------- synthesis ------
        const int Rate = 44100;

        // The bank, in the pastel theme's voice: soft mallets and glass bells rather than beeps and
        // buzzers. Every sound is built from Bell() (a sine with a quiet third partial and a long
        // exponential tail) or from breathy noise, and nothing here is loud or sharp - the loudest
        // moments are the portal and the win, and even those are chimes.
        void BuildBank()
        {
            // Touching the world: a soft glass tap; letting go: a breath.
            generated[PtwSfx.Grab] = Make("ptw_grab", 0.22f, t =>
                Bell(t, 0f, 880f, 18f) * 0.45f + Bell(t, 0f, 1320f, 30f) * 0.12f);

            generated[PtwSfx.Release] = Make("ptw_release", 0.3f, t =>
                Bell(t, 0f, 660f, 12f) * 0.3f + Noise(t) * Env(t, 22f) * 0.05f);

            // Landing: a padded thud with a faint glass overtone, so the orb reads as glass but lands soft.
            generated[PtwSfx.Impact] = Make("ptw_impact", 0.4f, t =>
                Sine(Mathf.Lerp(140f, 70f, Mathf.Clamp01(t * 6f)), t) * Env(t, 14f) * 0.55f +
                Bell(t, 0f, 1760f, 40f) * 0.10f +
                Noise(t) * Env(t, 60f) * 0.10f);

            // Win: a rising pentatonic bell cascade, cream-toned, the biggest sound in the game.
            generated[PtwSfx.Win] = Make("ptw_win", 1.6f, t =>
                Bell(t, 0.00f, 523.25f, 3.0f) * 0.55f + Bell(t, 0.11f, 659.25f, 3.0f) * 0.5f +
                Bell(t, 0.22f, 783.99f, 2.8f) * 0.5f + Bell(t, 0.36f, 1046.5f, 2.4f) * 0.45f +
                Bell(t, 0.52f, 1318.5f, 2.0f) * 0.35f + Bell(t, 0.70f, 1567.98f, 1.7f) * 0.25f);

            // Fail: a soft descending pair of bells, sympathetic rather than a buzzer.
            generated[PtwSfx.Fail] = Make("ptw_fail", 0.9f, t =>
                Bell(t, 0.00f, 392f, 5f) * 0.4f + Bell(t, 0.16f, 311.13f, 4f) * 0.4f +
                Sine(Mathf.Lerp(120f, 70f, Mathf.Clamp01(t * 2f)), t) * Env(t, 5f) * 0.12f);

            generated[PtwSfx.PlateOn] = Make("ptw_plate_on", 0.5f, t =>
                Bell(t, 0f, 587.33f, 8f) * 0.45f + Bell(t, 0.05f, 880f, 9f) * 0.25f);

            generated[PtwSfx.PlateOff] = Make("ptw_plate_off", 0.4f, t =>
                Bell(t, 0f, 440f, 9f) * 0.35f);

            // Fire going out, crates breaking: a soft puff of air.
            generated[PtwSfx.Smother] = Make("ptw_smother", 0.6f, t =>
                Noise(t) * Env(t, 7f) * 0.35f * (1f - Mathf.Clamp01(t * 1.3f)) + Bell(t, 0f, 330f, 6f) * 0.08f);

            // The rotation tick. No longer played by anything (ImpactFeedback ticks haptically
            // only - a tick five times a second under the music read as a fault, not as feel);
            // kept in the bank so the enum and any authored slot stay valid.
            generated[PtwSfx.SpinTick] = Make("ptw_spin_tick", 0.09f, t =>
                Bell(t, 0f, 2093f, 70f) * 0.16f);

            generated[PtwSfx.Unlock] = Make("ptw_unlock", 0.9f, t =>
                Bell(t, 0.00f, 587.33f, 4f) * 0.4f + Bell(t, 0.12f, 880f, 3.6f) * 0.4f + Bell(t, 0.26f, 1174.66f, 3f) * 0.3f);

            // A sparkle: two very high bells a fifth apart, short. The orb whispers with this.
            generated[PtwSfx.Sparkle] = Make("ptw_sparkle", 0.45f, t =>
                Bell(t, 0f, 2637f, 14f) * 0.22f + Bell(t, 0.04f, 3951f, 18f) * 0.12f);

            // UI: a soft wooden tap with a glass edge.
            generated[PtwSfx.UiTap] = Make("ptw_ui_tap", 0.16f, t =>
                Sine(520f, t) * Env(t, 45f) * 0.35f + Bell(t, 0f, 1568f, 60f) * 0.1f);

            // Entering the portal: a warm swell of the hum's partials with a shimmer on top.
            generated[PtwSfx.PortalEnter] = Make("ptw_portal_enter", 1.3f, t =>
                (Sine(220f, t) * 0.4f + Sine(330f, t) * 0.25f + Sine(440f, t) * 0.15f) * (1f - Env(t, 9f)) * Env(t, 2.2f) * 0.5f +
                Bell(t, 0.15f, 1760f, 5f) * 0.2f + Bell(t, 0.30f, 2637f, 5f) * 0.14f);

            // --- the enemy: throat sounds, quieter and lower than before so they sit in the pastel
            // world as a shadow rather than a jump-scare. All built on Growl (odd harmonics under a
            // fast tremor).
            generated[PtwSfx.EnemyAlert] = Make("ptw_enemy_alert", 0.55f, t =>
                Noise(t) * Env(t, 5f) * 0.22f * (0.6f + 0.4f * Sine(31f, t)) +
                Growl(70f, t) * (1f - Env(t, 9f)) * Env(t, 3.5f) * 0.3f);

            generated[PtwSfx.EnemySnarl] = Make("ptw_enemy_snarl", 0.32f, t =>
                Growl(Mathf.Lerp(140f, 66f, Mathf.Clamp01(t * 3f)), t) * Env(t, 7f) * 0.4f +
                Noise(t) * Env(t, 20f) * 0.2f);

            generated[PtwSfx.EnemyBite] = Make("ptw_enemy_bite", 0.3f, t =>
                Noise(t) * Env(t, 28f) * 0.45f +
                Sine(90f, t) * Env(t, 12f) * 0.4f);

            generated[PtwSfx.EnemyDie] = Make("ptw_enemy_die", 0.7f, t =>
                Sine(Mathf.Lerp(900f, 240f, Mathf.Clamp01(t * 1.6f)) * (1f + 0.02f * Sine(24f, t)), t)
                    * Env(t, 3.2f) * 0.28f +
                Noise(t) * Env(t, 5f) * 0.18f);

            // The spring pad: a soft "boing" - a bell that slides up.
            generated[PtwSfx.Bounce] = Make("ptw_bounce", 0.36f, t =>
                Sine(Mathf.Lerp(220f, 520f, Mathf.Clamp01(t * 4f)), t) * Env(t, 8f) * 0.4f +
                Bell(t, 0.05f, 1046.5f, 10f) * 0.18f);
        }

        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Env(float t, float rate) => Mathf.Exp(-t * rate);

        /// <summary>A soft bell: fundamental plus a quiet third partial, exponential tail, fast attack.</summary>
        static float Bell(float t, float start, float hz, float decay)
        {
            float lt = t - start;
            if (lt < 0f) return 0f;
            float attack = 1f - Mathf.Exp(-lt * 400f);
            return (Sine(hz, lt) + 0.28f * Sine(hz * 3f, lt) * Env(lt, decay * 1.8f)) * Env(lt, decay) * attack;
        }

        /// <summary>A seamless loop: rendered slightly long and cross-faded into its own start.</summary>
        static AudioClip MakeLoop(string name, float seconds, Func<float, float> fn)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            int fade = Mathf.Min(n / 4, Rate / 5);
            var data = new float[n];
            noiseState = 0f;
            for (int i = 0; i < n; i++) data[i] = fn(i / (float)Rate);
            // Blend the tail into the head so the seam is inaudible whatever fn does.
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                float tail = fn((n + i) / (float)Rate);
                data[i] = Mathf.Clamp(data[i] * k + tail * (1f - k), -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A throaty tone: odd harmonics (a rounded square) with a 27 Hz tremor.</summary>
        static float Growl(float hz, float t) =>
            (Sine(hz, t) + 0.5f * Sine(hz * 3f, t) + 0.25f * Sine(hz * 5f, t)) * (0.7f + 0.3f * Sine(27f, t));

        static float Note(float t, float start, float hz, float decay)
        {
            float lt = t - start;
            if (lt < 0f) return 0f;
            return Sine(hz, lt) * Env(lt, decay) * 0.35f;
        }

        // Deterministic white noise through a one-pole low-pass (~1.7 kHz), so it reads as a soft
        // breath of air. It used to be Perlin noise sampled 7000 lattice cells per second, which is
        // not white: gradient noise carries most of its energy at the lattice frequency, so the
        // "wind" under the music was a faint 7 kHz whistle rather than a hiss.
        static float noiseState;
        static float Noise(float t)
        {
            uint h = (uint)Mathf.RoundToInt(t * Rate) * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            float raw = h / (float)uint.MaxValue * 2f - 1f;
            noiseState = Mathf.Lerp(noiseState, raw, 0.22f);
            return noiseState;
        }

        static AudioClip Make(string name, float duration, Func<float, float> fn)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(duration * Rate));
            var data = new float[n];
            noiseState = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(fn(t), -1f, 1f);
            }
            // Short fade out so nothing clicks at the tail.
            int fade = Mathf.Min(400, n / 4);
            for (int i = 0; i < fade; i++)
                data[n - 1 - i] *= i / (float)fade;

            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
