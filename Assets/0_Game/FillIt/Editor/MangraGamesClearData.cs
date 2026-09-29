using System.IO;
using Shared.PlayerData;
using UnityEditor;
using UnityEngine;

public static class MangraGamesClearData
{
    [MenuItem("MangraGames/Clear/All")]
    public static void ClearAll()
    {
        ClearPlayerPrefs();
        ClearPlayerData();
        Debug.Log("MangraGames all saved data cleared.");
    }

    [MenuItem("MangraGames/Clear/PlayerPrefs")]
    public static void ClearPlayerPrefs()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("MangraGames PlayerPrefs cleared.");
    }

    [MenuItem("MangraGames/Clear/PlayerData")]
    public static void ClearPlayerData()
    {
        bool playerDataDeleted = DeletePersistentFile(PlayerDataDefaults.FileName);
        bool levelProgressionDeleted = DeletePersistentFile("level-progression.json");
        Debug.Log($"MangraGames player data cleared. {PlayerDataDefaults.FileName} deleted: {playerDataDeleted}. level-progression.json deleted: {levelProgressionDeleted}.");
    }

    private static bool DeletePersistentFile(string fileName)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        return false;
    }
}
