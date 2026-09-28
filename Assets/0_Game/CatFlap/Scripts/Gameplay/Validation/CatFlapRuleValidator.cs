using System.Collections.Generic;
using CatFlap.Models;

namespace CatFlap.Validation
{
    public sealed class CatFlapRuleValidator
    {
        public CatFlapRuleValidationResult Validate(CatFlapBoardModel board)
        {
            var result = new CatFlapRuleValidationResult();
            if (board == null)
            {
                result.AddViolation(CatFlapRule.EqualCounts, "Board is missing.", new string[0]);
                return result;
            }

            ValidateAdjacent(board, result);
            ValidateCounts(board, result);
            return result;
        }

        /// <summary>
        /// Same as <see cref="Validate"/>, but only re-checks the given row and the given column
        /// instead of scanning the whole board. A single cell edit can only ever affect whether
        /// its own row and its own column obey the rules, so callers that already have a full
        /// <see cref="CatFlapRuleValidationResult"/> for every other line can merge this scoped
        /// result in via <see cref="CatFlapRuleValidationResult.ReplaceLines"/> instead of
        /// recomputing every other line for nothing.
        /// </summary>
        public CatFlapRuleValidationResult ValidateLines(CatFlapBoardModel board, int row, int column)
        {
            var result = new CatFlapRuleValidationResult();
            if (board == null)
            {
                return result;
            }

            if (row >= 0 && row < board.RowCount)
            {
                ValidateAdjacentRow(board, row, result);
                ValidateCountsRow(board, row, result);
            }

            if (column >= 0 && column < board.ColumnCount)
            {
                ValidateAdjacentColumn(board, column, result);
                ValidateCountsColumn(board, column, result);
            }

            return result;
        }

        private static void ValidateAdjacent(CatFlapBoardModel board, CatFlapRuleValidationResult result)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                ValidateAdjacentRow(board, row, result);
            }

