using Shared.GameConfig;
using System;
using System.Collections.Generic;

namespace Shared.Leaderboard.Bots
{
    /// <summary>
    /// Turns a seed and a config snapshot into the roster's complete plan for one event: who is
    /// playing, when they arrive, and every level each of them will play.
    ///
    /// Plain C#, no Unity: the whole thing is a pure function of (seed, config, event length), so a
    /// test can plan ten thousand events without a scene. Planning runs once per event - the board
    /// is then read by evaluating this plan at a time, never by stepping a simulation forward.
    ///
    /// Every draw goes through DeterministicRandom with a fixed slot from BotDrawSlot. Adding a new
    /// draw later must use a NEW slot number, or every existing board re-rolls.
    /// </summary>
    public static class BotPlanner
    {
        /// <summary>
        /// The GDD asks for at least one grinder among the starting bots, so the board opens with
        /// somebody visibly pushing the top. Matched by name; a config that renames or drops the
        /// preset simply loses the guarantee.
        /// </summary>
        private const string GrinderPresetName = "grinder";

        private const double SecondsPerHour = 3600d;
        private const double SecondsPerMinute = 60d;

        /// <summary>Generated ids look like the GDD's V0H8EK. No vowels, so nothing spells a word by accident.</summary>
        private const string IdAlphabet = "0123456789BCDFGHJKLMNPQRSTVWXYZ";
        private const int GeneratedIdLength = 6;

        /// <summary>
        /// Plans every bot for one event.
        /// </summary>
        /// <param name="eventSeed">Hash(InstallSeed, EventId) - the one input that makes this board this board.</param>
        /// <param name="config">The event's frozen config snapshot, already normalized.</param>
        /// <param name="eventSeconds">Event length in seconds. Longer than DayHours repeats the day plan.</param>
        /// <param name="namePool">Rival pool names. Used without repeats for NamedBotRatio of the roster.</param>
        /// <param name="avatarCount">Profile catalog avatar count, for picking a look. 0 leaves indices at 0.</param>
        /// <param name="frameCount">Profile catalog frame count.</param>
        public static List<BotPlan> Plan(
            int eventSeed,
            LeaderboardBotConfigData config,
            double eventSeconds,
            IReadOnlyList<string> namePool,
            int avatarCount,
            int frameCount)
        {
            var plans = new List<BotPlan>();
            if (config == null || config.BotsMax <= 0 || eventSeconds <= 0d)
            {
                return plans;
            }

            List<LeaderboardBotPresetData> roster = BuildRoster(eventSeed, config);
            double[] joinSeconds = BuildJoinTimes(eventSeed, config, eventSeconds, roster.Count);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < roster.Count; index++)
            {
                // Per-bot seed, so a bot's whole plan is independent of how many bots came before
                // it - the roster can grow without re-rolling anyone.
                int botSeed = DeterministicRandom.DeriveSeed(eventSeed, index);
                LeaderboardBotPresetData preset = roster[index];

                var plan = new BotPlan
                {
                    Index = index,
                    PresetName = preset.Name,
                    JoinSeconds = joinSeconds[index],
                    IsStartingBot = joinSeconds[index] <= 0d,
                    AvatarIndex = avatarCount > 0 ? (int)(DeterministicRandom.Unit(botSeed, BotDrawSlot.Identity) * avatarCount) : 0,
                    FrameIndex = frameCount > 0 ? (int)(DeterministicRandom.Unit(botSeed, BotDrawSlot.Identity + 1) * frameCount) : 0
                };

                plan.Name = ResolveName(botSeed, index, config, namePool, usedNames);

                // Parameters resolve base preset -> arrival-window override for this bot's join
                // time. The rubberband override is applied per burst later, not here.
                BotParameters parameters = ResolveParameters(preset, config, plan.JoinSeconds / SecondsPerHour);
                plan.Skill = DeterministicRandom.Range(botSeed, BotDrawSlot.Skill, parameters.Skill.Min, parameters.Skill.Max);

                plan.PreOpenLastSeenSeconds = plan.IsStartingBot
                    ? -DeterministicRandom.Range(
                        botSeed,
                        BotDrawSlot.PreOpenLastSeen,
                        config.StartingBotsLastSeenHours.Min,
                        config.StartingBotsLastSeenHours.Max) * SecondsPerHour
                    : plan.JoinSeconds;

                BuildBursts(plan, botSeed, config, parameters, eventSeconds);
                plans.Add(plan);
            }

