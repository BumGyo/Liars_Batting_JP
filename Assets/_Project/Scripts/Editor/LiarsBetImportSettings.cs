#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// Resources/Cards, Backgrounds, Buttons, Hud 内のPNGをインポートする際、自動的にSprite設定を適用します。
public class LiarsBetImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        bool isCard = assetPath.Contains("/Resources/Cards/");
        bool isBackground = assetPath.Contains("/Resources/Backgrounds/");
        bool isButton = assetPath.Contains("/Resources/Buttons/");
        bool isHud = assetPath.Contains("/Resources/Hud/") || assetPath.Contains("/Resources/Logo/");   // same uncompressed full-rect sprite treatment
        bool isChoice = assetPath.Contains("/Resources/ChoiceButtons/");
        bool isResult = assetPath.Contains("/Resources/Result/");
        bool isCosmetic = assetPath.Contains("/Resources/Cosmetics/");   // 報酬画像（フレーム・背景・カードスキン）
        if (!isCard && !isBackground && !isButton && !isHud && !isChoice && !isResult && !isCosmetic) return;

        var imp = (TextureImporter)assetImporter;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;   // 角丸の透過処理
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;

        if (isButton)
        {
            // 9-slice: リベットがある左右端は引き伸ばさず、中央部分のみ引き伸ばします。
            imp.spritePixelsPerUnit = 200;                       // 600x104 -> キャンバス上で 300x52
            imp.spriteBorder = new Vector4(56, 22, 56, 22);      // left, bottom, right, top
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isResult)
        {
            // リザルトポップアップ: レザーパネルは9-slice、タイトル画像はそのまま
            bool panel = assetPath.Contains("result_panel");
            imp.spritePixelsPerUnit = 200;
            imp.spriteBorder = panel ? new Vector4(100, 100, 100, 100) : Vector4.zero;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isChoice)
        {
            // 選択ウィンドウのボタン/ボックス: 9-slice（角は引き伸ばさず、中央のみ引き伸ばす）
            bool panel = assetPath.Contains("choice_panel");
            imp.spritePixelsPerUnit = 200;
            imp.spriteBorder = panel ? new Vector4(40, 40, 40, 40) : new Vector4(26, 26, 26, 26);
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isCosmetic && assetPath.Contains("/frame_"))
        {
            // 肖像画フレーム: 細い金属帯のため、圧縮による劣化を防ぐため非圧縮の矩形メッシュとしてインポート。
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isHud)
        {
            // 砂時計（充填画像）・コイン: 非圧縮、矩形メッシュとしてインポートします。
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else
        {
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
#endif
