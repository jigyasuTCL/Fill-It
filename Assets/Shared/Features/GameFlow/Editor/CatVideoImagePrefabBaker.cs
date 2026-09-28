using System.IO;
using Shared.GameFlow;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Shared.GameFlow.Editor
{
    /// <summary>
    /// Permanently authors the Level Win / Out of Lives popups' cat-video setup - the RawImage,
    /// the VideoPlayer, and a real RenderTexture project asset (with the clip/targetTexture
    /// already assigned) - directly into their prefabs, by running
    /// LevelWinPopupController/OutOfLivesPopupController's own BakeCatVideoSetup(RenderTexture)
    /// once, at edit time, on the loaded prefab contents. Nothing about the cat video needs to be
    /// created or configured at runtime any more after this has been run once - EnsureCatVideoPlayer
    /// at runtime is a pure lookup (GetComponent/Find), never a construction step.
    /// </summary>
    public static class CatVideoImagePrefabBaker
    {
        private const string LevelWinPrefabPath = "Assets/Shared/Features/GameFlow/Prefabs/UI/LevelWinUIRoot.prefab";
        private const string OutOfLivesPrefabPath = "Assets/Shared/Features/GameFlow/Prefabs/UI/OutOfLivesRoot.prefab";
        private const string RenderTextureFolder = "Assets/Shared/Features/GameFlow/Art/RenderTextures";

        [MenuItem("MangraGames/Bake Cat Video Image Into Prefabs")]
        public static void BakeAll()
        {
            Bake<LevelWinPopupController>(
                LevelWinPrefabPath,
                RenderTextureFolder + "/LevelWinCatVideoRT.renderTexture",
                (controller, rt) => controller.BakeCatVideoSetup(rt),
                controller => controller.CatOpenVideoClip);

            Bake<OutOfLivesPopupController>(
                OutOfLivesPrefabPath,
                RenderTextureFolder + "/OutOfLivesCatVideoRT.renderTexture",
                (controller, rt) => controller.BakeCatVideoSetup(rt),
                controller => controller.CatOpenVideoClip);
        }

        private static void Bake<T>(
            string prefabPath,
            string renderTexturePath,
            System.Action<T, RenderTexture> bake,
            System.Func<T, VideoClip> getClip) where T : Component
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                T controller = root.GetComponentInChildren<T>(true);
                if (controller == null)
                {
                    Debug.LogError($"CatVideoImagePrefabBaker: no {typeof(T).Name} found in {prefabPath}.");
                    return;
                }

                VideoClip clip = getClip(controller);
                if (clip == null)
                {
                    Debug.LogError($"CatVideoImagePrefabBaker: {typeof(T).Name} on {prefabPath} has no Cat Open Video Clip assigned - assign one before baking.");
                    return;
                }

                RenderTexture renderTexture = CreateOrReplaceRenderTextureAsset(renderTexturePath, clip);
                bake(controller, renderTexture);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"CatVideoImagePrefabBaker: baked the cat video setup into {prefabPath} (RenderTexture: {renderTexturePath}).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static RenderTexture CreateOrReplaceRenderTextureAsset(string assetPath, VideoClip clip)
        {
            string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            int width = clip.width > 0 ? (int)clip.width : 720;
            int height = clip.height > 0 ? (int)clip.height : 720;

            RenderTexture existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(assetPath);
            if (existing != null)
            {
                if (existing.width != width || existing.height != height)
                {
                    existing.Release();
                    existing.width = width;
                    existing.height = height;
                    AssetDatabase.SaveAssets();
                }

                return existing;
            }

            var renderTexture = new RenderTexture(width, height, 0);
            AssetDatabase.CreateAsset(renderTexture, assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<RenderTexture>(assetPath);
        }
    }
}