            return plans;
        }

        // =========================================================
        // ROSTER
        // =========================================================

        /// <summary>
        /// Picks each bot's preset: exact quotas by weight, shuffled, with a grinder pulled into the
        /// starting group.
        /// </summary>
        private static List<LeaderboardBotPresetData> BuildRoster(int eventSeed, LeaderboardBotConfigData config)
        {
            var roster = new List<LeaderboardBotPresetData>(config.BotsMax);
            List<LeaderboardBotPresetData> presets = config.Presets;

            // Largest remainder, so the quotas sum to BotsMax exactly. Plain rounding would leave
            // the roster a bot or two short or over.
            var exact = new double[presets.Count];
            var floors = new int[presets.Count];
            int assigned = 0;
            for (int index = 0; index < presets.Count; index++)
            {
                exact[index] = presets[index].WeightPercent / 100d * config.BotsMax;
                floors[index] = (int)Math.Floor(exact[index]);
                assigned += floors[index];
            }

            for (int remainder = 0; remainder < config.BotsMax - assigned; remainder++)
            {
                int best = 0;
                double bestFraction = -1d;
                for (int index = 0; index < presets.Count; index++)
                {
                    double fraction = exact[index] - floors[index];
                    if (fraction > bestFraction)
                    {
                        bestFraction = fraction;
                        best = index;
                    }
                }

                floors[best]++;
                exact[best] = floors[best]; // consumed, so the next pass picks a different preset
            }

            for (int index = 0; index < presets.Count; index++)
            {
                for (int count = 0; count < floors[index]; count++)
                {
                    roster.Add(presets[index]);
                }
            }

            ShuffleDeterministic(roster, eventSeed);
            EnsureGrinderAmongStarters(roster, config.BotsStart);
            return roster;
        }

        /// <summary>Fisher-Yates driven by the event seed, so the order is the same on every device.</summary>
        private static void ShuffleDeterministic(List<LeaderboardBotPresetData> roster, int eventSeed)
        {
            for (int index = roster.Count - 1; index > 0; index--)
            {
                int swap = (int)(DeterministicRandom.Unit(eventSeed, 5000 + index) * (index + 1));
                (roster[index], roster[swap]) = (roster[swap], roster[index]);
            }
        }

        /// <summary>
        /// Swaps a grinder into the starting group if the shuffle did not put one there. Without
        /// this the board can open with nobody pushing the top score, and the first hours read as
        /// an easy win.
        /// </summary>
        private static void EnsureGrinderAmongStarters(List<LeaderboardBotPresetData> roster, int botsStart)
        {
            int starters = Math.Min(botsStart, roster.Count);
            for (int index = 0; index < starters; index++)
            {
                if (IsGrinder(roster[index]))
                {
                    return;
                }
            }

            for (int index = starters; index < roster.Count; index++)
            {
                if (IsGrinder(roster[index]))
                {
                    // Into the last starting slot, so the earliest joiners keep their drawn presets.
                    (roster[starters - 1], roster[index]) = (roster[index], roster[starters - 1]);
                    return;
                }
            }
        }

