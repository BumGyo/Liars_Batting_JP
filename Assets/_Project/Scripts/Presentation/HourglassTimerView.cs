using UnityEngine;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    // Hourglass that shows the remaining time: the sand in the top bulb sinks, the pile
    // in the bottom bulb grows, and a thin stream runs between them while time is
    // flowing. Driven by CountdownTimerView (SetRemaining every frame, Idle when it stops).
    // Sprites live in Resources/Hud: frame (glass + wood, drawn on top), sand_top, sand_bottom.
    public class HourglassTimerView
    {
        // Pixel geometry of the 720x1280 hourglass sprites (see the art export).
        private const float CW = 720f, CH = 1280f;
        private const float TopX0 = 124f, TopY0 = 140f, TopX1 = 596f, TopY1 = 640f;
        private const float BotX0 = 124f, BotY0 = 840f, BotX1 = 596f, BotY1 = 1148f;
        private const float NeckY = 640f;

        public readonly RectTransform Root;
        private readonly Image _sandTop;
        private readonly Image _sandBottom;
        private readonly RectTransform _streamRt;
        private readonly GameObject _stream;
        private readonly Text _seconds;

        public HourglassTimerView(Transform parent)
        {
            var go = new GameObject("Hourglass", typeof(RectTransform));
            Root = (RectTransform)go.transform;
            Root.SetParent(parent, false);

            _sandTop = MakeImage("SandTop", "hourglass_sand_top", TopX0, TopY0, TopX1, TopY1, filled: true);
            _sandBottom = MakeImage("SandBottom", "hourglass_sand_bottom", BotX0, BotY0, BotX1, BotY1, filled: true);

            var streamGo = new GameObject("Stream", typeof(RectTransform), typeof(Image));
            _stream = streamGo;
            _streamRt = (RectTransform)streamGo.transform;
            _streamRt.SetParent(Root, false);
            var streamImg = streamGo.GetComponent<Image>();
            streamImg.color = new Color32(230, 190, 110, 235);
            streamImg.raycastTarget = false;

            MakeImage("Frame", "hourglass_frame", 0f, 0f, CW, CH, filled: false); // on top of the sand

            _seconds = UiFactory.Text(Root, "", 26, new Color32(255, 236, 200, 255), TextAnchor.MiddleCenter, FontStyle.Bold);
            _seconds.raycastTarget = false;
            _seconds.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetBox((RectTransform)_seconds.transform, 160f, 230f, 560f, 430f);
            var outline = _seconds.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            Idle();
        }

        // remaining/total in seconds; called every frame while a countdown runs.
        public void SetRemaining(float remaining, float total)
        {
            float f = Mathf.Clamp01(total > 0f ? remaining / total : 0f);
            _sandTop.fillAmount = f;
            _sandBottom.fillAmount = 1f - f;

            bool flowing = f > 0f && f < 1f;
            _stream.SetActive(flowing);
            if (flowing)
            {
                float surface = BotY1 - (BotY1 - BotY0) * (1f - f); // top of the growing pile
                SetBox(_streamRt, 357f, NeckY - 8f, 363f, surface);
            }
            _seconds.text = Mathf.CeilToInt(remaining).ToString();
        }

        // No countdown running: all the sand rests in the top bulb.
        public void Idle()
        {
            _sandTop.fillAmount = 1f;
            _sandBottom.fillAmount = 0f;
            _stream.SetActive(false);
            _seconds.text = "";
        }

        private Image MakeImage(string name, string spriteName, float x0, float y0, float x1, float y1, bool filled)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(Root, false);
            SetBox(rt, x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = Resources.Load<Sprite>("Hud/" + spriteName);
            if (img.sprite == null)
            {
                Debug.LogWarning("HourglassTimerView: スプライトが見つかりません Resources/Hud/" + spriteName);
                img.color = Color.clear;
            }
            if (filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Vertical;
                img.fillOrigin = (int)Image.OriginVertical.Bottom;
                img.fillAmount = 1f;
            }
            return img;
        }

        // Positions a child by a pixel box inside the 720x1280 sprite canvas (y grows downward).
        private static void SetBox(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0 / CW, 1f - y1 / CH);
            rt.anchorMax = new Vector2(x1 / CW, 1f - y0 / CH);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
