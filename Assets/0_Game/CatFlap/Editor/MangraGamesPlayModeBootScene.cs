using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MangraGamesPlayModeBootScene
{
    private const string BootScenePath = "Assets/0_Game/CatFlap/Scenes/BootScene.unity";

    static MangraGamesPlayModeBootScene()
    {
        ConfigureBootSceneStart();
    }

    [MenuItem("MangraGames/Scene/Use Boot Scene On Play")]
    public static void ConfigureBootSceneStart()
    {
        SceneAsset bootScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath);
        if (bootScene == null)
        {
            EditorSceneManager.playModeStartScene = null;
            Debug.LogWarning($"MangraGames play mode boot scene not found at {BootScenePath}.");
            return;
        }

        EditorSceneManager.playModeStartScene = bootScene;
    }
}
