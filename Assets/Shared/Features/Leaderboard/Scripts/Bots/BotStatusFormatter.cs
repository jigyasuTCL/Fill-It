using System;

namespace Shared.Leaderboard.Bots
{
    /// <summary>
    /// Turns a status and a last-seen time into the string on the row.
    ///
    /// Separate from the evaluator so the wording can be changed, localized or swapped for chips
    /// without touching the simulation, and so the formats are testable without building a board.
    /// </summary>
    public static class BotStatusFormatter
    {
        public const string PlayingLabel = "Playing now";
        public const string OnlineLabel = "Online now";

        /// <summary>
        /// The row's status text. Offline falls back to a relative last-seen: "12m ago" under an
        /// hour, "3h 20m ago" under a day, "2d ago" past that.
        /// </summary>
        public static string Format(BotActivityStatus status, double secondsSinceLastSeen)
        {
            switch (status)
            {
                case BotActivityStatus.Playing:
                    return PlayingLabel;

                case BotActivityStatus.Online:
                    return OnlineLabel;

                default:
                    return FormatLastSeen(secondsSinceLastSeen);
            }
        }

        /// <summary>
        /// Relative time, coarsening as it grows - nobody needs "1d 4h 12m". Negative or tiny gaps
        /// read as "0m ago" rather than something nonsensical, which can happen briefly when a
        /// clock correction lands between two reads.
        /// </summary>
        public static string FormatLastSeen(double secondsSinceLastSeen)
        {
            if (secondsSinceLastSeen < 0d)
            {
                secondsSinceLastSeen = 0d;
            }

            var span = TimeSpan.FromSeconds(secondsSinceLastSeen);

            if (span.TotalHours < 1d)
            {
                return $"{(int)span.TotalMinutes}m ago";
            }

            if (span.TotalDays < 1d)
            {
                return $"{(int)span.TotalHours}h {span.Minutes}m ago";
            }

            return $"{(int)span.TotalDays}d ago";
        }
    }
}
