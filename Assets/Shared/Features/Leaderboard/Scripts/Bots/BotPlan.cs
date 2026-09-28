using System.Collections.Generic;

namespace Shared.Leaderboard.Bots
{
    /// <summary>
    /// A bot's entire event, planned up front.
    ///
    /// Every time here is SECONDS FROM THE EVENT START, not a DateTime. The planner and evaluator
    /// are pure C# with no Unity and no clock of their own; keeping the plan in relative seconds is
    /// what lets a test evaluate the same plan at a thousand arbitrary times without a calendar.
    /// LeaderboardManager converts at the edge.
    /// </summary>
    public sealed class BotPlan
    {
        /// <summary>Position in the roster. The last tie-break in ranking, so it must be stable.</summary>
        public int Index;

        public string PresetName;

        /// <summary>Display name - a pool name for NamedBotRatio of bots, otherwise a generated 6-character id.</summary>
        public string Name;

        public int AvatarIndex;
        public int FrameIndex;

        /// <summary>0 for the bots present at the open; later for everyone who arrives on the join curve.</summary>
        public double JoinSeconds;

        /// <summary>True for the first BotsStart bots. They get a seeded pre-open last-seen; joiners do not.</summary>
        public bool IsStartingBot;

        /// <summary>Mean fish per level, 1-3. Drawn once and fixed for the event.</summary>
        public double Skill;

        /// <summary>
        /// Where a starting bot's "last seen" sits before its first burst - NEGATIVE seconds, i.e.
        /// before the event opened. A board that opens with ten bots all last seen at exactly 0
        /// reads as a switch being flipped rather than people who were already playing.
        /// </summary>
        public double PreOpenLastSeenSeconds;

        public readonly List<BotBurst> Bursts = new List<BotBurst>();
    }

    /// <summary>
    /// One sitting: the bot wakes, waits a moment, plays a few levels, and goes away again.
    ///
    /// Carries BOTH level lists. Which one is played is decided at evaluation time from the burst's
    /// start, so a plan never has to be rebuilt when the player's absence changes - see
    /// BotEvaluator. That is what keeps scores monotonic.
    /// </summary>
    public sealed class BotBurst
    {
        /// <summary>Goes online. Seconds from event start.</summary>
        public double WakeSeconds;

        /// <summary>Starts playing, a short lead after waking.</summary>
        public double StartSeconds;

        /// <summary>Played when the player was around.</summary>
        public BotLevel[] NormalLevels;

        /// <summary>Played when this burst began inside an absence window - fewer levels, slower.</summary>
        public BotLevel[] RubberbandLevels;

        public BotLevel[] Levels(bool rubberband)
        {
            return rubberband ? RubberbandLevels : NormalLevels;
        }

        /// <summary>When the last level of the chosen variant finishes.</summary>
        public double EndSeconds(bool rubberband)
        {
            double end = StartSeconds;
            BotLevel[] levels = Levels(rubberband);
            if (levels == null)
            {
                return end;
            }

            for (int index = 0; index < levels.Length; index++)
            {
                end += levels[index].PaceSeconds;
            }

            return end;
        }
    }

    /// <summary>One level a bot plays: how long it takes and what it pays.</summary>
    public struct BotLevel
    {
        public double PaceSeconds;
        public int Fish;

        public BotLevel(double paceSeconds, int fish)
        {
            PaceSeconds = paceSeconds;
            Fish = fish;
        }
    }

    /// <summary>
    /// A stretch the player was away for, in seconds from the event start. A burst whose START
    /// falls inside one of these plays its rubberband variant.
    /// </summary>
    public struct BotAbsenceWindow
    {
        public double StartSeconds;
        public double EndSeconds;

        public BotAbsenceWindow(double startSeconds, double endSeconds)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
        }

        public bool Contains(double seconds)
        {
            // Half-open, so two adjacent windows cannot both claim the same instant.
            return seconds >= StartSeconds && seconds < EndSeconds;
        }
    }

    public enum BotActivityStatus
    {
        Offline = 0,
        Online = 1,
        Playing = 2
    }

    /// <summary>What a bot looks like at one moment - everything the board needs for a row.</summary>
    /// <summary>
    /// How much presence to show beyond what the bursts themselves produce - see
    /// BotEvaluator.ApplyIdlePresence.
    ///
    /// A struct with a meaningful default: all-zero means "no idle presence at all", so an
    /// evaluator call that does not pass one behaves exactly as it did before this existed. That
    /// is what keeps the cheats and tests working without threading config through every one.
    /// </summary>
    public struct BotPresenceSettings
    {
        /// <summary>0..1 of offline bots to show as Online.</summary>
        public double OnlineShare;

        /// <summary>0..1 to show as Playing. Drawn from the same roll, so the two never overlap.</summary>
        public double PlayingShare;

        /// <summary>How long one set stays put before it reshuffles.</summary>
        public double BucketSeconds;

        public BotPresenceSettings(double onlineShare, double playingShare, double bucketSeconds)
        {
            OnlineShare = onlineShare;
            PlayingShare = playingShare;
            BucketSeconds = bucketSeconds;
        }
    }

    public struct BotSnapshot
    {
        public int Score;

        /// <summary>
        /// When the bot reached this score. The first ranking tie-break, so two bots on the same
        /// score are ordered by who got there first rather than arbitrarily.
        /// </summary>
        public double ReachedSeconds;

        public BotActivityStatus Status;

        /// <summary>Latest burst end at or before now; the join time, or the pre-open value, before any burst.</summary>
        public double LastSeenSeconds;

        /// <summary>False before the bot has joined - it is not on the board at all yet.</summary>
        public bool IsVisible;
    }
}
