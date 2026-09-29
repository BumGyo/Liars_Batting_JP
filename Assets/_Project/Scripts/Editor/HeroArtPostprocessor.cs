using UnityEditor;
using UnityEngine;

namespace LiarsBatting.EditorTools
{
    // Anything under Resources/Heroes/ is a UI sprite (hero portrait or ability
    // icon) -- set that on import automatically instead of relying on every
    // teammate to remember to flip Texture Type by hand. Lives under Resources/
    // specifically so Resources.Load<Sprite> can find it at runtime.
    public class HeroArtPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Heroes/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
        }
    }
}
