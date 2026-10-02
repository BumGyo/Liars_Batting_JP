#if UNITY_EDITOR
using UnityEditor;

/// Resources/Cards, Resources/Backgrounds 안의 PNG를 가져올 때 자동으로 Sprite 설정을 적용합니다.
public class LiarsBetImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/Cards/") && !assetPath.Contains("/Resources/Backgrounds/")) return;
        var imp = (TextureImporter)assetImporter;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;   // 카드 둥근 모서리 투명 처리
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
#endif
