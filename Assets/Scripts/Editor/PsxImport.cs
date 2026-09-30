using UnityEditor;
using UnityEngine;

namespace RockGame.EditorTools
{
    /// <summary>
    /// Crunchy PSX import settings.
    /// - The alien player model: point-filtered, uncompressed textures, no animation clips (PlayerNet swaps in URP materials
    ///   at runtime). The rigged alien keeps its skin as a Generic rig so the body bends with the bones BodyAnimator moves.
    /// - The PSX graphics trees (Resources/PsxTrees): point-filtered cutout textures, readable meshes (the trunk is measured
    ///   so the weak-spot X sits on it), no imported materials, and the alpha-cutout material they're drawn with.
    /// </summary>
    public class PsxImport : AssetPostprocessor
    {
        const string Folder = "Assets/Game/Resources/Alien/";
        const string Trees = "Assets/Game/Resources/PsxTrees/";
        public const string CutoutPath = Trees + "PsxCutout.mat";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder) && !assetPath.StartsWith(Trees)) return;
            var ti = (TextureImporter)assetImporter;
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            if (assetPath.StartsWith(Trees))
            {
                // leaves are cut out of the alpha; mipmaps keep distant trees from shimmering
                ti.alphaIsTransparency = true;
                ti.isReadable = true; // bark and leaf colours are read off it for the particles
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
            else ti.mipmapEnabled = false;
        }

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(Trees))
            {
                var tm = (ModelImporter)assetImporter;
                tm.importCameras = false;
                tm.importLights = false;
                tm.importAnimation = false;
                tm.animationType = ModelImporterAnimationType.None;
                tm.materialImportMode = ModelImporterMaterialImportMode.None;
                tm.isReadable = true;
                return;
            }
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

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(Trees)) { EnsureCutoutMaterial(); return; }
        }

        /// <summary>URP Lit, alpha clipped, both sides drawn: the foliage cards of the PSX trees (and the pixel X).</summary>
        [MenuItem("Rock Game/Make PSX Cutout Material")]
        public static void EnsureCutoutMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(CutoutPath) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            var m = new Material(shader) { name = "PsxCutout" };
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.doubleSidedGI = true;
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            AssetDatabase.CreateAsset(m, CutoutPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[RockGame] Made " + CutoutPath);
        }
    }
}
