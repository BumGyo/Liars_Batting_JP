using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // One shared countdown badge (lives in the header) reused for every timed
    // decision: hero pick, secret pick, guess submission, defend choice, and
    // trust/challenge choice. Start() with a fresh deadline and an onExpired
    // fallback each time; only one timer ever runs, so starting a new one
    // implicitly cancels whatever the previous decision point was timing.
    public class CountdownTimerView
    {
        private readonly GameObject _root;
        private readonly Text _text;
        private readonly RectTransform _fill;
        private readonly MonoBehaviour _host;
        private Coroutine _routine;

        public CountdownTimerView(Transform parent, MonoBehaviour host)
        {
            _host = host;

            var wrap = UiFactory.HorizontalGroup(parent, "Timer", spacing: 8, childAlign: TextAnchor.MiddleRight);
            UiFactory.SetSize(wrap, 120, 30);
            _root = wrap.gameObject;

            var track = UiFactory.Panel(wrap, "TimerTrack", UITheme.Surface2);
            UiFactory.SetSize(track, 70, 10);
            _fill = UiFactory.Panel(track, "TimerFill", UITheme.Accent);
            _fill.anchorMin = new Vector2(0, 0);
            _fill.anchorMax = new Vector2(1, 1);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;
            _fill.pivot = new Vector2(0, 0.5f);

            _text = UiFactory.Text(wrap, "", 14, UITheme.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UiFactory.SetSize(_text, 26, 30);

            _root.SetActive(false);
        }

        public void Start(float seconds, Action onExpired)
        {
            Stop();
            _routine = _host.StartCoroutine(Run(seconds, onExpired));
        }

        public void Stop()
        {
            if (_routine != null)
            {
                _host.StopCoroutine(_routine);
                _routine = null;
            }
            _root.SetActive(false);
        }

        private IEnumerator Run(float seconds, Action onExpired)
        {
            _root.SetActive(true);
            float total = seconds;
            float remaining = seconds;
            while (remaining > 0f)
            {
                _text.text = Mathf.CeilToInt(remaining).ToString();
                _fill.localScale = new Vector3(Mathf.Clamp01(remaining / total), 1f, 1f);
                bool urgent = remaining <= 3f;
                _fill.GetComponent<Image>().color = urgent ? UITheme.Clay : UITheme.Accent;
                _text.color = urgent ? UITheme.Clay : UITheme.Ink;
                yield return null;
                remaining -= Time.deltaTime;
            }
            _routine = null;
            _root.SetActive(false);
            onExpired?.Invoke();
        }
    }
}