            for (int column = 0; column < board.ColumnCount; column++)
            {
                ValidateAdjacentColumn(board, column, result);
            }
        }

        private static void ValidateAdjacentRow(CatFlapBoardModel board, int row, CatFlapRuleValidationResult result)
        {
            for (int column = 2; column < board.ColumnCount; column++)
            {
                CatFlapCellValue value = board.GetEffectiveValue(row, column);
                if (value != CatFlapCellValue.Empty &&
                    board.GetEffectiveValue(row, column - 1) == value &&
                    board.GetEffectiveValue(row, column - 2) == value)
                {
                    result.AddViolation(
                        CatFlapRule.MaxTwoAdjacent,
                        $"Row {row} contains more than two adjacent {value} cells.",
                        new[] { Key(row, column), Key(row, column - 1), Key(row, column - 2) });
                }
            }
        }

        private static void ValidateAdjacentColumn(CatFlapBoardModel board, int column, CatFlapRuleValidationResult result)
        {
            for (int row = 2; row < board.RowCount; row++)
            {
                CatFlapCellValue value = board.GetEffectiveValue(row, column);
                if (value != CatFlapCellValue.Empty &&
                    board.GetEffectiveValue(row - 1, column) == value &&
                    board.GetEffectiveValue(row - 2, column) == value)
                {
                    result.AddViolation(
                        CatFlapRule.MaxTwoAdjacent,
                        $"Column {column} contains more than two adjacent {value} cells.",
                        new[] { Key(row, column), Key(row - 1, column), Key(row - 2, column) });
                }
            }
        }

        private static void ValidateCounts(CatFlapBoardModel board, CatFlapRuleValidationResult result)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                ValidateCountsRow(board, row, result);
            }

            for (int column = 0; column < board.ColumnCount; column++)
            {
                ValidateCountsColumn(board, column, result);
            }
        }

        private static void ValidateCountsRow(CatFlapBoardModel board, int row, CatFlapRuleValidationResult result)
        {
            int rowTarget = board.ColumnCount / 2;
            CountLine(board, true, row, out int fish, out int cats);
            if (fish > rowTarget)
            {
                result.AddViolation(CatFlapRule.EqualCounts, $"Row {row} has too many Fish cells.", CellsInRowWithValue(board, row, CatFlapCellValue.Fish));
            }

            if (cats > rowTarget)
            {
                result.AddViolation(CatFlapRule.EqualCounts, $"Row {row} has too many Cat cells.", CellsInRowWithValue(board, row, CatFlapCellValue.Cat));
            }
        }

        private static void ValidateCountsColumn(CatFlapBoardModel board, int column, CatFlapRuleValidationResult result)
        {
            int columnTarget = board.RowCount / 2;
            CountLine(board, false, column, out int fish, out int cats);
            if (fish > columnTarget)
            {
                result.AddViolation(CatFlapRule.EqualCounts, $"Column {column} has too many Fish cells.", CellsInColumnWithValue(board, column, CatFlapCellValue.Fish));
            }

            if (cats > columnTarget)
            {
                result.AddViolation(CatFlapRule.EqualCounts, $"Column {column} has too many Cat cells.", CellsInColumnWithValue(board, column, CatFlapCellValue.Cat));
            }
        }

        private static void ValidateUniqueLines(CatFlapBoardModel board, CatFlapRuleValidationResult result)
        {
            for (int first = 0; first < board.RowCount; first++)
            {
                for (int second = first + 1; second < board.RowCount; second++)
                {
                    if (IsFullRow(board, first) && IsFullRow(board, second) && RowsEqual(board, first, second))
                    {
                        result.AddViolation(CatFlapRule.UniqueLines, $"Rows {first} and {second} are identical.", Combine(RowKeys(board, first), RowKeys(board, second)));
                    }
                }
            }

            for (int first = 0; first < board.ColumnCount; first++)
            {
                for (int second = first + 1; second < board.ColumnCount; second++)
                {
                    if (IsFullColumn(board, first) && IsFullColumn(board, second) && ColumnsEqual(board, first, second))
                    {
                        result.AddViolation(CatFlapRule.UniqueLines, $"Columns {first} and {second} are identical.", Combine(ColumnKeys(board, first), ColumnKeys(board, second)));
                    }
                }
            }
        }

        private static void CountLine(CatFlapBoardModel board, bool isRow, int index, out int fish, out int cats)
        {
            fish = 0;
            cats = 0;
            int length = isRow ? board.ColumnCount : board.RowCount;
            for (int offset = 0; offset < length; offset++)
            {
                CatFlapCellValue value = isRow ? board.GetEffectiveValue(index, offset) : board.GetEffectiveValue(offset, index);
                if (value == CatFlapCellValue.Fish)
                {
                    fish++;
                }
                else if (value == CatFlapCellValue.Cat)
                {
                    cats++;
                }
            }
        }

        private static bool IsFullRow(CatFlapBoardModel board, int row)
        {
            for (int column = 0; column < board.ColumnCount; column++)
            {
                if (board.GetEffectiveValue(row, column) == CatFlapCellValue.Empty)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFullColumn(CatFlapBoardModel board, int column)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                if (board.GetEffectiveValue(row, column) == CatFlapCellValue.Empty)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RowsEqual(CatFlapBoardModel board, int first, int second)
        {
            for (int column = 0; column < board.ColumnCount; column++)
            {
                if (board.GetEffectiveValue(first, column) != board.GetEffectiveValue(second, column))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ColumnsEqual(CatFlapBoardModel board, int first, int second)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                if (board.GetEffectiveValue(row, first) != board.GetEffectiveValue(row, second))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<string> CellsInRowWithValue(CatFlapBoardModel board, int row, CatFlapCellValue value)
        {
            for (int column = 0; column < board.ColumnCount; column++)
            {
                if (board.GetEffectiveValue(row, column) == value)
                {
                    yield return Key(row, column);
                }
            }
        }

        private static IEnumerable<string> CellsInColumnWithValue(CatFlapBoardModel board, int column, CatFlapCellValue value)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                if (board.GetEffectiveValue(row, column) == value)
                {
                    yield return Key(row, column);
                }
            }
        }

        private static IEnumerable<string> RowKeys(CatFlapBoardModel board, int row)
        {
            for (int column = 0; column < board.ColumnCount; column++)
            {
                yield return Key(row, column);
            }
        }

        private static IEnumerable<string> ColumnKeys(CatFlapBoardModel board, int column)
        {
            for (int row = 0; row < board.RowCount; row++)
            {
                yield return Key(row, column);
            }
        }

        private static IEnumerable<string> Combine(IEnumerable<string> first, IEnumerable<string> second)
        {
            foreach (string value in first)
            {
                yield return value;
            }

            foreach (string value in second)
            {
                yield return value;
            }
        }

        private static string Key(int row, int column)
        {
            return $"{row},{column}";
        }
    }
}
