using CatFlap.Levels;
using CatFlap.Models;
using System;
using System.Collections.Generic;

namespace CatFlap.Boosters
{
    public sealed class CatFlapHintService
    {
        private readonly Random random;
        private readonly CatFlapSolutionHintProvider solutionHintProvider;

        public CatFlapHintService()
            : this(new Random())
        {
        }

        internal CatFlapHintService(Random random)
        {
            this.random = random ?? new Random();
            this.solutionHintProvider = new CatFlapSolutionHintProvider();
        }

        /// <summary>
        /// Routes to the new solution-driven selector (Mode A) whenever the level carries a
        /// stored solution - true for every level in this project today - otherwise falls back
        /// to the legacy rule-based reasoning below (Mode B), unchanged.
        /// </summary>
        public CatFlapCellRevealResult RevealNext(CatFlapBoardModel board, CatFlapLevelData level, CatFlapCellPosition? lastInteractedCell = null)
        {
            if (board == null || level == null)
            {
                return CatFlapCellRevealResult.Failed("Cannot reveal a hint before a level is loaded.");
            }

            if (HasStoredSolution(level))
            {
                return solutionHintProvider.GetHint(board, level, lastInteractedCell);
            }

            return RevealNextRuleBased(board, level);
        }

        private static bool HasStoredSolution(CatFlapLevelData level)
        {
            return level.Solution != null && level.Solution.Length > 0;
        }

        private CatFlapCellRevealResult RevealNextRuleBased(CatFlapBoardModel board, CatFlapLevelData level)
        {
            List<HintCandidate> candidates = FindCandidates(board, level);
            if (candidates.Count == 0)
            {
                return CatFlapCellRevealResult.Failed("No hint is available.");
            }

            HintCandidate candidate;
            if (TryFindOneMissingCorrectLineCandidate(board, level, candidates, out candidate))
            {
                return ToRevealResult(candidate, "OneMissingLine");
            }

            if (TryFindMaxTwoAdjacentCandidate(board, level, candidates, out candidate))
            {
                return ToRevealResult(candidate, "MaxTwoAdjacent");
            }

            if (TryFindLeastEmptyLineCandidate(board, candidates, out candidate))
            {
                return ToRevealResult(candidate, "LeastEmptyLine");
            }

            return ToRevealResult(PickRandom(candidates), "Random");
        }

        private static List<HintCandidate> FindCandidates(CatFlapBoardModel board, CatFlapLevelData level)
        {
            var candidates = new List<HintCandidate>();
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = board.GetCell(row, column);
                    if (cell.IsLocked)
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (!solution.HasValue)
                    {
                        continue;
                    }

                    CatFlapCellValue solutionValue = (CatFlapCellValue)solution.Value;
                    if (cell.Value != solutionValue || cell.IsNote)
                    {
                        candidates.Add(new HintCandidate(row, column, solutionValue));
                    }
                }
            }

