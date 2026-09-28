using System.Collections.Generic;
using CatFlap.Models;
using CatFlap.Validation;
using CatFlap.Views;
using Shared.GameFlow;

namespace CatFlap.Runtime
{
    public sealed partial class CatFlapRuntimeController
    {
        private List<CatFlapCellPosition> GetLockedCells()
        {
            var locked = new List<CatFlapCellPosition>();
            if (!session.HasLevel)
            {
                return locked;
            }

            for (int row = 0; row < session.Board.RowCount; row++)
            {
                for (int column = 0; column < session.Board.ColumnCount; column++)
                {
                    if (session.Board.GetCell(row, column).IsLocked)
                    {
                        locked.Add(new CatFlapCellPosition(row, column));
                    }
                }
            }

            return locked;
        }

        // Green only ever comes from an actually-validated-complete row/column - a given clue cell
        // must NOT turn green just for being locked/pre-placed at level start; it stays in its
        // normal locked look until its row or column genuinely completes, then turns green
        // together with the rest of that line, same as any player-placed cell in it.
        private bool ShouldUseLockedColor(int row, int column)
        {
            if (Contains(pendingCompletedVisualCells, row, column))
            {
                return false;
            }

            return IsValidatedCompletedRow(row) ||
                   IsValidatedCompletedColumn(column);
        }

