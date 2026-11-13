using UnityEngine;

/// カード/背景スプライトをResourcesから読み込むヘルパー。
/// uGUI(Image.sprite)とUI Toolkit(style.backgroundImage)の両方で使用できます。
///
/// カードスキン（報酬）を装備すると Resources/Cosmetics/<スキンid>/card_N 画像を代わりに使用します。
/// 画像がないスキンはTint色調のみで表現します。
public static class CardArt
{
    // The equipped card skin's tint, used only when that skin has no art of its own. Every view
    // that shows a number card multiplies its card image by this.
    public static Color Tint = Color.white;

    static string _skinFolder;   // "Cosmetics/<skin id>" when the equipped skin has art, else null

    public static void SetSkin(string skinId, Color fallbackTint)
    {
        _skinFolder = null;
        Tint = fallbackTint;
        if (string.IsNullOrEmpty(skinId)) return;
        string folder = $"Cosmetics/{skinId}";
        if (Resources.Load<Sprite>($"{folder}/card_0") == null) return;
        _skinFolder = folder;
        Tint = Color.white;
    }

    public static Sprite Digit(int n)
    {
        if (n < 0 || n > 9) { Debug.LogWarning($"CardArt: 0〜9の範囲外です ({n})"); return null; }
        return Card($"card_{n}");
    }
    public static Sprite Back()       => Card("card_back");
    public static Sprite Unknown()    => Card("card_unknown");
    public static Sprite Background() => Load("Backgrounds/background_underground");

    // A background skin's art (Resources/Cosmetics/<id>), or null when it has none.
    public static Sprite BackgroundSkin(string skinId) =>
        string.IsNullOrEmpty(skinId) ? null : Resources.Load<Sprite>($"Cosmetics/{skinId}");

    static Sprite Card(string name)
    {
        if (_skinFolder != null)
        {
            var skinned = Resources.Load<Sprite>($"{_skinFolder}/{name}");
            if (skinned != null) return skinned;
        }
        return Load($"Cards/{name}");
    }

    static Sprite Load(string path)
    {
        var s = Resources.Load<Sprite>(path);
        if (s == null) Debug.LogWarning("CardArt: スプライトが見つかりません Resources/" + path);
        return s;
    }
}
