using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Victory / defeat popup: a heavy black-leather ledger panel with a blind-embossed double border,
    // a muted foil title ("勝利" in aged brass, "敗北" in dull red), a one-line verdict, the revealed
    // secret as cards, and a single button back to the main menu.
    //
    // Entrance (deliberately heavy, no glow or particles): the room dims, the panel sinks into place,
    // the title lands like a stamp (a short thud shakes the panel), the verdict fades in, the cards turn
    // over one by one, and the button appears last. A defeat waits a beat longer and thuds harder.
    // Art lives in Resources/Result: result_panel (9-sliced), result_title_win, result_title_lose.
    public class ResultPopupView
    {
        // ---- timing knobs (seconds / pixels) ----
        private const float DimDuration = 0.5f;
        private const float PanelDelay = 0.1f;
        private const float PanelDuration = 0.5f;
        private const float PanelSinkPixels = 32f;
        private const float StampDelayWin = 0.65f;
        private const float StampDelayLose = 0.95f;       // a defeat lets the silence hang a little longer
        private const float StampDuration = 0.16f;
        private const float ShakeDuration = 0.16f;
        private const float ShakePixelsWin = 3f;
        private const float ShakePixelsLose = 6f;
        private const float MessageFadeDuration = 0.35f;
        private const float CardStep = 0.3f;              // gap between one card turning and the next
        private const float CardFlipDuration = 0.2f;
        private const float ButtonFadeDuration = 0.3f;
        private const float DimAlpha = 0.78f;

        public readonly RectTransform Root;

        private readonly MonoBehaviour _host;
        private readonly Image _dim;
        private readonly RectTransform _panel;
        private readonly CanvasGroup _panelGroup;
        private readonly RectTransform _titleRect;
        private readonly Image _title;
        private readonly CanvasGroup _titleGroup;
        private readonly Text _message;
        private readonly CanvasGroup _messageGroup;
        private readonly Text _summary;
        private readonly RevealedDigitsRow _digits;
        private readonly CanvasGroup _buttonGroup;
        private readonly Sprite _winTitle;
        private readonly Sprite _loseTitle;
        private Coroutine _routine;

        public ResultPopupView(Transform parent, MonoBehaviour host, Action onMainMenu)
        {
            _host = host;
            _winTitle = Resources.Load<Sprite>("Result/result_title_win");
            _loseTitle = Resources.Load<Sprite>("Result/result_title_lose");

            // full-screen dim that also blocks clicks behind the popup
            var rootGo = new GameObject("ResultPopup", typeof(RectTransform), typeof(Image));
            Root = (RectTransform)rootGo.transform;
            Root.SetParent(parent, false);
            UiFactory.StretchToFillParent(Root);
            _dim = rootGo.GetComponent<Image>();
            _dim.color = new Color(0f, 0f, 0f, DimAlpha);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _panel = (RectTransform)panelGo.transform;
            _panel.SetParent(Root, false);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(640f, 470f);
            _panelGroup = panelGo.GetComponent<CanvasGroup>();
            var panelImage = panelGo.GetComponent<Image>();
            var panelSprite = Resources.Load<Sprite>("Result/result_panel");
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning("ResultPopupView: Resources/Result/result_panel を見つけられません");
                panelImage.color = UITheme.Bg;
            }

            var content = UiFactory.VerticalGroup(_panel, "Content", spacing: 8,
                padding: new RectOffset(70, 70, 44, 62), childAlign: TextAnchor.UpperCenter);
            UiFactory.StretchToFillParent(content);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            titleGo.transform.SetParent(content, false);
            UiFactory.SetSize(titleGo.transform, 210, 90);
            _titleRect = (RectTransform)titleGo.transform;
            _title = titleGo.GetComponent<Image>();
            _title.preserveAspect = true;
            _title.raycastTarget = false;
            _titleGroup = titleGo.GetComponent<CanvasGroup>();

            _message = UiFactory.Text(content, "", 22, new Color32(190, 182, 166, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _message.raycastTarget = false;
            UiFactory.SetHeight(_message, 30);
            var shadow = _message.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
            _messageGroup = _message.gameObject.AddComponent<CanvasGroup>();

            _digits = new RevealedDigitsRow(content, 58f, 81f, 12f);
            UiFactory.SetFlexible(_digits.Root, 1, 0);

            // XP / level / challenge summary, filled in by SetSummary once the match is settled.
            _summary = UiFactory.Text(content, "", 14, new Color32(214, 160, 70, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _summary.raycastTarget = false;
            UiFactory.SetHeight(_summary, 58);
            var summaryShadow = _summary.gameObject.AddComponent<Shadow>();
            summaryShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            summaryShadow.effectDistance = new Vector2(1f, -1.5f);

            var buttonRow = UiFactory.HorizontalGroup(content, "ButtonRow", spacing: 0, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(buttonRow, 52);
            UiFactory.SetFlexible(buttonRow, 1, 0);
            _buttonGroup = buttonRow.gameObject.AddComponent<CanvasGroup>();
            var button = UiFactory.Button(buttonRow, "メインメニューへ", UITheme.Accent, Color.white,
                () => onMainMenu?.Invoke(), 17);
            UiFactory.SetSize(button, 220, 48);
            MenuButtonStyle.ApplyChoice(button, UITheme.Accent);

            Root.gameObject.SetActive(false);
        }

        public void SetSummary(string text) => _summary.text = text;

        // digits: the secret to show as cards (null = no cards, e.g. the opponent's secret isn't known online).
        public void Show(bool won, string message, int[] digits)
        {
            if (_routine != null) _host.StopCoroutine(_routine);

            _title.sprite = won ? _winTitle : _loseTitle;
            _title.color = _title.sprite != null ? Color.white : Color.clear;
            _message.text = message;
            _summary.text = "";
            bool hasDigits = digits != null && digits.Length == 4;
            _digits.Root.gameObject.SetActive(hasDigits);

            // starting pose (set before the first frame so nothing flashes)
            _dim.color = new Color(0f, 0f, 0f, 0f);
            _panelGroup.alpha = 0f;
            _panel.anchoredPosition = new Vector2(0f, PanelSinkPixels);
            _titleGroup.alpha = 0f;
            _titleRect.localScale = Vector3.one * 1.3f;
            _messageGroup.alpha = 0f;
            if (hasDigits) _digits.ShowAllUnknown();
            _buttonGroup.alpha = 0f;
            _buttonGroup.interactable = false;
            _buttonGroup.blocksRaycasts = false;

            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            GameAudio.Play("SFX_Result_Dim_Drop");
            _routine = _host.StartCoroutine(PlayIntro(won, hasDigits ? digits : null));
        }

        private IEnumerator PlayIntro(bool won, int[] digits)
        {
            bool hasDigits = digits != null;
            float stampAt = won ? StampDelayWin : StampDelayLose;
            float shakeAmp = won ? ShakePixelsWin : ShakePixelsLose;
            float messageAt = stampAt + 0.45f;
            float cardsAt = messageAt + 0.5f;
            float buttonAt = hasDigits ? cardsAt + 3 * CardStep + CardFlipDuration + 0.1f : cardsAt;
            float total = buttonAt + ButtonFadeDuration;

            float t = 0f;
            bool stampPlayed = false;
            int nextCardSound = 0;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                if (!stampPlayed && t >= stampAt + StampDuration)
                {
                    stampPlayed = true;
                    GameAudio.Play(won ? "SFX_Result_Stamp_Win" : "SFX_Result_Stamp_Lose");
                }
                if (hasDigits && nextCardSound < 4 && t >= cardsAt + nextCardSound * CardStep)
                {
                    GameAudio.Play("SFX_Result_Card_Reveal");
                    nextCardSound++;
                }

                _dim.color = new Color(0f, 0f, 0f, DimAlpha * EaseOut(Seg(t, 0f, DimDuration)));

                float panelP = EaseOut(Seg(t, PanelDelay, PanelDuration));
                float shakeT = t - (stampAt + StampDuration);
                Vector2 shake = (shakeT >= 0f && shakeT < ShakeDuration)
                    ? UnityEngine.Random.insideUnitCircle * shakeAmp * (1f - shakeT / ShakeDuration)
                    : Vector2.zero;
                _panelGroup.alpha = panelP;
                _panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(PanelSinkPixels, 0f, panelP)) + shake;

                float s = Seg(t, stampAt, StampDuration);
                _titleGroup.alpha = Mathf.Clamp01(s * 3f);
                _titleRect.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, s * s);   // ease-in: it accelerates into the panel

                _messageGroup.alpha = Seg(t, messageAt, MessageFadeDuration);

                if (hasDigits)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float f = Seg(t, cardsAt + i * CardStep, CardFlipDuration);
                        var slot = _digits.Slot(i);
                        slot.sprite = f < 0.5f ? CardArt.Unknown() : CardArt.Digit(digits[i]);
                        slot.rectTransform.localScale = new Vector3(Mathf.Abs(1f - 2f * f), 1f, 1f);
                    }
                }

                float b = Seg(t, buttonAt, ButtonFadeDuration);
                _buttonGroup.alpha = b;
                bool clickable = b > 0.5f;
                _buttonGroup.interactable = clickable;
                _buttonGroup.blocksRaycasts = clickable;

                yield return null;
            }

            // final pose
            _dim.color = new Color(0f, 0f, 0f, DimAlpha);
            _panelGroup.alpha = 1f;
            _panel.anchoredPosition = Vector2.zero;
            _titleGroup.alpha = 1f;
            _titleRect.localScale = Vector3.one;
            _messageGroup.alpha = 1f;
            if (hasDigits)
            {
                _digits.ShowDigits(digits);
                for (int i = 0; i < 4; i++) _digits.Slot(i).rectTransform.localScale = Vector3.one;
            }
            _buttonGroup.alpha = 1f;
            _buttonGroup.interactable = true;
            _buttonGroup.blocksRaycasts = true;
            _routine = null;
        }

        private static float Seg(float t, float start, float duration)
            => Mathf.Clamp01((t - start) / duration);

        private static float EaseOut(float p)
        {
            float q = 1f - p;
            return 1f - q * q * q;
        }
    }
}
