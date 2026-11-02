// YouTube Playables bridge for Pull The World (WebGL builds only - Unity never compiles .jslib
// into other players). Every call goes through the documented `ytgame` API
// (https://developers.google.com/youtube/gaming/playables/reference/sdk); nothing else.
//
// Outside YouTube - a local server, the Editor's "Build and Run" - the SDK is a no-op and
// ytgame.IN_PLAYABLES_ENV is false, so every function here quietly reports "not available" and the
// C# side (Playables.cs) keeps the game's normal behaviour.
//
// Callbacks into C# are function pointers registered once by PtwYt_Init and invoked with
// makeDynCall, so they work even before the page has a unityInstance to SendMessage to.

var PtwPlayablesLib = {
  $PtwYt: {
    cb: {},
    env: function () {
      return typeof ytgame !== "undefined" && !!ytgame.IN_PLAYABLES_ENV;
    },
    // Unity's own WebAudio context. Suspended while YouTube has the game paused, so no sound
    // can leak out even though the C# side also pauses the AudioListener.
    audioContext: function () {
      return (typeof WEBAudio !== "undefined" && WEBAudio.audioContext) ? WEBAudio.audioContext : null;
    },
    errorCode: function (e) {
      // 1 ok, -1 API_UNAVAILABLE, -2 INVALID_PARAMS, -3 SIZE_LIMIT_EXCEEDED, -4 anything else
      var t = e && e.errorType;
      if (typeof ytgame !== "undefined" && ytgame.SdkErrorType) {
        if (t === ytgame.SdkErrorType.API_UNAVAILABLE) return -1;
        if (t === ytgame.SdkErrorType.INVALID_PARAMS) return -2;
        if (t === ytgame.SdkErrorType.SIZE_LIMIT_EXCEEDED) return -3;
      }
      return -4;
    },
    str: function (s) {
      var n = lengthBytesUTF8(s) + 1;
      var p = _malloc(n);
      stringToUTF8(s, p, n);
      return p;
    }
  },

  // onPause, onResume, onAudio(int enabled), onResult(int requestId, int code)
  PtwYt_Init: function (onPause, onResume, onAudio, onResult) {
    PtwYt.cb.pause = onPause;
    PtwYt.cb.resume = onResume;
    PtwYt.cb.audio = onAudio;
    PtwYt.cb.result = onResult;
    if (!PtwYt.env()) return 0;

    ytgame.system.onPause(function () {
      // C# first: it saves and freezes the game while the main loop can still run its handler.
      {{{ makeDynCall('v', 'PtwYt.cb.pause') }}}();
      var ctx = PtwYt.audioContext();
      if (ctx && ctx.state === "running") ctx.suspend();
      Module.pauseMainLoop();
      // The engine's 1 s background tick and the audio resume poll (see the template).
      if (typeof window.ptwSuspendTimers === "function") window.ptwSuspendTimers();
    });
    ytgame.system.onResume(function () {
      // Restart Unity before handing control back, so the C# handler runs on a live player.
      if (typeof window.ptwResumeTimers === "function") window.ptwResumeTimers();
      Module.resumeMainLoop();
      var ctx = PtwYt.audioContext();
      if (ctx && ctx.state === "suspended") ctx.resume();
      {{{ makeDynCall('v', 'PtwYt.cb.resume') }}}();
    });
    ytgame.system.onAudioEnabledChange(function (enabled) {
      {{{ makeDynCall('vi', 'PtwYt.cb.audio') }}}(enabled ? 1 : 0);
    });
    return 1;
  },

  PtwYt_InEnv: function () {
    return PtwYt.env() ? 1 : 0;
  },

  PtwYt_AudioEnabled: function () {
    if (!PtwYt.env()) return 1;
    return ytgame.system.isAudioEnabled() ? 1 : 0;
  },

  PtwYt_GameReady: function () {
    if (PtwYt.env()) ytgame.game.gameReady();
  },

  // The template starts ytgame.game.loadData() at page load (window.ptwSave). 0 pending,
  // 1 loaded, 2 failed, 3 not available.
  PtwYt_LoadState: function () {
    var s = (typeof window !== "undefined") && window.ptwSave;
    return s ? s.state : 3;
  },

  PtwYt_LoadedData: function () {
    var s = window.ptwSave;
    return PtwYt.str(s && typeof s.data === "string" ? s.data : "");
  },

  PtwYt_RetryLoad: function () {
    if (typeof window.ptwLoadSave === "function") window.ptwLoadSave();
  },

  PtwYt_Save: function (requestId, json) {
    var data = UTF8ToString(json);
    if (!PtwYt.env()) { {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, -1); return; }
    ytgame.game.saveData(data).then(function () {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, 1);
    }, function (e) {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, PtwYt.errorCode(e));
    });
  },

  // Result 1 only when YouTube confirms the reward was earned; 0 not earned; negative on error.
  PtwYt_RequestRewarded: function (requestId, rewardId) {
    var id = UTF8ToString(rewardId);
    if (!PtwYt.env()) { {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, -1); return; }
    ytgame.ads.requestRewardedAd(id).then(function (earned) {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, earned === true ? 1 : 0);
    }, function (e) {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, PtwYt.errorCode(e));
    });
  },

  PtwYt_RequestInterstitial: function (requestId) {
    if (!PtwYt.env()) { {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, -1); return; }
    ytgame.ads.requestInterstitialAd().then(function () {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, 1);
    }, function (e) {
      {{{ makeDynCall('vii', 'PtwYt.cb.result') }}}(requestId, PtwYt.errorCode(e));
    });
  },

  PtwYt_LogWarning: function () {
    if (PtwYt.env()) ytgame.health.logWarning();
  },

  PtwYt_LogError: function () {
    if (PtwYt.env()) ytgame.health.logError();
  }
};

autoAddDeps(PtwPlayablesLib, "$PtwYt");
mergeInto(LibraryManager.library, PtwPlayablesLib);
