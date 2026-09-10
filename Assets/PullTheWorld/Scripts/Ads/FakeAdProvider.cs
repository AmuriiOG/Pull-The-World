using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PullTheWorld.Ads
{
    /// <summary>
    /// A stand-in ad network for development builds: a full-screen card that says TEST AD, counts
    /// down, and then "rewards". It exists so every placement can be exercised on a phone - the
    /// skip button, the interstitial cadence, the pause and resume around an ad - long before a
    /// real SDK is wired in, and so the flow can be tested headlessly.
    /// </summary>
    public class FakeAdProvider : MonoBehaviour, IAdProvider
    {
        [SerializeField] float rewardedSeconds = 3f;
        [SerializeField] float interstitialSeconds = 2f;

        Canvas canvas;
        TMP_Text label;
        bool showing;

        public string Name => "Fake";
        public bool IsRewardedReady => !showing;
        public bool IsInterstitialReady => !showing;

        public void Initialize(Action<bool> ready) => ready?.Invoke(true);

        public void ShowRewarded(Action<bool> completed) =>
            StartCoroutine(Show("TEST AD\nREWARDED", rewardedSeconds, () => completed?.Invoke(true)));

        public void ShowInterstitial(Action closed) =>
            StartCoroutine(Show("TEST AD\nINTERSTITIAL", interstitialSeconds, closed));

        IEnumerator Show(string title, float seconds, Action done)
        {
            if (showing) { done?.Invoke(); yield break; }
            showing = true;
            EnsureCard();
            canvas.gameObject.SetActive(true);

            float t = seconds;
            while (t > 0f)
            {
                label.text = $"{title}\n\n<size=60%>closes in {Mathf.CeilToInt(t)}</size>";
                t -= Time.unscaledDeltaTime;                  // ads run while the game is paused
                yield return null;
            }

            canvas.gameObject.SetActive(false);
            showing = false;
            done?.Invoke();
        }

        void EnsureCard()
        {
            if (canvas) return;
            var go = new GameObject("FakeAdCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;                        // over everything, including the UI
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            go.AddComponent<GraphicRaycaster>();              // swallows taps under the card

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.96f);

            var txt = new GameObject("Label", typeof(RectTransform));
            txt.transform.SetParent(go.transform, false);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 400f);
            label = txt.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 72f;
            label.color = Color.white;
            label.raycastTarget = false;
        }
    }
}
