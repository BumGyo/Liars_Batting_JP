#if UNITY_EDITOR
using UnityEditor;

/// Resources/Cards and Resources/Backgrounds内のPNGにSprite設定を自動適用します。
public class LiarsBetImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/Cards/") && !assetPath.Contains("/Resources/Backgrounds/")) return;
        var imp = (TextureImporter)assetImporter;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;   // カードの角を透明として処理
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
#endif
