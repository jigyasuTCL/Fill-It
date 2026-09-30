using Shared.Boosters;
using Shared.GameConfig;
using Shared.Leaderboard;
using Shared.PlayerData;
using UnityEngine;

using PlayerDataModel = Shared.PlayerData.PlayerData;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Applies what a leaderboard cycle paid out to the actual game: coins into the session wallet,
    /// boosters into the player's stock.
    ///
    /// This exists because LeaderboardManager deliberately cannot do it. The leaderboard is a Shared
    /// feature and has no business knowing what a Fill It booster or a runtime session is, so it
    /// announces what was won (RewardsClaimed) and this Fill It adapter turns that into game state
    /// - the same split CLAUDE.md describes for every other shared/game pairing.
    ///
    /// Profile rewards are NOT handled here: ProfileManager is Shared too, so the leaderboard grants
    /// those itself before the event fires.
    /// </summary>
    public sealed class FillItLeaderboardRewardApplier : MonoBehaviour
    {
        [SerializeField] private LeaderboardManager leaderboard;
        // Optional. Coins go through the live session when there is one, so the HUD updates and the
        // normal save path runs; without it they fall back to PlayerData directly.
        [SerializeField] private FillItGameplayManager gameplayManager;
        [SerializeField] private string boosterCountKeyPrefix = PlayerDataDefaults.BoosterCountKeyPrefix;

        private readonly PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();

        private void OnEnable()
        {
            if (leaderboard != null)
            {
                leaderboard.RewardsClaimed -= HandleRewardsClaimed;
                leaderboard.RewardsClaimed += HandleRewardsClaimed;
            }
        }

        private void OnDisable()
        {
            if (leaderboard != null)
            {
                leaderboard.RewardsClaimed -= HandleRewardsClaimed;
            }
        }

        private void HandleRewardsClaimed(LeaderboardPendingRewardsData claimed)
        {
            if (claimed == null || claimed.Rewards == null)
            {
                return;
            }

            for (int index = 0; index < claimed.Rewards.Count; index++)
            {
                LeaderboardRewardData reward = claimed.Rewards[index];
                switch (reward.Kind)
                {
                    case LeaderboardRewardKind.Coins:
                        GrantCoins(reward.Amount);
                        break;

                    case LeaderboardRewardKind.Booster:
                        GrantBooster(reward.ItemId, reward.Amount);
                        break;

                    // ProfileAvatar/ProfileFrame are already granted by LeaderboardManager itself -
                    // ProfileManager is Shared, so it needs no game-side translation.
                }
            }
        }

        private void GrantCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            // Through the session when one is live: that is what refreshes the coin HUD and runs
            // the normal persistence, rather than writing a number the running game would overwrite
            // on its next save.
            FillIt.Runtime.FillItRuntimeController controller = gameplayManager != null ? gameplayManager.Controller : null;
            if (controller?.Session != null)
            {
                controller.Session.AddCoins(amount);
                return;
            }

            // No session yet - claiming from the main menu before a level has loaded.
            PlayerDataModel data = LoadPlayerData();
            data.TotalCoins = Mathf.Max(0, data.TotalCoins + amount);
            SavePlayerData(data);
        }

        /// <summary>
        /// Adds to the player's stored booster count, the same key the cheat panel and
        /// FillItGameProgressSaveManager both use, so a granted booster is picked up by the next
        /// wallet load exactly like a purchased one.
        /// </summary>
        private void GrantBooster(string boosterTypeName, int amount)
        {
            if (amount <= 0 || string.IsNullOrWhiteSpace(boosterTypeName))
            {
                return;
            }

            if (!System.Enum.TryParse(boosterTypeName, ignoreCase: true, out BoosterType boosterType))
            {
                Debug.LogWarning($"[Leaderboard] Reward names an unknown booster '{boosterTypeName}' - nothing granted.", this);
                return;
            }

            PlayerDataModel data = LoadPlayerData();
            string key = (boosterCountKeyPrefix ?? PlayerDataDefaults.BoosterCountKeyPrefix) + boosterType;
            data.TryGetInt(key, out int current);
            data.SetInt(key, Mathf.Max(0, current + amount));
            SavePlayerData(data);

            // Also into the live wallet, or the player would not see the new charge until the next
            // level load re-read it from disk.
            FillIt.Runtime.FillItRuntimeController controller = gameplayManager != null ? gameplayManager.Controller : null;
            if (controller?.Session?.BoosterWallet != null)
            {
                controller.Session.BoosterWallet.RestoreCount(boosterType, Mathf.Max(0, current + amount));
            }
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
