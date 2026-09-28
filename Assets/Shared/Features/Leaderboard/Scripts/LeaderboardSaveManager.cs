using Newtonsoft.Json;
using Shared.PlayerData;
using UnityEngine;

// Shared.PlayerData.PlayerData collides with its own namespace - alias it rather than scattering
// fully-qualified names, same problem CatFlapGameplayManager/VibrationManager already hit.
using PlayerDataModel = Shared.PlayerData.PlayerData;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Reads/writes the leaderboard's two persisted values through PlayerData's Variables list,
    /// following the same convention as CatFlapGameProgressSaveManager (string keys + SetInt/
    /// SetString, no new fields on the PlayerData class itself).
    /// </summary>
    public sealed class LeaderboardSaveManager
    {
        private const string ScoreKey = "CatFlap.Leaderboard.Score";
        private const string StateKey = "CatFlap.Leaderboard.State";
        private const string PendingRewardsKey = "CatFlap.Leaderboard.PendingRewards";

        private readonly PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();

        public int LoadPlayerScore()
        {
            PlayerDataModel data = LoadPlayerData();
            return data.TryGetInt(ScoreKey, out int score) ? Mathf.Max(0, score) : 0;
        }

        public void SavePlayerScore(int score)
        {
            PlayerDataModel data = LoadPlayerData();
            data.SetInt(ScoreKey, Mathf.Max(0, score));
            SavePlayerData(data);
        }

        /// <summary>
        /// Returns null when there is nothing usable stored - a first run, an empty value, corrupt
        /// JSON, or state written by a different schema version. The caller regenerates from
        /// scratch in all four cases, same as LoadLoopLevelSequence's own corrupt-JSON fallback.
        /// </summary>
        public LeaderboardStateData LoadState()
        {
            PlayerDataModel data = LoadPlayerData();
            if (!data.TryGetString(StateKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                LeaderboardStateData state = JsonConvert.DeserializeObject<LeaderboardStateData>(json);
                if (state == null || state.Version != LeaderboardStateData.CurrentVersion)
                {
                    return null;
                }

                return state;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public void SaveState(LeaderboardStateData state)
        {
            if (state == null)
            {
                return;
            }

            PlayerDataModel data = LoadPlayerData();
            data.SetString(StateKey, JsonConvert.SerializeObject(state));
            SavePlayerData(data);
        }

        /// <summary>
        /// Never null - nothing pending is a perfectly ordinary state, so every failure path
        /// (first run, empty, corrupt, foreign version) returns an empty record rather than
        /// something the caller has to null-check.
        /// </summary>
        public LeaderboardPendingRewardsData LoadPendingRewards()
        {
            PlayerDataModel data = LoadPlayerData();
            if (!data.TryGetString(PendingRewardsKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return new LeaderboardPendingRewardsData();
            }

            try
            {
                LeaderboardPendingRewardsData pending = JsonConvert.DeserializeObject<LeaderboardPendingRewardsData>(json);
                if (pending == null || pending.Version != LeaderboardPendingRewardsData.CurrentVersion)
                {
                    return new LeaderboardPendingRewardsData();
                }

                if (pending.Rewards == null)
                {
                    pending.Rewards = new System.Collections.Generic.List<LeaderboardRewardData>();
                }

                return pending;
            }
            catch (JsonException)
            {
                return new LeaderboardPendingRewardsData();
            }
        }

        public void SavePendingRewards(LeaderboardPendingRewardsData pending)
        {
            PlayerDataModel data = LoadPlayerData();
            data.SetString(PendingRewardsKey, JsonConvert.SerializeObject(pending ?? new LeaderboardPendingRewardsData()));
            SavePlayerData(data);
        }

        private PlayerDataModel LoadPlayerData()
        {
            if (!PlayerDataRuntimeContext.HasData)
            {
                PlayerDataModel created = saveSystem.LoadOrCreate(PlayerDataDefaults.FileName, 0);
                PlayerDataModel.Init(created, PlayerDataDefaults.FileName);
            }

            return PlayerDataModel.Instance;
        }

        private void SavePlayerData(PlayerDataModel data)
        {
            string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
                ? PlayerDataDefaults.FileName
                : PlayerDataRuntimeContext.FileName;

            saveSystem.Save(fileName, data);
        }
    }
}
