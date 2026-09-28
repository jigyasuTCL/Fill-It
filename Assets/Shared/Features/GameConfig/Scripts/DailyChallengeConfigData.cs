using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.GameConfig
{
    /// <summary>
    /// One reward tier in the Daily Challenge's cumulative-days-completed progress track -
    /// e.g. "4 days -> 50 coins", "10 days -> 100 coins". Position in the parent list doesn't
    /// drive selection (unlike SpinTheWheel's Stages) - every tier is checked independently by
    /// DaysRequired against DailyChallengeController.GetTotalCompletedDays(), so tiers can be
    /// added/removed/reordered freely.
    /// </summary>
    [Serializable]
    public sealed class DailyChallengeRewardTierConfigData
    {
        [JsonProperty("daysRequired")]
        public int DaysRequired;

        [JsonProperty("rewardCoins")]
        public int RewardCoins;

        // Key into IconDatabaseConfig for this tier's reward icon (collect-screen animation +
        // slot view). Leave blank to fall back to "coin" at the call site.
        [JsonProperty("iconId")]
        public string IconId;

        public DailyChallengeRewardTierConfigData CloneNormalized()
        {
            return new DailyChallengeRewardTierConfigData
            {
                DaysRequired = Math.Max(1, DaysRequired),
                RewardCoins = Math.Max(0, RewardCoins),
                IconId = IconId ?? string.Empty
            };
        }
    }

    [Serializable]
    public sealed class DailyChallengeConfigData
    {
        [JsonProperty("enabled")]
        public bool Enabled = true;

        [JsonProperty("unlockLevel")]
        public int UnlockLevel = 1;

        [JsonProperty("newLevelUnlockHours")]
        public float NewLevelUnlockHours = 24f;

        [JsonProperty("totalLevels")]
        public int TotalLevels = 40;

        // One puzzle per day now (was 3 sub-levels/day) - each DailyChallengeData ends up with a
        // single-entry LevelStates array, so a day's progress is exactly one Win/Lost/NotPlayed
        // value instead of a "x/3" count.
        [JsonProperty("totalSubLevels")]
        public int TotalSubLevels = 1;

        // Coins granted for winning a single Daily Challenge sub-level - separate from the main
        // game's GlobalConfig.LevelWinCoins, and separate from RewardTiers below (which reward
        // cumulative DAYS completed, not individual level wins).
        [JsonProperty("levelWinCoins")]
        public int LevelWinCoins = 10;

        // Cumulative-days-completed reward track - e.g. 4 days -> 50 coins, 10 days -> 100 coins.
        // See DailyChallengeRewardTierConfigData and DailyChallengeController.Rewards.cs.
        [JsonProperty("rewardTiers")]
        public List<DailyChallengeRewardTierConfigData> RewardTiers = new List<DailyChallengeRewardTierConfigData>();

        public DailyChallengeConfigData CloneNormalized()
        {
            var clonedRewardTiers = new List<DailyChallengeRewardTierConfigData>();
            if (RewardTiers != null)
            {
                foreach (var tier in RewardTiers)
                    clonedRewardTiers.Add((tier ?? new DailyChallengeRewardTierConfigData()).CloneNormalized());
            }

            return new DailyChallengeConfigData
            {
                Enabled = Enabled,
                UnlockLevel = Math.Max(0, UnlockLevel),
                NewLevelUnlockHours = Math.Max(0f, NewLevelUnlockHours),
                TotalLevels = Math.Max(1, TotalLevels),
                TotalSubLevels = Math.Max(1, TotalSubLevels),
                LevelWinCoins = Math.Max(0, LevelWinCoins),
                RewardTiers = clonedRewardTiers
            };
        }
    }
   }
