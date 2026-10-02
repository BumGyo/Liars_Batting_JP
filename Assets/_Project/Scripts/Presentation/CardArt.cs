using UnityEngine;

/// 카드/배경 스프라이트를 Resources에서 불러오는 헬퍼.
/// uGUI(Image.sprite)와 UI Toolkit(style.backgroundImage) 모두에서 쓸 수 있습니다.
public static class CardArt
{
    public static Sprite Digit(int n)
    {
        if (n < 0 || n > 9) { Debug.LogWarning($"CardArt: 0~9 범위가 아닙니다 ({n})"); return null; }
        return Load($"Cards/card_{n}");
    }
    public static Sprite Back()       => Load("Cards/card_back");
    public static Sprite Unknown()    => Load("Cards/card_unknown");
    public static Sprite Background() => Load("Backgrounds/background_underground");

    static Sprite Load(string path)
    {
        var s = Resources.Load<Sprite>(path);
        if (s == null) Debug.LogWarning("CardArt: 스프라이트를 찾을 수 없음 Resources/" + path);
        return s;
    }
}
