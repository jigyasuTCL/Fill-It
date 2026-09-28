using System;
using System.Collections.Generic;
using Shared.PlayerData;
using UnityEngine;

// Only job: load/save RewardSaveState. No rules, no timing logic.
// Persisted through the shared PlayerData save file (Shared.PlayerData.PlayerData.Variables)
// instead of PlayerPrefs, so reward progress lives in the same save as the rest of the game.
public class RewardStateStore
{
    private const string SaveKey = "daily_reward_state_v1";
    private readonly PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();

    public RewardSaveState Load()
    {
        PlayerData data = PlayerData.Instance;
        if (!data.TryGetString(SaveKey, out string json) || string.IsNullOrEmpty(json))
        {
            // First ever launch: both cycles ready immediately, no
            // countdowns, starting at the first entry in each list.
            var fresh = new RewardSaveState
            {
                mainReadyAtUnixSeconds = 0,
                mainCycleIndex = 0,
                subReadyAtUnixSeconds = 0,
                subCycleIndex = 0,
                claimedSubRewardIds = new List<string>()
            };
            Save(fresh);
            return fresh;
        }
        return JsonUtility.FromJson<RewardSaveState>(json);
    }

    public void Save(RewardSaveState state)
    {
        PlayerData data = PlayerData.Instance;
        data.SetString(SaveKey, JsonUtility.ToJson(state));
        string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
            ? PlayerDataDefaults.FileName
            : PlayerDataRuntimeContext.FileName;
        saveSystem.Save(fileName, data);
    }
}