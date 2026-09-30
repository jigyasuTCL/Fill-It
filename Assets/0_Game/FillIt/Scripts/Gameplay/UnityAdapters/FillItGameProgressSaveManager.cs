using System.Collections.Generic;
using Newtonsoft.Json;
using Shared.PlayerData;
using Shared.Boosters;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    public sealed class FillItGameProgressSaveManager : MonoBehaviour
    {
        [SerializeField] private string currentLevelIndexKey = "FillIt.CurrentLevelIndex";
        [SerializeField] private string loopLevelSequenceKey = "FillIt.LoopLevel.Sequence";
        [SerializeField] private string playerDataFileName = PlayerDataDefaults.FileName;
        [SerializeField] private string boosterCountKeyPrefix = PlayerDataDefaults.BoosterCountKeyPrefix;

        private readonly PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();

        public void ApplySaveKey(string saveKey)
        {
            if (!string.IsNullOrWhiteSpace(saveKey))
            {
                currentLevelIndexKey = saveKey;
            }
        }

        public int LoadCurrentLevelIndex(int fallback, int levelCount)
        {
            PlayerData data = LoadPlayerData();
            int savedIndex = data.TryGetInt(currentLevelIndexKey, out int customSavedIndex)
                ? customSavedIndex
                : data.CurrentLevelIndex;

            if (savedIndex < 0)
            {
                savedIndex = fallback;
            }

            if (levelCount <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(savedIndex, 0, levelCount - 1);
        }

        public int LoadTotalCoins(int fallback)
        {
            PlayerData data = LoadPlayerData(fallback);
            return data == null ? Mathf.Max(0, fallback) : Mathf.Max(0, data.TotalCoins);
        }

        public void SaveCurrentLevelIndex(int currentLevelIndex)
        {
            PlayerData data = LoadPlayerData();
            ApplyLevelIndex(data, currentLevelIndex);
            SavePlayerData(data);
        }

        public void SaveProgress(int currentLevelIndex, BoosterWallet wallet)
        {
            PlayerData data = LoadPlayerData();
            ApplyLevelIndex(data, currentLevelIndex);
            if (wallet != null)
            {
                data.TotalCoins = Mathf.Max(0, wallet.CurrentCoins);
                SaveBoosterCounts(data, wallet);
            }

            SavePlayerData(data);
        }

        public void ApplySavedBoosterState(BoosterWallet wallet)
        {
            if (wallet == null)
            {
                return;
            }

            PlayerData data = LoadPlayerData();
            foreach (BoosterType boosterType in System.Enum.GetValues(typeof(BoosterType)))
            {
                if (data.TryGetInt(CreateBoosterCountKey(boosterType), out int count))
                {
                    wallet.RestoreCount(boosterType, count);
                }
            }
        }

        // Loop-level sequence: the concrete, already-shuffled level numbers resolved so far for
        // loop content past GlobalConfig.TotalLevels (see FillItLoopLevelSequencer/
        // FillItGameplayManager.ResolveLoopLevelNumber). Persisted as plain JSON so re-entering
        // the same loop index always resolves to the exact same level instead of reshuffling.
        public List<int> LoadLoopLevelSequence()
        {
            PlayerData data = LoadPlayerData();
            if (!data.TryGetString(loopLevelSequenceKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return new List<int>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<int>>(json) ?? new List<int>();
            }
            catch (JsonException)
            {
                return new List<int>();
            }
        }

        public void SaveLoopLevelSequence(List<int> sequence)
        {
            PlayerData data = LoadPlayerData();
            data.SetString(loopLevelSequenceKey, JsonConvert.SerializeObject(sequence ?? new List<int>()));
            SavePlayerData(data);
        }

        public void Clear()
        {
            PlayerData data = LoadPlayerData();
            data.CurrentLevelIndex = 0;
            data.SetInt(currentLevelIndexKey, 0);
            data.SetString(loopLevelSequenceKey, string.Empty);
            SavePlayerData(data);
        }

        private PlayerData LoadPlayerData(int initialCoins = 0)
        {
            if (!PlayerDataRuntimeContext.HasData)
            {
                PlayerData data = saveSystem.LoadOrCreate(playerDataFileName, Mathf.Max(0, initialCoins));
                PlayerData.Init(data, playerDataFileName);
            }

            return PlayerData.Instance;
        }

        private void SavePlayerData(PlayerData data)
        {
            string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
                ? playerDataFileName
                : PlayerDataRuntimeContext.FileName;
            saveSystem.Save(fileName, data);
        }

        private void ApplyLevelIndex(PlayerData data, int currentLevelIndex)
        {
            data.CurrentLevelIndex = Mathf.Max(0, currentLevelIndex);
            data.SetInt(currentLevelIndexKey, data.CurrentLevelIndex);
        }

        private void SaveBoosterCounts(PlayerData data, BoosterWallet wallet)
        {
            foreach (BoosterType boosterType in System.Enum.GetValues(typeof(BoosterType)))
            {
                data.SetInt(CreateBoosterCountKey(boosterType), wallet.GetCount(boosterType));
            }
        }

        private string CreateBoosterCountKey(BoosterType boosterType)
        {
            return boosterCountKeyPrefix + boosterType;
        }
    }
}
