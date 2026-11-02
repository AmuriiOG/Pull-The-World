using System.Collections.Generic;
using UnityEngine;

namespace PullTheWorld
{
    /// <summary>
    /// GameProgress storage inside YouTube Playables: ytgame.game.loadData / saveData through
    /// <see cref="Playables"/>. Driven by <see cref="PlayablesHost"/> (Tick, FlushNow).
    ///
    /// The rules, in order of importance:
    ///  1. Nothing is saved until a load has SUCCEEDED (YouTube also rejects saveData before
    ///     loadData). A failed load is retried with backoff; meanwhile the session plays on in
    ///     memory and its progress is merged in when the load finally lands - so a temporary
    ///     outage can never overwrite a player's progress with defaults.
    ///  2. A save this version cannot read (corrupt, or written by a newer game version) is never
    ///     overwritten: the session continues read-only and logs a warning to YouTube.
    ///  3. Saves are coalesced (one request in flight, a short debounce) and flushed at once when
    ///     YouTube pauses the game.
    /// </summary>
    public sealed class CloudProgressStore : IProgressStore
    {
        public enum State { Loading, Loaded, LoadFailed, ReadOnly }

        const float SaveDebounce = 0.5f, SaveRetry = 3f, FirstLoadRetry = 2f, MaxLoadRetry = 30f;

        readonly Dictionary<string, int> values = PlayablesSave.Defaults();
        readonly HashSet<string> changed = new HashSet<string>();
        bool progressReset;
        bool dirty;
        bool inFlight;
        float saveTimer;
        float loadRetryTimer, loadBackoff = FirstLoadRetry;
        string lastSaved;

        public State Status { get; private set; } = State.Loading;
        /// <summary>True once the load has resolved one way or the other (gameReady may follow).</summary>
        public bool Resolved => Status != State.Loading || loadRetries > 0;
        int loadRetries;

        public int GetInt(string key, int defaultValue) => values.TryGetValue(key, out int v) ? v : defaultValue;

        public void SetInt(string key, int value)
        {
            if (key == GameProgress.KeyUnlocked && value < GetInt(key, 0)) progressReset = true;   // only Restart All goes down
            values[key] = value;
            changed.Add(key);
        }

        public void Save()
        {
            dirty = true;
            saveTimer = SaveDebounce;
        }

        public void Tick(float dt)
        {
            switch (Status)
            {
                case State.Loading:
                    var s = Playables.CurrentLoadState;
                    if (s == Playables.LoadState.Loaded) Apply(Playables.LoadedData);
                    else if (s == Playables.LoadState.Failed || s == Playables.LoadState.Unavailable)
                    {
                        Status = State.LoadFailed;
                        loadRetryTimer = loadBackoff;
                        Debug.LogWarning($"PTW_YT loadData failed; retrying in {loadBackoff:F0}s. Progress will not be saved until it succeeds.");
                    }
                    break;

                case State.LoadFailed:
                    loadRetryTimer -= dt;
                    if (loadRetryTimer <= 0f)
                    {
                        loadRetries++;
                        loadBackoff = Mathf.Min(loadBackoff * 2f, MaxLoadRetry);
                        Status = State.Loading;
                        Playables.RetryLoad();
                    }
                    break;

                case State.Loaded:
                    if (dirty && !inFlight)
                    {
                        saveTimer -= dt;
                        if (saveTimer <= 0f) Send();
                    }
                    break;
            }
        }

        /// <summary>YouTube is pausing (maybe for good): save now if allowed.</summary>
        public void FlushNow()
        {
            if (Status != State.Loaded) return;
            if (dirty || lastSaved == null) Send(force: true);
        }

        void Apply(string json)
        {
            var status = PlayablesSave.Parse(json, out var cloud);
            if (status == PlayablesSave.ParseStatus.Unreadable || status == PlayablesSave.ParseStatus.NewerVersion)
            {
                Status = State.ReadOnly;
                Debug.LogWarning($"PTW_YT cloud save is {status}; keeping it untouched and playing this session without saving.");
                Playables.LogWarning();
                return;
            }

            var merged = PlayablesSave.Merge(cloud, values, changed, progressReset);
            string cloudJson = status == PlayablesSave.ParseStatus.Empty ? null : PlayablesSave.Serialize(cloud);
            values.Clear();
            foreach (var kv in merged) values[kv.Key] = kv.Value;
            changed.Clear();
            progressReset = false;
            Status = State.Loaded;
            lastSaved = cloudJson;
            if (PlayablesSave.Serialize(values) != cloudJson) { dirty = true; saveTimer = 0f; }
            Debug.Log($"PTW_YT loadData ok ({status}) unlocked={GetInt(GameProgress.KeyUnlocked, 0)}");
            GameProgress.NotifyReloaded();
        }

        void Send(bool force = false)
        {
            if (inFlight && !force) return;
            string json = PlayablesSave.Serialize(values);
            if (json == lastSaved) { dirty = false; return; }
            inFlight = true;
            dirty = false;
            Debug.Log("PTW_YT saveData " + json);
            Playables.Save(json, code =>
            {
                inFlight = false;
                if (code == Playables.Ok) { lastSaved = json; return; }
                Debug.LogWarning($"PTW_YT saveData failed ({code})");
                if (code == Playables.InvalidParams || code == Playables.SizeLimit) { Playables.LogError(); return; }
                dirty = true;
                saveTimer = SaveRetry;
            });
        }
    }
}