        private bool IsCompletedCorrectRow(int row)
        {
            for (int column = 0; column < session.Board.ColumnCount; column++)
            {
                if (!IsSolvedCell(row, column))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCompletedCorrectColumn(int column)
        {
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                if (!IsSolvedCell(row, column))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidatedCompletedRow(int row)
        {
            return IsCompletedCorrectRow(row) && IsRowLocked(row);
        }

        private bool IsValidatedCompletedColumn(int column)
        {
            return IsCompletedCorrectColumn(column) && IsColumnLocked(column);
        }

        /// <summary>
        /// True once (row, column) sits in a row/column that has fully validated as complete and
        /// correct against the level's solution (shown green) - used by the locked-tap negative
        /// feedback (see CatFlapGameplayManager.HandleCellSelected) to skip the shake/haptic/SFX
        /// for a pre-placed clue cell that's since become part of a completed line.
        /// </summary>
        public bool IsCellPartOfCompletedLine(int row, int column)
        {
            return session.HasLevel && (IsValidatedCompletedRow(row) || IsValidatedCompletedColumn(column));
        }

        private bool IsRowLocked(int row)
        {
            for (int column = 0; column < session.Board.ColumnCount; column++)
            {
                if (!session.Board.GetCell(row, column).IsLocked)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsColumnLocked(int column)
        {
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                if (!session.Board.GetCell(row, column).IsLocked)
                {
                    return false;
                }
            }

            return true;
        }

        // Backs CatFlapRuntimeController.HasPendingRowOutcome/HasPendingColumnOutcome - a
        // synchronous, no-delay read of whether this one line (row or column) currently shows any
        // of the three outcomes a delayed timer would eventually need to act on:
        //   0. Currently shows Error (checked first, unconditionally) - this line still has cells
        //      in ruleErrorCells from a PRIOR validation pass. Only a real ValidateBoard/
        //      ResolveMistake pass can clear a stale entry there (via CatFlapRuleValidationResult.
        //      ReplaceLines) - if this edit just fixed the line but it isn't full yet, cases 1-3
        //      below would all say "nothing to do" and skip starting a timer, permanently
        //      stranding the old violation in ruleErrorCells even though the board itself is fine
        //      again. So a line already marked Error always gets one more confirm pass regardless
        //      of what the fresh checks below say.
        //   1. Rule violation (Fail) - a partially-filled line can already violate Max 2 Adjacent
        //      or exceed a count target, so this doesn't require the line to be full.
        //   2. Complete - fully filled, matches the level's solution, and not already locked (an
        //      already-locked line has nothing new to confirm).
        //   3. Wrong solution (Warning) - fully filled, rule-legal, but doesn't match the
        //      solution.
        // A line that's currently clean (not Error) and still safely mid-fill with none of 1-3
        // reports false - nothing would change for it by waiting, since any further edit to this
        // exact line restarts its own independent timer from scratch anyway (see
        // CatFlapGameplayManager.StartDelayedValidation).
        private bool LineHasPendingOutcome(int index, bool isRow)
        {
            if (LineCurrentlyShowsError(index, isRow) || LineCurrentlyShowsWrongSequence(index, isRow))
            {
                return true;
            }

            CatFlapRuleValidationResult result = isRow
                ? session.PreviewRuleValidation(index, -1)
                : session.PreviewRuleValidation(-1, index);

            if (!result.IsValid)
            {
                return true;
            }

            bool filled = isRow ? IsFilledRow(index) : IsFilledColumn(index);
            if (!filled)
            {
                return false;
            }

            bool matchesSolution = isRow ? IsCompletedCorrectRow(index) : IsCompletedCorrectColumn(index);
            if (!matchesSolution)
            {
                return true;
            }

            bool alreadyLocked = isRow ? IsRowLocked(index) : IsColumnLocked(index);
            return !alreadyLocked;
        }

        // Reads session.LastRuleResult (the actual persisted/merged validation state) rather
        // than the transient ruleErrorCells visual list. This matters because
        // ClearValidationFeedbackForCellEdit -> RemoveRuleErrorLine optimistically strips a
        // just-edited cell's whole row/column out of ruleErrorCells the INSTANT it's tapped, as
        // immediate feedback - well before this gate ever runs - even though the underlying
        // LastRuleResult still has the real violation. Checking ruleErrorCells here would see
        // that optimistic clear and wrongly conclude "nothing pending", permanently stranding
        // the stale violation in LastRuleResult since nothing would ever run the confirm pass
        // that clears it via CatFlapRuleValidationResult.ReplaceLines.
        private bool LineCurrentlyShowsError(int index, bool isRow)
        {
            return LineHasViolation(session.LastRuleResult, isRow, index);
        }

        // Same reasoning as LineCurrentlyShowsError, for the orange Warning/wrong-solution case.
        // wrongSequenceCells has no LastRuleResult-equivalent persisted source (it's always
        // recomputed fresh, never incrementally merged), but RemoveWarningLine - the equivalent
        // optimistic clear for this case - only strips wrongSequenceCells itself, never
        // previousWrongRows/previousWrongColumns (the bookkeeping set from the last REAL
        // UpdateWrongSequenceCells pass). So that bookkeeping is what's checked here instead of
        // the just-cleared visual list.
        private bool LineCurrentlyShowsWrongSequence(int index, bool isRow)
        {
            return isRow ? previousWrongRows.Contains(index) : previousWrongColumns.Contains(index);
        }

        private bool IsSolvedCell(int row, int column)
        {
            CatFlapCellModel cell = session.Board.GetCell(row, column);
            int? solution = session.Level.Solution[row].Cells[column];
            return solution.HasValue && !cell.IsNote && cell.Value == (CatFlapCellValue)solution.Value;
        }

        private CatFlapCellVisualState ResolveVisualState(int row, int column)
        {
            if (Contains(mistakeCells, row, column) || Contains(ruleErrorCells, row, column))
            {
                return CatFlapCellVisualState.Error;
            }

            if (Contains(wrongSequenceCells, row, column))
            {
                return CatFlapCellVisualState.Warning;
            }

            CatFlapCellModel cell = session.Board.GetCell(row, column);
            return cell.IsLocked &&
                   !Contains(pendingCompletedVisualCells, row, column) &&
                   (IsValidatedCompletedRow(row) || IsValidatedCompletedColumn(column))
                ? CatFlapCellVisualState.Completed
                : CatFlapCellVisualState.Normal;
        }

        private List<CatFlapCellPosition> GetCompletedLineCells(List<CatFlapCellPosition> newlyLocked)
        {
            var positions = new List<CatFlapCellPosition>();
            if (newlyLocked == null)
            {
                return positions;
            }

            for (int index = 0; index < newlyLocked.Count; index++)
            {
                CatFlapCellPosition position = newlyLocked[index];
                if (IsValidatedCompletedRow(position.Row))
                {
                    AddRowCells(positions, position.Row);
                }

                if (IsValidatedCompletedColumn(position.Column))
                {
                    AddColumnCells(positions, position.Column);
                }
            }

            return positions;
        }

        /// <summary>
        /// Groups the cells from <see cref="GetCompletedLineCells"/> into reveal steps: every cell
        /// within a step reveals in the same instant, and only the NEXT step waits the stagger
        /// delay. A plain single-line completion (just a row, or just a column) produces one cell
        /// per step, same as a flat cell-by-cell reveal. A corner tap that completes a row AND a
        /// column at once instead interleaves them - step 0 takes the row's 1st cell together with
        /// the column's 1st cell, step 1 takes both 2nd cells, and so on - so the row and column
        /// visibly flip together rather than the whole row finishing before the column even starts.
        /// The shared intersection cell only ever appears once, in whichever line's step reaches it
        /// first.
        /// </summary>
        private List<List<CatFlapCellPosition>> GetCompletedLineRevealGroups(List<CatFlapCellPosition> newlyLocked)
        {
            var groups = new List<List<CatFlapCellPosition>>();
            if (newlyLocked == null)
            {
                return groups;
            }

            var lines = new List<List<CatFlapCellPosition>>();
            var seenRows = new List<int>();
            var seenColumns = new List<int>();

            for (int index = 0; index < newlyLocked.Count; index++)
            {
                CatFlapCellPosition position = newlyLocked[index];
                if (IsValidatedCompletedRow(position.Row) && !seenRows.Contains(position.Row))
                {
                    seenRows.Add(position.Row);
                    var line = new List<CatFlapCellPosition>();
                    AddRowCells(line, position.Row);
                    lines.Add(line);
                }

                if (IsValidatedCompletedColumn(position.Column) && !seenColumns.Contains(position.Column))
                {
                    seenColumns.Add(position.Column);
                    var line = new List<CatFlapCellPosition>();
                    AddColumnCells(line, position.Column);
                    lines.Add(line);
                }
            }

            int maxLineLength = 0;
            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index].Count > maxLineLength)
                {
                    maxLineLength = lines[index].Count;
                }
            }

            var alreadyScheduled = new List<CatFlapCellPosition>();
            for (int step = 0; step < maxLineLength; step++)
            {
                var group = new List<CatFlapCellPosition>();
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    if (step >= lines[lineIndex].Count)
                    {
                        continue;
                    }

                    CatFlapCellPosition cell = lines[lineIndex][step];
                    if (Contains(alreadyScheduled, cell.Row, cell.Column))
                    {
                        continue;
                    }

                    group.Add(cell);
                    alreadyScheduled.Add(cell);
                }

                if (group.Count > 0)
                {
                    groups.Add(group);
                }
            }

            return groups;
        }

        private void AddRowCells(List<CatFlapCellPosition> positions, int row)
        {
            for (int column = 0; column < session.Board.ColumnCount; column++)
            {
                if (!Contains(positions, row, column))
                {
                    positions.Add(new CatFlapCellPosition(row, column));
                }
            }
        }

