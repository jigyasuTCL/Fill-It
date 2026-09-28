using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.GameConfig
{
    /// <summary>
    /// Tuning for the local leaderboard (Assets/Shared/Features/Leaderboard). Content - the rival
    /// name pool - deliberately lives in LeaderboardRivalPoolAsset instead, so designers can edit
    /// names without touching config JSON.
    ///
    /// Note there is no points-per-heart value here: a win is worth exactly the number of hearts
    /// left over, 1:1, by design.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardConfigData
    {
        [JsonProperty("enabled")]
        public bool Enabled = true;

        // Mirrors GlobalConfig.LevelAttemptUnlockLevel - wins below this level score nothing,
        // because hearts are spent but never shown to the player before it (see
        // CatFlapGameplayManager.HandleLevelWon's scoring hook), so the board would be scoring
        // mistakes the player was never told about.
        [JsonProperty("unlockLevel")]
        public int UnlockLevel = 8;

        // Simulated rivals on the board; the player occupies one more row on top of these.
        [JsonProperty("rivalCount")]
        public int RivalCount = 19;

        // Points an average (1.0x) rival earns per real hour - roughly 19/day.
        [JsonProperty("basePointsPerHour")]
        public float BasePointsPerHour = 0.8f;

        // Rivals get an even deterministic spread of rates between these, so the board always has
        // credible runaways and stragglers rather than one uniform pace.
        [JsonProperty("minRateMultiplier")]
        public float MinRateMultiplier = 0.25f;

        [JsonProperty("maxRateMultiplier")]
        public float MaxRateMultiplier = 1.9f;

        // Hard cap on how much rival progress one absence can produce. Without it, a player
        // returning after a holiday finds themselves lapped by thousands of points, which reads as
        // "I can never catch up" rather than "I should play".
        [JsonProperty("maxCatchUpHours")]
        public float MaxCatchUpHours = 48f;

        // Score spread across the whole board on a brand-new install, where the player is on 0.
        // Small on purpose: a few wins should visibly move the player up the board.
        [JsonProperty("firstRunSpreadPoints")]
        public int FirstRunSpreadPoints = 14;

        // How many rivals rubber-band toward the player's score; the rest free-run on fixed rates.
        // Anchoring ALL of them would read as fake immediately - see LeaderboardManager.
        [JsonProperty("anchoredRivalCount")]
        public int AnchoredRivalCount = 10;

        // Length of one leaderboard cycle. At the end of it the board is snapshotted, rewards are
        // worked out from the final placings, and everything resets - see
        // LeaderboardManager.RollCycle.
        [JsonProperty("cycleHours")]
        public float CycleHours = 24f;

        // Quiet gap between one event ending and the next starting. The board freezes on its final
        // standings for this long: scores stop moving, the timer counts down to the new event, and
        // a winner can collect at their leisure instead of having the result thrown at them the
        // instant it happens. Zero runs events back to back, as before.
        [JsonProperty("cooldownHours")]
        public float CooldownHours = 1f;

        // All bot tuning - presets, join curve, rubberbanding, status. Replaces the old constant
        // rate simulation (RivalCount / BasePointsPerHour / the rate multipliers / MaxCatchUpHours /
        // FirstRunSpreadPoints / AnchoredRivalCount), which are now unused and due for removal once
        // nothing references them.
        [JsonProperty("bots")]
        public LeaderboardBotConfigData Bots = new LeaderboardBotConfigData();

        // What each final placement pays out - one entry per rank, each holding every prize that
        // placement gets. Empty means the cycle still resets but pays nothing.
        [JsonProperty("rewards")]
        public List<LeaderboardRewardConfigData> Rewards = DefaultRewards();

        /// <summary>
        /// Top three only, matching the design. A rank with no row here is paid nothing, so adding
        /// a fourth place is one more entry rather than a code change.
        /// </summary>
        private static List<LeaderboardRewardConfigData> DefaultRewards()
        {
            return new List<LeaderboardRewardConfigData>
            {
                RankReward(1, new LeaderboardRewardItemData { Kind = LeaderboardRewardKind.Coins, Amount = 500 }),
                RankReward(2, new LeaderboardRewardItemData { Kind = LeaderboardRewardKind.Coins, Amount = 300 }),
                RankReward(3, new LeaderboardRewardItemData { Kind = LeaderboardRewardKind.Coins, Amount = 150 })
            };
        }

        private static LeaderboardRewardConfigData RankReward(int rank, params LeaderboardRewardItemData[] rewards)
        {
            return new LeaderboardRewardConfigData
            {
                Rank = rank,
                Rewards = new List<LeaderboardRewardItemData>(rewards)
            };
        }

        public LeaderboardConfigData CloneNormalized()
        {
            int rivalCount = Math.Max(0, RivalCount);
            float minRate = Math.Max(0.01f, MinRateMultiplier);

            return new LeaderboardConfigData
            {
                Enabled = Enabled,
                // -1, not 0: the project-wide convention is -1 coming soon / 0 always / positive
                // level gate, and flooring at 0 silently turned "coming soon" into "available to
                // everyone" - the opposite of what was authored.
                UnlockLevel = Math.Max(-1, UnlockLevel),
                RivalCount = rivalCount,
                BasePointsPerHour = Math.Max(0.01f, BasePointsPerHour),
                MinRateMultiplier = minRate,
                // Kept at or above the minimum so the Lerp across rivals can never invert.
                MaxRateMultiplier = Math.Max(minRate, MaxRateMultiplier),
                MaxCatchUpHours = Math.Max(0f, MaxCatchUpHours),
                FirstRunSpreadPoints = Math.Max(0, FirstRunSpreadPoints),
                AnchoredRivalCount = Math.Min(Math.Max(0, AnchoredRivalCount), rivalCount),
                // Floored well above zero: a cycle of 0 would expire on the frame it started
                // and reset the board forever.
                CycleHours = Math.Max(0.1f, CycleHours),
                // Zero is legitimate here, unlike CycleHours - it just means no gap at all.
                CooldownHours = Math.Max(0f, CooldownHours),
                Rewards = NormalizeRewards(Rewards),
                Bots = (Bots ?? new LeaderboardBotConfigData()).CloneNormalized()
            };
        }

        private static List<LeaderboardRewardConfigData> NormalizeRewards(List<LeaderboardRewardConfigData> rewards)
        {
            var normalized = new List<LeaderboardRewardConfigData>();
            if (rewards == null)
            {
                return normalized;
            }

            for (int index = 0; index < rewards.Count; index++)
            {
                if (rewards[index] != null)
                {
                    normalized.Add(rewards[index].CloneNormalized());
                }
            }

            return normalized;
        }
    }
}
