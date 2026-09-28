using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        #region Reward tier progress

        /// <summary>
        /// Fired right after TryClaimRewardTier grants a tier - the Cat Flap-side bridge (kept
        /// outside this Shared feature, same pattern as DailyChallengeGameplayRequested/
        /// DailyChallengeResultResolved) subscribes to this to actually add the coins to the
        /// player's wallet. This feature only tracks eligibility/claim-state, never touches
        /// PlayerData.TotalCoins directly - Cat Flap's economy is the source of truth for that.
        /// </summary>
        public event Action<Shared.GameConfig.DailyChallengeRewardTierConfigData> RewardTierClaimed;

        private static List<Shared.GameConfig.DailyChallengeRewardTierConfigData> RewardTiers =>
            Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config.DailyChallenge.RewardTiers
                : null;

        private List<string> GetCompletedChallengeDates()
        {
            var dates = jsonConfig?.completedChallengeDates ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dates))
            {
                return new List<string>();
            }

            return dates
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // "yyyy-MM" of the real calendar month claimedRewardTierDays currently belongs to - not
        // necessarily the month the calendar popup happens to be showing (the player can browse
        // other months; the reward track always tracks THIS month). Checked/rolled by
        // EnsureRewardTierMonthCurrent below.
        private static string CurrentMonthKey => DailyChallengeClock.Now.ToString("yyyy-MM");

        // The reward track is a monthly milestone strip (see GetCompletedDaysThisMonth /
        // RefreshProgressBar) - once the real month rolls over, every tier becomes claimable
        // again from zero. Called at the top of every claimed-days read/write so no caller has to
        // remember to check it themselves.
        private void EnsureRewardTierMonthCurrent()
        {
            if (jsonConfig == null)
            {
                return;
            }

            string currentKey = CurrentMonthKey;
            if (string.Equals(jsonConfig.rewardTierMonthKey, currentKey, StringComparison.Ordinal))
            {
                return;
            }

            jsonConfig.claimedRewardTierDays = string.Empty;
            jsonConfig.rewardTierMonthKey = currentKey;
            SaveJsonConfig();
        }

        private List<int> GetClaimedRewardTierDays()
        {
            EnsureRewardTierMonthCurrent();

            var claimed = jsonConfig?.claimedRewardTierDays ?? string.Empty;
            if (string.IsNullOrWhiteSpace(claimed))
            {
                return new List<int>();
            }

            return claimed
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => int.TryParse(item.Trim(), out var value) ? value : -1)
                .Where(value => value >= 0)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Total distinct days the player has fully completed (won) across all time. General
        /// lifetime stat - NOT what reward-tier eligibility is checked against, see
        /// GetCompletedDaysThisMonth.
        /// </summary>
        public int GetTotalCompletedDays()
        {
            return GetCompletedChallengeDates().Count;
        }

        /// <summary>
        /// Distinct days completed (won) within the real current calendar month - this is what
        /// drives both the reward track's progress bar (a monthly-completion bar, not a
        /// lifetime one) and reward-tier eligibility (see CanClaimRewardTier). Filters the same
        /// completedChallengeDates list GetTotalCompletedDays uses, just scoped to "yyyy-MM" of
        /// DateTime.Now - no separate reset needed, last month's dates just stop matching once the
        /// month rolls over.
        /// </summary>
        public int GetCompletedDaysThisMonth()
        {
            string monthPrefix = CurrentMonthKey;
            return GetCompletedChallengeDates().Count(date => date.StartsWith(monthPrefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Marks today's - or any given day's - challenge as completed for reward-tier progress
        /// purposes. Safe to call repeatedly for the same day; only the first call for a given
        /// date advances the count. Called from ReportGameplayResult once a day's LevelStates are
        /// all Win (see DailyChallengeData.HasWin).
        /// </summary>
        private void MarkDayCompleted(DailyChallengeData data)
        {
            if (data == null || jsonConfig == null)
            {
                return;
            }

            var dateKey = GetDateKey(data);
            if (string.IsNullOrEmpty(dateKey))
            {
                return;
            }

            var dates = GetCompletedChallengeDates();
            if (dates.Contains(dateKey, StringComparer.Ordinal))
            {
                return;
            }

            dates.Add(dateKey);
            jsonConfig.completedChallengeDates = string.Join(",", dates);
            SaveJsonConfig();
        }

        /// <summary>
        /// The next reward tier the player hasn't claimed yet, in ascending DaysRequired order -
        /// null once every configured tier has been claimed (or none are configured).
        /// </summary>
        public Shared.GameConfig.DailyChallengeRewardTierConfigData GetNextUnclaimedRewardTier()
        {
            var tiers = RewardTiers;
            if (tiers == null || tiers.Count == 0)
            {
                return null;
            }

            var claimedDays = GetClaimedRewardTierDays();

            return tiers
                .Where(tier => tier != null && !claimedDays.Contains(tier.DaysRequired))
                .OrderBy(tier => tier.DaysRequired)
                .FirstOrDefault();
        }

        public bool CanClaimRewardTier(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            if (tier == null)
            {
                return false;
            }

            return GetCompletedDaysThisMonth() >= tier.DaysRequired
                && !GetClaimedRewardTierDays().Contains(tier.DaysRequired);
        }

        public bool IsRewardTierClaimed(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            return tier != null && GetClaimedRewardTierDays().Contains(tier.DaysRequired);
        }

        /// <summary>
        /// Marks a reward tier claimed and fires RewardTierClaimed so the Cat Flap-side bridge can
        /// grant the coins. Returns false (no-op) if the tier isn't actually claimable yet - see
        /// CanClaimRewardTier.
        /// </summary>
        public bool TryClaimRewardTier(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            if (!CanClaimRewardTier(tier))
            {
                return false;
            }

            var claimedDays = GetClaimedRewardTierDays();
            claimedDays.Add(tier.DaysRequired);
            jsonConfig.claimedRewardTierDays = string.Join(",", claimedDays);
            SaveJsonConfig();

            RewardTierClaimed?.Invoke(tier);

            return true;
        }

        #endregion

        #region Reward progression testing (debug-only)

        /// <summary>
        /// Debug-only: fakes one more day completed this month purely to test the reward track's
        /// progress bar/tier claiming without actually winning a puzzle. Adds the next unused
        /// synthetic date in the current month - not tied to any real calendar day, so it never
        /// marks an actual DailyChallengeDateButton as won.
        /// </summary>
        public void DebugAddCompletedDayThisMonth()
        {
            if (jsonConfig == null)
            {
                Debug.LogWarning("DailyChallengeController: cannot debug-add a completed day before Initialize().");
                return;
            }

            var dates = GetCompletedChallengeDates();
            string monthPrefix = CurrentMonthKey;
            DateTime now = DailyChallengeClock.Now;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);

            for (int day = 1; day <= daysInMonth; day++)
            {
                string candidate = $"{monthPrefix}-{day:00}";
                if (dates.Contains(candidate, StringComparer.Ordinal))
                {
                    continue;
                }

                dates.Add(candidate);
                jsonConfig.completedChallengeDates = string.Join(",", dates);
                SaveJsonConfig();

                return;
            }
        }

        /// <summary>
        /// Debug-only: clears this month's completed-day count and every claimed reward tier,
        /// resetting the reward track back to empty so progression testing can be repeated.
        /// </summary>
        public void DebugResetRewardProgress()
        {
            if (jsonConfig == null)
            {
                return;
            }

            string monthPrefix = CurrentMonthKey;
            var remaining = GetCompletedChallengeDates()
                .Where(date => !date.StartsWith(monthPrefix, StringComparison.Ordinal))
                .ToList();

            jsonConfig.completedChallengeDates = string.Join(",", remaining);
            jsonConfig.claimedRewardTierDays = string.Empty;
            jsonConfig.rewardTierMonthKey = monthPrefix;
            SaveJsonConfig();
        }

        #endregion
    }
}
