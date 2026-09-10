using System;
using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    public enum PtwSfx
    {
        Grab, Release, Impact, Win, Fail, PlateOn, PlateOff, Smother, SpinTick, Unlock
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
        }

        void OnDestroy() { if (instance == this) instance = null; }

        public static void Play(PtwSfx sfx, float volume = 1f, float pitch = 1f)
        {
            if (instance == null) return;
            instance.PlayInternal(sfx, volume, pitch);
        }

        void PlayInternal(PtwSfx sfx, float volume, float pitch)
        {
            if (!audioEnabled || pool == null) return;

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

        void BuildBank()
        {
            generated[PtwSfx.Grab] = Make("ptw_grab", 0.14f, t =>
                Sine(620f, t) * Env(t, 40f) * 0.5f + Sine(930f, t) * Env(t, 70f) * 0.2f);

            generated[PtwSfx.Release] = Make("ptw_release", 0.16f, t =>
                Sine(400f, t) * Env(t, 26f) * 0.45f);

            generated[PtwSfx.Impact] = Make("ptw_impact", 0.34f, t =>
                Sine(Mathf.Lerp(150f, 62f, Mathf.Clamp01(t * 7f)), t) * Env(t, 13f) * 0.85f +
                Noise(t) * Env(t, 46f) * 0.22f);

            generated[PtwSfx.Win] = Make("ptw_win", 0.95f, t =>
                Note(t, 0.00f, 523.25f, 5.5f) + Note(t, 0.09f, 659.25f, 5.5f) +
                Note(t, 0.18f, 783.99f, 4.5f) + Note(t, 0.30f, 1046.5f, 3.2f) * 0.7f);

            generated[PtwSfx.Fail] = Make("ptw_fail", 0.5f, t =>
                Sine(Mathf.Lerp(240f, 96f, Mathf.Clamp01(t * 2.4f)), t) * Env(t, 6.5f) * 0.6f +
                Sine(Mathf.Lerp(121f, 48f, Mathf.Clamp01(t * 2.4f)), t) * Env(t, 6.5f) * 0.3f);

            generated[PtwSfx.PlateOn] = Make("ptw_plate_on", 0.2f, t =>
                Sine(740f, t) * Env(t, 22f) * 0.5f + Sine(1108f, t) * Env(t, 38f) * 0.18f);

            generated[PtwSfx.PlateOff] = Make("ptw_plate_off", 0.2f, t =>
                Sine(430f, t) * Env(t, 24f) * 0.45f);

            generated[PtwSfx.Smother] = Make("ptw_smother", 0.55f, t =>
                Noise(t) * Env(t, 6.5f) * 0.5f * (1f - Mathf.Clamp01(t * 1.4f)));

            generated[PtwSfx.SpinTick] = Make("ptw_spin_tick", 0.07f, t =>
                Sine(1180f, t) * Env(t, 80f) * 0.28f);

            generated[PtwSfx.Unlock] = Make("ptw_unlock", 0.5f, t =>
                Note(t, 0.00f, 587.33f, 7f) + Note(t, 0.10f, 880f, 5f));
        }

        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Env(float t, float rate) => Mathf.Exp(-t * rate);

        static float Note(float t, float start, float hz, float decay)
        {
            float lt = t - start;
            if (lt < 0f) return 0f;
            return Sine(hz, lt) * Env(lt, decay) * 0.35f;
        }

        // Deterministic value noise, smoothed a little so it reads as a soft hiss not a buzz.
        static float noiseState;
        static float Noise(float t)
        {
            float raw = Mathf.PerlinNoise(t * 7000f, 0.37f) * 2f - 1f;
            noiseState = Mathf.Lerp(noiseState, raw, 0.6f);
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
