using UnityEditor;
using UnityEditor.SceneManagement;

public static class MangraGamesSceneMenu
{
    private const string BootScenePath = "Assets/0_Game/CatFlap/Scenes/BootScene.unity";
    private const string GamePlayScenePath = "Assets/0_Game/CatFlap/Scenes/GamePlay.unity";

    [MenuItem("MangraGames/Scene/Load Boot")]
    public static void LoadBoot()
    {
        OpenScene(BootScenePath);
    }

    [MenuItem("MangraGames/Scene/Load GamePlay")]
    public static void LoadGamePlay()
    {
        OpenScene(GamePlayScenePath);
    }

    private static void OpenScene(string scenePath)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }
}
