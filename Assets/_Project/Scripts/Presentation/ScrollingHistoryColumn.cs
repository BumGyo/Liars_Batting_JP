using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using LiarsBatting.Core;

namespace LiarsBatting.Presentation
{
    // A labeled column that keeps every past turn (no capping) in a real
    // ScrollRect, auto-scrolling to the newest row as it's added.
    public class ScrollingHistoryColumn
    {
        public readonly RectTransform Root;
        private readonly RectTransform _content;
        private readonly RectTransform _viewportRt;
        private readonly ScrollRect _scrollRect;

        // ScrollRect's built-in mouse-wheel handling goes through uGUI's
        // IScrollHandler event, which with the New Input System's
        // InputSystemUIInputModule doesn't reliably reach a runtime-built
        // ScrollRect like this one. Reading the wheel directly here sidesteps
        // that event-routing question entirely -- drag-to-scroll (which uses
        // ScrollRect's native pointer-drag handling, a different code path)
        // still works too.
        public ScrollingHistoryColumn(Transform parent, string label, MonoBehaviour host)
        {
            Root = UiFactory.VerticalGroup(parent, "HistoryColumn", spacing: 6);
            UiFactory.SetFlexible(Root, 1, 1);

            UiFactory.Text(Root, label, 12, UITheme.Muted, TextAnchor.UpperLeft, FontStyle.Bold);

            var scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(Root, false);
            UiFactory.SetFlexible(scrollRt, 1, 1);
            scrollGo.GetComponent<Image>().color = UITheme.Bg;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            _viewportRt = (RectTransform)viewportGo.transform;
            _viewportRt.SetParent(scrollRt, false);
            UiFactory.StretchToFillParent(_viewportRt);
            viewportGo.GetComponent<Image>().color = Color.white; // required as the mask's raycast/graphic source
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            _content = UiFactory.VerticalGroup(_viewportRt, "Content", spacing: 4,
                padding: new RectOffset(4, 4, 4, 4));
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.anchoredPosition = Vector2.zero;
            // With anchors stretched horizontally, sizeDelta.x is an OFFSET from the
            // parent width, not the width itself -- it must be zeroed or Content ends
            // up 100px wider than the viewport (RectTransform's un-set default), which
            // pushes every row's Text out past what the row's own layout expects.
            _content.sizeDelta = Vector2.zero;

            _scrollRect = scrollGo.GetComponent<ScrollRect>();
            _scrollRect.viewport = _viewportRt;
            _scrollRect.content = _content;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;

            host.StartCoroutine(PollScrollWheel());
        }

        private IEnumerator PollScrollWheel()
        {
            while (true)
            {
                yield return null;
                if (Mouse.current == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(
                        _viewportRt, Mouse.current.position.ReadValue(), null))
                    continue;

                float scrollY = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) < 0.01f) continue;

                // A fixed pixel step per notch regardless of the raw delta's
                // magnitude/units (which differ between input backends) --
                // positive scroll (wheel up) reveals earlier rows, which sit
                // ABOVE in this top-anchored, downward-growing content, so it
                // increases verticalNormalizedPosition (1 = top).
                float viewportHeight = _viewportRt.rect.height;
                float contentHeight = _content.rect.height;
                float scrollableRange = contentHeight - viewportHeight;
                if (scrollableRange <= 0f) continue;

                float step = Mathf.Sign(scrollY) * 60f;
                _scrollRect.verticalNormalizedPosition =
                    Mathf.Clamp01(_scrollRect.verticalNormalizedPosition + step / scrollableRange);
            }
        }

        // revealLie: false hides whether this particular row was a lie (used when
        // logging what the OPPONENT told the player, so a bluff stays a bluff).
        public void AddRow(int[] guess, JudgeResult reported, bool wasLie, bool revealLie = true)
        {
            var row = UiFactory.HorizontalGroup(_content, "Row", spacing: 6);
            UiFactory.SetHeight(row, 28);

            var guessText = UiFactory.Text(row, string.Join(" ", guess), 13, UITheme.Ink, TextAnchor.MiddleLeft);
            UiFactory.SetWidth(guessText, 72);

            if (reported.IsOut)
            {
                UiFactory.Badge(row, "OUT", UITheme.OutBg, UITheme.OutFg);
            }
            else
            {
                if (reported.Strike > 0) UiFactory.Badge(row, reported.Strike + "S", UITheme.StrikeBg, UITheme.StrikeFg);
                if (reported.Ball > 0) UiFactory.Badge(row, reported.Ball + "B", UITheme.BallBg, UITheme.BallFg);
            }

            if (wasLie && revealLie)
                UiFactory.Badge(row, "嘘", UITheme.ClaySoft, UITheme.Clay);

            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        public void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);
        }
    }
}
