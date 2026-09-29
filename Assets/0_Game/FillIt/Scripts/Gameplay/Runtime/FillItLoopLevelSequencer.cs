using System.Collections.Generic;
using System.Text;
using Shared.GameConfig;
using UnityEngine;

namespace CatFlap.Runtime
{
    // Builds one "loop set" of level numbers once the player has cleared every level in
    // GlobalConfig.TotalLevels - see CatFlapGameplayManager.ResolveLoopLevelNumber. Pure/stateless:
    // every call reshuffles from scratch and returns a brand-new set, so the caller (which persists
    // the resolved sequence) decides when a fresh set is actually needed.
    public static class CatFlapLoopLevelSequencer
    {
        private enum Difficulty
        {
            Easy,
            Medium,
            Hard
        }

        public static bool HasAnyLevels(LoopLevelConfig config)
        {
            return config != null &&
                ((config.EasyLevels?.Length ?? 0) > 0 ||
                 (config.MediumLevels?.Length ?? 0) > 0 ||
                 (config.HardLevels?.Length ?? 0) > 0);
        }

        // Shuffles EasyLevels/MediumLevels/HardLevels independently, then interleaves them
        // according to config.Pattern, repeating the pattern as many times as the pools allow
        // without reusing an entry. Any remainder left over when a pool doesn't divide evenly by
        // the pattern's per-cycle count for that difficulty is left unused for this set - it goes
        // back into the pool for the next reshuffle. Can return an empty list (no levels configured
        // for a difficulty the pattern actually requires) - callers must not loop on that.
        public static List<int> GenerateSet(LoopLevelConfig config)
        {
            var result = new List<int>();
            if (config == null)
            {
                return result;
            }

            List<int> easy = Shuffle(config.EasyLevels);
            List<int> medium = Shuffle(config.MediumLevels);
            List<int> hard = Shuffle(config.HardLevels);

            List<Difficulty> pattern = ParsePattern(config.Pattern);
            if (pattern.Count == 0)
            {
                return result;
            }

            int easyPerCycle = CountOf(pattern, Difficulty.Easy);
            int mediumPerCycle = CountOf(pattern, Difficulty.Medium);
            int hardPerCycle = CountOf(pattern, Difficulty.Hard);

            int cycles = MinCycles(
                MinCycles(CyclesFor(easy.Count, easyPerCycle), CyclesFor(medium.Count, mediumPerCycle)),
                CyclesFor(hard.Count, hardPerCycle));

            if (cycles <= 0)
            {
                return result;
            }

            int easyIndex = 0;
            int mediumIndex = 0;
            int hardIndex = 0;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int index = 0; index < pattern.Count; index++)
                {
                    switch (pattern[index])
                    {
                        case Difficulty.Easy:
                            result.Add(easy[easyIndex++]);
                            break;
                        case Difficulty.Medium:
                            result.Add(medium[mediumIndex++]);
                            break;
                        case Difficulty.Hard:
                            result.Add(hard[hardIndex++]);
                            break;
                    }
                }
            }

            return result;
        }

        // int.MaxValue means "this difficulty isn't used by the pattern at all" - unconstrained,
        // so it never becomes the limiting factor in MinCycles below.
        private static int CyclesFor(int poolCount, int perCycle)
        {
            return perCycle <= 0 ? int.MaxValue : poolCount / perCycle;
        }

        private static int MinCycles(int a, int b)
        {
            return a < b ? a : b;
        }

        private static int CountOf(List<Difficulty> pattern, Difficulty difficulty)
        {
            int count = 0;
            for (int index = 0; index < pattern.Count; index++)
            {
                if (pattern[index] == difficulty)
                {
                    count++;
                }
            }

            return count;
        }

        // "2e_2m_1h" -> [Easy, Easy, Medium, Medium, Hard]. Falls back to that same default shape
        // if the configured pattern is missing/malformed (unknown letter, non-digit count, etc.),
        // so a bad config value degrades gracefully instead of producing an empty loop forever.
        private static List<Difficulty> ParsePattern(string pattern)
        {
            List<Difficulty> parsed = TryParsePattern(pattern);
            return parsed ?? TryParsePattern(LoopLevelConfig.DefaultPattern) ?? new List<Difficulty>();
        }

        private static List<Difficulty> TryParsePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return null;
            }

            var slots = new List<Difficulty>();
            string[] tokens = pattern.Split('_');
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                string token = tokens[tokenIndex].Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var digits = new StringBuilder();
                int charIndex = 0;
                while (charIndex < token.Length && char.IsDigit(token[charIndex]))
                {
                    digits.Append(token[charIndex]);
                    charIndex++;
                }

                if (digits.Length == 0 || charIndex >= token.Length || !int.TryParse(digits.ToString(), out int count) || count <= 0)
                {
                    return null;
                }

                Difficulty difficulty;
                switch (char.ToLowerInvariant(token[charIndex]))
                {
                    case 'e':
                        difficulty = Difficulty.Easy;
                        break;
                    case 'm':
                        difficulty = Difficulty.Medium;
                        break;
                    case 'h':
                        difficulty = Difficulty.Hard;
                        break;
                    default:
                        return null;
                }

                for (int repeat = 0; repeat < count; repeat++)
                {
                    slots.Add(difficulty);
                }
            }

            return slots.Count > 0 ? slots : null;
        }

        private static List<int> Shuffle(int[] source)
        {
            var shuffled = new List<int>(source ?? System.Array.Empty<int>());
            for (int index = shuffled.Count - 1; index > 0; index--)
            {
                int swapIndex = Random.Range(0, index + 1);
                (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
            }

            return shuffled;
        }
    }
}
