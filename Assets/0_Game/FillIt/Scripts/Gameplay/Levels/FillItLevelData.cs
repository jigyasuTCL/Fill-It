using System;

namespace FillIt.Levels
{
    /// <summary>
    /// One Fillomino puzzle. Clues and Solution are row-major, Size * Size long. A clue of 0 is an
    /// empty cell for the player to fill; every non-zero clue must match the solution.
    /// </summary>
    [Serializable]
    public sealed class FillItLevelData
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Difficulty = string.Empty;
        public int Size;
        public int? LevelMistakeLimit;
        // Timer mode: seconds the player has to finish this level. Null = not authored
        // (the runtime falls back to GameRulesConfig's per-empty-cell estimate).
        public int? TimeLimitSeconds;
        public int[] Clues = Array.Empty<int>();
        public int[] Solution = Array.Empty<int>();

        /// <summary>
        /// Default Timer-mode limit for a level with no authored timeLimitSeconds:
        /// empty cells x secondsPerEmptyCell, at least minimumSeconds, rounded up to 5 s.
        /// </summary>
        public int EstimateTimeLimitSeconds(float secondsPerEmptyCell, float minimumSeconds)
        {
            int emptyCells = 0;
            if (Clues != null)
            {
                foreach (int clue in Clues)
                {
                    if (clue == 0)
                    {
                        emptyCells++;
                    }
                }
            }

            double seconds = Math.Max(minimumSeconds, emptyCells * secondsPerEmptyCell);
            return (int)Math.Ceiling(seconds / 5d) * 5;
        }

        public int GetSolutionValue(int index)
        {
            return Solution != null && index >= 0 && index < Solution.Length ? Solution[index] : 0;
        }
    }
}
