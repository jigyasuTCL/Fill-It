using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        #region Ad watch tracking

        // Watched-date tracking lives inside jsonConfig (persisted to PlayerData - see
        // Persistence.cs), never in PlayerPrefs. jsonConfig.adWatchedDates is a comma-joined list
        // of "yyyy-MM-dd" strings, mirroring the format the old PlayerPrefs value used.

        private List<string> GetWatchedDates()
        {
            var watchedDates = jsonConfig?.adWatchedDates ?? string.Empty;
            if (string.IsNullOrWhiteSpace(watchedDates))
            {
                return new List<string>();
            }

            return watchedDates
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private void ResetAdWatchStateForNewDay()
        {
            if (jsonConfig == null)
            {
                return;
            }

            var todayKey = DailyChallengeClock.Now.Date.ToString("yyyy-MM-dd");
            if (string.Equals(jsonConfig.lastAdResetDate, todayKey, StringComparison.Ordinal))
            {
                return;
            }

            jsonConfig.adWatchedDates = string.Empty;
            jsonConfig.lastAdResetDate = todayKey;
            SaveJsonConfig();
        }

        private bool HasWatchedAdForDate(DailyChallengeData data)
        {
            if (data == null)
            {
                return false;
            }

            var dateKey = GetDateKey(data);
            return !string.IsNullOrEmpty(dateKey) && GetWatchedDates().Contains(dateKey, StringComparer.Ordinal);
        }

        private void MarkAdWatchedForDate(DailyChallengeData data)
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

            var dates = GetWatchedDates();
            if (!dates.Contains(dateKey, StringComparer.Ordinal))
            {
                dates.Add(dateKey);
                jsonConfig.adWatchedDates = string.Join(",", dates);
                SaveJsonConfig();
            }
        }

        private void ClearAdWatchFlagForDate(DailyChallengeData data)
        {
            if (data == null)
            {
                return;
            }

            ClearAdWatchFlagForDate(data.Year, data.Month, data.Day);
        }

        // Overload that doesn't need a loaded DailyChallengeData - used by crash/quit recovery,
        // which only has the raw year/month/day persisted from the interrupted attempt.
        private void ClearAdWatchFlagForDate(int year, int month, int day)
        {
            if (jsonConfig == null)
            {
                return;
            }

            var dateKey = GetDateKey(year, month, day);
            if (string.IsNullOrEmpty(dateKey))
            {
                return;
            }

            var dates = GetWatchedDates()
                .Where(item => !string.Equals(item, dateKey, StringComparison.Ordinal))
                .ToList();

            jsonConfig.adWatchedDates = string.Join(",", dates);
            SaveJsonConfig();
        }

        private bool ShouldRequireAdForSelectedDate(DailyChallengeData data)
        {
            if (data == null)
            {
                return false;
            }

            var selectedDate = new DateTime(data.Year, data.Month, data.Day).Date;
            var today = DailyChallengeClock.Now.Date;

            if (selectedDate == today)
            {
                return data.HasLost && !HasWatchedAdForDate(data);
            }

            return !HasWatchedAdForDate(data);
        }

        private void SyncAdWatchStateForDate(DailyChallengeData data)
        {
            if (data == null)
            {
                return;
            }

            if (data.HasLost)
            {
                // A new loss was just registered - the player must watch an ad again
                // before their next attempt, no matter which date this challenge belongs to.
                ClearAdWatchFlagForDate(data);
            }
        }
        #endregion
    }
}
