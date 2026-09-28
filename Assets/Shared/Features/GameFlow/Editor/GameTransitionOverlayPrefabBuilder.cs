using System.IO;
using Shared.GameFlow;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.GameFlow.Editor
{
    /// <summary>
    /// Bakes GameTransitionOverlay's default runtime-built layout (Canvas + CanvasGroup +
    /// full-screen background Image) into a real prefab under Resources, so it can be opened and
    /// tweaked in the Inspector (swap the background sprite, add extra decoration, retune
    /// hold/fade durations) instead of only existing as code. GameTransitionOverlay.Instance
    /// loads this prefab from Resources when present and falls back to building the same default
    /// layout in code when it's missing, so re-running this is optional, not required.
    /// </summary>
    public static class GameTransitionOverlayPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Shared/Features/GameFlow/Resources/GameFlow";
        private const string PrefabPath = PrefabFolder + "/GameTransitionOverlayRoot.prefab";
        private const string BackgroundSpritePath = "Assets/Shared/Features/GameFlow/Resources/GameFlow/TransitionLoadingScreen.png";

        [MenuItem("MangraGames/Build Game Transition Overlay Prefab")]
        public static void Build()
        {
            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            GameObject root = new GameObject("GameTransitionOverlayRoot");
            try
            {
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue;
                root.AddComponent<CanvasScaler>();
                root.AddComponent<GraphicRaycaster>();

                CanvasGroup canvasGroup = root.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;

                GameObject backgroundGO = new GameObject("Background", typeof(RectTransform));
                backgroundGO.transform.SetParent(root.transform, false);
                RectTransform rect = backgroundGO.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                Image image = backgroundGO.AddComponent<Image>();
                Sprite backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
                if (backgroundSprite != null)
                {
                    image.sprite = backgroundSprite;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                }
                else
                {
                    image.color = Color.black;
                }

                GameTransitionOverlay overlay = root.AddComponent<GameTransitionOverlay>();
                SerializedObject serializedOverlay = new SerializedObject(overlay);
                serializedOverlay.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
                serializedOverlay.ApplyModifiedPropertiesWithoutUndo();

                bool existed = File.Exists(PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"GameTransitionOverlayPrefabBuilder: failed to save prefab at {PrefabPath}.");
                    return;
                }

                Debug.Log(existed
                    ? $"GameTransitionOverlayPrefabBuilder: updated {PrefabPath}."
                    : $"GameTransitionOverlayPrefabBuilder: created {PrefabPath}. Open it to customize the background art, extra decoration, or timings.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
