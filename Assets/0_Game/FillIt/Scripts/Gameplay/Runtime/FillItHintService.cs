using System.Collections.Generic;
using FillIt.Levels;
using FillIt.Models;
using FillIt.Rules;

namespace FillIt.Runtime
{
    public static class FillItHintReason
    {
        public const string FixWrongNumber = "FixWrongNumber";
        public const string CompleteRegion = "CompleteRegion";
        public const string GrowRegion = "GrowRegion";
        public const string SingleCell = "SingleCell";
        public const string StartRegion = "StartRegion";
    }

    public readonly struct FillItHintResult
    {
        private FillItHintResult(bool success, int index, int row, int column, int value, string reasonKey, string message)
        {
            Success = success;
            Index = index;
            Row = row;
            Column = column;
            Value = value;
            ReasonKey = reasonKey;
            Message = message;
        }

        public bool Success { get; }
        public int Index { get; }
        public int Row { get; }
        public int Column { get; }
        public int Value { get; }
        public string ReasonKey { get; }
        public string Message { get; }

        public static FillItHintResult Found(FillItBoardModel board, int index, int value, string reasonKey)
        {
            return new FillItHintResult(true, index, board.RowOf(index), board.ColumnOf(index), value, reasonKey, $"This cell is a {value}.");
        }

        public static FillItHintResult Failed(string message)
        {
            return new FillItHintResult(false, -1, -1, -1, 0, string.Empty, message);
        }
    }

    /// <summary>
    /// Picks the most useful cell to reveal from the level's stored solution, and finds the wrong
    /// numbers the Fix booster clears. Every generated level has exactly one solution, so "differs
    /// from the solution" is the same as "can never be part of a valid fill".
    /// </summary>
    public static class FillItHintService
    {
        public static FillItHintResult FindHint(FillItBoardModel board, FillItLevelData level, FillItBoardAnalysis analysis, int selectedIndex)
        {
            if (board == null || level == null)
            {
                return FillItHintResult.Failed("No level is loaded.");
            }

            // 1. The cell the player is looking at, if it's wrong or empty.
            if (board.IsValidIndex(selectedIndex) && IsWrongOrEmpty(board, level, selectedIndex))
            {
                return FillItHintResult.Found(board, selectedIndex, level.GetSolutionValue(selectedIndex), ResolveReason(board, level, analysis, selectedIndex));
            }

            // 2. A number the player placed that doesn't belong there.
            for (int index = 0; index < board.CellCount; index++)
            {
                if (!board.IsGiven(index) && !board.IsEmpty(index) && board.GetValue(index) != level.GetSolutionValue(index))
                {
                    return FillItHintResult.Found(board, index, level.GetSolutionValue(index), FillItHintReason.FixWrongNumber);
                }
            }

            // 3. An empty cell that grows an existing region - the region closest to done first.
            int bestIndex = -1;
            int bestMissing = int.MaxValue;
            for (int index = 0; index < board.CellCount; index++)
            {
                if (!board.IsEmpty(index))
                {
                    continue;
                }

                int solution = level.GetSolutionValue(index);
                int[] neighbors = board.Neighbors(index);
                for (int n = 0; n < neighbors.Length; n++)
                {
                    FillItRegion region = analysis?.GetRegionAt(neighbors[n]);
                    if (region == null || region.Value != solution || region.Status != FillItRegionStatus.Open)
                    {
                        continue;
                    }

                    int missing = region.Value - region.Size;
                    if (missing < bestMissing)
                    {
                        bestMissing = missing;
                        bestIndex = index;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                return FillItHintResult.Found(board, bestIndex, level.GetSolutionValue(bestIndex), bestMissing == 1 ? FillItHintReason.CompleteRegion : FillItHintReason.GrowRegion);
            }

            // 4. Any empty cell next to something already filled, then any empty cell at all.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < board.CellCount; index++)
                {
                    if (!board.IsEmpty(index) || (pass == 0 && !TouchesFilledCell(board, index)))
                    {
                        continue;
                    }

                    return FillItHintResult.Found(board, index, level.GetSolutionValue(index), ResolveReason(board, level, analysis, index));
                }
            }

            return FillItHintResult.Failed("Nothing left to hint - check the red regions.");
        }

        /// <summary>Every player-placed number that doesn't match the solution.</summary>
        public static List<int> FindWrongCells(FillItBoardModel board, FillItLevelData level)
        {
            var result = new List<int>();
            if (board == null || level == null)
            {
                return result;
            }

            for (int index = 0; index < board.CellCount; index++)
            {
                if (!board.IsGiven(index) && !board.IsEmpty(index) && board.GetValue(index) != level.GetSolutionValue(index))
                {
                    result.Add(index);
                }
            }

            return result;
        }

        private static bool IsWrongOrEmpty(FillItBoardModel board, FillItLevelData level, int index)
        {
            return !board.IsGiven(index) && board.GetValue(index) != level.GetSolutionValue(index);
        }

        private static bool TouchesFilledCell(FillItBoardModel board, int index)
        {
            int[] neighbors = board.Neighbors(index);
            for (int n = 0; n < neighbors.Length; n++)
            {
                if (!board.IsEmpty(neighbors[n]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveReason(FillItBoardModel board, FillItLevelData level, FillItBoardAnalysis analysis, int index)
        {
            if (!board.IsEmpty(index))
            {
                return FillItHintReason.FixWrongNumber;
            }

            int solution = level.GetSolutionValue(index);
            if (solution == 1)
            {
                return FillItHintReason.SingleCell;
            }

            int[] neighbors = board.Neighbors(index);
            for (int n = 0; n < neighbors.Length; n++)
            {
                FillItRegion region = analysis?.GetRegionAt(neighbors[n]);
                if (region != null && region.Value == solution)
                {
                    return region.Value - region.Size == 1 ? FillItHintReason.CompleteRegion : FillItHintReason.GrowRegion;
                }
            }

            return FillItHintReason.StartRegion;
        }
    }
}
