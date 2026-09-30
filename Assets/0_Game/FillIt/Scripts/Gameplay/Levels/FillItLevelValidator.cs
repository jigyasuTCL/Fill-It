using System.Collections.Generic;
using FillIt.Models;
using FillIt.Rules;

namespace FillIt.Levels
{
    public static class FillItLevelValidator
    {
        public const int MinSize = 2;
        public const int MaxSize = 12;

        public static List<string> Validate(FillItLevelData level)
        {
            var errors = new List<string>();
            if (level == null)
            {
                errors.Add("Level is missing.");
                return errors;
            }

            string label = string.IsNullOrEmpty(level.Id) ? "Level" : $"Level '{level.Id}'";
            if (level.Size < MinSize || level.Size > MaxSize)
            {
                errors.Add($"{label}: size must be between {MinSize} and {MaxSize} (was {level.Size}).");
                return errors;
            }

            int count = level.Size * level.Size;
            if (level.Solution == null || level.Solution.Length != count)
            {
                errors.Add($"{label}: solution must have {count} digits (has {level.Solution?.Length ?? 0}).");
            }

            if (level.Clues == null || level.Clues.Length != count)
            {
                errors.Add($"{label}: clues must have {count} digits (has {level.Clues?.Length ?? 0}).");
            }

            if (errors.Count > 0)
            {
                return errors;
            }

            for (int index = 0; index < count; index++)
            {
                int solution = level.Solution[index];
                int clue = level.Clues[index];
                if (solution < 1 || solution > FillItBoardModel.MaxValue)
                {
                    errors.Add($"{label}: solution cell {index} must be 1-9 (was {solution}).");
                }

                if (clue != 0 && clue != solution)
                {
                    errors.Add($"{label}: clue at cell {index} ({clue}) does not match the solution ({solution}).");
                }
            }

            if (errors.Count == 0 && !FillItRegionAnalyzer.IsValidSolution(level.Size, level.Solution))
            {
                errors.Add($"{label}: solution is not a valid Fillomino fill (a region's size doesn't match its number).");
            }

            return errors;
        }
    }
}
