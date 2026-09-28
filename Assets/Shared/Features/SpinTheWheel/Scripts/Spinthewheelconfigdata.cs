using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace Shared.GameConfig
{
    /// <summary>
    /// One "stage" of the wheel's unlock progression � mirrors
    /// Game.SpinWheel.SpinStageConfig, but Newtonsoft-serializable so it can
    /// live directly inside GlobalConfig instead of a separate JSON asset.
    /// Position in the parent list is what actually drives selection
    /// (SpinWheelRemoteConfig.GetStageForSpinIndex indexes by list position);
    /// SpinIndex is kept only as a human-readable label.
    /// </summary>
    [Serializable]
    public sealed class SpinStageConfigData
    {
        [JsonProperty("spinIndex")]
        public int SpinIndex;

        [JsonProperty("winsRequired")]
        public int WinsRequired;

        [JsonProperty("isFree")]
        public bool IsFree;

        [JsonProperty("isCapStage")]
        public bool IsCapStage;

        public SpinStageConfigData CloneNormalized()
        {
            return new SpinStageConfigData
            {
                SpinIndex = Math.Max(1, SpinIndex),
                WinsRequired = Math.Max(0, WinsRequired),
                IsFree = IsFree,
                IsCapStage = IsCapStage
            };
        }
    }

    /// <summary>
    /// One reward entry � mirrors Game.SpinWheel.RewardConfig. Reuses
    /// Game.SpinWheel.RewardType directly (rather than a separate enum) so
    /// there's exactly one definition of "Coins vs Booster" in the codebase.
    /// StringEnumConverter makes the JSON read "Coins"/"Booster" instead of
    /// a raw 0/1.
    /// </summary>
    [Serializable]
    public sealed class RewardConfigData
    {
        [JsonProperty("rewardId")]
        public string RewardId;

        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Game.SpinWheel.RewardType Type = Game.SpinWheel.RewardType.Coins;

        [JsonProperty("amount")]
        public int Amount;

        [JsonProperty("boosterId")]
        public string BoosterId;

        // Currently unused by RewardSelector's positional playback (see
        // Game.SpinWheel.RewardSelector) � kept for a possible future
        // weighted-random mode. Safe to leave at 0 for now.
        [JsonProperty("weight")]
        public float Weight;

        // Key into IconDatabaseConfig for this reward's image. Leave blank to
        // fall back to RewardId as the icon key.
        [JsonProperty("rewardIconKey")]
        public string RewardIconKey;

        public RewardConfigData CloneNormalized()
        {
            return new RewardConfigData
            {
                RewardId = string.IsNullOrWhiteSpace(RewardId) ? "reward" : RewardId,
                Type = Type,
                Amount = Math.Max(0, Amount),
                BoosterId = BoosterId ?? string.Empty,
                Weight = Math.Max(0f, Weight),
                RewardIconKey = RewardIconKey ?? string.Empty
            };
        }
    }

    /// <summary>
    /// A named group of rewards played straight through in order � mirrors
    /// Game.SpinWheel.RewardTable. TableId is a readability label only;
    /// selection is purely by position in the parent RewardTables list.
    /// </summary>
    [Serializable]
    public sealed class RewardTableConfigData
    {
        [JsonProperty("tableId")]
        public string TableId;

        [JsonProperty("rewards")]
        public List<RewardConfigData> Rewards = new List<RewardConfigData>();

        public RewardTableConfigData CloneNormalized()
        {
            var clonedRewards = new List<RewardConfigData>();
            if (Rewards != null)
            {
                foreach (var r in Rewards)
                    clonedRewards.Add((r ?? new RewardConfigData()).CloneNormalized());
            }

            return new RewardTableConfigData
            {
                TableId = string.IsNullOrWhiteSpace(TableId) ? "Table" : TableId,
                Rewards = clonedRewards
            };
        }
    }

    [Serializable]
    public sealed class SpinTheWheelConfigData
    {
        [JsonProperty("enabled")]
        public bool Enabled = true;

        [JsonProperty("unlockLevel")]
        public int UnlockLevel = 1;

        // Stage N = the win requirement to unlock the Nth spin. Stage 1 is
        // normally WinsRequired 0 / IsFree true. Any spin beyond the last
        // stage reuses that stage's WinsRequired forever (mark it
        // IsCapStage for clarity � position in the list, not SpinIndex, is
        // what actually drives this).
        [JsonProperty("stages")]
        public List<SpinStageConfigData> Stages = new List<SpinStageConfigData>();

        // Tables play in order � every reward in table 0, then every reward
        // in table 1, and so on, wrapping back to table 0 once every table
        // has been played through once. See
        // Game.SpinWheel.SpinWheelRemoteConfig.ResolveActiveReward.
        [JsonProperty("rewardTables")]
        public List<RewardTableConfigData> RewardTables = new List<RewardTableConfigData>();

        public SpinTheWheelConfigData CloneNormalized()
        {
            var clonedStages = new List<SpinStageConfigData>();
            if (Stages != null)
            {
                foreach (var s in Stages)
                    clonedStages.Add((s ?? new SpinStageConfigData()).CloneNormalized());
            }

            var clonedTables = new List<RewardTableConfigData>();
            if (RewardTables != null)
            {
                foreach (var t in RewardTables)
                    clonedTables.Add((t ?? new RewardTableConfigData()).CloneNormalized());
            }

            return new SpinTheWheelConfigData
            {
                Enabled = Enabled,
                UnlockLevel = UnlockLevel < 0 ? -1 : Math.Max(0, UnlockLevel),
                Stages = clonedStages,
                RewardTables = clonedTables
            };
        }
    }
}