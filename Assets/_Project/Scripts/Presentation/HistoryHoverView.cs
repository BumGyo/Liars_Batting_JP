using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A small iron-plate button ("自分の推測履歴 (3)"). Hovering it pops up the complete,
    // scrollable guess history above the button. Exposes the same AddRow / Clear calls the
    // old always-open ScrollingHistoryColumn had.
    public struct HistoryRecord
    {
        public int[] Guess;
        public JudgeResult Reported;
        public bool WasLie;
        public bool RevealLie;
        public bool TruthConfirmed;   // the opponent's answer was proven true
    }

    public class HistoryHoverView
    {
        public readonly RectTransform Root;

        private readonly List<HistoryRecord> _records = new List<HistoryRecord>();
        public IReadOnlyList<HistoryRecord> Records => _records;

        private readonly string _label;
        private readonly Text _labelText;
        private readonly ScrollingHistoryColumn _full;
        private readonly GameObject _popup;
        private int _count;
        private bool _hovered;

        // popupAlignRight: the popup's right edge lines up with the button's right edge
        // (use it for a button on the right side so the popup stays on screen).
        public HistoryHoverView(Transform parent, string label, string popupTitle, MonoBehaviour host,
            bool popupAlignRight = false)
        {
            _label = label;

            var go = new GameObject("HistoryButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Root = (RectTransform)go.transform;
            Root.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = UITheme.Surface;                  // fallback if the plate sprites are missing
            go.GetComponent<Button>().targetGraphic = image;

            _labelText = UiFactory.Text(Root, label, 13, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _labelText.raycastTarget = false;
            var labelRt = (RectTransform)_labelText.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(30, 4);
            labelRt.offsetMax = new Vector2(-30, -4);
            MenuButtonStyle.Apply(go.GetComponent<Button>(), primary: false);

            // Popup: a child of the button, so hovering the popup itself keeps it open. It gets
            // its own sorting override (and raycaster) so it always draws above the HUD.
            _popup = new GameObject("FullHistoryPopup", typeof(RectTransform), typeof(Image));
            var popupRt = (RectTransform)_popup.transform;
            popupRt.SetParent(Root, false);
            float ax = popupAlignRight ? 1f : 0f;
            popupRt.anchorMin = new Vector2(ax, 1);
            popupRt.anchorMax = new Vector2(ax, 1);
            popupRt.pivot = new Vector2(ax, 0);
            popupRt.anchoredPosition = new Vector2(0, 6);
            popupRt.sizeDelta = new Vector2(364, 280);
            _popup.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.045f, 0.97f);
            var canvas = _popup.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30;
            _popup.AddComponent<GraphicRaycaster>();

            _full = new ScrollingHistoryColumn(popupRt, popupTitle, host);
            UiFactory.StretchToFillParent(_full.Root);
            _full.Root.offsetMin = new Vector2(8, 8);
            _full.Root.offsetMax = new Vector2(-8, -8);
            _full.HoverOverride = () => _hovered;

            var hover = go.AddComponent<HoverTrigger>();
            hover.OnEnter = () =>
            {
                _hovered = true;
                _popup.SetActive(true);
                _full.ScrollToBottom();
            };
            hover.OnExit = () =>
            {
                _hovered = false;
                _popup.SetActive(false);
            };
            _popup.SetActive(false);
        }

        // revealLie: false hides whether this row was a lie (used for what the OPPONENT told me).
        public void AddRow(int[] guess, JudgeResult reported, bool wasLie, bool revealLie = true)
        {
            _full.AddRow(guess, reported, wasLie, revealLie);
            _records.Add(new HistoryRecord { Guess = guess, Reported = reported, WasLie = wasLie, RevealLie = revealLie });
            _count++;
            RefreshLabel();
        }

        // The last row was a lie that just got exposed (challenged successfully): show the "嘘" badge on it.
        public void MarkLastRowAsLie()
        {
            if (_records.Count == 0) return;
            var last = _records[_records.Count - 1];
            last.WasLie = true;
            last.RevealLie = true;
            last.TruthConfirmed = false;
            _records[_records.Count - 1] = last;
            RebuildFull();
        }

        // The last row's answer was proven true (a wrong accusation, or a successful truth check): show "真実".
        public void MarkLastRowAsTruth()
        {
            if (_records.Count == 0) return;
            var last = _records[_records.Count - 1];
            last.WasLie = false;
            last.TruthConfirmed = true;
            _records[_records.Count - 1] = last;
            RebuildFull();
        }

        private void RebuildFull()
        {
            _full.Clear();
            foreach (var r in _records)
                _full.AddRow(r.Guess, r.Reported, r.WasLie, r.RevealLie, r.TruthConfirmed);
        }

        public void Clear()
        {
            _full.Clear();
            _records.Clear();
            _count = 0;
            RefreshLabel();
        }

        private void RefreshLabel()
        {
            _labelText.text = _count > 0 ? $"{_label} ({_count})" : _label;
        }
    }
}
