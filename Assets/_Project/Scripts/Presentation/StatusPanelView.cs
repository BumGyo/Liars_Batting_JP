using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Left-hand panel: pure information, no decisions happen here anymore (those
    // moved to ChoiceOverlayView). Top to bottom: my own secret in plain view,
    // reveal status for both secrets, then two side-by-side scrolling histories --
    // the opponent's guesses at me on the left, my guesses at them on the right.
    public class StatusPanelView
    {
        public readonly RectTransform Root;
        public readonly RevealedDigitsRow MyRevealed;
        public readonly RevealedDigitsRow OpponentRevealed;
        public readonly ScrollingHistoryColumn OpponentAttackHistory;
        public readonly ScrollingHistoryColumn MyAttackHistory;

        private readonly Image[] _mySecretSlots = new Image[4];

        public StatusPanelView(Transform parent, MonoBehaviour host)
        {
            Root = UiFactory.VerticalGroup(parent, "StatusPanel", spacing: 8,
                padding: new RectOffset(14, 14, 10, 10));
            UiFactory.StretchToFillParent(Root);

            UiFactory.Text(Root, "내 비밀번호", 13, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            var secretRow = UiFactory.HorizontalGroup(Root, "MySecretRow", spacing: 8);
            UiFactory.SetHeight(secretRow, 45);
            for (int i = 0; i < 4; i++)
            {
                var slot = UiFactory.Panel(secretRow, $"MySecret{i}", UITheme.Surface2);
                UiFactory.SetSize(slot, 32, 45);
                var img = slot.GetComponent<Image>();
                img.preserveAspect = true;
                _mySecretSlots[i] = img;
            }

            UiFactory.Text(Root, "공개현황", 13, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            var revealRow = UiFactory.HorizontalGroup(Root, "RevealRow", spacing: 14);

            var myRevealCol = UiFactory.VerticalGroup(revealRow, "MyRevealCol", spacing: 4);
            UiFactory.SetFlexible(myRevealCol, 1, 0);
            UiFactory.Text(myRevealCol, "내 비밀번호", 11, UITheme.Muted, TextAnchor.UpperLeft);
            MyRevealed = new RevealedDigitsRow(myRevealCol);

            var opponentRevealCol = UiFactory.VerticalGroup(revealRow, "OpponentRevealCol", spacing: 4);
            UiFactory.SetFlexible(opponentRevealCol, 1, 0);
            UiFactory.Text(opponentRevealCol, "상대 비밀번호", 11, UITheme.Muted, TextAnchor.UpperLeft);
            OpponentRevealed = new RevealedDigitsRow(opponentRevealCol);

            var historyRow = UiFactory.HorizontalGroup(Root, "HistoryRow", spacing: 14);
            UiFactory.SetFlexible(historyRow, 1, 1);
            OpponentAttackHistory = new ScrollingHistoryColumn(historyRow, "상대의 추측 기록 (내 판정)", host);
            MyAttackHistory = new ScrollingHistoryColumn(historyRow, "나의 추측 기록 (상대 응답)", host);
        }

        public void ShowMySecret(int[] secret)
        {
            for (int i = 0; i < 4; i++)
            {
                _mySecretSlots[i].sprite = CardArt.Digit(secret[i]);
                _mySecretSlots[i].color = Color.white;
            }
        }
    }
}
