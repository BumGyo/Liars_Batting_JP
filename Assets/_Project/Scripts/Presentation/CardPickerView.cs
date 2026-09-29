using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The 0-9 number-card tray used both for picking your secret at setup and
    // for building a guess during your attack turn. Clicking a tray card COPIES
    // its digit into the first empty slot (the source card just greys out while
    // in use); clicking a filled slot DELETES it and frees the tray card again --
    // exactly the "copy / delete" interaction from the design doc, and it can
    // never produce a duplicate digit because a used tray card can't be clicked.
    public class CardPickerView
    {
        public readonly RectTransform Root;

        private readonly Button[] _trayButtons = new Button[10];
        private readonly Image[] _trayImages = new Image[10];
        private readonly bool[] _used = new bool[10];
        private readonly bool[] _digitDisabled = new bool[10]; // DemonHunter's 0~8 restriction

        private readonly RectTransform[] _slots = new RectTransform[4];
        private readonly Text[] _slotTexts = new Text[4];
        private readonly int[] _slotValues = { -1, -1, -1, -1 };

        private readonly Button _submitButton;
        private readonly Action<int[]> _onSubmit;

        public CardPickerView(Transform parent, string title, string submitLabel, Action<int[]> onSubmit)
        {
            _onSubmit = onSubmit;
            Root = UiFactory.VerticalGroup(parent, "CardPicker", spacing: 12);

            UiFactory.Text(Root, title, 14, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);

            var slotsRow = UiFactory.HorizontalGroup(Root, "Slots", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(slotsRow, 64);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var slotGo = new GameObject($"Slot{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                var slotRt = (RectTransform)slotGo.transform;
                slotRt.SetParent(slotsRow, false);
                UiFactory.SetSize(slotRt, 64, 64);
                var img = slotGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                var btn = slotGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ClearSlot(index));

                var text = UiFactory.Text(slotRt, "", 22, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                var textRt = (RectTransform)text.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;

                _slots[i] = slotRt;
                _slotTexts[i] = text;
            }

            UiFactory.Text(Root, "카드를 클릭해 복사, 채워진 슬롯을 클릭해 삭제합니다.", 12, UITheme.Muted, TextAnchor.UpperLeft);

            var tray = UiFactory.Grid(Root, "Tray", columns: 5, cellSize: 56, spacing: 8);
            UiFactory.SetHeight(tray, 56 * 2 + 8);
            for (int digit = 0; digit < 10; digit++)
            {
                int d = digit;
                var cardGo = new GameObject($"Card{d}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(tray, false);
                var img = cardGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlaceDigit(d));

                var text = UiFactory.Text(cardGo.transform, d.ToString(), 20, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                var textRt = (RectTransform)text.transform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;

                _trayButtons[d] = btn;
                _trayImages[d] = img;
            }

            _submitButton = UiFactory.Button(Root, submitLabel, UITheme.Accent, Color.white, Submit, 16);
            UiFactory.SetHeight(_submitButton, 44);
            RefreshSubmitInteractable();
        }

        private void PlaceDigit(int digit)
        {
            if (_used[digit]) return;
            int emptyIndex = Array.IndexOf(_slotValues, -1);
            if (emptyIndex < 0) return;

            _slotValues[emptyIndex] = digit;
            _slotTexts[emptyIndex].text = digit.ToString();
            _slots[emptyIndex].GetComponent<Image>().color = Color.Lerp(UITheme.Surface2, UITheme.Accent, 0.25f);
            _used[digit] = true;
            _trayImages[digit].color = UITheme.Border;
            RefreshSubmitInteractable();
        }

        private void ClearSlot(int slotIndex)
        {
            int digit = _slotValues[slotIndex];
            if (digit < 0) return;

            _slotValues[slotIndex] = -1;
            _slotTexts[slotIndex].text = "";
            _slots[slotIndex].GetComponent<Image>().color = UITheme.Surface2;
            _used[digit] = false;
            _trayImages[digit].color = UITheme.Surface2;
            RefreshSubmitInteractable();
        }

        private void RefreshSubmitInteractable()
        {
            bool full = Array.IndexOf(_slotValues, -1) < 0;
            _submitButton.interactable = full;
        }

        private void Submit()
        {
            if (Array.IndexOf(_slotValues, -1) >= 0) return;
            var result = new[] { _slotValues[0], _slotValues[1], _slotValues[2], _slotValues[3] };
            ResetAll();
            _onSubmit?.Invoke(result);
        }

        public void ResetAll()
        {
            for (int i = 0; i < 4; i++) ClearSlot(i);
        }

        public void SetInteractable(bool interactable, string disabledHint = null)
        {
            for (int d = 0; d < 10; d++)
                _trayButtons[d].interactable = interactable && !_digitDisabled[d];
            for (int i = 0; i < 4; i++) _slots[i].GetComponent<Button>().interactable = interactable;
            RefreshSubmitInteractable();
            if (!interactable) _submitButton.interactable = false;
        }

        // A permanently (for this game) disabled digit stays disabled even
        // through later SetInteractable(true) calls -- used for DemonHunter's
        // "opponent's pool is 0~8 only" restriction. Pass enabled:true to lift
        // it again (e.g. when a new game against a non-DemonHunter starts).
        public void SetDigitEnabled(int digit, bool enabled)
        {
            _digitDisabled[digit] = !enabled;
            _trayButtons[digit].interactable = enabled && !_used[digit];
            _trayImages[digit].color = enabled ? UITheme.Surface2 : UITheme.Border;
        }
    }
}
