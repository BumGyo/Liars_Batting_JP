using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Small helpers for building UGUI at runtime with consistent styling, so the
    // whole game can boot from a single script with no hand-wired scene/prefabs.
    // Uses the legacy UI Text component on purpose (not TextMeshPro) -- it needs
    // no "Import TMP Essentials" step, which would otherwise be a silent trap for
    // a runtime-built UI with nothing placed in the Editor to trigger the prompt.
    public static class UiFactory
    {
        private static Font _font;
        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static Canvas CreateCanvas(string name = "GameCanvas")
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            // Picks the right input module for whichever Active Input Handling mode
            // the project is set to (Project Settings > Player), so this works the
            // same whether the New Input System package is active or not.
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform FullScreen(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        // Layout-group containers (VerticalGroup/HorizontalGroup) size themselves
        // to their PREFERRED content size, not their parent -- they only stretch
        // to fill a parent when that parent is itself a LayoutGroup applying
        // childControlWidth/Height. Call this when parenting one directly under a
        // plain Panel/FullScreen rect instead, or it renders as a small centered box.
        public static void StretchToFillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static RectTransform VerticalGroup(Transform parent, string name, float spacing = 8,
            RectOffset padding = null, bool fitHeight = false, TextAnchor childAlign = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;
            layout.childAlignment = childAlign;
            if (fitHeight)
                go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        public static RectTransform HorizontalGroup(Transform parent, string name, float spacing = 8,
            RectOffset padding = null, TextAnchor childAlign = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;
            layout.childAlignment = childAlign;
            return rt;
        }

        public static RectTransform Grid(Transform parent, string name, int columns, float cellSize, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var grid = go.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellSize, cellSize);
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            return rt;
        }

        public static void SetSize(Component target, float width, float height)
        {
            var le = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minWidth = width;
            le.minHeight = height;
        }

        public static void SetHeight(Component target, float height)
        {
            var le = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
        }

        public static void SetWidth(Component target, float width)
        {
            var le = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
        }

        public static void SetFlexible(Component target, float flexWidth = 1, float flexHeight = 0)
        {
            var le = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = flexWidth;
            le.flexibleHeight = flexHeight;
        }

        public static Text Text(Transform parent, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = DefaultFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string label, Color bg, Color fg, Action onClick,
            int fontSize = 15)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = Text(rt, label, fontSize, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            var txtRt = (RectTransform)txt.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(6, 4);
            txtRt.offsetMax = new Vector2(-6, -4);

            // Buttons carry no visible content on their own outer RectTransform
            // (the label is a child), so without this a parent layout group has
            // nothing to size them by and they collapse to zero width.
            SetSize(rt, Mathf.Max(70, label.Length * 9f + 32f), 36);
            return btn;
        }

        public static RectTransform Badge(Transform parent, string label, Color bg, Color fg)
        {
            var panel = Panel(parent, "Badge_" + label, bg);
            SetSize(panel, 46, 26);
            var txt = Text(panel, label, 13, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            var txtRt = (RectTransform)txt.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            return panel;
        }

        public static InputField InputField(Transform parent, string placeholder, int characterLimit = 12)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = UITheme.Surface2;
            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.characterLimit = characterLimit;

            var text = Text(rt, "", 18, UITheme.Ink, TextAnchor.MiddleLeft);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(14, 4);
            textRt.offsetMax = new Vector2(-14, -4);

            var placeholderText = Text(rt, placeholder, 18, UITheme.Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
            var placeholderRt = (RectTransform)placeholderText.transform;
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = new Vector2(14, 4);
            placeholderRt.offsetMax = new Vector2(-14, -4);

            input.textComponent = text;
            input.placeholder = placeholderText;
            SetHeight(rt, 48);
            return input;
        }
    }
}
