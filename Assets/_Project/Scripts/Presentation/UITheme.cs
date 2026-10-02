using UnityEngine;

namespace LiarsBatting.Presentation
{
    // Dark underground palette. Bg/Surface carry some transparency so the table
    // background image shows through the panels. Strike/ball/out badges keep
    // their light pastel fills (they read well on dark): strike = yellow,
    // ball = green, out = red.
    public static class UITheme
    {
        public static readonly Color Bg = Hex("0B0B0AB8");
        public static readonly Color Surface = Hex("15130FC8");
        public static readonly Color Surface2 = Hex("2A2620");
        public static readonly Color Border = Hex("4A4236");
        public static readonly Color Ink = Hex("E8DFC8");
        public static readonly Color Muted = Hex("9A8F7A");
        public static readonly Color Accent = Hex("3A7563");
        public static readonly Color Clay = Hex("C46A3A");
        public static readonly Color ClaySoft = Hex("3A2218");

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
