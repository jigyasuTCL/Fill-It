using System;
using UnityEngine;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        #region Cheat - date jump

        /// <summary>
        /// Debug-only: offsets DailyChallengeClock.Now by the given number of days (negative goes
        /// backward) and fully rebuilds the calendar against the new "today". Unlock flags,
        /// today's countdown, the ad-watch daily reset, and the reward track's monthly reset all
        /// follow automatically since every one of them reads DailyChallengeClock.Now instead of
        /// the real clock - see DailyChallengeClock. The offset is in-memory only and resets on
        /// app restart, same as every other Cheat UI value in this codebase.
        /// </summary>
        public void CheatJumpDays(int days)
        {
            DailyChallengeClock.JumpByDays(days);
            RebuildForClockChange();
        }

        /// <summary>Debug-only: jumps straight to a specific date (see CheatJumpDays).</summary>
        public void CheatJumpToDate(DateTime targetDate)
        {
            DailyChallengeClock.JumpToDate(targetDate);
            RebuildForClockChange();
        }

        /// <summary>Debug-only: clears the cheat offset, going back to the real device date.</summary>
        public void CheatResetToRealDate()
        {
            DailyChallengeClock.Reset();
            RebuildForClockChange();
        }

        private void RebuildForClockChange()
        {
            DateTime now = DailyChallengeClock.Now;

            // Re-Initialize (not just RefreshForPossibleDayChange) even when the month didn't
            // change - ResetAdWatchStateForNewDay, RecoverInterruptedAttempt and
            // SelectDefaultDay all need to re-run against the new "today" too, not just the
            // per-day IsUnlocked flags.
            Initialize(now.Year, now.Month);
        }

        #endregion
    }
}
