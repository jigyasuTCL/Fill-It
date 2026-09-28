using System;
using System.Collections.Generic;

namespace Game.SpinWheel
{
    /// <summary>
    /// One "stage" of the wheel's unlock progression.
    /// Stage 1 = the first (free) spin. Stage 2 = the spin unlocked after
    /// WinsRequired regular-level wins following stage 1, and so on.
    ///
    /// Design this list in Balancy exactly as your table:
    ///   Stage 1: WinsRequired = 0,  IsFree = true
    ///   Stage 2: WinsRequired = 5
    ///   Stage 3: WinsRequired = 5
    ///   Stage 4: WinsRequired = 7
    ///   Stage 5: WinsRequired = 7
    ///   Stage 6: WinsRequired = 10   <-- last entry, marked IsCapStage = true
    ///
    /// Any spin index beyond the list length reuses the last stage's
    /// WinsRequired (the "6th onwards" cap) automatically — see
    /// SpinWheelRemoteConfig.GetStageForSpinIndex.
    /// </summary>
    [Serializable]
    public class SpinStageConfig
    {
        public int SpinIndex;        // 1-based: 1, 2, 3, 4, 5, 6...
        public int WinsRequired;     // regular level wins needed to unlock this spin
        public bool IsFree;          // true only for spin 1
        public bool IsCapStage;      // true on the last configured entry (the repeating tail)
    }

    public enum RewardType
    {
        Coins,
        Booster
    }

    /// <summary>
    /// One entry in the reward pool. Weight is the "percentage" from your table —
    /// it doesn't need to sum to exactly 100, it's normalized at selection time,
    /// which makes it safe to tweak individual rows in Balancy without touching the rest.
    /// </summary>
    [Serializable]
    public class RewardConfig
    {
        public string RewardId;      // unique key, e.g. "coins_5_a", "booster_1"
        public RewardType Type;
        public int Amount;           // coin amount, or booster count
        public string BoosterId;     // only used when Type == Booster
        public float Weight;         // relative percentage weight, e.g. 30 = "30%"
        public string RewardIconKey; // key into IconDatabaseConfig; falls back to RewardId when empty
    }

    /// <summary>
    /// A named group of rewards, played straight through in order. When one
    /// table is exhausted, the next table (by position in RewardTables)
    /// becomes active; after the last table is exhausted, it wraps back to
    /// the first table. TableId is just for readability in Balancy/logs —
    /// selection is purely by position in the RewardTables list.
    /// </summary>
    [Serializable]
    public class RewardTable
    {
        public string TableId;             // e.g. "EarlyGame", "Tier2" — for readability only
        public List<RewardConfig> Rewards; // played in order, index 0 first
    }

    /// <summary>
    /// Root config object — this is the shape you'd publish as a single
    /// Balancy remote document (e.g. "SpinWheelConfig").
    /// </summary>
    [Serializable]
    public class SpinWheelRemoteConfig
    {
        //public int UnlockAtRegularLevel;      // "x" — level that unlocks the feature
        public List<SpinStageConfig> Stages;   // progression table
        public List<RewardTable> RewardTables;  // played table-by-table, looping after the last one

        /// <summary>
        /// Returns the stage config to use for a given 1-based spin index,
        /// automatically applying the cap (reusing the last stage entry for
        /// any index beyond the configured list).
        /// </summary>
        public SpinStageConfig GetStageForSpinIndex(int spinIndex)
        {
            if (Stages == null || Stages.Count == 0)
                throw new InvalidOperationException("SpinWheelRemoteConfig.Stages is empty.");

            if (spinIndex <= Stages.Count)
                return Stages[spinIndex - 1];

            // Beyond the configured list: repeat the last (capped) stage.
            return Stages[Stages.Count - 1];
        }

        /// <summary>
        /// Given how many spins the player has ALREADY claimed (0-based —
        /// 0 for their very first spin ever), works out which table is
        /// active and which reward within it, treating all tables as one
        /// long sequence that wraps back to table 0 once every table has
        /// been played through once.
        ///
        /// Example with tables of length 6 and 4 (10 total):
        ///   completedSpinCount 0..5   -> table 0, local index 0..5
        ///   completedSpinCount 6..9   -> table 1, local index 0..3
        ///   completedSpinCount 10     -> wraps: table 0, local index 0 again
        /// </summary>
        public (RewardTable table, int tableIndex, int localIndex) ResolveActiveReward(int completedSpinCount)
        {
            if (RewardTables == null || RewardTables.Count == 0)
                throw new InvalidOperationException("SpinWheelRemoteConfig.RewardTables is empty.");

            int totalAcrossAllTables = 0;
            foreach (var t in RewardTables)
                totalAcrossAllTables += t.Rewards?.Count ?? 0;

            if (totalAcrossAllTables == 0)
                throw new InvalidOperationException("SpinWheelRemoteConfig.RewardTables has no rewards in any table.");

            int remaining = completedSpinCount % totalAcrossAllTables; // <-- the "loop after every list" wrap

            for (int tableIndex = 0; tableIndex < RewardTables.Count; tableIndex++)
            {
                var table = RewardTables[tableIndex];
                int count = table.Rewards?.Count ?? 0;

                if (remaining < count)
                    return (table, tableIndex, remaining);

                remaining -= count; // this table used up, move on to the next
            }

            // Should be unreachable given the modulo above, but fail loudly rather
            // than silently returning something wrong if it ever is.
            throw new InvalidOperationException("[SpinWheel] ResolveActiveReward failed to resolve a table — check RewardTables for empty entries.");
        }
    }
}