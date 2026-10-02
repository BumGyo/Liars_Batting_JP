using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // A row of 4 cards showing the hidden "?" card for a still-hidden digit and
    // the actual digit card once a trust/challenge call forces that position open.
    public class RevealedDigitsRow
    {
        private readonly Image[] _slots = new Image[4];

        public RevealedDigitsRow(Transform parent)
        {
            var row = UiFactory.HorizontalGroup(parent, "RevealedRow", spacing: 8);
            UiFactory.SetHeight(row, 39);
            for (int i = 0; i < 4; i++)
            {
                var slot = UiFactory.Panel(row, $"Rev{i}", Color.white);
                UiFactory.SetSize(slot, 28, 39);
                var img = slot.GetComponent<Image>();
                img.sprite = CardArt.Unknown();
                img.preserveAspect = true;
                _slots[i] = img;
            }
        }

        public void Refresh(int[] secret, bool[] revealed)
        {
            for (int i = 0; i < 4; i++)
                _slots[i].sprite = revealed[i] ? CardArt.Digit(secret[i]) : CardArt.Unknown();
        }
    }
}
