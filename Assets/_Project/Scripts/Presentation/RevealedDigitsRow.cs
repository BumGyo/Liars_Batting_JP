using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // A row of 4 boxes showing "?" for a still-hidden digit and the actual digit
    // once a trust/challenge call forces that position open.
    public class RevealedDigitsRow
    {
        private readonly Text[] _slots = new Text[4];

        public RevealedDigitsRow(Transform parent)
        {
            var row = UiFactory.HorizontalGroup(parent, "RevealedRow", spacing: 8);
            UiFactory.SetHeight(row, 36);
            for (int i = 0; i < 4; i++)
            {
                var slot = UiFactory.Panel(row, $"Rev{i}", UITheme.Surface2);
                UiFactory.SetSize(slot, 36, 36);
                var txt = UiFactory.Text(slot, "?", 16, UITheme.Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
                var trt = (RectTransform)txt.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                _slots[i] = txt;
            }
        }

        public void Refresh(int[] secret, bool[] revealed)
        {
            for (int i = 0; i < 4; i++)
            {
                bool isOpen = revealed[i];
                _slots[i].text = isOpen ? secret[i].ToString() : "?";
                _slots[i].color = isOpen ? UITheme.Clay : UITheme.Muted;
            }
        }
    }
}
