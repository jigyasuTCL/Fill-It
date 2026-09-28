using System.Collections.Generic;
using CatFlap.Levels;
using CatFlap.Models;

namespace CatFlap.Boosters
{
    /// <summary>
    /// Mode A hint selector: used when a level carries a stored solution
    /// (<see cref="CatFlapLevelData.Solution"/> populated - true for every level in this
    /// project today, see <see cref="CatFlapHintService"/> for the Mode B/no-solution fallback
    /// path this provider is routed alongside).
    ///
    /// Priority order (first non-empty group wins), per the hint-system spec:
    /// 1. Wrong tile currently in a rule violation.
    /// 2. Wrong tile not currently in a violation.
    /// 3. Forced empty cell (only one legal value).
    /// 4. Empty cell whose correct placement unlocks the most new forced cells.
    /// 5. Empty cell in the row/column closest to completion.
    /// 6 / 7 are tie-breaks (distance from last tap, then stable top-left) applied within
    /// every group above, not separate top-level priorities.
    ///
    /// Simulation for forced-cell/follow-on checks is entirely non-mutating: it goes through
    /// <see cref="CatFlapBoardModel.CreateSnapshot"/>/<see cref="CatFlapBoardModel.Restore"/>,
    /// never triggers gameplay callbacks, animations, saves, or GameObject instantiation, and
    /// never uses randomness - selection is fully deterministic for a given board/notes/last-tap.
    /// </summary>
    public sealed class CatFlapSolutionHintProvider
    {
        public const string NoMoveMessage = "No obvious single move found. Double-check for rule violations!";

        public CatFlapCellRevealResult GetHint(CatFlapBoardModel board, CatFlapLevelData level, CatFlapCellPosition? lastInteractedCell)
        {
            if (board == null || level == null)
            {
                return CatFlapCellRevealResult.Failed("Cannot reveal a hint before a level is loaded.");
            }

            CatFlapSolutionHintCandidate best =
                FindBest(FindWrongTileCandidates(board, level, true), lastInteractedCell) ??
                FindBest(FindWrongTileCandidates(board, level, false), lastInteractedCell) ??
                FindBest(FindForcedCandidates(board, level), lastInteractedCell) ??
                FindBest(FindUnlocksForcedCandidates(board, level), lastInteractedCell) ??
                FindBest(FindClosestToCompletionCandidates(board, level), lastInteractedCell);

            if (best != null)
            {
                return ToRevealResult(best);
            }

            List<CatFlapCellPosition> correctNotes = FindCorrectNoteCells(board, level);
            if (correctNotes.Count > 0)
            {
                // Highlight-only case: the note is already correct, so the engine must not
                // overwrite it. This project's current RevealHint architecture always writes
                // CatFlapCellRevealResult.Value into the board on Success, so this is
                // intentionally reported as a non-mutating "no charge, no board change" result
                // (see the "Known limitations" note in the final report) rather than Success=true.
                return CatFlapCellRevealResult.Failed("Your note here is correct - commit it.");
            }

            return CatFlapCellRevealResult.Failed(NoMoveMessage);
        }

        private static List<CatFlapSolutionHintCandidate> FindWrongTileCandidates(CatFlapBoardModel board, CatFlapLevelData level, bool requireViolation)
        {
            var candidates = new List<CatFlapSolutionHintCandidate>();
            var validator = new global::CatFlap.Validation.CatFlapRuleValidator();
            global::CatFlap.Validation.CatFlapRuleValidationResult result = validator.Validate(board);
            var violationCounts = BuildViolationCounts(result);

            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = board.GetCell(row, column);
                    if (cell.IsLocked || cell.IsNote || cell.Value == CatFlapCellValue.Empty)
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (!solution.HasValue || (int)cell.Value == solution.Value)
                    {
                        continue;
                    }

                    int violationCount = violationCounts.TryGetValue(row + "," + column, out int count) ? count : 0;
                    bool inViolation = violationCount > 0;
                    if (inViolation != requireViolation)
                    {
                        continue;
                    }

                    candidates.Add(new CatFlapSolutionHintCandidate
                    {
                        Cell = new CatFlapCellPosition(row, column),
                        CorrectValue = (CatFlapCellValue)solution.Value,
                        Reason = requireViolation ? CatFlapHintReason.WrongTileInViolation : CatFlapHintReason.WrongTile,
                        IsWrongTile = true,
                        IsInViolation = inViolation,
                        ViolationCount = violationCount,
                        RowRemaining = CountUnresolved(board, level, row, -1),
                        ColumnRemaining = CountUnresolved(board, level, -1, column)
                    });
                }
            }

