using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // One shared countdown reused for every timed decision: hero pick, secret pick,
    // guess submission, defend choice, and trust/challenge choice. Start() with a fresh
    // deadline and an onExpired fallback each time; only one timer ever runs, so starting
    // a new one implicitly cancels whatever the previous decision point was timing.
    //
    // Outside a match (hero pick, secret pick) the time is shown as a large number
    // centred just under the header -- numbers only, no gauge. During a match the HUD
    // hourglass (driven through OnTick) shows it instead and this number stays hidden.
    public class CountdownTimerView
    {
        private readonly GameObject _root;
        private readonly Text _text;
        private readonly MonoBehaviour _host;
        private Coroutine _routine;

        public Action<float, float> OnTick;   // (remaining, total) every frame while running
        public Action OnStopped;              // fired when a countdown ends or is cancelled
        private static readonly Color Brass = new Color32(214, 160, 70, 255);

        public bool ShowBadge = true;         // false while the hourglass shows the time instead

        // parent should be the full-screen root: the number is positioned relative to it,
        // under the 56px header.
        public CountdownTimerView(Transform parent, MonoBehaviour host)
        {
            _host = host;

            _root = new GameObject("TimerNumber", typeof(RectTransform));
            var rt = (RectTransform)_root.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -58f);
            rt.sizeDelta = new Vector2(240f, 60f);

            _text = UiFactory.Text(rt, "", 56, Brass, TextAnchor.MiddleCenter, FontStyle.Bold);
            _text.supportRichText = true;
            _text.raycastTarget = false;
            UiFactory.StretchToFillParent((RectTransform)_text.transform);
            var shadow = _text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);

            _root.SetActive(false);
        }

        public void Start(float seconds, Action onExpired)
        {
            Stop();
            _routine = _host.StartCoroutine(Run(seconds, onExpired));
        }

        // Display-only countdown for the OPPONENT's decision time (their guess, their truth-or-lie
        // choice, their trust-or-challenge choice), so the hourglass keeps running on my screen while
        // it's their move. Nothing happens when it runs out -- their own client acts on its own timer --
        // and none of the urgent tick / timeout sounds play, since it isn't my clock.
        public void StartSpectating(float seconds)
        {
            Stop();
            IsSpectating = true;
            _routine = _host.StartCoroutine(Spectate(seconds));
        }

        // True while the running countdown is the opponent's (display-only).
        public bool IsSpectating { get; private set; }

        public void Stop()
        {
            if (_routine != null)
            {
                _host.StopCoroutine(_routine);
                _routine = null;
            }
            IsSpectating = false;
            _root.SetActive(false);
            OnStopped?.Invoke();
        }

        private IEnumerator Spectate(float seconds)
        {
            float remaining = seconds;
            while (remaining > 0f)
            {
                OnTick?.Invoke(remaining, seconds);
                yield return null;
                remaining -= Time.deltaTime;
            }
            _routine = null;
            IsSpectating = false;
            OnStopped?.Invoke();
        }

        private IEnumerator Run(float seconds, Action onExpired)
        {
            _root.SetActive(ShowBadge);
            // Drawn above the screen that is currently showing; popups that open later
            // move themselves to the front again, so they still cover it.
            _root.transform.SetAsLastSibling();
            float total = seconds;
            float remaining = seconds;
            int lastTick = -1;
            while (remaining > 0f)
            {
                int displayedSeconds = Mathf.CeilToInt(remaining);
                _text.text = $"{displayedSeconds}<size=26>秒</size>";
                if (displayedSeconds <= 5 && displayedSeconds != lastTick)
                {
                    lastTick = displayedSeconds;
                    GameAudio.Play("SFX_Timer_Tick_Urgent", 1f + (5 - displayedSeconds) * 0.05f);
                }
                _text.color = remaining <= 5f ? UITheme.Clay : Brass;
                OnTick?.Invoke(Mathf.Max(0f, remaining), total);
                yield return null;
                remaining -= Time.deltaTime;
            }
            _routine = null;
            _root.SetActive(false);
            OnStopped?.Invoke();
            GameAudio.Play("SFX_Timer_Timeout");
            onExpired?.Invoke();
        }
    }
}
