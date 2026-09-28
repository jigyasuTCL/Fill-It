using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.GameConfig
{
    /// <summary>
    /// What a leaderboard placement pays out. Deliberately not an enum of concrete game systems -
    /// the leaderboard is a Shared feature and must not know what a Cat Flap booster is, so it
    /// names a KIND and lets the game-side listener decide how to grant it (see
    /// LeaderboardManager.RewardsClaimed).
    /// </summary>
    public enum LeaderboardRewardKind
    {
        Coins = 0,
        Booster = 1,
        ProfileAvatar = 2,
        ProfileFrame = 3
    }

    /// <summary>
    /// One prize inside a placement's payout: what kind, how many, which icon.
    ///
    /// No display name on purpose - the results popup builds the wording from the kind and the
    /// amount, so a table can never drift out of step with what it actually grants ("500 Coins"
    /// labelling a row that pays 300).
    /// </summary>
    [Serializable]
    public sealed class LeaderboardRewardItemData
    {
        [JsonProperty("kind")]
        public LeaderboardRewardKind Kind = LeaderboardRewardKind.Coins;

        /// <summary>Coins/boosters: how many. Ignored by the profile kinds, which grant one item.</summary>
        [JsonProperty("amount")]
        public int Amount = 100;

        /// <summary>
        /// Which thing, for the kinds that need naming: a BoosterType name for Booster, or a
        /// ProfileCatalogAsset item id for the profile kinds. Unused by Coins.
        /// </summary>
        [JsonProperty("itemId")]
        public string ItemId = string.Empty;

        /// <summary>
        /// Which sprite this prize shows, resolved through the shared IconDatabaseConfig the store
        /// cards already use. Blank means the row shows no icon at all.
        /// </summary>
        [JsonProperty("iconKey")]
        public string IconKey = string.Empty;

        public LeaderboardRewardItemData CloneNormalized()
        {
            return new LeaderboardRewardItemData
            {
                Kind = Kind,
                Amount = Math.Max(0, Amount),
                ItemId = ItemId ?? string.Empty,
                IconKey = IconKey ?? string.Empty
            };
        }
    }

    /// <summary>
    /// One placement in the reward table: "rank 1 gets these". The whole list is paid, so a single
    /// placement can hand out coins AND a booster AND a frame.
    ///
    /// Keyed by an exact rank rather than a range, because only the top few places are ever paid
    /// and one row per place is what a designer actually wants to read. A rank with no row - which
    /// is everyone outside the table - is paid nothing and never sees the results popup.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardRewardConfigData
    {
        [JsonProperty("rank")]
        public int Rank = 1;

        [JsonProperty("rewards")]
        public List<LeaderboardRewardItemData> Rewards = new List<LeaderboardRewardItemData>();

        public bool Matches(int rank)
        {
            return Rank == rank;
        }

        public LeaderboardRewardConfigData CloneNormalized()
        {
            var clone = new LeaderboardRewardConfigData
            {
                // Floored at 1: rank 0 is "unranked" everywhere else in the feature, so a row
                // claiming it would pay a placement that cannot exist.
                Rank = Math.Max(1, Rank),
                Rewards = new List<LeaderboardRewardItemData>()
            };

            if (Rewards == null)
            {
                return clone;
            }

            for (int index = 0; index < Rewards.Count; index++)
            {
                if (Rewards[index] != null)
                {
                    clone.Rewards.Add(Rewards[index].CloneNormalized());
                }
            }

            return clone;
        }
    }
}
