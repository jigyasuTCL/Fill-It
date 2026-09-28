using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.GameConfig
{
    /// <summary>An inclusive min-max range. Swapped on normalize rather than trusted.</summary>
    [Serializable]
    public sealed class LeaderboardRangeData
    {
        [JsonProperty("min")]
        public float Min;

        [JsonProperty("max")]
        public float Max;

        public LeaderboardRangeData() { }

        public LeaderboardRangeData(float min, float max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Ordered and floored. A designer entering 165-100 means 100-165, and silently swapping is
        /// far better than a range that draws nothing; the caller logs it.
        /// </summary>
        public LeaderboardRangeData CloneNormalized(float floor, out bool wasSwapped)
        {
            float min = Min;
            float max = Max;
            wasSwapped = min > max;
            if (wasSwapped)
            {
                (min, max) = (max, min);
            }

            return new LeaderboardRangeData(Math.Max(floor, min), Math.Max(floor, max));
        }

        public LeaderboardRangeData Clamped(float lower, float upper)
        {
            float min = Math.Min(Math.Max(Min, lower), upper);
            float max = Math.Min(Math.Max(Max, lower), upper);
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return new LeaderboardRangeData(min, max);
        }
    }

    /// <summary>
    /// One archetype of bot - how often it plays, how long for, and how well.
    ///
    /// Every number is the GDD's dummy value. The mechanics are fixed by the planner; these only
    /// tune it.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardBotPresetData
    {
        [JsonProperty("name")]
        public string Name = string.Empty;

        /// <summary>Share of the roster. Normalized across presets, so these need not sum to 100.</summary>
        [JsonProperty("weightPercent")]
        public float WeightPercent = 20f;

        [JsonProperty("burstsPerDay")]
        public LeaderboardRangeData BurstsPerDay = new LeaderboardRangeData(1f, 2f);

        [JsonProperty("levelsPerBurst")]
        public LeaderboardRangeData LevelsPerBurst = new LeaderboardRangeData(1f, 2f);

        /// <summary>Seconds per level. Bigger is slower.</summary>
        [JsonProperty("paceSeconds")]
        public LeaderboardRangeData PaceSeconds = new LeaderboardRangeData(130f, 220f);

        /// <summary>Mean fish per level, 1-3. Drawn once per bot.</summary>
        [JsonProperty("skill")]
        public LeaderboardRangeData Skill = new LeaderboardRangeData(1.6f, 2.3f);

        /// <summary>Hours after the event opens before this bot first plays. Starting bots only.</summary>
        [JsonProperty("firstWakeHours")]
        public LeaderboardRangeData FirstWakeHours = new LeaderboardRangeData(0f, 8f);
    }

    /// <summary>
    /// What a preset does instead while the player is away: fewer levels, slower pace.
    ///
    /// Weight, burst count and skill are deliberately NOT overridable - changing those would move
    /// burst start times, and a burst that moves can carry a score backwards.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardBotRubberbandData
    {
        [JsonProperty("preset")]
        public string Preset = string.Empty;

        [JsonProperty("levelsPerBurst")]
        public LeaderboardRangeData LevelsPerBurst = new LeaderboardRangeData(1f, 1f);

        [JsonProperty("paceSeconds")]
        public LeaderboardRangeData PaceSeconds = new LeaderboardRangeData(260f, 400f);
    }

    /// <summary>
    /// A per-preset override that applies to bots joining within one stretch of the event. Any
    /// field left null falls back to the base preset.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardBotWindowOverrideData
    {
        [JsonProperty("preset")]
        public string Preset = string.Empty;

        [JsonProperty("burstsPerDay")]
        public LeaderboardRangeData BurstsPerDay;

        [JsonProperty("levelsPerBurst")]
        public LeaderboardRangeData LevelsPerBurst;

        [JsonProperty("paceSeconds")]
        public LeaderboardRangeData PaceSeconds;

        [JsonProperty("skill")]
        public LeaderboardRangeData Skill;
    }

    /// <summary>
    /// Bots joining in [StartHour, EndHour) use these overrides. Half-open, sorted, first match
    /// wins; a gap simply uses the base presets.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardBotArrivalWindowData
    {
        [JsonProperty("startHour")]
        public float StartHour;

        [JsonProperty("endHour")]
        public float EndHour;

        [JsonProperty("overrides")]
        public List<LeaderboardBotWindowOverrideData> Overrides = new List<LeaderboardBotWindowOverrideData>();
    }

    /// <summary>One point on the join curve: by Hour, the board holds Count bots.</summary>
    [Serializable]
    public sealed class LeaderboardJoinKeyframeData
    {
        [JsonProperty("hour")]
        public float Hour;

        [JsonProperty("count")]
        public int Count;

        public LeaderboardJoinKeyframeData() { }

        public LeaderboardJoinKeyframeData(float hour, int count)
        {
            Hour = hour;
            Count = count;
        }
    }

    /// <summary>
    /// All bot tuning for the leaderboard event, normalized on load like the rest of GlobalConfig.
    ///
    /// Defaults are the GDD's dummy values, so a project that never touches this still gets a
    /// working board. Normalization is not cosmetic here: the planner assumes ordered ranges, a
    /// monotonic join curve and weights that sum to 1, and would otherwise produce a board that is
    /// merely strange rather than obviously broken.
    /// </summary>
    [Serializable]
    public sealed class LeaderboardBotConfigData
    {
        // Floors the Config edge-case table calls for.
        private const float MinPaceSeconds = 10f;
        private const float MinSkill = 1f;
        private const float MaxSkill = 3f;

        [JsonProperty("presets")]
        public List<LeaderboardBotPresetData> Presets = DefaultPresets();

        [JsonProperty("rubberband")]
        public List<LeaderboardBotRubberbandData> Rubberband = DefaultRubberband();

        [JsonProperty("arrivalWindows")]
        public List<LeaderboardBotArrivalWindowData> ArrivalWindows = new List<LeaderboardBotArrivalWindowData>();

        // --- roster and joining ---

        [JsonProperty("botsStart")]
        public int BotsStart = 10;

        [JsonProperty("botsMax")]
        public int BotsMax = 50;

        [JsonProperty("joinCurve")]
        public List<LeaderboardJoinKeyframeData> JoinCurve = DefaultJoinCurve();

        /// <summary>Event length the curve was authored for. Keyframe hours scale by EventHours / this.</summary>
        [JsonProperty("curveReferenceHours")]
        public float CurveReferenceHours = 24f;

        [JsonProperty("joinBatchSize")]
        public LeaderboardRangeData JoinBatchSize = new LeaderboardRangeData(1f, 3f);

        /// <summary>No bot joins in the final stretch - a board still filling at the whistle reads as broken.</summary>
        [JsonProperty("lastJoinBeforeEndMinutes")]
        public float LastJoinBeforeEndMinutes = 60f;

        // --- a bot's day ---

        [JsonProperty("dayHours")]
        public float DayHours = 24f;

        /// <summary>Shortest gap between two bursts. Stops a "day" collapsing into one long session.</summary>
        [JsonProperty("minAwayHours")]
        public float MinAwayHours = 2f;

        /// <summary>Online but not yet playing, before each burst.</summary>
        [JsonProperty("wakeLeadSeconds")]
        public LeaderboardRangeData WakeLeadSeconds = new LeaderboardRangeData(30f, 180f);

        /// <summary>Joiners start playing soon after they appear, rather than drawing a first-wake in hours.</summary>
        [JsonProperty("joinerFirstWakeMinutes")]
        public LeaderboardRangeData JoinerFirstWakeMinutes = new LeaderboardRangeData(0f, 30f);

        // --- status ---

        [JsonProperty("onlineTailMinutes")]
        public float OnlineTailMinutes = 5f;

        // Presence the bursts alone do not produce.
        //
        // A bot plays two or three short bursts across a whole day, so the share genuinely mid-
        // burst at any instant is a percent or two - on a board of fifty that is often nobody, and
        // the list reads as a graveyard of "3h ago". These put a believable number of people
        // around between their own sessions: browsing the app without playing (Online), or playing
        // something the board does not score (Playing).
        //
        // Cosmetic only. Neither adds a single point, so ranking, settlement and rewards are
        // untouched - this changes what the status column says and nothing else.
        [JsonProperty("idleOnlinePercent")]
        public float IdleOnlinePercent = 16f;

        [JsonProperty("idlePlayingPercent")]
        public float IdlePlayingPercent = 6f;

        // How long one set of idle-present bots stays that way before it reshuffles. Short enough
        // that the board changes while you watch it, long enough that a status does not flicker
        // between two refreshes.
        [JsonProperty("presenceBucketMinutes")]
        public float PresenceBucketMinutes = 4f;

        /// <summary>Fake last-seen before the open, so starting bots look like people who were already here.</summary>
        [JsonProperty("startingBotsLastSeenHours")]
        public LeaderboardRangeData StartingBotsLastSeenHours = new LeaderboardRangeData(1f, 8f);

        // --- rubberbanding ---

        /// <summary>How long the player must be away before bots start slowing down.</summary>
        [JsonProperty("rubberbandAfterHours")]
        public float RubberbandAfterHours = 3f;

        // --- identity ---

        /// <summary>Share of bots using pool names; the rest get generated 6-character ids.</summary>
        [JsonProperty("namedBotRatio")]
        public float NamedBotRatio = 0.3f;

        public LeaderboardBotConfigData CloneNormalized()
        {
            var clone = new LeaderboardBotConfigData
            {
                BotsMax = Math.Max(1, BotsMax),
                CurveReferenceHours = Math.Max(0.1f, CurveReferenceHours),
                LastJoinBeforeEndMinutes = Math.Max(0f, LastJoinBeforeEndMinutes),
                DayHours = Math.Max(0.1f, DayHours),
                MinAwayHours = Math.Max(0f, MinAwayHours),
                OnlineTailMinutes = Math.Max(0f, OnlineTailMinutes),
                // Clamped as a pair: together they can never claim more than the whole board.
                IdleOnlinePercent = Math.Min(100f, Math.Max(0f, IdleOnlinePercent)),
                IdlePlayingPercent = Math.Min(100f, Math.Max(0f, IdlePlayingPercent)),
                // Floored above zero - a bucket of zero would divide by nothing.
                PresenceBucketMinutes = Math.Max(0.1f, PresenceBucketMinutes),
                RubberbandAfterHours = Math.Max(0f, RubberbandAfterHours),
                NamedBotRatio = Math.Min(1f, Math.Max(0f, NamedBotRatio)),
                JoinBatchSize = NormalizeRange(JoinBatchSize, 1f, "joinBatchSize"),
                WakeLeadSeconds = NormalizeRange(WakeLeadSeconds, 0f, "wakeLeadSeconds"),
                JoinerFirstWakeMinutes = NormalizeRange(JoinerFirstWakeMinutes, 0f, "joinerFirstWakeMinutes"),
                StartingBotsLastSeenHours = NormalizeRange(StartingBotsLastSeenHours, 0f, "startingBotsLastSeenHours"),
                Presets = NormalizePresets(Presets)
            };

            // Never more starting bots than the board will ever hold.
            clone.BotsStart = Math.Min(Math.Max(0, BotsStart), clone.BotsMax);
            clone.Rubberband = NormalizeRubberband(Rubberband, clone.Presets);
            clone.ArrivalWindows = NormalizeArrivalWindows(ArrivalWindows, clone.Presets);
            clone.JoinCurve = NormalizeJoinCurve(JoinCurve, clone.BotsStart, clone.BotsMax);
            return clone;
        }

        private static LeaderboardRangeData NormalizeRange(LeaderboardRangeData range, float floor, string label)
        {
            LeaderboardRangeData source = range ?? new LeaderboardRangeData(floor, floor);
            LeaderboardRangeData normalized = source.CloneNormalized(floor, out bool swapped);
            if (swapped)
            {
                Warn($"range '{label}' had min above max and was swapped.");
            }

            return normalized;
        }

        private static List<LeaderboardBotPresetData> NormalizePresets(List<LeaderboardBotPresetData> presets)
        {
            var normalized = new List<LeaderboardBotPresetData>();
            if (presets != null)
            {
                for (int index = 0; index < presets.Count; index++)
                {
                    LeaderboardBotPresetData preset = presets[index];
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    {
                        continue;
                    }

                    normalized.Add(new LeaderboardBotPresetData
                    {
                        Name = preset.Name.Trim(),
                        WeightPercent = Math.Max(0f, preset.WeightPercent),
                        // At least one burst and one level, or a preset contributes nothing and the
                        // roster quietly shrinks.
                        BurstsPerDay = NormalizeRange(preset.BurstsPerDay, 1f, preset.Name + ".burstsPerDay"),
                        LevelsPerBurst = NormalizeRange(preset.LevelsPerBurst, 1f, preset.Name + ".levelsPerBurst"),
                        PaceSeconds = NormalizeRange(preset.PaceSeconds, MinPaceSeconds, preset.Name + ".paceSeconds"),
                        Skill = NormalizeRange(preset.Skill, MinSkill, preset.Name + ".skill").Clamped(MinSkill, MaxSkill),
                        FirstWakeHours = NormalizeRange(preset.FirstWakeHours, 0f, preset.Name + ".firstWakeHours")
                    });
                }
            }

            if (normalized.Count == 0)
            {
                Warn("no bot presets configured - falling back to the built-in set.");
                return DefaultPresets();
            }

            // Weights are shares, so what matters is their ratio. All-zero means the designer never
            // set them; equal shares is the only sane reading.
            float total = 0f;
            for (int index = 0; index < normalized.Count; index++)
            {
                total += normalized[index].WeightPercent;
            }

            if (total <= 0f)
            {
                Warn("all preset weights were zero - using equal shares.");
                for (int index = 0; index < normalized.Count; index++)
                {
                    normalized[index].WeightPercent = 100f / normalized.Count;
                }
            }
            else if (Math.Abs(total - 100f) > 0.001f)
            {
                for (int index = 0; index < normalized.Count; index++)
                {
                    normalized[index].WeightPercent = normalized[index].WeightPercent / total * 100f;
                }
            }

            return normalized;
        }

        private static List<LeaderboardBotRubberbandData> NormalizeRubberband(
            List<LeaderboardBotRubberbandData> rubberband,
            List<LeaderboardBotPresetData> presets)
        {
            var normalized = new List<LeaderboardBotRubberbandData>();
            if (rubberband == null)
            {
                return normalized;
            }

            for (int index = 0; index < rubberband.Count; index++)
            {
                LeaderboardBotRubberbandData entry = rubberband[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Preset))
                {
                    continue;
                }

                if (!HasPreset(presets, entry.Preset))
                {
                    Warn($"rubberband override names unknown preset '{entry.Preset}' - ignored.");
                    continue;
                }

                normalized.Add(new LeaderboardBotRubberbandData
                {
                    Preset = entry.Preset.Trim(),
                    LevelsPerBurst = NormalizeRange(entry.LevelsPerBurst, 1f, entry.Preset + ".rubberband.levelsPerBurst"),
                    PaceSeconds = NormalizeRange(entry.PaceSeconds, MinPaceSeconds, entry.Preset + ".rubberband.paceSeconds")
                });
            }

            return normalized;
        }

        private static List<LeaderboardBotArrivalWindowData> NormalizeArrivalWindows(
            List<LeaderboardBotArrivalWindowData> windows,
            List<LeaderboardBotPresetData> presets)
        {
            var normalized = new List<LeaderboardBotArrivalWindowData>();
            if (windows == null)
            {
                return normalized;
            }

            for (int index = 0; index < windows.Count; index++)
            {
                LeaderboardBotArrivalWindowData window = windows[index];
                if (window == null)
                {
                    continue;
                }

                float start = Math.Max(0f, window.StartHour);
                float end = Math.Max(start, window.EndHour);

                var overrides = new List<LeaderboardBotWindowOverrideData>();
                if (window.Overrides != null)
                {
                    for (int overrideIndex = 0; overrideIndex < window.Overrides.Count; overrideIndex++)
                    {
                        LeaderboardBotWindowOverrideData entry = window.Overrides[overrideIndex];
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Preset))
                        {
                            continue;
                        }

                        if (!HasPreset(presets, entry.Preset))
                        {
                            Warn($"arrival-window override names unknown preset '{entry.Preset}' - ignored.");
                            continue;
                        }

                        // Null stays null: an omitted field means "fall back to the base preset",
                        // which is different from a field set to the preset's own values.
                        overrides.Add(new LeaderboardBotWindowOverrideData
                        {
                            Preset = entry.Preset.Trim(),
                            BurstsPerDay = entry.BurstsPerDay == null ? null : NormalizeRange(entry.BurstsPerDay, 1f, "window.burstsPerDay"),
                            LevelsPerBurst = entry.LevelsPerBurst == null ? null : NormalizeRange(entry.LevelsPerBurst, 1f, "window.levelsPerBurst"),
                            PaceSeconds = entry.PaceSeconds == null ? null : NormalizeRange(entry.PaceSeconds, MinPaceSeconds, "window.paceSeconds"),
                            Skill = entry.Skill == null ? null : NormalizeRange(entry.Skill, MinSkill, "window.skill").Clamped(MinSkill, MaxSkill)
                        });
                    }
                }

                normalized.Add(new LeaderboardBotArrivalWindowData
                {
                    StartHour = start,
                    EndHour = end,
                    Overrides = overrides
                });
            }

            // Sorted so "first match wins" is a stable rule rather than authoring order.
            normalized.Sort((left, right) => left.StartHour.CompareTo(right.StartHour));
            return normalized;
        }

        /// <summary>
        /// Makes the curve usable: sorted by hour, never decreasing, opening at BotsStart and
        /// closing at BotsMax. The planner reads a bot's join time off this curve, so a count that
        /// dips would ask for a bot to un-join.
        /// </summary>
        private static List<LeaderboardJoinKeyframeData> NormalizeJoinCurve(
            List<LeaderboardJoinKeyframeData> curve,
            int botsStart,
            int botsMax)
        {
            var normalized = new List<LeaderboardJoinKeyframeData>();
            if (curve != null)
            {
                for (int index = 0; index < curve.Count; index++)
                {
                    LeaderboardJoinKeyframeData frame = curve[index];
                    if (frame != null)
                    {
                        normalized.Add(new LeaderboardJoinKeyframeData(Math.Max(0f, frame.Hour), Math.Max(0, frame.Count)));
                    }
                }
            }

            if (normalized.Count == 0)
            {
                normalized = DefaultJoinCurve();
            }

            normalized.Sort((left, right) => left.Hour.CompareTo(right.Hour));

            normalized[0] = new LeaderboardJoinKeyframeData(0f, botsStart);
            for (int index = 1; index < normalized.Count; index++)
            {
                int atLeast = Math.Max(normalized[index].Count, normalized[index - 1].Count);
                normalized[index] = new LeaderboardJoinKeyframeData(normalized[index].Hour, Math.Min(atLeast, botsMax));
            }

            int last = normalized.Count - 1;
            normalized[last] = new LeaderboardJoinKeyframeData(normalized[last].Hour, botsMax);
            return normalized;
        }

        private static bool HasPreset(List<LeaderboardBotPresetData> presets, string name)
        {
            if (presets == null)
            {
                return false;
            }

            for (int index = 0; index < presets.Count; index++)
            {
                if (string.Equals(presets[index].Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Warn(string message)
        {
            UnityEngine.Debug.LogWarning("[Leaderboard] Bot config: " + message);
        }

        // =========================================================
        // GDD DEFAULTS
        // =========================================================

        private static List<LeaderboardBotPresetData> DefaultPresets()
        {
            return new List<LeaderboardBotPresetData>
            {
                new LeaderboardBotPresetData
                {
                    Name = "grinder", WeightPercent = 8f,
                    BurstsPerDay = new LeaderboardRangeData(3f, 3f),
                    LevelsPerBurst = new LeaderboardRangeData(2f, 3f),
                    PaceSeconds = new LeaderboardRangeData(100f, 165f),
                    Skill = new LeaderboardRangeData(2.35f, 2.85f),
                    FirstWakeHours = new LeaderboardRangeData(0f, 3f)
                },
                new LeaderboardBotPresetData
                {
                    Name = "steady", WeightPercent = 26f,
                    BurstsPerDay = new LeaderboardRangeData(2f, 3f),
                    LevelsPerBurst = new LeaderboardRangeData(1f, 3f),
                    PaceSeconds = new LeaderboardRangeData(110f, 190f),
                    Skill = new LeaderboardRangeData(2.00f, 2.60f),
                    FirstWakeHours = new LeaderboardRangeData(0f, 5f)
                },
                new LeaderboardBotPresetData
                {
                    Name = "casual", WeightPercent = 38f,
                    BurstsPerDay = new LeaderboardRangeData(1f, 2f),
                    LevelsPerBurst = new LeaderboardRangeData(1f, 2f),
                    PaceSeconds = new LeaderboardRangeData(130f, 220f),
                    Skill = new LeaderboardRangeData(1.60f, 2.30f),
                    FirstWakeHours = new LeaderboardRangeData(0f, 8f)
                },
                new LeaderboardBotPresetData
                {
                    Name = "dipper", WeightPercent = 20f,
                    BurstsPerDay = new LeaderboardRangeData(1f, 1f),
                    LevelsPerBurst = new LeaderboardRangeData(1f, 2f),
                    PaceSeconds = new LeaderboardRangeData(150f, 250f),
                    Skill = new LeaderboardRangeData(1.35f, 2.10f),
                    FirstWakeHours = new LeaderboardRangeData(0f, 10f)
                },
                new LeaderboardBotPresetData
                {
                    Name = "sleeper", WeightPercent = 8f,
                    BurstsPerDay = new LeaderboardRangeData(1f, 2f),
                    LevelsPerBurst = new LeaderboardRangeData(1f, 3f),
                    PaceSeconds = new LeaderboardRangeData(120f, 200f),
                    Skill = new LeaderboardRangeData(1.85f, 2.50f),
                    FirstWakeHours = new LeaderboardRangeData(8f, 14f)
                }
            };
        }

        private static List<LeaderboardBotRubberbandData> DefaultRubberband()
        {
            return new List<LeaderboardBotRubberbandData>
            {
                new LeaderboardBotRubberbandData { Preset = "grinder", LevelsPerBurst = new LeaderboardRangeData(1f, 1f), PaceSeconds = new LeaderboardRangeData(260f, 400f) },
                new LeaderboardBotRubberbandData { Preset = "steady",  LevelsPerBurst = new LeaderboardRangeData(1f, 1f), PaceSeconds = new LeaderboardRangeData(260f, 500f) },
                new LeaderboardBotRubberbandData { Preset = "casual",  LevelsPerBurst = new LeaderboardRangeData(1f, 1f), PaceSeconds = new LeaderboardRangeData(360f, 400f) },
                new LeaderboardBotRubberbandData { Preset = "dipper",  LevelsPerBurst = new LeaderboardRangeData(1f, 1f), PaceSeconds = new LeaderboardRangeData(560f, 800f) },
                new LeaderboardBotRubberbandData { Preset = "sleeper", LevelsPerBurst = new LeaderboardRangeData(1f, 1f), PaceSeconds = new LeaderboardRangeData(360f, 500f) }
            };
        }

        /// <summary>
        /// The GDD's hour table, read as a cumulative curve: by hour H the board holds N bots.
        /// Normalization pins the first point to BotsStart and the last to BotsMax.
        /// </summary>
        private static List<LeaderboardJoinKeyframeData> DefaultJoinCurve()
        {
            return new List<LeaderboardJoinKeyframeData>
            {
                new LeaderboardJoinKeyframeData(0f, 10),
                new LeaderboardJoinKeyframeData(1f, 18),
                new LeaderboardJoinKeyframeData(5f, 26),
                new LeaderboardJoinKeyframeData(11f, 36),
                new LeaderboardJoinKeyframeData(19f, 45),
                new LeaderboardJoinKeyframeData(24f, 50)
            };
        }
    }
}
