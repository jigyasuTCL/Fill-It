// using System;
// using System.Collections.Generic;
// using System.Linq;
// using UnityEngine;

// namespace Game.SpinWheel
// {
//     /// <summary>
//     /// Picks the reward for a given spin, honoring:
//     ///  1) the first N spins being a fixed predefined sequence (from remote config)
//     ///  2) after that, either looping the same sequence, or switching to
//     ///     weighted-random using each RewardConfig.Weight (also remote-controlled).
//     ///
//     /// completedSpinCount = how many spins the player has ALREADY claimed
//     /// (0 for their very first spin ever).
//     /// </summary>
//     public static class RewardSelector
//     {
//         public static RewardConfig SelectReward(SpinWheelRemoteConfig config, int completedSpinCount, System.Random rng = null)
//         {
//             rng ??= new System.Random();

//             var predefined = config.PredefinedRewardSequence;
//             if (predefined != null && predefined.Count > 0)
//             {
//                 if (completedSpinCount < predefined.Count)
//                 {
//                     return ResolveById(config, predefined[completedSpinCount]);
//                 }

//                 if (config.LoopPredefinedSequence)
//                 {
//                     int loopedIndex = completedSpinCount % predefined.Count;
//                     return ResolveById(config, predefined[loopedIndex]);
//                 }
//                 // else: fall through to weighted-random below
//             }

//             return WeightedRandomPick(config.Rewards, rng);
//         }

//         private static RewardConfig ResolveById(SpinWheelRemoteConfig config, string rewardId)
//         {
//             var reward = config.Rewards.FirstOrDefault(r => r.RewardId == rewardId);
//             if (reward == null)
//             {
//                 Debug.LogError($"[SpinWheel] PredefinedRewardSequence references unknown RewardId '{rewardId}'. " +
//                                 "Falling back to weighted-random. Check your Balancy config.");
//                 return WeightedRandomPick(config.Rewards, new System.Random());
//             }
//             return reward;
//         }

//         private static RewardConfig WeightedRandomPick(List<RewardConfig> rewards, System.Random rng)
//         {
//             if (rewards == null || rewards.Count == 0)
//                 throw new InvalidOperationException("[SpinWheel] Reward pool is empty.");

//             float totalWeight = rewards.Sum(r => Mathf.Max(0f, r.Weight));
//             if (totalWeight <= 0f)
//             {
//                 // Misconfigured weights (all zero/negative) — fail safe to uniform pick
//                 // rather than crashing the wheel for the player.
//                 Debug.LogWarning("[SpinWheel] All reward weights are zero — falling back to uniform random.");
//                 return rewards[rng.Next(rewards.Count)];
//             }

//             float roll = (float)(rng.NextDouble() * totalWeight);
//             float cumulative = 0f;
//             foreach (var reward in rewards)
//             {
//                 cumulative += Mathf.Max(0f, reward.Weight);
//                 if (roll <= cumulative)
//                     return reward;
//             }

//             return rewards[rewards.Count - 1]; // rounding-error fallback
//         }
//     }
// }
using System;

namespace Game.SpinWheel
{
    /// <summary>
    /// Picks the reward for a given spin by playing through RewardTables in
    /// order: every reward in table 0, then every reward in table 1, and so
    /// on — once the last table is exhausted, it wraps back to table 0. The
    /// actual table/index math lives on SpinWheelRemoteConfig.ResolveActiveReward
    /// since the manager also needs it (to know which table is "active" for
    /// display purposes) — this just calls through to it.
    /// </summary>
    public static class RewardSelector
    {
        public static RewardConfig SelectReward(SpinWheelRemoteConfig config, int completedSpinCount)
        {
            var (table, _, localIndex) = config.ResolveActiveReward(completedSpinCount);
            return table.Rewards[localIndex];
        }
    }
}