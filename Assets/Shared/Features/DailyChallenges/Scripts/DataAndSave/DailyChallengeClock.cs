using System;

namespace DailyChallenges
{
    /// <summary>
    /// Single source of "now" for the whole Daily Challenge feature. Every DateTime.Now/Today call
    /// this feature makes (day-unlock checks, the countdown to the next date, monthly reward-tier
    /// reset, ad-watch daily reset, which calendar month loads by default) goes through
    /// DailyChallengeClock.Now instead of the real clock directly, so a "jump date" cheat can
    /// offset what this feature thinks today is without touching the device clock or any save
    /// data.
    ///
    /// Purely in-memory - the offset resets to zero on app restart, same as every other Cheat UI
    /// value in this codebase (e.g. CheatPanelController's validation delay overrides).
    /// </summary>
    public static class DailyChallengeClock
    {
        private static TimeSpan offset = TimeSpan.Zero;

        public static bool IsOverridden => offset != TimeSpan.Zero;

        public static DateTime Now => DateTime.Now + offset;

        /// <summary>Offsets "now" by the given number of days - negative moves backward. Stacks
        /// with any previous jump rather than replacing it.</summary>
        public static void JumpByDays(int days)
        {
            offset += TimeSpan.FromDays(days);
        }

        /// <summary>Sets "now" so that Now.Date equals targetDate.Date, preserving the real
        /// time-of-day.</summary>
        public static void JumpToDate(DateTime targetDate)
        {
            offset = targetDate.Date - DateTime.Now.Date;
        }

        /// <summary>Clears the offset - Now goes back to reading the real device clock.</summary>
        public static void Reset()
        {
            offset = TimeSpan.Zero;
        }
    }
}
