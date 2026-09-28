using CatFlap.Models;
using CatFlap.Validation;

namespace CatFlap.Boosters
{
    /// <summary>
    /// Determines whether an empty, unlocked cell currently has only one legal value
    /// (Cat or Fish) given the existing rule validator, and classifies why. Simulation is
    /// non-mutating: it uses <see cref="CatFlapBoardModel.CreateSnapshot"/>/<see cref="CatFlapBoardModel.Restore"/>
    /// around a temporary write, never leaving the live board changed.
    /// </summary>
    public static class CatFlapForcedCellAnalyzer
    {
        public static bool IsForcedCell(CatFlapBoardModel board, int row, int column)
        {
            return TryGetForcedValue(board, row, column, out _, out _);
        }

        public static bool TryGetForcedValue(
            CatFlapBoardModel board,
            int row,
            int column,
            out CatFlapCellValue forcedValue,
            out CatFlapHintReason reason)
        {
            forcedValue = CatFlapCellValue.Empty;
            reason = CatFlapHintReason.ForcedLineMaximum;

            if (board.GetEffectiveValue(row, column) != CatFlapCellValue.Empty)
            {
                return false;
            }

            bool fishLegal = IsLegalPlacement(board, row, column, CatFlapCellValue.Fish);
            bool catLegal = IsLegalPlacement(board, row, column, CatFlapCellValue.Cat);
            if (fishLegal == catLegal)
            {
                return false;
            }

            forcedValue = fishLegal ? CatFlapCellValue.Fish : CatFlapCellValue.Cat;
            reason = ClassifyForcedReason(board, row, column);
            return true;
        }

        private static bool IsLegalPlacement(CatFlapBoardModel board, int row, int column, CatFlapCellValue value)
        {
            CatFlapBoardSnapshot snapshot = board.CreateSnapshot();
            try
            {
                board.SetCellValue(row, column, value, false);
                var validator = new CatFlapRuleValidator();
                CatFlapRuleValidationResult result = validator.Validate(board);
                string key = row + "," + column;
                for (int index = 0; index < result.Violations.Count; index++)
                {
                    CatFlapRuleViolation violation = result.Violations[index];
                    for (int cellIndex = 0; cellIndex < violation.CellKeys.Count; cellIndex++)
                    {
                        if (violation.CellKeys[cellIndex] == key)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            finally
            {
                board.Restore(snapshot);
            }
        }

        private static CatFlapHintReason ClassifyForcedReason(CatFlapBoardModel board, int row, int column)
        {
            if (HasAdjacentPairPattern(board, row, column, true) || HasAdjacentPairPattern(board, row, column, false))
            {
                return CatFlapHintReason.ForcedAdjacentPair;
            }

            if (HasSandwichPattern(board, row, column, true) || HasSandwichPattern(board, row, column, false))
            {
                return CatFlapHintReason.ForcedSandwich;
            }

            return CatFlapHintReason.ForcedLineMaximum;
        }

        private static bool HasAdjacentPairPattern(CatFlapBoardModel board, int row, int column, bool horizontal)
        {
            return HasPairAt(board, row, column, horizontal, -2, -1) || HasPairAt(board, row, column, horizontal, 1, 2);
        }

        private static bool HasPairAt(CatFlapBoardModel board, int row, int column, bool horizontal, int offsetA, int offsetB)
        {
            if (!TryGetValueAt(board, row, column, horizontal, offsetA, out CatFlapCellValue a) ||
                !TryGetValueAt(board, row, column, horizontal, offsetB, out CatFlapCellValue b))
            {
                return false;
            }

            return a != CatFlapCellValue.Empty && a == b;
        }

        private static bool HasSandwichPattern(CatFlapBoardModel board, int row, int column, bool horizontal)
        {
            if (!TryGetValueAt(board, row, column, horizontal, -1, out CatFlapCellValue before) ||
                !TryGetValueAt(board, row, column, horizontal, 1, out CatFlapCellValue after))
            {
                return false;
            }

            return before != CatFlapCellValue.Empty && before == after;
        }

        private static bool TryGetValueAt(CatFlapBoardModel board, int row, int column, bool horizontal, int offset, out CatFlapCellValue value)
        {
            int targetRow = horizontal ? row : row + offset;
            int targetColumn = horizontal ? column + offset : column;
            if (targetRow < 0 || targetRow >= board.RowCount || targetColumn < 0 || targetColumn >= board.ColumnCount)
            {
                value = CatFlapCellValue.Empty;
                return false;
            }

            value = board.GetEffectiveValue(targetRow, targetColumn);
            return true;
        }
    }
}