        private void AddColumnCells(List<CatFlapCellPosition> positions, int column)
        {
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                if (!Contains(positions, row, column))
                {
                    positions.Add(new CatFlapCellPosition(row, column));
                }
            }
        }

        // Additive, never a blanket replace/clear - two different completions from two separate
        // moves can now have their own staggered reveals genuinely running in parallel (see
        // CatFlapRuntimeController.PlayLockedCellsThenPageState), each with its own still-pending
        // cells in this SAME shared list. Wiping the whole list here for a second completion would
        // rip the first one's still-mid-reveal cells out of "pending" early, snapping them
        // straight to green with no flip and killing their own particle/appreciation beat. Each
        // cell removes only itself, one at a time, once its OWN reveal actually plays it (see
        // RevealCompletedCell) - or, if there's no boardView to drive that, PlayLockedCellsThenPageState's
        // fallback branch removes this batch's own cells directly.
        private void SetPendingCompletedVisualCells(List<CatFlapCellPosition> positions)
        {
            if (positions == null)
            {
                return;
            }

            for (int index = 0; index < positions.Count; index++)
            {
                CatFlapCellPosition position = positions[index];
                if (!Contains(pendingCompletedVisualCells, position.Row, position.Column))
                {
                    pendingCompletedVisualCells.Add(position);
                }
            }
        }

        private List<CatFlapCellPosition> GetNewlyLockedCells(List<CatFlapCellPosition> lockedBefore)
        {
            var newlyLocked = new List<CatFlapCellPosition>();
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                for (int column = 0; column < session.Board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = session.Board.GetCell(row, column);
                    if (cell.IsLocked && !Contains(lockedBefore, row, column))
                    {
                        newlyLocked.Add(new CatFlapCellPosition(row, column));
                    }
                }
            }

