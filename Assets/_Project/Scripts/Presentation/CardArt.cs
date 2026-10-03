using UnityEngine;

    /// カードと背景のスプライトをResourcesから読み込むヘルパー。
    /// uGUI(Image.sprite)とUI Toolkit(style.backgroundImage)の両方で使用できます。
public static class CardArt
{
    public static Sprite Digit(int n)
    {
        if (n < 0 || n > 9) { Debug.LogWarning($"CardArt：0〜9の範囲外です（{n}）"); return null; }
        return Load($"Cards/card_{n}");
    }
    public static Sprite Back()       => Load("Cards/card_back");
    public static Sprite Unknown()    => Load("Cards/card_unknown");
    public static Sprite Background() => Load("Backgrounds/background_underground");

    static Sprite Load(string path)
    {
        var s = Resources.Load<Sprite>(path);
        if (s == null) Debug.LogWarning("CardArt：スプライトが見つかりません Resources/" + path);
        return s;
    }
}
