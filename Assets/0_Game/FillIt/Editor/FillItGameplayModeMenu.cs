using Shared.GameConfig;
using UnityEditor;
using UnityEngine;

namespace FillIt.EditorTools
{
    /// <summary>
    /// Editor shortcut for GlobalConfig.GameplayMode (Hearts / Timer) - the same value the
    /// GlobalConfig asset's Inspector and remote config JSON ("gameplayMode") control.
    /// </summary>
    public static class FillItGameplayModeMenu
    {
        private const string GlobalConfigPath = "Assets/Resources/Configs/GlobalConfig.asset";
        private const string HeartsMenu = "MangraGames/Fill It/Gameplay Mode/Hearts";
        private const string TimerMenu = "MangraGames/Fill It/Gameplay Mode/Timer";

        [MenuItem(HeartsMenu)]
        private static void UseHearts()
        {
            SetMode(GameplayMode.Hearts);
        }

        [MenuItem(TimerMenu)]
        private static void UseTimer()
        {
            SetMode(GameplayMode.Timer);
        }

        [MenuItem(HeartsMenu, true)]
        private static bool ValidateHearts()
        {
            Menu.SetChecked(HeartsMenu, GetMode() == GameplayMode.Hearts);
            return true;
        }

        [MenuItem(TimerMenu, true)]
        private static bool ValidateTimer()
        {
            Menu.SetChecked(TimerMenu, GetMode() == GameplayMode.Timer);
            return true;
        }

        public static void SetMode(GameplayMode mode)
        {
            GlobalConfigAsset asset = AssetDatabase.LoadAssetAtPath<GlobalConfigAsset>(GlobalConfigPath);
            if (asset == null)
            {
                Debug.LogError($"[FillIt] GlobalConfig asset not found at {GlobalConfigPath}.");
                return;
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty property = serialized.FindProperty("config.GameplayMode");
            property.enumValueIndex = (int)mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FillIt] Gameplay mode set to {mode} (GlobalConfig.asset). Applies from the next level load.");
        }

        private static GameplayMode GetMode()
        {
            GlobalConfigAsset asset = AssetDatabase.LoadAssetAtPath<GlobalConfigAsset>(GlobalConfigPath);
            return asset != null ? asset.ToConfig().GameplayMode : GameplayMode.Hearts;
        }
    }
}
