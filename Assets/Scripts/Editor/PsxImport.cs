using UnityEditor;
using UnityEngine;

namespace RockGame.EditorTools
{
    /// <summary>
    /// Crunchy PSX import settings for the alien player model: point-filtered, uncompressed textures, no animation clips
    /// (PlayerNet swaps in URP materials at runtime). The rigged alien keeps its skin as a Generic rig so the body bends
    /// with the bones BodyAnimator moves (with no rig the skin is dropped and the mesh never deforms).
    /// </summary>
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
            bool rigged = assetPath.Contains("Rigged");
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (rigged)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                mi.optimizeGameObjects = false; // BodyAnimator needs the bone transforms
            }
        }
    }
}
