using System;

namespace Shared.GameFlow
{
    /// <summary>
    /// Single "HH MM" format (e.g. "3H 33M") shared by every reset-timer display across Daily
    /// Reward and Daily Challenges - one place to retune so every screen stays consistent instead
    /// of each one carrying its own copy of the format string.
    /// </summary>
    public static class CountdownTimeFormatter
    {
        public static string Format(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            return $"{(int)remaining.TotalHours}H {remaining.Minutes:00}M";
        }
    }
}
