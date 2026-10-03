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
        private readonly Image[] _slotImages = new Image[4];

        // Card art is 5:7. Tints are multiplied over the white card sprite.
        private const float CardW = 40f, CardH = 56f;
        private static readonly Color TrayUsedTint = new Color(0.30f, 0.30f, 0.30f, 1f);
        private static readonly Color TrayDisabledTint = new Color(0.20f, 0.20f, 0.20f, 1f);
        private readonly int[] _slotValues = { -1, -1, -1, -1 };

        private readonly Button _submitButton;
        private readonly Action<int[]> _onSubmit;

        public CardPickerView(Transform parent, string title, string submitLabel, Action<int[]> onSubmit)
        {
            _onSubmit = onSubmit;
            Root = UiFactory.VerticalGroup(parent, "CardPicker", spacing: 12);

            UiFactory.Text(Root, title, 14, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);

            var slotsRow = UiFactory.HorizontalGroup(Root, "Slots", spacing: 10, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(slotsRow, CardH);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var slotGo = new GameObject($"Slot{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                var slotRt = (RectTransform)slotGo.transform;
                slotRt.SetParent(slotsRow, false);
                UiFactory.SetSize(slotRt, CardW, CardH);
                var img = slotGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                img.preserveAspect = true;
                var btn = slotGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => ClearSlot(index));

                _slots[i] = slotRt;
                _slotImages[i] = img;
            }

            UiFactory.Text(Root, "カードをクリックしてコピーし、埋まったスロットをクリックして削除します。", 12, UITheme.Muted, TextAnchor.UpperLeft);

            var tray = UiFactory.Grid(Root, "Tray", columns: 5, cellSize: 56, spacing: 8);
            tray.GetComponent<GridLayoutGroup>().cellSize = new Vector2(CardW, CardH);
            UiFactory.SetHeight(tray, CardH * 2 + 8);
            for (int digit = 0; digit < 10; digit++)
            {
                int d = digit;
                var cardGo = new GameObject($"Card{d}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(tray, false);
                var img = cardGo.GetComponent<Image>();
                img.sprite = CardArt.Digit(d);
                img.color = Color.white;
                img.preserveAspect = true;
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlaceDigit(d));

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
            _slotImages[emptyIndex].sprite = CardArt.Digit(digit);
            _slotImages[emptyIndex].color = Color.white;
            _used[digit] = true;
            _trayImages[digit].color = TrayUsedTint;
            RefreshSubmitInteractable();
        }

        private void ClearSlot(int slotIndex)
        {
            int digit = _slotValues[slotIndex];
            if (digit < 0) return;

            _slotValues[slotIndex] = -1;
            _slotImages[slotIndex].sprite = null;
            _slotImages[slotIndex].color = UITheme.Surface2;
            _used[digit] = false;
            _trayImages[digit].color = _digitDisabled[digit] ? TrayDisabledTint : Color.white;
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
            _trayImages[digit].color = enabled ? Color.white : TrayDisabledTint;
        }
    }
}
