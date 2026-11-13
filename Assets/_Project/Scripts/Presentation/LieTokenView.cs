using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // My remaining LIE TOKENs as silver joker coins; a spent token turns into the dark
    // "used" coin. Only my own count is shown (the opponent's stays hidden by design).
    public class LieTokenView
    {
        public const int MaxTokens = 2;
        public readonly RectTransform Root;
        private readonly Image[] _coins = new Image[MaxTokens];
        private readonly Sprite _full;
        private readonly Sprite _used;

        public LieTokenView(Transform parent)
        {
            _full = Resources.Load<Sprite>("Hud/lie_token_coin");
            _used = Resources.Load<Sprite>("Hud/lie_token_coin_used");
            if (_full == null || _used == null)
                Debug.LogWarning("LieTokenView: Resources/Hud/lie_token_coin(.png / _used.png)が見つかりません");

            Root = UiFactory.VerticalGroup(parent, "LieTokens", spacing: 4, childAlign: TextAnchor.UpperCenter);
            var label = UiFactory.Text(Root, "LIE TOKEN", 11, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.raycastTarget = false;
            UiFactory.SetHeight(label, 16);

            var row = UiFactory.HorizontalGroup(Root, "Coins", spacing: 8, childAlign: TextAnchor.MiddleCenter);
            UiFactory.SetHeight(row, 46);
            for (int i = 0; i < MaxTokens; i++)
            {
                var coin = UiFactory.Panel(row, $"Coin{i}", Color.white);
                UiFactory.SetSize(coin, 46, 46);
                var img = coin.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _coins[i] = img;
            }
            Set(MaxTokens);
        }

        public void Set(int remaining)
        {
            for (int i = 0; i < MaxTokens; i++)
            {
                var sprite = i < remaining ? _full : _used;
                _coins[i].sprite = sprite;
                _coins[i].color = sprite != null ? Color.white : (i < remaining ? UITheme.Muted : UITheme.Border);
            }
        }
    }
}
