using System.Collections.Generic;
using CatFlap.Levels;
using CatFlap.Models;
using CatFlap.Validation;

namespace CatFlap.Boosters
{
    /// <summary>
    /// Board difficulty score used only for hint star-rating/diagnostics (never for
    /// choosing which cell to hint - see <see cref="CatFlapSolutionHintProvider"/> priority order).
    /// </summary>
    public static class CatFlapBoardDifficultyScorer
    {
        private const int UnsolvedNormalCellScore = 3;
        private const int UnsolvedForcedCellScore = 1;
        private const int WrongCommittedTileScore = 8;
        private const int ViolationCellScore = 2;

        public static int Score(CatFlapBoardModel board, CatFlapLevelData level)
        {
            if (board == null || level == null)
            {
                return 0;
            }

            var validator = new CatFlapRuleValidator();
            CatFlapRuleValidationResult result = validator.Validate(board);
            HashSet<string> violatedCells = CollectViolatedCells(result);

            int score = 0;
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    score += ScoreCell(board, level, row, column, violatedCells);
                }
            }

            return score;
        }

        private static int ScoreCell(CatFlapBoardModel board, CatFlapLevelData level, int row, int column, HashSet<string> violatedCells)
        {
            CatFlapCellModel cell = board.GetCell(row, column);
            if (cell.IsLocked)
            {
                return 0;
            }

            CatFlapCellValue effective = board.GetEffectiveValue(row, column);
            int? solution = level.Solution[row].Cells[column];

            int score = 0;
            if (effective == CatFlapCellValue.Empty)
            {
                score += CatFlapForcedCellAnalyzer.IsForcedCell(board, row, column)
                    ? UnsolvedForcedCellScore
                    : UnsolvedNormalCellScore;
            }
            else if (solution.HasValue && (int)effective != solution.Value)
            {
                score += WrongCommittedTileScore;
            }

            if (violatedCells.Contains(row + "," + column))
            {
                score += ViolationCellScore;
            }

            return score;
        }

        private static HashSet<string> CollectViolatedCells(CatFlapRuleValidationResult result)
        {
            var set = new HashSet<string>();
            for (int index = 0; index < result.Violations.Count; index++)
            {
                CatFlapRuleViolation violation = result.Violations[index];
                for (int cellIndex = 0; cellIndex < violation.CellKeys.Count; cellIndex++)
                {
                    set.Add(violation.CellKeys[cellIndex]);
                }
            }

            return set;
        }
    }
}
