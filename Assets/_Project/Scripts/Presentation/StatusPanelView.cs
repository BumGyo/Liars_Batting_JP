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

        private readonly Text[] _mySecretSlots = new Text[4];

        public StatusPanelView(Transform parent, MonoBehaviour host)
        {
            Root = UiFactory.VerticalGroup(parent, "StatusPanel", spacing: 14,
                padding: new RectOffset(18, 18, 18, 18));
            UiFactory.StretchToFillParent(Root);

            UiFactory.Text(Root, "내 비밀번호", 13, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            var secretRow = UiFactory.HorizontalGroup(Root, "MySecretRow", spacing: 8);
            UiFactory.SetHeight(secretRow, 48);
            for (int i = 0; i < 4; i++)
            {
                var slot = UiFactory.Panel(secretRow, $"MySecret{i}", UITheme.Accent);
                UiFactory.SetSize(slot, 48, 48);
                var txt = UiFactory.Text(slot, "", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                var trt = (RectTransform)txt.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                _mySecretSlots[i] = txt;
            }

            UiFactory.Text(Root, "공개현황", 13, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            var revealRow = UiFactory.HorizontalGroup(Root, "RevealRow", spacing: 14);

            var myRevealCol = UiFactory.VerticalGroup(revealRow, "MyRevealCol", spacing: 6);
            UiFactory.SetFlexible(myRevealCol, 1, 0);
            UiFactory.Text(myRevealCol, "내 비밀번호", 11, UITheme.Muted, TextAnchor.UpperLeft);
            MyRevealed = new RevealedDigitsRow(myRevealCol);

            var opponentRevealCol = UiFactory.VerticalGroup(revealRow, "OpponentRevealCol", spacing: 6);
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
                _mySecretSlots[i].text = secret[i].ToString();
        }
    }
}
