using UnityEngine;

namespace LiarsBatting.Presentation
{
    // Same palette as the design doc's HTML mockup, so the in-editor build
    // matches what was agreed on: strike = yellow, ball = green, out = red.
    public static class UITheme
    {
        public static readonly Color Bg = Hex("FFFFFF");
        public static readonly Color Surface = Hex("F2F4F7");
        public static readonly Color Surface2 = Hex("E7EAF0");
        public static readonly Color Border = Hex("D7DBE4");
        public static readonly Color Ink = Hex("191C26");
        public static readonly Color Muted = Hex("5B6072");
        public static readonly Color Accent = Hex("1F3A5F");
        public static readonly Color Clay = Hex("A6552B");
        public static readonly Color ClaySoft = Hex("F3E2D5");

        public static readonly Color StrikeBg = Hex("FCE38A");
        public static readonly Color StrikeFg = Hex("7A5B00");
        public static readonly Color BallBg = Hex("B7E4C7");
        public static readonly Color BallFg = Hex("1B5E3A");
        public static readonly Color OutBg = Hex("F4A8A0");
        public static readonly Color OutFg = Hex("7A1F16");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