            return candidates;
        }

        private static List<CatFlapSolutionHintCandidate> FindForcedCandidates(CatFlapBoardModel board, CatFlapLevelData level)
        {
            var candidates = new List<CatFlapSolutionHintCandidate>();
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    if (!IsEligibleEmptyCell(board, level, row, column))
                    {
                        continue;
                    }

                    if (!CatFlapForcedCellAnalyzer.TryGetForcedValue(board, row, column, out CatFlapCellValue forcedValue, out CatFlapHintReason reason))
                    {
                        continue;
                    }

                    candidates.Add(new CatFlapSolutionHintCandidate
                    {
                        Cell = new CatFlapCellPosition(row, column),
                        CorrectValue = forcedValue,
                        Reason = reason,
                        IsForced = true,
                        RowRemaining = CountUnresolved(board, level, row, -1),
                        ColumnRemaining = CountUnresolved(board, level, -1, column)
                    });
                }
            }

            return candidates;
        }

        private static List<CatFlapSolutionHintCandidate> FindUnlocksForcedCandidates(CatFlapBoardModel board, CatFlapLevelData level)
        {
            var candidates = new List<CatFlapSolutionHintCandidate>();
            int forcedBefore = CountForcedCells(board, level);

            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    if (!IsEligibleEmptyCell(board, level, row, column))
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (!solution.HasValue)
                    {
                        continue;
                    }

                    CatFlapBoardSnapshot snapshot = board.CreateSnapshot();
                    int newForcedCount;
                    try
                    {
                        board.SetCellValue(row, column, (CatFlapCellValue)solution.Value, false);
                        int forcedAfter = CountForcedCells(board, level);
                        newForcedCount = System.Math.Max(0, forcedAfter - (forcedBefore - 1));
                    }
                    finally
                    {
                        board.Restore(snapshot);
                    }

                    if (newForcedCount <= 0)
                    {
                        continue;
                    }

                    candidates.Add(new CatFlapSolutionHintCandidate
                    {
                        Cell = new CatFlapCellPosition(row, column),
                        CorrectValue = (CatFlapCellValue)solution.Value,
                        Reason = CatFlapHintReason.UnlocksForcedCells,
                        FollowOnForcedCount = newForcedCount,
                        RowRemaining = CountUnresolved(board, level, row, -1),
                        ColumnRemaining = CountUnresolved(board, level, -1, column)
                    });
                }
            }

            return candidates;
        }

        private static List<CatFlapSolutionHintCandidate> FindClosestToCompletionCandidates(CatFlapBoardModel board, CatFlapLevelData level)
        {
            var candidates = new List<CatFlapSolutionHintCandidate>();
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    if (!IsEligibleEmptyCell(board, level, row, column))
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (!solution.HasValue)
                    {
                        continue;
                    }

                    candidates.Add(new CatFlapSolutionHintCandidate
                    {
                        Cell = new CatFlapCellPosition(row, column),
                        CorrectValue = (CatFlapCellValue)solution.Value,
                        Reason = CatFlapHintReason.ClosestToCompletion,
                        RowRemaining = CountUnresolved(board, level, row, -1),
                        ColumnRemaining = CountUnresolved(board, level, -1, column)
                    });
                }
            }

            return candidates;
        }

        private static List<CatFlapCellPosition> FindCorrectNoteCells(CatFlapBoardModel board, CatFlapLevelData level)
        {
            var cells = new List<CatFlapCellPosition>();
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = board.GetCell(row, column);
                    if (cell.IsLocked || !cell.IsNote)
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (solution.HasValue && cell.Value == (CatFlapCellValue)solution.Value)
                    {
                        cells.Add(new CatFlapCellPosition(row, column));
                    }
                }
            }

            return cells;
        }

        private static bool IsEligibleEmptyCell(CatFlapBoardModel board, CatFlapLevelData level, int row, int column)
        {
            CatFlapCellModel cell = board.GetCell(row, column);
            if (cell.IsLocked)
            {
                return false;
            }

            if (board.GetEffectiveValue(row, column) != CatFlapCellValue.Empty)
            {
                return false;
            }

            // A note cell holding the CORRECT tentative value is handled separately
            // ("commit it") rather than treated as an open placement target.
            if (cell.IsNote)
            {
                int? solution = level.Solution[row].Cells[column];
                if (solution.HasValue && cell.Value == (CatFlapCellValue)solution.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountForcedCells(CatFlapBoardModel board, CatFlapLevelData level)
        {
            int count = 0;
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    if (IsEligibleEmptyCell(board, level, row, column) && CatFlapForcedCellAnalyzer.IsForcedCell(board, row, column))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountUnresolved(CatFlapBoardModel board, CatFlapLevelData level, int row, int column)
        {
            int count = 0;
            if (row >= 0)
            {
                for (int c = 0; c < board.ColumnCount; c++)
                {
                    if (!IsSolvedCell(board, level, row, c))
                    {
                        count++;
                    }
                }
            }
            else
            {
                for (int r = 0; r < board.RowCount; r++)
                {
                    if (!IsSolvedCell(board, level, r, column))
                    {
                        count++;
                    }
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

        private static Dictionary<string, int> BuildViolationCounts(global::CatFlap.Validation.CatFlapRuleValidationResult result)
        {
            var counts = new Dictionary<string, int>();
            for (int index = 0; index < result.Violations.Count; index++)
            {
                global::CatFlap.Validation.CatFlapRuleViolation violation = result.Violations[index];
                for (int cellIndex = 0; cellIndex < violation.CellKeys.Count; cellIndex++)
                {
                    string key = violation.CellKeys[cellIndex];
                    counts[key] = counts.TryGetValue(key, out int existing) ? existing + 1 : 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Reduces a candidate list (already restricted to a single priority tier) down to one
        /// deterministic winner: most violations, then strongest row/column completion score,
        /// then most follow-on forced cells, then nearest to the last tapped cell, then a stable
        /// top-left (row ascending, then column ascending) tie-break. Never uses randomness.
        /// </summary>
        private static CatFlapSolutionHintCandidate FindBest(List<CatFlapSolutionHintCandidate> candidates, CatFlapCellPosition? lastInteractedCell)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            List<CatFlapSolutionHintCandidate> remaining = candidates;
            remaining = KeepMax(remaining, c => c.ViolationCount);
            remaining = KeepMax(remaining, c => -c.CompletionScore);
            remaining = KeepMax(remaining, c => c.FollowOnForcedCount);

            if (lastInteractedCell.HasValue)
            {
                CatFlapCellPosition lastCell = lastInteractedCell.Value;
                foreach (CatFlapSolutionHintCandidate candidate in remaining)
                {
                    candidate.DistanceFromLastTap = ManhattanDistance(candidate.Cell, lastCell);
                }

                remaining = KeepMax(remaining, c => -c.DistanceFromLastTap);
            }

            remaining = KeepMax(remaining, c => -(c.Cell.Row * 1000 + c.Cell.Column));
            return remaining[0];
        }

        private static List<CatFlapSolutionHintCandidate> KeepMax(List<CatFlapSolutionHintCandidate> candidates, System.Func<CatFlapSolutionHintCandidate, int> selector)
        {
            int best = int.MinValue;
            for (int index = 0; index < candidates.Count; index++)
            {
                int value = selector(candidates[index]);
                if (value > best)
                {
                    best = value;
                }
            }

            var matches = new List<CatFlapSolutionHintCandidate>();
            for (int index = 0; index < candidates.Count; index++)
            {
                if (selector(candidates[index]) == best)
                {
                    matches.Add(candidates[index]);
                }
            }

            return matches;
        }

        private static int ManhattanDistance(CatFlapCellPosition a, CatFlapCellPosition b)
        {
            return System.Math.Abs(a.Row - b.Row) + System.Math.Abs(a.Column - b.Column);
        }

        private static CatFlapCellRevealResult ToRevealResult(CatFlapSolutionHintCandidate candidate)
        {
            string message = BuildMessage(candidate);
            return new CatFlapCellRevealResult(true, candidate.Cell.Row, candidate.Cell.Column, candidate.CorrectValue, message, candidate.Reason.ToString());
        }

        private static string BuildMessage(CatFlapSolutionHintCandidate candidate)
        {
            string cellLabel = $"R{candidate.Cell.Row + 1}·C{candidate.Cell.Column + 1}";
            string valueLabel = candidate.CorrectValue == CatFlapCellValue.Cat ? "Cat 🐱" : "Fish 🐟";

            if (candidate.IsWrongTile)
            {
                return $"Fix {cellLabel} → {valueLabel}";
            }

            return $"Place {valueLabel} at {cellLabel}";
        }
    }
}
