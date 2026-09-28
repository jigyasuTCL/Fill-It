using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Shared.GameConfig
{
    // Replaces the old standalone DailyRewardConfig (previously loaded from
    // Resources/daily_reward_config.json via LocalRewardConfigProvider).
    // Now lives on GlobalConfig.DailyReward, alongside the other
    // *ConfigData types (BoosterConfigData, DailyChallengeConfigData,
    // SpinTheWheelConfigData), so it's tunable from the same place as the
    // rest of the game's remote config.
    //
    // Field names are unchanged from the old DailyRewardConfig on purpose,
    // so RewardCycleService / RewardPanelController / DailyRewardManager
    // don't need any call-site changes beyond their type references.
    //
    // RewardItem / SubRewardItem / SubRewardSet / RewardRequireType are
    // still defined in RewardModels.cs (global namespace).
    [Serializable]
    public sealed class DailyRewardConfigData
    {
        // Whole-feature gate. false = daily rewards are always accessible, no
        // level check. true = the player must be at least unlockLevel to
        // access ANY main or sub reward - the whole feature is locked below it.
        [JsonProperty("enableRestriction")]
        public bool enableRestriction = false;

        [JsonProperty("unlockLevel")]
        public int unlockLevel = 0;

        // Main reward cadence.
        [JsonProperty("cooldownMinutes")]
        public float cooldownMinutes = 60f;

        // One entry per cycle. Cycle N uses mainRewards[N % mainRewards.Count].
        [JsonProperty("mainRewards")]
        public List<RewardItem> mainRewards = new List<RewardItem>();

        [JsonProperty("subCooldownMinutes")]
        public float subCooldownMinutes = 60f * 12f;

        [JsonProperty("notificationMinutes")]
        public float NotificationMinutes = 60f * 6f;

        // One SET per cycle. Cycle N uses subRewardSets[N % subRewardSets.Count].
        [JsonProperty("subRewardSets")]
        public List<SubRewardSet> subRewardSets = new List<SubRewardSet>();

        public DailyRewardConfigData CloneNormalized()
        {
            return new DailyRewardConfigData
            {
                enableRestriction = enableRestriction,
                unlockLevel = unlockLevel < 0 ? -1 : Math.Max(0, unlockLevel),
                cooldownMinutes = Math.Max(0f, cooldownMinutes),
                mainRewards = mainRewards ?? new List<RewardItem>(),
                subCooldownMinutes = Math.Max(0f, subCooldownMinutes),
                NotificationMinutes = Math.Max(0f, NotificationMinutes),
                subRewardSets = subRewardSets ?? new List<SubRewardSet>()
            };
        }
    }
}