        private static bool IsGrinder(LeaderboardBotPresetData preset)
        {
            return string.Equals(preset.Name, GrinderPresetName, StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // IDENTITY
        // =========================================================

        /// <summary>
        /// A share of bots take pool names without repeats; the rest get generated ids. Repeats are
        /// refused rather than tolerated - two identical names on one board reads as a bug, and the
        /// pool is usually far smaller than the roster.
        /// </summary>
        private static string ResolveName(
            int botSeed,
            int index,
            LeaderboardBotConfigData config,
            IReadOnlyList<string> namePool,
            HashSet<string> usedNames)
        {
            bool wantsPoolName = namePool != null
                && namePool.Count > 0
                && DeterministicRandom.Unit(botSeed, BotDrawSlot.Identity + 2) < config.NamedBotRatio;

            if (wantsPoolName)
            {
                int start = (int)(DeterministicRandom.Unit(botSeed, BotDrawSlot.Identity + 3) * namePool.Count);
                for (int offset = 0; offset < namePool.Count; offset++)
                {
                    string candidate = namePool[(start + offset) % namePool.Count];
                    if (!string.IsNullOrWhiteSpace(candidate) && usedNames.Add(candidate))
                    {
                        return candidate;
                    }
                }
                // Pool exhausted - fall through to a generated id.
            }

            return GenerateId(botSeed, index);
        }

        private static string GenerateId(int botSeed, int index)
        {
            var characters = new char[GeneratedIdLength];
            for (int position = 0; position < GeneratedIdLength; position++)
            {
                double roll = DeterministicRandom.Unit(botSeed, BotDrawSlot.Identity + 10 + position);
                characters[position] = IdAlphabet[(int)(roll * IdAlphabet.Length)];
            }

            return new string(characters);
        }

        // =========================================================
        // JOINING
        // =========================================================

        /// <summary>
        /// When each bot appears. The first BotsStart are present at the open; the rest read their
        /// time off the join curve, then get grouped into batches and jittered.
        /// </summary>
        private static double[] BuildJoinTimes(
            int eventSeed,
            LeaderboardBotConfigData config,
            double eventSeconds,
            int botCount)
        {
            var joinSeconds = new double[botCount];
            int starters = Math.Min(config.BotsStart, botCount);
            for (int index = 0; index < starters; index++)
            {
                joinSeconds[index] = 0d;
            }

            if (starters >= botCount)
            {
                return joinSeconds;
            }

            // The curve was authored against CurveReferenceHours; a shorter or longer event
            // compresses or stretches it rather than truncating it.
            double scale = eventSeconds / (config.CurveReferenceHours * SecondsPerHour);
            double lastJoinLimit = eventSeconds - config.LastJoinBeforeEndMinutes * SecondsPerMinute;

            // Raw curve time per joiner, before batching.
            var raw = new double[botCount];
            for (int index = starters; index < botCount; index++)
            {
                raw[index] = SecondsForCount(config.JoinCurve, index + 1, scale);
            }

            int batchStart = starters;
            while (batchStart < botCount)
            {
                int batchSize = DeterministicRandom.RangeInt(
                    eventSeed,
                    9000 + batchStart,
                    (int)config.JoinBatchSize.Min,
                    (int)config.JoinBatchSize.Max);
                batchSize = Math.Max(1, batchSize);
                int batchEnd = Math.Min(batchStart + batchSize, botCount);

                double baseTime = raw[batchStart];
                double neighbourGap = batchEnd < botCount ? raw[batchEnd] - baseTime : baseTime - raw[Math.Max(starters, batchStart - 1)];
                // +-25% of the gap to the neighbouring batch, so jitter can never reorder batches.
                double jitter = (DeterministicRandom.Unit(eventSeed, 9500 + batchStart) - 0.5d) * 0.5d * Math.Abs(neighbourGap);
                double joinTime = Math.Max(0d, baseTime + jitter);

                if (joinTime > lastJoinLimit)
                {
                    // Late batches move back to the limit; past that there is no room and they are
                    // dropped, leaving the board below BotsMax - which the doc allows.
                    joinTime = lastJoinLimit;
                    if (joinTime <= 0d || joinTime >= eventSeconds)
                    {
                        for (int index = batchStart; index < botCount; index++)
                        {
                            joinSeconds[index] = double.PositiveInfinity;
                        }

                        break;
                    }
                }

                for (int index = batchStart; index < batchEnd; index++)
                {
                    joinSeconds[index] = joinTime;
                }

                batchStart = batchEnd;
            }

            return joinSeconds;
        }

        /// <summary>
        /// Inverse of the join curve: the time by which the board holds <paramref name="count"/>
        /// bots, interpolated between keyframes.
        /// </summary>
        private static double SecondsForCount(List<LeaderboardJoinKeyframeData> curve, int count, double scale)
        {
            if (curve == null || curve.Count == 0)
            {
                return 0d;
            }

            for (int index = 1; index < curve.Count; index++)
            {
                LeaderboardJoinKeyframeData previous = curve[index - 1];
                LeaderboardJoinKeyframeData current = curve[index];
                if (count > current.Count)
                {
                    continue;
                }

                int span = current.Count - previous.Count;
                double t = span <= 0 ? 0d : (count - previous.Count) / (double)span;
                double hour = previous.Hour + (current.Hour - previous.Hour) * t;
                return hour * SecondsPerHour * scale;
            }

            return curve[curve.Count - 1].Hour * SecondsPerHour * scale;
        }

        // =========================================================
        // DAY PLAN
        // =========================================================

        /// <summary>
        /// Effective ranges for one bot: the base preset with any arrival-window override applied.
        /// </summary>
        private struct BotParameters
        {
            public LeaderboardRangeData BurstsPerDay;
            public LeaderboardRangeData LevelsPerBurst;
            public LeaderboardRangeData PaceSeconds;
            public LeaderboardRangeData Skill;
            public LeaderboardRangeData FirstWakeHours;
        }

        private static BotParameters ResolveParameters(
            LeaderboardBotPresetData preset,
            LeaderboardBotConfigData config,
            double joinHours)
        {
            var parameters = new BotParameters
            {
                BurstsPerDay = preset.BurstsPerDay,
                LevelsPerBurst = preset.LevelsPerBurst,
                PaceSeconds = preset.PaceSeconds,
                Skill = preset.Skill,
                FirstWakeHours = preset.FirstWakeHours
            };

            if (config.ArrivalWindows == null)
            {
                return parameters;
            }

            // Half-open and sorted, so the first match is the only match. A gap between windows
            // simply leaves the base preset in place.
            for (int index = 0; index < config.ArrivalWindows.Count; index++)
            {
                LeaderboardBotArrivalWindowData window = config.ArrivalWindows[index];
                bool isLast = index == config.ArrivalWindows.Count - 1;
                bool inside = joinHours >= window.StartHour && (joinHours < window.EndHour || isLast);
                if (!inside || window.Overrides == null)
                {
                    continue;
                }

                for (int overrideIndex = 0; overrideIndex < window.Overrides.Count; overrideIndex++)
                {
                    LeaderboardBotWindowOverrideData entry = window.Overrides[overrideIndex];
                    if (!string.Equals(entry.Preset, preset.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Null means "not overridden" - the base preset's value stands.
                    parameters.BurstsPerDay = entry.BurstsPerDay ?? parameters.BurstsPerDay;
                    parameters.LevelsPerBurst = entry.LevelsPerBurst ?? parameters.LevelsPerBurst;
                    parameters.PaceSeconds = entry.PaceSeconds ?? parameters.PaceSeconds;
                    parameters.Skill = entry.Skill ?? parameters.Skill;
                    break;
                }

                break;
            }

            return parameters;
        }

        private static LeaderboardBotRubberbandData FindRubberband(LeaderboardBotConfigData config, string presetName)
        {
            if (config.Rubberband == null)
            {
                return null;
            }

            for (int index = 0; index < config.Rubberband.Count; index++)
            {
                if (string.Equals(config.Rubberband[index].Preset, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    return config.Rubberband[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Lays out a bot's bursts across its day (or days, for a long event), drawing both level
        /// variants for each one.
        /// </summary>
        private static void BuildBursts(
            BotPlan plan,
            int botSeed,
            LeaderboardBotConfigData config,
            BotParameters parameters,
            double eventSeconds)
        {
            if (double.IsInfinity(plan.JoinSeconds) || plan.JoinSeconds >= eventSeconds)
            {
                // Dropped joiner - present in the roster but never on the board.
                return;
            }

            LeaderboardBotRubberbandData rubberband = FindRubberband(config, plan.PresetName);
            double daySeconds = config.DayHours * SecondsPerHour;
            int dayCount = Math.Max(1, (int)Math.Ceiling((eventSeconds - plan.JoinSeconds) / daySeconds));
            int burstIndex = 0;

            for (int day = 0; day < dayCount; day++)
            {
                // A fresh seed per day, or every day of a long event plays out identically.
                int daySeed = day == 0 ? botSeed : DeterministicRandom.DeriveSeed(botSeed, 20000 + day);
                double dayStart = plan.JoinSeconds + day * daySeconds;
                if (dayStart >= eventSeconds)
                {
                    break;
                }

                int burstCount = Math.Max(1, DeterministicRandom.RangeInt(
                    daySeed,
                    BotDrawSlot.BurstCount,
                    (int)parameters.BurstsPerDay.Min,
                    (int)parameters.BurstsPerDay.Max));

                // Starting bots wake somewhere in the preset's first-wake band; joiners start
                // playing within minutes of appearing, since they just "arrived".
                double firstWakeDelay = plan.IsStartingBot && day == 0
                    ? DeterministicRandom.Range(daySeed, BotDrawSlot.FirstWake, parameters.FirstWakeHours.Min, parameters.FirstWakeHours.Max) * SecondsPerHour
                    : DeterministicRandom.Range(daySeed, BotDrawSlot.FirstWake, config.JoinerFirstWakeMinutes.Min, config.JoinerFirstWakeMinutes.Max) * SecondsPerMinute;

                double wake = dayStart + firstWakeDelay;
                // Average gap that would spread the remaining bursts across the rest of the day.
                double averageGap = Math.Max(1d, (daySeconds - firstWakeDelay) / burstCount);

                for (int burst = 0; burst < burstCount; burst++)
                {
                    if (wake >= dayStart + daySeconds || wake >= eventSeconds)
                    {
                        // Bursts that would wake after the day (or the event) ends are dropped
                        // rather than compressed - a bot simply did less that day.
                        break;
                    }

                    double lead = DeterministicRandom.Range(
                        daySeed,
                        BotDrawSlot.Burst(burstIndex, BotDrawSlot.BurstWakeLead),
                        config.WakeLeadSeconds.Min,
                        config.WakeLeadSeconds.Max);

                    double start = wake + lead;
                    if (start >= eventSeconds)
                    {
                        break;
                    }

                    var planned = new BotBurst
                    {
                        WakeSeconds = wake,
                        StartSeconds = start,
                        NormalLevels = BuildLevels(daySeed, burstIndex, false, plan.Skill, parameters.LevelsPerBurst, parameters.PaceSeconds),
                        RubberbandLevels = BuildLevels(
                            daySeed,
                            burstIndex,
                            true,
                            plan.Skill,
                            rubberband != null ? rubberband.LevelsPerBurst : parameters.LevelsPerBurst,
                            rubberband != null ? rubberband.PaceSeconds : parameters.PaceSeconds)
                    };

                    plan.Bursts.Add(planned);

                    // The next wake is measured from the NORMAL end, so which variant actually
                    // plays can never move a later burst - that is what keeps scores monotonic.
                    double end = planned.EndSeconds(false);
                    double away = Math.Max(
                        config.MinAwayHours * SecondsPerHour,
                        averageGap * DeterministicRandom.Range(daySeed, BotDrawSlot.Burst(burstIndex, BotDrawSlot.BurstAwayJitter), 0.6d, 1.4d));

                    wake = end + away;
                    burstIndex++;
                }
            }
        }

        private static BotLevel[] BuildLevels(
            int daySeed,
            int burstIndex,
            bool rubberband,
            double skill,
            LeaderboardRangeData levelsPerBurst,
            LeaderboardRangeData paceSeconds)
        {
            int count = Math.Max(1, DeterministicRandom.RangeInt(
                daySeed,
                BotDrawSlot.Burst(burstIndex, rubberband ? BotDrawSlot.BurstRubberbandLevelCount : BotDrawSlot.BurstNormalLevelCount),
                (int)levelsPerBurst.Min,
                (int)levelsPerBurst.Max));

            var levels = new BotLevel[count];
            for (int index = 0; index < count; index++)
            {
                double pace = DeterministicRandom.Range(
                    daySeed,
                    BotDrawSlot.Level(burstIndex, index, rubberband, BotDrawSlot.LevelPace),
                    paceSeconds.Min,
                    paceSeconds.Max);

                double roll = DeterministicRandom.Unit(daySeed, BotDrawSlot.Level(burstIndex, index, rubberband, BotDrawSlot.LevelFish));
                levels[index] = new BotLevel(pace, FishForLevel(skill, roll));
            }

            return levels;
        }

        /// <summary>
        /// fish = floor(s) + [u &lt; s - floor(s)], clamped to 1..3.
        ///
        /// Gives a mean of exactly s: skill 2.5 pays 2 half the time and 3 the other half, and an
        /// integer skill always pays that number. Cheaper and more controllable than sampling a
        /// distribution, and it keeps every level a win, as the GDD implies.
        /// </summary>
        public static int FishForLevel(double skill, double uniformRoll)
        {
            double clamped = Math.Min(3d, Math.Max(1d, skill));
            int whole = (int)Math.Floor(clamped);
            double fraction = clamped - whole;
            int fish = whole + (uniformRoll < fraction ? 1 : 0);
            return Math.Min(3, Math.Max(1, fish));
        }
    }
}
