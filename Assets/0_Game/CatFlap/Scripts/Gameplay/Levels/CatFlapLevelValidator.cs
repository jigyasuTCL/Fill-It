using CatFlap.Models;
using CatFlap.Validation;

namespace CatFlap.Levels
{
    public sealed class CatFlapLevelValidator
    {
        public CatFlapLevelValidationResult Validate(CatFlapLevelData level)
        {
            var result = new CatFlapLevelValidationResult();
            if (level == null)
            {
                result.AddError("Level data is missing.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(level.Id))
            {
                result.AddWarning("Level id is missing.");
            }

            if (level.Size <= 0 || level.Size % 2 != 0)
            {
                result.AddError("Level size must be a positive even number.");
            }

            ValidateGrid("solution", level.Solution, level.Size, false, result);
            ValidateGrid("clues", level.Clues, level.Size, true, result);

            if (!result.IsValid)
            {
                return result;
            }

            ValidateCluesMatchSolution(level, result);
            ValidateSolutionRules(level, result);
            return result;
        }

        private static void ValidateGrid(string label, CatFlapLevelRow[] rows, int size, bool allowNull, CatFlapLevelValidationResult result)
        {
            if (rows == null)
            {
                result.AddError($"Level {label} grid is missing.");
                return;
            }

            if (rows.Length != size)
            {
                result.AddError($"Level {label} grid row count must match size {size}.");
                return;
            }

            for (int row = 0; row < rows.Length; row++)
            {
                if (rows[row] == null || rows[row].Cells == null)
                {
                    result.AddError($"Level {label} row {row} is missing.");
                    continue;
                }

                if (rows[row].Cells.Length != size)
                {
                    result.AddError($"Level {label} row {row} column count must match size {size}.");
                    continue;
                }

                for (int column = 0; column < rows[row].Cells.Length; column++)
                {
                    int? value = rows[row].Cells[column];
                    if (!allowNull && !value.HasValue)
                    {
                        result.AddError($"Level {label} cell ({row},{column}) cannot be null.");
                    }
                    else if (value.HasValue && value.Value != 0 && value.Value != 1)
                    {
                        result.AddError($"Level {label} cell ({row},{column}) must be null, 0, or 1.");
                    }
                }
            }
        }

        private static void ValidateCluesMatchSolution(CatFlapLevelData level, CatFlapLevelValidationResult result)
        {
            for (int row = 0; row < level.Size; row++)
            {
                for (int column = 0; column < level.Size; column++)
                {
                    int? clue = level.Clues[row].Cells[column];
                    if (clue.HasValue && clue.Value != level.Solution[row].Cells[column])
                    {
                        result.AddError($"Clue at ({row},{column}) does not match the solution.");
                    }
                }
            }
        }

        private static void ValidateSolutionRules(CatFlapLevelData level, CatFlapLevelValidationResult result)
        {
            CatFlapBoardModel board = CatFlapBoardModel.FromLevel(CreateSolvedLevel(level));
            CatFlapRuleValidationResult rules = new CatFlapRuleValidator().Validate(board);
            foreach (CatFlapRuleViolation violation in rules.Violations)
            {
                result.AddError($"Solution violates {violation.Rule}: {violation.Message}");
            }
        }

        private static CatFlapLevelData CreateSolvedLevel(CatFlapLevelData level)
        {
            return new CatFlapLevelData
            {
                Id = level.Id,
                Name = level.Name,
                Difficulty = level.Difficulty,
                Size = level.Size,
                Lives = level.Lives,
                LevelMistakeLimit = level.LevelMistakeLimit,
                Solution = level.Solution,
                Clues = level.Solution
            };
        }
    }
}
