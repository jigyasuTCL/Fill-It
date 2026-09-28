namespace Shared.Leaderboard.Bots
{
    /// <summary>
    /// Counter-based random draws: every value is a pure hash of (seed, slot), never a stream.
    ///
    /// This is what makes the whole bot system reproducible. A stream generator would mean the
    /// Nth value depends on how many draws came before it, so adding one new draw anywhere would
    /// reshuffle every bot planned after it - and a board that changes shape on an app update is
    /// indistinguishable from a bug. With a fixed slot per draw, a new draw takes a new slot and
    /// disturbs nothing.
    ///
    /// Moved out of LeaderboardManager.Hash, unchanged, so the planner can live in plain C#.
    /// Deliberately NOT System.Random or GetHashCode: neither is guaranteed stable across
    /// runtimes or Unity versions, and the board has to survive an engine upgrade.
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>
        /// Avalanche hash of a seed and a slot. Same pair, same value, on every platform and every
        /// run - that is the only property anything here relies on.
        /// </summary>
        public static uint Hash(int seed, int slot)
        {
            unchecked
            {
                uint value = (uint)seed * 2654435761u ^ (uint)(slot * 40503);
                value ^= value >> 15;
                value *= 2246822519u;
                value ^= value >> 13;
                value *= 3266489917u;
                value ^= value >> 16;
                return value;
            }
        }

        /// <summary>Uniform in [0, 1). Never reaches 1, so it is safe as an index multiplier.</summary>
        public static double Unit(int seed, int slot)
        {
            // Divided by 2^32 rather than (max - 1), so the distribution stays flat and the top of
            // the range is excluded.
            return Hash(seed, slot) / 4294967296d;
        }

        /// <summary>Uniform in [min, max]. Swapped rather than throwing when a caller has them backwards.</summary>
        public static double Range(int seed, int slot, double min, double max)
        {
            if (min > max)
            {
                (min, max) = (max, min);
            }

            return min + (max - min) * Unit(seed, slot);
        }

        /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
        public static int RangeInt(int seed, int slot, int minInclusive, int maxInclusive)
        {
            if (minInclusive > maxInclusive)
            {
                (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
            }

            int span = maxInclusive - minInclusive + 1;
            return minInclusive + (int)(Unit(seed, slot) * span);
        }

        /// <summary>
        /// A derived seed for a nested scope - the event from the install, a bot from the event, a
        /// day from the bot. Keeps each scope's draws independent of its siblings.
        /// </summary>
        public static int DeriveSeed(int seed, int slot)
        {
            return unchecked((int)Hash(seed, slot));
        }
    }

    /// <summary>
    /// Every draw's slot number, in one place.
    ///
    /// These are part of the save format in everything but name: change one and every existing
    /// board re-rolls. Add new draws with NEW numbers at the end; never renumber or reuse.
    ///
    /// Per-burst and per-level draws are spaced out by stride so burst k's levels can never
    /// collide with burst k+1's, however many levels a burst turns out to have.
    /// </summary>
    public static class BotDrawSlot
    {
        // --- per bot, drawn once ---
        public const int Preset = 1;
        public const int Skill = 2;
        public const int Identity = 3;
        public const int FirstWake = 4;
        public const int PreOpenLastSeen = 5;
        public const int JoinJitter = 6;

        // --- per day ---
        public const int BurstCount = 10;

        // --- per burst k: BurstBase + k * BurstStride + offset ---
        public const int BurstBase = 1000;
        public const int BurstStride = 64;
        public const int BurstWakeLead = 0;
        public const int BurstAwayJitter = 1;
        public const int BurstNormalLevelCount = 2;
        public const int BurstRubberbandLevelCount = 3;

        // Level j inside burst k. Normal and rubberband variants draw from separate sub-ranges so
        // neither shifts the other - the doc's "neither shifts the other" requirement.
        public const int BurstNormalLevelBase = 8;
        public const int BurstRubberbandLevelBase = 36;

        // Two draws per level: pace and the fish roll.
        public const int LevelPace = 0;
        public const int LevelFish = 1;
        public const int LevelStride = 2;

        public static int Burst(int burstIndex, int offset)
        {
            return BurstBase + burstIndex * BurstStride + offset;
        }

        public static int Level(int burstIndex, int levelIndex, bool rubberband, int offset)
        {
            int variantBase = rubberband ? BurstRubberbandLevelBase : BurstNormalLevelBase;
            return Burst(burstIndex, variantBase + levelIndex * LevelStride + offset);
        }
    }
}
