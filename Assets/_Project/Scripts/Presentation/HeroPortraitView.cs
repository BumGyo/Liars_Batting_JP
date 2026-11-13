using System;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;
using LiarsBatting.Progression;

namespace LiarsBatting.Presentation
{
    // A hero badge: the character portrait and name, plus (for MY hero only) one
    // ability button underneath. Hovering the PORTRAIT shows the ability's name and
    // description -- the button is only for using / reading about the ability, it is
    // not where the tooltip lives. The button's meaning depends on the hero (see
    // GameBootstrap.RefreshMyAbilityUI): "能力使用" for active abilities, "パッシブ説明"
    // for passive ones. The opponent's badge has no button, and nothing about how
    // many charges THEY have left is ever shown (that stays hidden, like LIE TOKENs).
    public class HeroPortraitView
    {
        public readonly RectTransform Root;
        private readonly Image _portraitImage;
        private readonly Text _nameText;
        private readonly GameObject _tooltip;
        private readonly Text _tooltipText;
        private readonly Button _abilityButton;
        private readonly Text _abilityButtonText;
        private readonly Outline _frame;
        private readonly Text _subtitleText;
        private Action _onAbilityClick;

        public HeroPortraitView(Transform parent, bool isMine)
        {
            Root = UiFactory.VerticalGroup(parent, "HeroPortrait", spacing: 3, childAlign: TextAnchor.UpperCenter);

            // The art is a 512x620 arch, so keep that aspect.
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portraitGo.transform.SetParent(Root, false);
            UiFactory.SetSize(portraitGo.transform, 76, 92);
            _portraitImage = portraitGo.GetComponent<Image>();
            _portraitImage.preserveAspect = true;
            // Equipped portrait frame (player-level reward); off until SetFrame gives it a colour.
            _frame = portraitGo.AddComponent<Outline>();
            _frame.effectDistance = new Vector2(3f, -3f);
            _frame.enabled = false;

            _nameText = UiFactory.Text(Root, "", 12, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiFactory.SetHeight(_nameText, 16);

            // The player's equipped title under the hero name (mine, the online opponent's, or "AI"
            // for the AI). Hidden while empty so it takes no room. Always ONE line: a long title
            // shrinks to fit instead of wrapping onto (and over) the name.
            _subtitleText = UiFactory.Text(Root, "", 11, new Color32(214, 160, 70, 255), TextAnchor.MiddleCenter, FontStyle.Italic);
            _subtitleText.raycastTarget = false;
            _subtitleText.verticalOverflow = VerticalWrapMode.Truncate;
            _subtitleText.resizeTextForBestFit = true;
            _subtitleText.resizeTextMinSize = 7;
            _subtitleText.resizeTextMaxSize = 11;
            UiFactory.SetHeight(_subtitleText, 14);
            var subtitleShadow = _subtitleText.gameObject.AddComponent<Shadow>();
            subtitleShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            subtitleShadow.effectDistance = new Vector2(1f, -1f);
            _subtitleText.gameObject.SetActive(false);

            if (isMine)
            {
                _abilityButton = UiFactory.Button(Root, "", UITheme.Accent, Color.white, () => _onAbilityClick?.Invoke(), 11);
                UiFactory.SetSize(_abilityButton, 86, 22);
                _abilityButtonText = _abilityButton.GetComponentInChildren<Text>();
                var labelRt = (RectTransform)_abilityButtonText.transform;
                labelRt.offsetMin = new Vector2(2, 0);
                labelRt.offsetMax = new Vector2(-2, 0);
                _abilityButton.gameObject.SetActive(false);
            }

            // Tooltip: a child of the portrait itself, so it needs no screen-space
            // math -- anchored to the portrait's right side (the portraits sit near
            // the top/bottom edges, where "above" or "below" would clip).
            var tooltipGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
            tooltipGo.transform.SetParent(portraitGo.transform, false);
            var tooltipRt = (RectTransform)tooltipGo.transform;
            tooltipRt.anchorMin = new Vector2(1f, 0.5f);
            tooltipRt.anchorMax = new Vector2(1f, 0.5f);
            tooltipRt.pivot = new Vector2(0f, 0.5f);
            tooltipRt.anchoredPosition = new Vector2(8, 0);
            tooltipRt.sizeDelta = new Vector2(200, 84);
            var tooltipImage = tooltipGo.GetComponent<Image>();
            tooltipImage.color = new Color(0.04f, 0.04f, 0.04f, 0.95f);
            tooltipImage.raycastTarget = false;
            _tooltip = tooltipGo;
            var tooltipCanvas = tooltipGo.AddComponent<Canvas>();   // always above the HUD
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = 40;

            _tooltipText = UiFactory.Text(tooltipRt, "", 11, Color.white, TextAnchor.MiddleCenter);
            _tooltipText.raycastTarget = false;
            var tooltipTextRt = (RectTransform)_tooltipText.transform;
            tooltipTextRt.anchorMin = Vector2.zero;
            tooltipTextRt.anchorMax = Vector2.one;
            tooltipTextRt.offsetMin = new Vector2(8, 6);
            tooltipTextRt.offsetMax = new Vector2(-8, -6);

            var hover = portraitGo.AddComponent<HoverTrigger>();
            hover.OnEnter = () => { _tooltip.transform.SetAsLastSibling(); _tooltip.SetActive(true); };
            hover.OnExit = () => _tooltip.SetActive(false);
            _tooltip.SetActive(false);
        }

        public void SetHero(HeroId hero)
        {
            var info = HeroCatalog.Get(hero);
            string kind = info.Charges > 0 ? "アクティブ" : "パッシブ";
            _nameText.text = info.Name;
            _tooltipText.text = $"{info.AbilityName} ({kind})\n{info.AbilityDescription}";
            _portraitImage.sprite = Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");
        }

        // Configures the single ability button under MY portrait. No-op on the opponent's badge.
        public void SetAbilityButton(string label, bool interactable, Action onClick)
        {
            if (_abilityButton == null) return;
            _abilityButton.gameObject.SetActive(true);
            _abilityButtonText.text = label;
            _abilityButton.interactable = interactable;
            _onAbilityClick = onClick;
        }

        // The equipped mastery skin / moving effect for this hero (progress = the player's own,
        // or the online opponent's; null = the plain portrait). Call after SetHero.
        public void SetLook(HeroId hero, PlayerProgress progress) => PortraitLook.Apply(_portraitImage, hero, progress);

        // The equipped portrait frame: its art drawn over the portrait, or (for a frame without
        // art) a plain coloured outline. fallbackColor null = no frame at all.
        public void SetFrame(string frameId, Color? fallbackColor)
        {
            bool hasArt = PortraitFrame.Apply(_portraitImage, frameId);
            _frame.enabled = !hasArt && fallbackColor.HasValue;
            if (_frame.enabled) _frame.effectColor = fallbackColor.Value;
        }

        public void SetSubtitle(string text)
        {
            _subtitleText.text = text ?? "";
            _subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void HideAbilityButton()
        {
            if (_abilityButton != null) _abilityButton.gameObject.SetActive(false);
        }
    }
}
