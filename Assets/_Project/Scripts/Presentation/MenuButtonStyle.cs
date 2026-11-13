using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Button looks for the main menu (rusted iron plate, 9-sliced) and for the in-match choice
    // popups (clean charcoal plate with a brass border; Resources/ChoiceButtons).
    // Hover / pressed / disabled are separate sprites (SpriteSwap).
    public static class MenuButtonStyle
    {
        private static readonly Color PrimaryText = new Color32(255, 226, 170, 255);
        private static readonly Color32 LieText = new Color32(255, 184, 132, 255);
        private static readonly Color32 SpecialText = new Color32(255, 226, 150, 255);

        // ---------------- main menu (iron plate) ----------------
        // primary = the highlighted plate with the amber border.
        public static void Apply(Button button, bool primary)
        {
            var normal   = Load("Buttons/", primary ? "btn_primary" : "btn_normal");
            var hover    = Load("Buttons/", primary ? "btn_primary_hover" : "btn_hover");
            var pressed  = Load("Buttons/", primary ? "btn_primary_pressed" : "btn_pressed");
            var disabled = Load("Buttons/", "btn_disabled");
            if (normal == null) return; // sprites missing: leave the default flat style

            SetSprites(button, normal, hover, pressed, disabled);
            Style(button, primary ? PrimaryText : UITheme.Ink);
        }

        // ---------------- choice popups (真実を送信 / LIE TOKEN使用 / ストライク・ボール ...) ----------------
        // semantic = the colour the caller used before: Accent = neutral, Clay = lie / decline,
        // StrikeFg = special (gold), BallFg = green label, OutFg = red label.
        public static void ApplyChoice(Button button, Color semantic)
        {
            string v = "n";
            Color label = UITheme.Ink;
            if (semantic == UITheme.Clay)          { v = "l"; label = LieText; }
            else if (semantic == UITheme.StrikeFg) { v = "s"; label = SpecialText; }
            else if (semantic == UITheme.BallFg)   { label = UITheme.BallBg; }
            else if (semantic == UITheme.OutFg)    { label = UITheme.OutBg; }

            var normal   = Load("ChoiceButtons/", "choice_" + v);
            var hover    = Load("ChoiceButtons/", "choice_" + v + "_hover");
            var pressed  = Load("ChoiceButtons/", "choice_" + v + "_pressed");
            var disabled = Load("ChoiceButtons/", "choice_disabled");
            if (normal == null) { Apply(button, false); return; }

            SetSprites(button, normal, hover, pressed, disabled);
            Style(button, label);
        }

        // The choice popup's message box (9-sliced dark panel with a brass border).
        public static void ApplyPanel(Image panel)
        {
            var sprite = Load("ChoiceButtons/", "choice_panel");
            if (sprite == null) return;
            panel.sprite = sprite;
            panel.type = Image.Type.Sliced;
            panel.color = Color.white;
        }

        private static void SetSprites(Button button, Sprite normal, Sprite hover, Sprite pressed, Sprite disabled)
        {
            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            image.sprite = normal;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,   // don't stay "hover-looking" after a click
                disabledSprite = disabled
            };
        }

        private static void Style(Button button, Color labelColor)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label == null) return;
            label.color = labelColor;
            var shadow = label.GetComponent<Shadow>() ?? label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1.5f, -2f);
        }

        private static Sprite Load(string folder, string name)
        {
            var s = Resources.Load<Sprite>(folder + name);
            if (s == null) Debug.LogWarning("MenuButtonStyle: スプライトが見つかりません Resources/" + folder + name);
            return s;
        }
    }
}
