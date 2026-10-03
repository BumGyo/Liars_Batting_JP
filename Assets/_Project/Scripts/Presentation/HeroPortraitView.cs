using System;
using UnityEngine;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A Hearthstone-style hero badge: circular portrait, name, and the ability
    // icon below it. Used twice per match (mine and the opponent's) -- only
    // "mine" shows remaining ability charges, since how much of a resource the
    // opponent has left is exactly the kind of thing this game hides (same
    // reasoning as the LIE TOKEN count). The ability's name/description isn't
    // printed permanently (that ate the vertical space the match screen needs
    // for history) -- hovering the ability icon shows it instead.
    public class HeroPortraitView
    {
        public readonly RectTransform Root;
        private readonly Image _portraitImage;
        private readonly Image _abilityImage;
        private readonly Text _nameText;
        private readonly Text _chargesText;
        private readonly GameObject _tooltip;
        private readonly Text _tooltipText;
        private readonly bool _showCharges;
        private Button _abilityButton;

        public HeroPortraitView(Transform parent, bool showCharges)
        {
            _showCharges = showCharges;
            Root = UiFactory.VerticalGroup(parent, "HeroPortrait", spacing: 3, childAlign: TextAnchor.UpperCenter);

            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portraitGo.transform.SetParent(Root, false);
            UiFactory.SetSize(portraitGo.transform, 60, 60);
            _portraitImage = portraitGo.GetComponent<Image>();
            _portraitImage.preserveAspect = true;

            _nameText = UiFactory.Text(Root, "", 12, UITheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);

            var abilityGo = new GameObject("Ability", typeof(RectTransform), typeof(Image));
            abilityGo.transform.SetParent(Root, false);
            UiFactory.SetSize(abilityGo.transform, 32, 32);
            _abilityImage = abilityGo.GetComponent<Image>();
            _abilityImage.preserveAspect = true;

            _chargesText = UiFactory.Text(Root, "", 10, UITheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Tooltip: a child of the ability icon itself, so it needs no screen-
            // space math -- just anchor it above the icon and toggle visibility.
            var tooltipGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
            tooltipGo.transform.SetParent(abilityGo.transform, false);
            var tooltipRt = (RectTransform)tooltipGo.transform;
            tooltipRt.anchorMin = new Vector2(0.5f, 1f);
            tooltipRt.anchorMax = new Vector2(0.5f, 1f);
            tooltipRt.pivot = new Vector2(0.5f, 0f);
            tooltipRt.anchoredPosition = new Vector2(0, 8);
            tooltipRt.sizeDelta = new Vector2(190, 64);
            tooltipGo.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f, 0.95f);
            _tooltip = tooltipGo;

            _tooltipText = UiFactory.Text(tooltipRt, "", 11, Color.white, TextAnchor.MiddleCenter);
            var tooltipTextRt = (RectTransform)_tooltipText.transform;
            tooltipTextRt.anchorMin = Vector2.zero;
            tooltipTextRt.anchorMax = Vector2.one;
            tooltipTextRt.offsetMin = new Vector2(8, 6);
            tooltipTextRt.offsetMax = new Vector2(-8, -6);

            var hover = abilityGo.AddComponent<HoverTrigger>();
            hover.OnEnter = () => { _tooltip.transform.SetAsLastSibling(); _tooltip.SetActive(true); };
            hover.OnExit = () => _tooltip.SetActive(false);
            _tooltip.SetActive(false);
        }

        public void SetHero(HeroId hero, int charges = -1)
        {
            var info = HeroCatalog.Get(hero);
            _nameText.text = info.Name;
            _tooltipText.text = $"{info.AbilityName}\n{info.AbilityDescription}";
            _portraitImage.sprite = Resources.Load<Sprite>($"Heroes/Hero_{hero}_Portrait");
            _abilityImage.sprite = Resources.Load<Sprite>($"Heroes/Hero_{hero}_Ability");

            if (_showCharges && info.Charges > 0)
                RefreshCharges(charges, info.Charges);
            else
                _chargesText.text = "";

            // A stale "0 charges left" from a PREVIOUS game's hero must not carry
            // over and leave this game's ability permanently unusable.
            SetAbilityInteractable(charges != 0);
        }

        public void RefreshCharges(int remaining, int max)
        {
            if (_showCharges) _chargesText.text = $"{remaining}/{max}回";
        }

        // Lets the ability icon itself act as the "use ability" button.
        public void SetAbilityClickable(Action onClick)
        {
            _abilityButton = _abilityImage.gameObject.GetComponent<Button>();
            if (_abilityButton == null) _abilityButton = _abilityImage.gameObject.AddComponent<Button>();
            _abilityButton.targetGraphic = _abilityImage;
            _abilityButton.onClick.RemoveAllListeners();
            if (onClick != null) _abilityButton.onClick.AddListener(() => onClick());
        }

        public void SetAbilityInteractable(bool interactable)
        {
            if (_abilityButton != null) _abilityButton.interactable = interactable;
        }
    }
}