            return candidates;
        }

        private static bool TryFindOneMissingCorrectLineCandidate(
            CatFlapBoardModel board,
            CatFlapLevelData level,
            List<HintCandidate> candidates,
            out HintCandidate candidate)
        {
            var matches = new List<HintCandidate>();
            for (int index = 0; index < candidates.Count; index++)
            {
                HintCandidate current = candidates[index];
                if (IsOnlyMissingCorrectCellInRow(board, level, current.Row, current.Column) ||
                    IsOnlyMissingCorrectCellInColumn(board, level, current.Row, current.Column))
                {
                    matches.Add(current);
                }
            }

            candidate = matches.Count > 0 ? PickFirst(matches) : default(HintCandidate);
            return matches.Count > 0;
        }

        private static bool IsOnlyMissingCorrectCellInRow(CatFlapBoardModel board, CatFlapLevelData level, int targetRow, int targetColumn)
        {
            int missing = 0;
            for (int column = 0; column < board.ColumnCount; column++)
            {
                if (IsSolvedCell(board, level, targetRow, column))
                {
                    continue;
                }

                missing++;
                if (column != targetColumn || missing > 1)
                {
                    return false;
                }
            }

            return missing == 1;
        }

        private static bool IsOnlyMissingCorrectCellInColumn(CatFlapBoardModel board, CatFlapLevelData level, int targetRow, int targetColumn)
        {
            int missing = 0;
            for (int row = 0; row < board.RowCount; row++)
            {
                if (IsSolvedCell(board, level, row, targetColumn))
                {
                    continue;
                }

                missing++;
                if (row != targetRow || missing > 1)
                {
                    return false;
                }
            }

            return missing == 1;
        }

        private bool TryFindMaxTwoAdjacentCandidate(
            CatFlapBoardModel board,
            CatFlapLevelData level,
            List<HintCandidate> candidates,
            out HintCandidate candidate)
        {
            var matches = new List<HintCandidate>();
            for (int index = 0; index < candidates.Count; index++)
            {
                HintCandidate current = candidates[index];
                if (SolvesAdjacentPattern(board, current.Row, current.Column, true, current.Value) ||
                    SolvesAdjacentPattern(board, current.Row, current.Column, false, current.Value))
                {
                    matches.Add(current);
                }
            }

            candidate = matches.Count > 0 ? PickRandom(matches) : default(HintCandidate);
            return matches.Count > 0;
        }

        private static bool SolvesAdjacentPattern(
            CatFlapBoardModel board,
            int row,
            int column,
            bool horizontal,
            CatFlapCellValue solutionValue)
        {
            return HasPair(board, row, column, horizontal, -2, -1, solutionValue) ||
                   HasPair(board, row, column, horizontal, 1, 2, solutionValue) ||
                   HasPair(board, row, column, horizontal, -1, 1, solutionValue);
        }

        private static bool HasPair(
            CatFlapBoardModel board,
            int row,
            int column,
            bool horizontal,
            int firstOffset,
            int secondOffset,
            CatFlapCellValue solutionValue)
        {
            if (!TryGetLineValue(board, row, column, horizontal, firstOffset, out CatFlapCellValue first) ||
                !TryGetLineValue(board, row, column, horizontal, secondOffset, out CatFlapCellValue second))
            {
                return false;
            }

            return first != CatFlapCellValue.Empty && first == second && solutionValue != first;
        }

        private static bool TryGetLineValue(
            CatFlapBoardModel board,
            int row,
            int column,
            bool horizontal,
            int offset,
            out CatFlapCellValue value)
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

        private bool TryFindLeastEmptyLineCandidate(CatFlapBoardModel board, List<HintCandidate> candidates, out HintCandidate candidate)
        {
            int bestEmptyCount = int.MaxValue;
            var matches = new List<HintCandidate>();
            for (int index = 0; index < candidates.Count; index++)
            {
                HintCandidate current = candidates[index];
                int emptyCount = Math.Min(
                    CountEmptyInRow(board, current.Row),
                    CountEmptyInColumn(board, current.Column));

                if (emptyCount <= 0)
                {
                    continue;
                }

                if (emptyCount < bestEmptyCount)
                {
                    bestEmptyCount = emptyCount;
                    matches.Clear();
                }

                if (emptyCount == bestEmptyCount)
                {
                    matches.Add(current);
                }
            }

            candidate = matches.Count > 0 ? PickRandom(matches) : default(HintCandidate);
            return matches.Count > 0;
        }

        private static int CountEmptyInRow(CatFlapBoardModel board, int row)
        {
            int count = 0;
            for (int column = 0; column < board.ColumnCount; column++)
            {
                if (board.GetEffectiveValue(row, column) == CatFlapCellValue.Empty)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountEmptyInColumn(CatFlapBoardModel board, int column)
        {
            int count = 0;
            for (int row = 0; row < board.RowCount; row++)
            {
                if (board.GetEffectiveValue(row, column) == CatFlapCellValue.Empty)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsSolvedCell(CatFlapBoardModel board, CatFlapLevelData level, int row, int column)
        {
            CatFlapCellModel cell = board.GetCell(row, column);
            int? solution = level.Solution[row].Cells[column];
            return solution.HasValue && !cell.IsNote && cell.Value == (CatFlapCellValue)solution.Value;
        }

        private static HintCandidate PickFirst(List<HintCandidate> candidates)
        {
            return candidates[0];
        }

        private HintCandidate PickRandom(List<HintCandidate> candidates)
        {
            return candidates[random.Next(0, candidates.Count)];
        }

        private static CatFlapCellRevealResult ToRevealResult(HintCandidate candidate, string reasonKey)
        {
            return new CatFlapCellRevealResult(true, candidate.Row, candidate.Column, candidate.Value, "Hint revealed.", reasonKey);
        }

        private struct HintCandidate
        {
            public HintCandidate(int row, int column, CatFlapCellValue value)
            {
                Row = row;
                Column = column;
                Value = value;
            }

            public int Row { get; }
            public int Column { get; }
            public CatFlapCellValue Value { get; }
        }
    }
}
