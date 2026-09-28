using UnityEditor;
using UnityEngine;

namespace RockGame.EditorTools
{
    /// <summary>Crunchy PSX import settings for the alien player model: point-filtered, uncompressed textures, no rig/animation (PlayerNet swaps in URP materials at runtime).</summary>
    public class PsxImport : AssetPostprocessor
    {
        const string Folder = "Assets/Game/Resources/Alien/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
        }
    }
}
