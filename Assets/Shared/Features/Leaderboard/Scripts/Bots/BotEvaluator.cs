using System;
using System.Collections.Generic;

namespace Shared.Leaderboard.Bots
{
    /// <summary>
    /// Reads a planned bot at a moment in time: score, when it got there, what it is doing, and
    /// when it was last seen.
    ///
    /// Pure and stateless - the same plan, windows and time always give the same snapshot, so the
    /// board can be recomputed from scratch on every refresh instead of being stepped forward.
    /// Nothing here is stored per level.
    ///
    /// The monotonicity guarantee: a burst's variant is chosen from its START time alone, and
    /// absence windows only ever grow, so a burst that has already begun can never switch to the
    /// shorter variant and take points back off the board.
    /// </summary>
    public static class BotEvaluator
    {
        /// <summary>
        /// Everything a board row needs for one bot at <paramref name="atSeconds"/>.
        /// </summary>
        /// <param name="atSeconds">Now, in seconds from the event start.</param>
        /// <param name="eventEndSeconds">
        /// The event's end. Levels finishing after it never count, so evaluating past the end
        /// returns the frozen final board - which is exactly what settlement needs.
        /// </param>
        /// <param name="windows">
        /// Player absence windows, in seconds from the event start. A burst starting inside one
        /// plays its rubberband variant.
        /// </param>
        public static BotSnapshot Evaluate(
            BotPlan plan,
            double atSeconds,
            double eventEndSeconds,
            IReadOnlyList<BotAbsenceWindow> windows,
            double onlineTailSeconds,
            BotPresenceSettings presence = default)
        {
            var snapshot = new BotSnapshot
            {
                Score = 0,
                ReachedSeconds = plan.JoinSeconds,
                Status = BotActivityStatus.Offline,
                LastSeenSeconds = plan.PreOpenLastSeenSeconds,
                IsVisible = !double.IsInfinity(plan.JoinSeconds) && plan.JoinSeconds <= atSeconds
            };

            if (!snapshot.IsVisible)
            {
                return snapshot;
            }

            // Never look past the end: after it, the board is whatever it was at the whistle.
            double cutoff = Math.Min(atSeconds, eventEndSeconds);

            for (int burstIndex = 0; burstIndex < plan.Bursts.Count; burstIndex++)
            {
                BotBurst burst = plan.Bursts[burstIndex];
                if (burst.StartSeconds > cutoff)
                {
                    // Hasn't begun. It may still be the burst this bot is currently awake for.
                    if (burst.WakeSeconds <= cutoff)
                    {
                        snapshot.Status = BotActivityStatus.Online;
                    }

                    break;
                }

                bool rubberband = IsInsideWindow(burst.StartSeconds, windows);
                BotLevel[] levels = burst.Levels(rubberband);
                double completion = burst.StartSeconds;
                bool finishedAny = false;

                for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
                {
                    completion += levels[levelIndex].PaceSeconds;
                    if (completion > cutoff)
                    {
                        break;
                    }

                    snapshot.Score += levels[levelIndex].Fish;
                    // The moment the score last changed - the first ranking tie-break.
                    snapshot.ReachedSeconds = completion;
                    snapshot.LastSeenSeconds = completion;
                    finishedAny = true;
                }

                double burstEnd = burst.EndSeconds(rubberband);
                if (burstEnd > cutoff)
                {
                    // Mid-burst right now.
                    snapshot.Status = BotActivityStatus.Playing;
                    break;
                }

                if (!finishedAny)
                {
                    snapshot.LastSeenSeconds = Math.Max(snapshot.LastSeenSeconds, burstEnd);
                }

                // Still shown as around for a short tail after finishing, so a bot that just
                // stopped playing does not blink straight to "2m ago".
                snapshot.Status = cutoff < burstEnd + onlineTailSeconds
                    ? BotActivityStatus.Online
                    : BotActivityStatus.Offline;
            }

            // A joiner that has not played yet is "around" for the same tail after arriving.
            if (snapshot.Score == 0 && !plan.IsStartingBot && cutoff < plan.JoinSeconds + onlineTailSeconds)
            {
                snapshot.Status = BotActivityStatus.Online;
            }

            ApplyIdlePresence(ref snapshot, plan, cutoff, presence);

            return snapshot;
        }

        /// <summary>
        /// Puts a believable number of bots "around" between their own bursts.
        ///
        /// Only ever upgrades a bot that the real schedule says is Offline - a bot genuinely
        /// mid-burst keeps its Playing, and nothing here touches Score, ReachedSeconds or
        /// LastSeenSeconds. So the board still ranks purely on what was actually played.
        ///
        /// Deterministic from (bot index, time bucket): every device showing the same event at the
        /// same moment shows the same people online, and the answer does not change between two
        /// refreshes a second apart - which is what stops the status column flickering.
        /// </summary>
        private static void ApplyIdlePresence(
            ref BotSnapshot snapshot,
            BotPlan plan,
            double cutoff,
            BotPresenceSettings presence)
        {
            if (!snapshot.IsVisible
                || snapshot.Status != BotActivityStatus.Offline
                || presence.BucketSeconds <= 0d)
            {
                return;
            }

            double share = presence.OnlineShare + presence.PlayingShare;
            if (share <= 0d)
            {
                return;
            }

            // Floor, not round: the bucket must not advance twice for one interval.
            int bucket = (int)Math.Floor(cutoff / presence.BucketSeconds);
            double roll = DeterministicRandom.Unit(plan.Index, PresenceSlot + bucket);

            if (roll < presence.PlayingShare)
            {
                snapshot.Status = BotActivityStatus.Playing;
                return;
            }

            if (roll < share)
            {
                snapshot.Status = BotActivityStatus.Online;
            }
        }

        // Far from BotDrawSlot's ranges, so presence draws can never collide with a planning draw.
        private const int PresenceSlot = 500000;

        private static bool IsInsideWindow(double seconds, IReadOnlyList<BotAbsenceWindow> windows)
        {
            if (windows == null)
            {
                return false;
            }

            for (int index = 0; index < windows.Count; index++)
            {
                if (windows[index].Contains(seconds))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