            return newlyLocked;
        }

        private static bool Contains(List<CatFlapCellPosition> positions, int row, int column)
        {
            for (int index = 0; index < positions.Count; index++)
            {
                if (positions[index].Row == row && positions[index].Column == column)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddMistakeCell(int row, int column)
        {
            if (!Contains(mistakeCells, row, column))
            {
                mistakeCells.Add(new CatFlapCellPosition(row, column));
            }
        }

        private void RemoveErrorCell(int row, int column)
        {
            RemoveCell(mistakeCells, row, column);
            RemoveCell(ruleErrorCells, row, column);
        }

        private void ClearValidationFeedbackForCellEdit(int row, int column)
        {
            bool editedCellHadRuleError = Contains(ruleErrorCells, row, column);
            RemoveCell(mistakeCells, row, column);
            RemoveWarningLine(row, column);
            if (editedCellHadRuleError)
            {
                RemoveRuleErrorLine(row, column);
            }

            if (ruleErrorCells.Count == 0)
            {
                ResetRuleView();
            }
        }

        private void ClearErrorCells()
        {
            mistakeCells.Clear();
            ruleErrorCells.Clear();
            wrongSequenceCells.Clear();
            pendingCompletedVisualCells.Clear();
            // Hide any row/column warning icon left showing from before this clear - done BEFORE
            // previousWrongRows/Columns are cleared below, since those are what say which
            // indicators are currently showing one.
            foreach (int row in previousWrongRows)
            {
                SetLineWarningIndicator(isRow: true, index: row, isWrong: false);
            }

            foreach (int column in previousWrongColumns)
            {
                SetLineWarningIndicator(isRow: false, index: column, isWrong: false);
            }

            previousWrongRows.Clear();
            previousWrongColumns.Clear();
            lastChargedViolationSignatureByRule.Clear();
            wrongSolutionDismissArmed = false;
            wrongSolutionTextSuppressed = false;
            // Force, not the normal fade-out Hide - ClearErrorCells runs at the START of every
            // LoadLevel/Clear/Restart (and RefreshValidationVisuals' restore path), so a level that
            // starts clean must never play a fade-out just because the indicator happened to still
            // be showing from whatever was on screen before this level loaded. Whatever the
            // restored/loaded board's own wrong-sequence detection finds afterward (see
            // RefreshValidationVisuals -> UpdateWrongSequenceCells -> SyncWrongSolutionStatusText)
            // shows it again from scratch, with its normal pop-in, if genuinely warranted.
            statusView?.ForceHideWrongSolutionWarning();
            // Same "never linger from before this level loaded" reasoning as the warning above -
            // FinishLevelStartEntrance re-enables this later on, via
            // HardLevelPopupController.OnMoveToTargetArrived, only if the freshly loaded level is
            // actually a hard level.
            statusView?.DisableHardLevelUI();
            // A fresh/restarted level must never start with the Info button still disabled from
            // the previous level's win/loss (see CatFlapGameplayManager.HandleLevelWon/
            // HandleLevelFailed).
            statusView?.EnableInfoButton();
        }

        private void ResetRuleView()
        {
            statusView?.ShowRuleResult(null, false);
        }

        // Called only from ValidateBoard's Won branch - see the comment there for why Won can
        // still be reached with stale entries in these sets. Deliberately narrower than
        // ClearErrorCells: leaves pendingCompletedVisualCells alone (it drives whether
        // ShouldUseLockedColor treats a cell as revealed yet - clearing it here would make cells
        // still queued for the staggered win reveal jump straight to green with no flip) and
        // leaves lastChargedViolationSignatureByRule/wrongSolutionDismissArmed/
        // wrongSolutionTextSuppressed untouched (irrelevant once won, harmless either way).
        private void ClearValidationErrorStateForWin()
        {
            mistakeCells.Clear();
            ruleErrorCells.Clear();
            wrongSequenceCells.Clear();

            foreach (int row in previousWrongRows)
            {
                SetLineWarningIndicator(isRow: true, index: row, isWrong: false);
            }

            foreach (int column in previousWrongColumns)
            {
                SetLineWarningIndicator(isRow: false, index: column, isWrong: false);
            }

            previousWrongRows.Clear();
            previousWrongColumns.Clear();
            previousErrorRows.Clear();
            previousErrorColumns.Clear();
            statusView?.ForceHideWrongSolutionWarning();
        }

        // mistakeCells only ever shrinks per-cell, via ClearValidationFeedbackForCellEdit/
        // RemoveErrorCell when that EXACT cell is edited/fixed - so if a different cell's fix
        // resolves the same shared row/column violation (e.g. two cells contributed to one broken
        // rule and only one of them gets corrected), the other's stale red mistake highlight would
        // otherwise never clear on its own. Every UpdateRuleErrorCells call already has a freshly
        // computed validation result in hand, so prune here: any mistake cell no longer part of a
        // current violation is no longer "still broken" and should stop rendering as an error.
        private void PruneResolvedMistakeCells(CatFlapRuleValidationResult result)
        {
            for (int index = mistakeCells.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = mistakeCells[index];
                if (!ContainsViolationCell(result, position.Row, position.Column))
                {
                    mistakeCells.RemoveAt(index);
                }
            }
        }

        // Single choke point for the HUD rule-chip red/X (statusView.ShowRuleResult) - every
        // internal caller (ValidateBoard, ApplyAllNotes, RefreshValidationVisuals, UseFix) routes
        // through here instead of calling statusView directly, so SuppressWrongIndication only
        // needs handling in one place. A currently-invalid result is swapped for a synthetic
        // all-valid one while suppressed (an empty CatFlapRuleValidationResult has no violations,
        // so IsValid/IsRuleValid both read true) - a genuinely valid result passes through
        // unchanged either way.
        private void ShowRuleResult(CatFlapRuleValidationResult result, bool boardComplete)
        {
            statusView?.ShowRuleResult(SuppressWrongIndication && result != null && !result.IsValid
                ? new CatFlapRuleValidationResult()
                : result, boardComplete);
        }

        private void UpdateRuleErrorCells(CatFlapRuleValidationResult result)
        {
            PruneResolvedMistakeCells(result);
            ruleErrorCells.Clear();
            if (SuppressWrongIndication || result == null || result.IsValid)
            {
                previousErrorRows.Clear();
                previousErrorColumns.Clear();
                return;
            }

            var currentErrorRows = new List<int>();
            var currentErrorColumns = new List<int>();

            for (int violationIndex = 0; violationIndex < result.Violations.Count; violationIndex++)
            {
                IReadOnlyList<string> keys = result.Violations[violationIndex].CellKeys;
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    if (TryParseCellKey(keys[keyIndex], out int row, out int column) && !Contains(ruleErrorCells, row, column))
                    {
                        ruleErrorCells.Add(new CatFlapCellPosition(row, column));
                    }
                }

                // Same "which line does this violation belong to" logic as
                // CatFlapRuleValidationResult.BelongsToLine - every key sharing the same row means
                // a row violation, every key sharing the same column means a column violation
                // (both, for the rare single-key case).
                if (TryGetViolationLine(keys, out int violationRow, out int violationColumn))
                {
                    if (violationRow >= 0 && !currentErrorRows.Contains(violationRow))
                    {
                        currentErrorRows.Add(violationRow);
                    }

                    if (violationColumn >= 0 && !currentErrorColumns.Contains(violationColumn))
                    {
                        currentErrorColumns.Add(violationColumn);
                    }
                }
            }

            for (int index = 0; index < currentErrorRows.Count; index++)
            {
                int row = currentErrorRows[index];
                if (!previousErrorRows.Contains(row))
                {
                    LogRuleErrorLine(isRow: true, index: row);
                }
            }

            for (int index = 0; index < currentErrorColumns.Count; index++)
            {
                int column = currentErrorColumns[index];
                if (!previousErrorColumns.Contains(column))
                {
                    LogRuleErrorLine(isRow: false, index: column);
                }
            }

            previousErrorRows.Clear();
            previousErrorRows.UnionWith(currentErrorRows);
            previousErrorColumns.Clear();
            previousErrorColumns.UnionWith(currentErrorColumns);
        }

        // Determines which row and/or column a violation's cell keys belong to - a row (or
        // column) violation, has every key sharing that same row (or column). Returns -1 for
        // whichever dimension doesn't uniformly match (both can be set for a rare single-key
        // violation).
        private static bool TryGetViolationLine(IReadOnlyList<string> keys, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (keys.Count == 0)
            {
                return false;
            }

            bool allSameRow = true;
            bool allSameColumn = true;
            int firstRow = -1;
            int firstColumn = -1;
            for (int index = 0; index < keys.Count; index++)
            {
                if (!TryParseCellKey(keys[index], out int keyRow, out int keyColumn))
                {
                    return false;
                }

                if (index == 0)
                {
                    firstRow = keyRow;
                    firstColumn = keyColumn;
                }
                else
                {
                    allSameRow &= keyRow == firstRow;
                    allSameColumn &= keyColumn == firstColumn;
                }
            }

            if (allSameRow)
            {
                row = firstRow;
            }

            if (allSameColumn)
            {
                column = firstColumn;
            }

            return allSameRow || allSameColumn;
        }

        // Diagnostic-only: dumps a newly-broken rule-violation row/column's actual placed values
        // next to the level's expected solution values for that same line - mirrors
        // LogWrongSequenceLine, so an "why is this red" report can be verified straight from the
        // Console the same way a "why is this orange" one can.
        private void LogRuleErrorLine(bool isRow, int index)
        {
            if (session.Level?.Solution == null)
            {
                return;
            }
        }

        // Diagnostic-only: dumps the row/column that triggered a single-tap scoped validation
        // (see CatFlapRuntimeSession.ValidateCurrentBoard(int,int)) alongside every violation it
        // came back with, so a stale rule-error highlight can be traced to whether it came from
        // the freshly-rescanned row/column or was carried over unchanged from a prior full/scoped
        // result.
        private void LogRuleResult(string caller, int triggerRow, int triggerColumn, CatFlapRuleValidationResult result)
        {
            if (result == null)
            {
                return;
            }
        }

        // Only the cells that just became part of a broken rule (weren't already in ruleErrorCells
        // before this validation pass) should play the rule-break blink/Sad reaction - a cell
        // that's been sitting in an earlier, still-unresolved violation already played that
        // reaction once and should just stay statically red, not replay it every time some OTHER
        // rule breaks elsewhere on the board. Pass the pre-UpdateRuleErrorCells snapshot in.
        private List<CatFlapCellPosition> GetNewlyBrokenRuleCells(List<CatFlapCellPosition> ruleErrorCellsBefore)
        {
            var newlyBroken = new List<CatFlapCellPosition>();
            for (int index = 0; index < ruleErrorCells.Count; index++)
            {
                CatFlapCellPosition position = ruleErrorCells[index];
                if (!Contains(ruleErrorCellsBefore, position.Row, position.Column))
                {
                    newlyBroken.Add(position);
                }
            }

            return newlyBroken;
        }

        private static bool ContainsViolationCell(CatFlapRuleValidationResult result, int targetRow, int targetColumn)
        {
            if (result == null || result.IsValid)
            {
                return false;
            }

            for (int violationIndex = 0; violationIndex < result.Violations.Count; violationIndex++)
            {
                IReadOnlyList<string> keys = result.Violations[violationIndex].CellKeys;
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    if (TryParseCellKey(keys[keyIndex], out int row, out int column) &&
                        row == targetRow &&
                        column == targetColumn)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // True when every violation group containing (row, column) is an exact repeat (same rule,
        // same cell-key set) of the violation last charged for that rule - i.e. a mistake has
        // already been charged for this exact still-broken rule instance, just via a different
        // cell in the same violation. Two cells placed in quick succession each run their own
        // independent delayed-validation coroutine (see CatFlapGameplayManager.StartDelayedValidation),
        // and if both land as part of the SAME violation (e.g. two fast taps that together push a
        // row to 3 adjacent fish), each would otherwise call ResolveMistake and spend its own
        // separate mistake for what is really one rule break. ResolveMistake calls this first to
        // charge it only once per violation.
        //
        // Comparing the full cell-key SET (not mere cell overlap against all-time mistakeCells)
        // matters: adjacent violation windows slide and necessarily share cells (e.g. {1,2,3} and
        // {2,3,4}), so a plain overlap check would wrongly treat placing a 4th adjacent fish as
        // "already charged" just because it shares cells with the 3rd fish's violation.
        private bool IsViolationAlreadyChargedAtCell(int row, int column, bool checkRow, bool checkColumn, out List<(CatFlapRule rule, string signature)> touchedViolations)
        {
            touchedViolations = new List<(CatFlapRule rule, string signature)>();
            CatFlapRuleValidationResult result = session.PreviewRuleValidation(checkRow ? row : -1, checkColumn ? column : -1);
            if (result == null || result.IsValid)
            {
                return false;
            }

            bool allAlreadyCharged = true;
            for (int violationIndex = 0; violationIndex < result.Violations.Count; violationIndex++)
            {
                CatFlapRuleViolation violation = result.Violations[violationIndex];
                IReadOnlyList<string> keys = violation.CellKeys;
                bool containsTarget = false;
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    if (TryParseCellKey(keys[keyIndex], out int keyRow, out int keyColumn) && keyRow == row && keyColumn == column)
                    {
                        containsTarget = true;
                        break;
                    }
                }

                if (!containsTarget)
                {
                    continue;
                }

                string signature = BuildViolationSignature(keys);
                touchedViolations.Add((violation.Rule, signature));
                bool alreadyCharged = lastChargedViolationSignatureByRule.TryGetValue(violation.Rule, out string lastSignature) && lastSignature == signature;
                allAlreadyCharged &= alreadyCharged;
            }

            return touchedViolations.Count > 0 && allAlreadyCharged;
        }

        private static string BuildViolationSignature(IReadOnlyList<string> cellKeys)
        {
            var sortedKeys = new List<string>(cellKeys);
            sortedKeys.Sort(System.StringComparer.Ordinal);
            return string.Join("|", sortedKeys);
        }

        // playFeedback must stay false for every load/restart/continue/periodic-refresh call
        // (RefreshValidationVisuals) - the one-shot SHAKE should only play for a line the player
        // just broke via a live action (ValidateBoard, ApplyAllNotes), never replayed for a line
        // that was already wrong before this session even started. Same principle as
        // RenderStatus's animateHeartLoss guard. The BLINK itself is not gated by this - it always
        // starts for a newly-wrong line (even on load/resume, matching the always-synced
        // SyncWrongSolutionStatusText text below) so the orange highlight and the HUD text never
        // fall out of sync.
        private void UpdateWrongSequenceCells(CatFlapRuleValidationResult result, bool playFeedback = false)
        {
            UpdateWrongSequenceCells(result, -1, -1, playFeedback);
        }

        /// <summary>
        /// scopedRow/scopedColumn identify which line(s) this validation pass actually just
        /// freshly rechecked (see CatFlapRuntimeSession.ValidateCurrentBoard(int,int)) - pass -1
        /// for both (or use the parameterless overload) for a full-board revalidation, which
        /// re-derives every row's and column's wrong-sequence status from scratch.
        ///
        /// A per-line independent timer (see CatFlapGameplayManager.Validation) instead passes
        /// its own scoped row and/or column, and this only re-derives wrong-sequence status for
        /// THAT line - every other line's existing membership in wrongSequenceCells is left
        /// untouched. This matters because a DIFFERENT line that was edited more recently may
        /// still have its own independent timer pending - a naive full-board scan gated on the
        /// single merged result.IsValid flag would see that line as "no known violation yet"
        /// (since its own timer hasn't rechecked it) and wrongly flag it wrong-sequence (orange)
        /// instead of leaving it alone until its own timer resolves it (potentially to a genuine
        /// rule violation/red instead).
        /// </summary>
        private void UpdateWrongSequenceCells(CatFlapRuleValidationResult result, int scopedRow, int scopedColumn, bool playFeedback = false)
        {
            if (scopedRow < 0 && scopedColumn < 0)
            {
                UpdateWrongSequenceCellsFullBoard(result, playFeedback);
                return;
            }

            if (scopedRow >= 0)
            {
                UpdateWrongSequenceForLine(isRow: true, index: scopedRow, result, playFeedback);
            }

            if (scopedColumn >= 0)
            {
                UpdateWrongSequenceForLine(isRow: false, index: scopedColumn, result, playFeedback);
            }

            SyncWrongSolutionStatusText();
        }

        private void UpdateWrongSequenceCellsFullBoard(CatFlapRuleValidationResult result, bool playFeedback)
        {
            wrongSequenceCells.Clear();

            var currentWrongRows = new List<int>();
            var currentWrongColumns = new List<int>();

            if (result != null && result.IsValid)
            {
                for (int row = 0; row < session.Board.RowCount; row++)
                {
                    if (IsFilledRow(row) && !IsCompletedCorrectRow(row))
                    {
                        AddRowCells(wrongSequenceCells, row);
                        currentWrongRows.Add(row);
                    }
                }

                for (int column = 0; column < session.Board.ColumnCount; column++)
                {
                    if (IsFilledColumn(column) && !IsCompletedCorrectColumn(column))
                    {
                        AddColumnCells(wrongSequenceCells, column);
                        currentWrongColumns.Add(column);
                    }
                }
            }

            // Start the blink once per row/column that just became wrong-sequence (wasn't already
            // flagged before this call) - never restarted on every re-validation while it stays
            // wrong (PlayWarningBlink is itself idempotent, but there's no need to keep reissuing
            // it). Deliberately not fired for the separate rule-violation/mistake case (that one
            // already gets heart break + rule-break-blink). This always runs (not gated on
            // playFeedback) so the blink can never fall out of sync with the always-synced HUD text
            // below - only the one-shot shake is suppressed on a silent load/resume.
            var newlyWrongCells = new List<CatFlapCellPosition>();
            for (int index = 0; index < currentWrongRows.Count; index++)
            {
                int row = currentWrongRows[index];
                if (!previousWrongRows.Contains(row))
                {
                    AddRowCells(newlyWrongCells, row);
                    LogWrongSequenceLine(isRow: true, index: row);
                }
            }

            for (int index = 0; index < currentWrongColumns.Count; index++)
            {
                int column = currentWrongColumns[index];
                if (!previousWrongColumns.Contains(column))
                {
                    AddColumnCells(newlyWrongCells, column);
                    LogWrongSequenceLine(isRow: false, index: column);
                }
            }

            if (newlyWrongCells.Count > 0)
            {
                // A genuinely new occurrence overrides any earlier dismiss from a previous
                // wrong-sequence line - text goes back to visible for this one (SyncWrongSolutionStatusText below).
                wrongSolutionTextSuppressed = false;
                wrongSolutionDismissArmed = false;
                boardView?.PlayWarningBlink(newlyWrongCells, playShake: playFeedback);
            }

            previousWrongRows.Clear();
            previousWrongRows.UnionWith(currentWrongRows);
            previousWrongColumns.Clear();
            previousWrongColumns.UnionWith(currentWrongColumns);

            SyncWrongSolutionStatusText();
            SyncWarningIndicators(currentWrongRows, currentWrongColumns);
        }

        // Shows/hides each row's and column's target-indicator Wrong Solution icon (see
        // CatFlapTargetIndicatorViewAdapter.SetWarningState) - unconditionally re-synced for every
        // line every call (SetWarningState itself no-ops when a line's wrong state hasn't
        // actually changed), simpler than diffing against previousWrongRows/Columns here too.
        private void SyncWarningIndicators(List<int> currentWrongRows, List<int> currentWrongColumns)
        {
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                SetLineWarningIndicator(isRow: true, index: row, isWrong: currentWrongRows.Contains(row));
            }

            for (int column = 0; column < session.Board.ColumnCount; column++)
            {
                SetLineWarningIndicator(isRow: false, index: column, isWrong: currentWrongColumns.Contains(column));
            }
        }

        // Shared by both UpdateWrongSequenceCellsFullBoard (via SyncWarningIndicators) and
        // UpdateWrongSequenceForLine - shows/hides one row's or column's target-indicator Wrong
        // Solution icon (see CatFlapTargetIndicatorViewAdapter.SetWarningState, itself a no-op when
        // isWrong hasn't actually changed for that indicator).
        private void SetLineWarningIndicator(bool isRow, int index, bool isWrong)
        {
            if (boardView is not CatFlap.UnityAdapters.CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                return;
            }

            if (isRow)
            {
                spriteBoardView.SetRowWarningState(index, isWrong);
            }
            else
            {
                spriteBoardView.SetColumnWarningState(index, isWrong);
            }
        }

        // Re-derives wrong-sequence status for exactly one row or column (never both - see the
        // two-call pattern in the scoped UpdateWrongSequenceCells overload above), using
        // LineHasViolation(result, ...) rather than the global result.IsValid so this stays
        // correct even while some OTHER line's own independent timer is still pending.
        private void UpdateWrongSequenceForLine(bool isRow, int index, CatFlapRuleValidationResult result, bool playFeedback)
        {
            bool filled = isRow ? IsFilledRow(index) : IsFilledColumn(index);
            bool matchesSolution = isRow ? IsCompletedCorrectRow(index) : IsCompletedCorrectColumn(index);
            bool hasViolation = LineHasViolation(result, isRow, index);
            bool isWrong = filled && !matchesSolution && !hasViolation;
            bool wasWrong = isRow ? previousWrongRows.Contains(index) : previousWrongColumns.Contains(index);

            // Only strips cells whose OTHER dimension isn't itself still a known-wrong line (per
            // previousWrongRows/Columns, unaffected by this pass since only `index`'s own
            // dimension is being rechecked here). Without this, revalidating a row that just
            // completed correctly would blanket-clear every cell in it, including one that only
            // sits in wrongSequenceCells because its OWN column is still wrong-sequence - wiping
            // that column's highlight for this one row even though the column itself never
            // changed and is never rechecked in this same pass.
            if (isRow)
            {
                RemoveRowCellsExceptStillWrongOtherDimension(wrongSequenceCells, index);
            }
            else
            {
                RemoveColumnCellsExceptStillWrongOtherDimension(wrongSequenceCells, index);
            }

            SetLineWarningIndicator(isRow, index, isWrong);

            if (!isWrong)
            {
                if (isRow)
                {
                    previousWrongRows.Remove(index);
                }
                else
                {
                    previousWrongColumns.Remove(index);
                }

                return;
            }

            var cells = new List<CatFlapCellPosition>();
            if (isRow)
            {
                AddRowCells(cells, index);
                previousWrongRows.Add(index);
            }
            else
            {
                AddColumnCells(cells, index);
                previousWrongColumns.Add(index);
            }

            for (int index2 = 0; index2 < cells.Count; index2++)
            {
                CatFlapCellPosition cell = cells[index2];
                if (!Contains(wrongSequenceCells, cell.Row, cell.Column))
                {
                    wrongSequenceCells.Add(cell);
                }
            }

            if (!wasWrong)
            {
                LogWrongSequenceLine(isRow, index);
                // Same "new occurrence overrides an earlier dismiss" reasoning as
                // UpdateWrongSequenceCellsFullBoard above.
                wrongSolutionTextSuppressed = false;
                wrongSolutionDismissArmed = false;
                boardView?.PlayWarningBlink(cells, playShake: playFeedback);
            }
        }

        private void RemoveRowCellsExceptStillWrongOtherDimension(List<CatFlapCellPosition> positions, int row)
        {
            for (int index = positions.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = positions[index];
                if (position.Row == row && !previousWrongColumns.Contains(position.Column))
                {
                    positions.RemoveAt(index);
                }
            }
        }

        private void RemoveColumnCellsExceptStillWrongOtherDimension(List<CatFlapCellPosition> positions, int column)
        {
            for (int index = positions.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = positions[index];
                if (position.Column == column && !previousWrongRows.Contains(position.Row))
                {
                    positions.RemoveAt(index);
                }
            }
        }

        // True when `result` currently carries a violation that belongs entirely to this row (or
        // column) - mirrors CatFlapRuleValidationResult.BelongsToLine's per-key-row/per-key-column
        // check, since CatFlapRuleViolation only exposes its cell keys, not structured row/column
        // metadata.
        private static bool LineHasViolation(CatFlapRuleValidationResult result, bool isRow, int index)
        {
            if (result == null || result.IsValid)
            {
                return false;
            }

            for (int violationIndex = 0; violationIndex < result.Violations.Count; violationIndex++)
            {
                IReadOnlyList<string> keys = result.Violations[violationIndex].CellKeys;
                bool allMatch = keys.Count > 0;
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    if (!TryParseCellKey(keys[keyIndex], out int row, out int column) ||
                        (isRow ? row != index : column != index))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    return true;
                }
            }

            return false;
        }

        // Diagnostic-only: dumps a newly-wrong-sequence row/column's actual placed values next to
        // the level's expected solution values for that same line, so a "why is this orange"
        // report can be verified straight from the Console instead of re-deriving it by eye from
        // the board.
        private void LogWrongSequenceLine(bool isRow, int index)
        {
            if (session.Level?.Solution == null)
            {
                return;
            }

            WrongSolutionDetected(this, System.EventArgs.Empty);
        }

        // Keeps the shared "Wrong Solution" HUD text (WrongSolutionText under GamePlayUIRoot) in
        // sync with whether any row/column is currently flagged wrong-sequence - shown while at
        // least one is, hidden the instant none are left (whether cleared by a re-validation
        // finding it fixed, or by RemoveWarningLine on a direct edit) - EXCEPT while
        // wrongSolutionTextSuppressed is set: the blink already ran its course and the player's
        // next tap already dismissed the text (see ConsumeWrongSolutionTextDismissIfArmed), so it
        // stays hidden even though the same line is still wrong, until a fresh line goes wrong.
        private void SyncWrongSolutionStatusText()
        {
            if (wrongSequenceCells.Count > 0)
            {
                if (!wrongSolutionTextSuppressed)
                {
                    statusView?.ShowWrongSolutionWarning();
                }
            }
            else
            {
                wrongSolutionTextSuppressed = false;
                wrongSolutionDismissArmed = false;
                statusView?.HideWrongSolutionWarning();
            }
        }

        // Called once PlayWarningBlink's fixed-cycle blink has finished on its own for whatever is
        // currently wrong (see CatFlapGameplayManager.Validation's poll on
        // CatFlapSpriteBoardViewAdapter.IsAnyWarningBlinking) - arms the dismiss without touching
        // the text itself; it stays visible until the player's next tap.
        public void ArmWrongSolutionTextDismiss()
        {
            if (wrongSequenceCells.Count > 0 && !wrongSolutionTextSuppressed)
            {
                wrongSolutionDismissArmed = true;
            }
        }

        // Called on every board cell tap (see CatFlapGameplayManager.HandleCellSelected) - hides
        // the Wrong Solution text the instant a dismiss is armed, regardless of whether this
        // particular tap fixes the wrong line or not. A no-op otherwise.
        public void ConsumeWrongSolutionTextDismissIfArmed()
        {
            if (!wrongSolutionDismissArmed)
            {
                return;
            }

            wrongSolutionDismissArmed = false;
            wrongSolutionTextSuppressed = true;
            statusView?.HideWrongSolutionWarning();
        }

        private bool IsFilledRow(int row)
        {
            for (int column = 0; column < session.Board.ColumnCount; column++)
            {
                if (!IsFilledFinalValue(row, column))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFilledColumn(int column)
        {
            for (int row = 0; row < session.Board.RowCount; row++)
            {
                if (!IsFilledFinalValue(row, column))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFilledFinalValue(int row, int column)
        {
            CatFlapCellModel cell = session.Board.GetCell(row, column);
            return cell.Value != CatFlapCellValue.Empty && !cell.IsNote;
        }

        private static void RemoveCell(List<CatFlapCellPosition> positions, int row, int column)
        {
            for (int index = positions.Count - 1; index >= 0; index--)
            {
                if (positions[index].Row == row && positions[index].Column == column)
                {
                    positions.RemoveAt(index);
                }
            }
        }

        private void RemoveRuleErrorLine(int row, int column)
        {
            for (int index = ruleErrorCells.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = ruleErrorCells[index];
                if (position.Row == row || position.Column == column)
                {
                    ruleErrorCells.RemoveAt(index);
                }
            }
        }

        // Removing tiles requirement: if the player edits any tile in a wrong-solution
        // row/column, that line's highlight blink stops immediately - it doesn't wait for the
        // next full re-validation.
        //
        // Only clears a dimension the edited cell's OWN row/column is actually flagged
        // wrong-sequence for (checked via previousWrongRows/Columns, the bookkeeping set from the
        // last real UpdateWrongSequenceCells pass - see LineCurrentlyShowsWrongSequence). A cell
        // can sit in wrongSequenceCells purely because its COLUMN is wrong while its row is
        // perfectly fine (or vice versa); a naive "shares this row OR this column" match would
        // wipe that column's still-valid warning highlight the instant the player edits any other
        // cell in the same row, even though nothing about the wrong column changed.
        private void RemoveWarningLine(int row, int column)
        {
            bool rowIsWrong = previousWrongRows.Contains(row);
            bool columnIsWrong = previousWrongColumns.Contains(column);
            if (!rowIsWrong && !columnIsWrong)
            {
                return;
            }

            for (int index = wrongSequenceCells.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = wrongSequenceCells[index];
                if ((rowIsWrong && position.Row == row) || (columnIsWrong && position.Column == column))
                {
                    wrongSequenceCells.RemoveAt(index);
                    boardView?.StopWarningBlink(position.Row, position.Column);
                }
            }

            // Deliberately does NOT touch previousWrongRows/previousWrongColumns or the target
            // indicator icon here - see LineCurrentlyShowsWrongSequence above. Those stay stale on
            // purpose so the next real UpdateWrongSequenceCells pass still runs its confirm check
            // for this line instead of the pending-outcome gate wrongly concluding "nothing to do"
            // right after this optimistic clear.
            SyncWrongSolutionStatusText();
        }

        private static bool TryParseCellKey(string key, out int row, out int column)
        {
            row = 0;
            column = 0;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string[] parts = key.Split(',');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], out row) &&
                   int.TryParse(parts[1], out column);
        }

        private static GameFlowState ToGameFlowState(CatFlapGameState state)
        {
            switch (state)
            {
                case CatFlapGameState.MainMenu:
                case CatFlapGameState.Uninitialized:
                    return GameFlowState.MainMenu;
                case CatFlapGameState.Won:
                    return GameFlowState.Win;
                case CatFlapGameState.Failed:
                    return GameFlowState.Fail;
                default:
                    return GameFlowState.Gameplay;
            }
        }
    }
}
