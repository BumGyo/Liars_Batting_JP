using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // The 0-9 number-card tray used both for picking your secret at setup and for building
    // a guess during your attack turn. Clicking a tray card COPIES its digit into the first
    // empty slot (the source card just greys out while in use); clicking a filled slot, or
    // the greyed-out tray card itself, DELETES it and frees the tray card again -- it can
    // never produce a duplicate digit because a used tray card only ever takes itself back.
    //
    // Two layouts: the classic stacked one (setup screen) and a split one for the match
    // HUD where the 4 slots sit in the middle of the screen and the tray + submit button sit
    // in the right column.
    public class CardPickerView
    {
        public readonly RectTransform Root;

        private readonly Button[] _trayButtons = new Button[10];
        private readonly Image[] _trayImages = new Image[10];
        private readonly bool[] _used = new bool[10];
        private readonly bool[] _digitDisabled = new bool[10]; // DemonHunter's 0~8 restriction

        private readonly RectTransform[] _slots = new RectTransform[4];
        private readonly Image[] _slotImages = new Image[4];
        private readonly int[] _slotValues = { -1, -1, -1, -1 };

        private readonly Button _submitButton;
        private readonly Action<int[]> _onSubmit;
        private readonly CardFlightAnimator _flightAnimator;
        private readonly bool _isAttackPicker;

        private static readonly Color TrayUsedTint = new Color(0.30f, 0.30f, 0.30f, 1f);
        private static readonly Color TrayDisabledTint = new Color(0.20f, 0.20f, 0.20f, 1f);

        // Classic stacked layout (secret-pick screen): centred, bigger 5:7 cards.
        public CardPickerView(Transform parent, string title, string submitLabel, Action<int[]> onSubmit)
        {
            _onSubmit = onSubmit;
            _flightAnimator = GetFlightAnimator(parent);
            var slotCard = new Vector2(64f, 90f);
            var trayCard = new Vector2(56f, 78f);
            Root = UiFactory.VerticalGroup(parent, "CardPicker", spacing: 14, childAlign: TextAnchor.UpperCenter);

            UiFactory.Text(Root, title, 15, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            var slotsRow = BuildSlots(Root, slotCard, 14f);
            UiFactory.SetHeight(slotsRow, slotCard.y);
            UiFactory.SetFlexible(slotsRow, 1, 0);   // otherwise the row soaks up spare height and the slots turn into tall bars

            UiFactory.Text(Root, "カードをクリックして配置、配置済みスロットや使用中のカードを再度クリックして解除します。", 12, UITheme.Muted, TextAnchor.MiddleCenter);
            BuildTray(Root, trayCard, 10f);

            var submitRow = UiFactory.HorizontalGroup(Root, "SubmitRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(submitRow, 52);
            UiFactory.SetFlexible(submitRow, 1, 0);
            _submitButton = UiFactory.Button(submitRow, submitLabel, UITheme.Accent, Color.white, Submit, 17);
            UiFactory.SetSize(_submitButton, 300, 52);
            MenuButtonStyle.ApplyChoice(_submitButton, UITheme.Accent);
            RefreshSubmitInteractable();
        }

        // Split layout for the match HUD: slots go into slotsParent, tray + submit into trayParent.
        public CardPickerView(Transform slotsParent, Transform trayParent, string submitLabel,
            Action<int[]> onSubmit, Vector2 slotCard, Vector2 trayCard)
        {
            _onSubmit = onSubmit;
            _isAttackPicker = true;
            _flightAnimator = GetFlightAnimator(slotsParent);

            var slotsRow = BuildSlots(slotsParent, slotCard, 16f);
            UiFactory.StretchToFillParent(slotsRow);

            Root = UiFactory.VerticalGroup(trayParent, "TrayColumn", spacing: 8, childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(Root);
            var hint = UiFactory.Text(Root, "カードをクリックして配置、再クリックで解除", 12, UITheme.Muted, TextAnchor.MiddleLeft);
            hint.raycastTarget = false;
            UiFactory.SetHeight(hint, 16);
            BuildTray(Root, trayCard, 8f);

            _submitButton = UiFactory.Button(Root, submitLabel, UITheme.Accent, Color.white, Submit, 16);
            UiFactory.SetHeight(_submitButton, 40);
            MenuButtonStyle.ApplyChoice(_submitButton, UITheme.Accent);
            RefreshSubmitInteractable();
        }

        private RectTransform BuildSlots(Transform parent, Vector2 card, float spacing)
        {
            var row = UiFactory.HorizontalGroup(parent, "Slots", spacing: spacing, childAlign: TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var slotGo = new GameObject($"Slot{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                var slotRt = (RectTransform)slotGo.transform;
                slotRt.SetParent(row, false);
                UiFactory.SetSize(slotRt, card.x, card.y);
                var img = slotGo.GetComponent<Image>();
                img.color = UITheme.Surface2;
                img.preserveAspect = true;
                var btn = slotGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() =>
                {
                    if (_slotValues[index] >= 0) GameAudio.Play("SFX_Card_Deselect");
                    ClearSlot(index);
                });
                slotGo.AddComponent<ButtonHoverSound>().ClipName = "SFX_Card_Hover";

                _slots[i] = slotRt;
                _slotImages[i] = img;
            }
            return row;
        }

        private RectTransform BuildTray(Transform parent, Vector2 card, float spacing)
        {
            var tray = UiFactory.Grid(parent, "Tray", columns: 5, cellSize: card.x, spacing: spacing);
            var grid = tray.GetComponent<GridLayoutGroup>();
            grid.cellSize = card;
            grid.childAlignment = TextAnchor.MiddleCenter;
            UiFactory.SetHeight(tray, card.y * 2 + spacing);

            for (int digit = 0; digit < 10; digit++)
            {
                int d = digit;
                var cardGo = new GameObject($"Card{d}", typeof(RectTransform), typeof(Image), typeof(Button));
                cardGo.transform.SetParent(tray, false);
                var img = cardGo.GetComponent<Image>();
                img.sprite = CardArt.Digit(d);
                img.color = CardArt.Tint;
                img.preserveAspect = true;
                var btn = cardGo.GetComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => PlaceDigit(d));
                cardGo.AddComponent<ButtonHoverSound>().ClipName = "SFX_Card_Hover";

                _trayButtons[d] = btn;
                _trayImages[d] = img;
            }
            return tray;
        }

        private void PlaceDigit(int digit)
        {
            if (_used[digit])
            {
                // Clicking a greyed-out (already placed) tray card takes it back, same as
                // clicking the slot it sits in.
                int slotIndex = Array.IndexOf(_slotValues, digit);
                if (slotIndex >= 0)
                {
                    GameAudio.Play("SFX_Card_Deselect");
                    ClearSlot(slotIndex);
                }
                return;
            }
            int emptyIndex = Array.IndexOf(_slotValues, -1);
            if (emptyIndex < 0) return;

            GameAudio.Play("SFX_Card_Select");
            _slotValues[emptyIndex] = digit;
            _slotImages[emptyIndex].sprite = CardArt.Digit(digit);
            _slotImages[emptyIndex].color = CardArt.Tint;
            if (_flightAnimator != null)
                _flightAnimator.Play(_trayImages[digit], _slots[emptyIndex], _slotImages[emptyIndex]);
            _used[digit] = true;
            _trayImages[digit].color = TrayUsedTint;
            RefreshSubmitInteractable();
        }

        private static CardFlightAnimator GetFlightAnimator(Transform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            var animator = canvas.GetComponent<CardFlightAnimator>();
            return animator != null ? animator : canvas.gameObject.AddComponent<CardFlightAnimator>();
        }

        // Draws a temporary copy above the UI and moves it from the tray card to its slot.
        private sealed class CardFlightAnimator : MonoBehaviour
        {
            private const float Duration = 0.28f;

            public void Play(Image source, RectTransform destination, Image destinationImage)
            {
                var canvas = GetComponent<Canvas>().rootCanvas;
                var flightObject = new GameObject("CardFlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var flightRect = (RectTransform)flightObject.transform;
                flightRect.SetParent(canvas.transform, false);
                flightRect.SetAsLastSibling();
                var flightImage = flightObject.GetComponent<Image>();
                flightImage.sprite = source.sprite;
                flightImage.preserveAspect = true;
                flightImage.raycastTarget = false;
                var sourceRect = (RectTransform)source.transform;
                var sourceScreen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, sourceRect.TransformPoint(sourceRect.rect.center));
                var destinationScreen = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, destination.TransformPoint(destination.rect.center));
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, sourceScreen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var start);
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, destinationScreen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var end);
                flightRect.anchoredPosition = start;
                flightRect.sizeDelta = sourceRect.rect.size;
                destinationImage.canvasRenderer.SetAlpha(0f);
                StartCoroutine(Animate(flightRect, flightObject, destinationImage, start, end));
            }

            private static IEnumerator Animate(RectTransform rect, GameObject flightObject, Image destinationImage, Vector2 start, Vector2 end)
            {
                float elapsed = 0f;
                while (elapsed < Duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / Duration);
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    rect.anchoredPosition = Vector2.LerpUnclamped(start, end, eased);
                    rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, eased);
                    yield return null;
                }
                if (destinationImage != null) destinationImage.canvasRenderer.SetAlpha(1f);
                Destroy(flightObject);
            }
        }

        private void ClearSlot(int slotIndex)
        {
            int digit = _slotValues[slotIndex];
            if (digit < 0) return;

            if (_flightAnimator != null)
                _flightAnimator.Play(_slotImages[slotIndex], (RectTransform)_trayImages[digit].transform, _trayImages[digit]);

            _slotValues[slotIndex] = -1;
            _slotImages[slotIndex].sprite = null;
            _slotImages[slotIndex].color = UITheme.Surface2;
            _used[digit] = false;
            _trayImages[digit].color = _digitDisabled[digit] ? TrayDisabledTint : CardArt.Tint;
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
            if (_isAttackPicker) GameAudio.Play("SFX_Guess_Submit");
            var result = new[] { _slotValues[0], _slotValues[1], _slotValues[2], _slotValues[3] };
            ResetAll();
            _onSubmit?.Invoke(result);
        }

        // Re-applies the card skin (art and tint) after it changes.
        public void RefreshTint()
        {
            for (int d = 0; d < 10; d++)
            {
                _trayImages[d].sprite = CardArt.Digit(d);
                if (!_used[d] && !_digitDisabled[d]) _trayImages[d].color = CardArt.Tint;
            }
            for (int i = 0; i < 4; i++)
                if (_slotValues[i] >= 0)
                {
                    _slotImages[i].sprite = CardArt.Digit(_slotValues[i]);
                    _slotImages[i].color = CardArt.Tint;
                }
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

        // A permanently (for this game) disabled digit stays disabled even through later
        // SetInteractable(true) calls -- used for DemonHunter's "opponent's pool is 0~8 only"
        // restriction. Pass enabled:true to lift it again.
        public void SetDigitEnabled(int digit, bool enabled)
        {
            _digitDisabled[digit] = !enabled;
            _trayButtons[digit].interactable = enabled && !_used[digit];
            _trayImages[digit].color = enabled ? (_used[digit] ? TrayUsedTint : CardArt.Tint) : TrayDisabledTint;
        }
    }
}
