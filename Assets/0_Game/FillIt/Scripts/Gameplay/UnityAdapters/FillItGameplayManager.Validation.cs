using System.Collections;
using Shared.Vibration;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public partial class CatFlapGameplayManager
    {
        // Runs the delayed wrong/correct check for exactly one dimension (row or column) of the
        // tapped cell - checkRow/checkColumn are mutually exclusive here, never both true. Row and
        // column are validated by two fully independent coroutines (see StartDelayedValidation)
        // rather than one combined per-cell coroutine, so a later tap that shares this cell's row
        // cancels/restarts only the row check - the column check started by this same original tap
        // keeps running to completion untouched, and vice versa for a later tap sharing the column.
        // Two genuinely separate checkpoints within this ONE coroutine (never two independent
        // coroutines racing each other - that's what used to cause the "two different total wait
        // durations" bug this was previously collapsed to avoid):
        //   correctValidationDelaySeconds (fast) - only a fresh Complete resolves here. Anything
        //   else (still filling, a rule violation, or a filled-but-wrong sequence) falls through
        //   and keeps waiting.
        //   wrongValidationDelaySeconds (slow, >= the fast one) - the final, authoritative check:
        //   same Complete/RuleBreak/WrongSolution logic as before, re-evaluated fresh in case
        //   anything changed while waiting out the remainder.
        // This means a correct placement is confirmed quickly, while a mistake still gets the
        // full delay as a grace period to be fixed before it's penalized.
        // correctCheckSecondsOverride is the fast checkpoint's own delay for THIS tap - normally
        // correctValidationDelaySeconds, but StartDelayedValidation passes
        // activeRulesConfig.LastCellCorrectValidationDelaySeconds instead when this tap just
        // filled the board's last remaining empty cell (see
        // CatFlapRuntimeSession.IsBoardComplete). wrongValidationDelaySeconds is never affected
        // either way.
        private IEnumerator ValidateLineAfterDelay(int row, int column, bool checkRow, bool checkColumn, float correctCheckSecondsOverride)
        {
            float correctCheckSeconds = Mathf.Max(0f, correctCheckSecondsOverride);
            float wrongCheckSeconds = Mathf.Max(correctCheckSeconds, wrongValidationDelaySeconds);

            yield return new WaitForSeconds(correctCheckSeconds);
            if (!controller.Session.HasLevel)
            {
                yield break;
            }

            while (controller.IsSequencing)
            {
                yield return null;
            }

            if (!controller.Session.HasLevel)
            {
                yield break;
            }

            // Fast checkpoint - Complete only. Mirrors the original guard: ValidateBoard is only
            // safe to treat as authoritative when there's no rule violation right now.
            if (!controller.HasRuleViolationAtCell(row, column, checkRow, checkColumn) &&
                controller.ValidateBoard(row, column, checkRow, checkColumn))
            {
                ApplyLineCompleteFeedback();
                FinishLineOutcome(row, column, checkRow, checkColumn);
                yield break;
            }

            // Not resolved as Complete yet - wait out the remainder before the final, slower
            // wrong/error check. Never shortened even if a rule violation is already visible now.
            float remainingSeconds = wrongCheckSeconds - correctCheckSeconds;
            if (remainingSeconds > 0f)
            {
                yield return new WaitForSeconds(remainingSeconds);
            }

            if (!controller.Session.HasLevel)
            {
                yield break;
            }

            while (controller.IsSequencing)
            {
                yield return null;
            }

            if (!controller.Session.HasLevel)
            {
                yield break;
            }

            // The three outcomes below are mutually exclusive for this one line at this instant -
            // a line can't simultaneously be a fresh Complete (filled + matches the solution),
            // a RuleBreak (violates Max 2 Adjacent / Equal Cats & Fish), and a WrongSolution
            // (filled, no rule violation, but doesn't match the solution). Checked in that exact
            // priority order: Complete first, RuleBreak second, WrongSolution third.
            bool ruleFailed = controller.HasRuleViolationAtCell(row, column, checkRow, checkColumn);

            // FTUE levels 1-3 never show wrong-answer feedback (no heart loss, no red/blink, no
            // sound) - guided input already keeps the player on the correct cells/values, this is
            // just a safety net against ever flagging a "mistake" during the tutorial.
            if (ruleFailed && CurrentLevelNumber <= 3)
            {
                RemoveDelayedValidation(row, column, checkRow, checkColumn);
                yield break;
            }

            if (!ruleFailed)
            {
                // 1. Complete - ValidateBoard is the only way to freshly learn this (it locks the
                // line if it just validated as complete+correct), and since ruleFailed is already
                // known false here, calling it can never also need the RuleBreak branch below in
                // the same pass.
                if (controller.ValidateBoard(row, column, checkRow, checkColumn))
                {
                    ApplyLineCompleteFeedback();
                }
                else if (controller.HasWrongSequenceAtCell(row, column))
                {
                    // 3. Wrong solution - line is fully filled and has no rule violation, but
                    // doesn't match the solution (the "yellow-orange" highlight case). Its own
                    // distinct sound, not the rule-violation one below - a genuine Easy-streak
                    // break, unlike the harmless 0-count ValidateBoard call above.
                    PlaySound(global::SoundName.LineWrong);
                    controller.ResetEasyStreak();
                    StartCoroutine(ArmWrongSolutionDismissWhenBlinkFinishes());
                }

                FinishLineOutcome(row, column, checkRow, checkColumn);
                yield break;
            }

            // 2. Rule break - a genuine Easy-streak break, unlike the harmless 0-count
            // ValidateBoard call the other dimension's own independent timer produces for the very
            // same successful move (see ValidateBoard's own comment on newlyLocked.Count > 0).
            controller.ResetEasyStreak();
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Heavy);
            // A violation on a still-unfinished line is a softer, early nudge; once the line
            // is fully placed the violation is locked in and unrecoverable as-is.
            PlaySound(global::SoundName.RuleBreakError);
            PlaySound(global::SoundName.HeartLose);
            controller.ResolveMistake(row, column, checkRow, checkColumn);
            SaveAll();
            RemoveDelayedValidation(row, column, checkRow, checkColumn);

            // Reserved-step reaction tip (see CatFlapFtueLevelStepsEntry.WalkthroughStepCount/
            // CatFlapFtueDriver.ShowMistakeTip) - a silent no-op on every level that doesn't
            // reserve one (the walkthrough already covers 100% of its steps there), so this is
            // safe to call unconditionally on every rule break, not just level 4's. Deferred
            // until the rule-break pulse (PlayRuleBreakBlink, started synchronously inside
            // ResolveMistake above) has actually finished playing on every cell it touched -
            // otherwise the FTUE banner/dim overlay would pop in on top of the red pulse instead
            // of after the player has seen it. Also what lets the rules walkthrough's own last-cell
            // free tap advance to its guided fix step (see
            // CatFlapFtueDriver.NotifyLineOutcomeResolved) only once that same red feedback has
            // actually finished playing, not the instant the tap landed.
            StartCoroutine(NotifyFtueWhenRuleBreakBlinkFinishes(row, column));
        }

        // Same "wait for the fixed-cycle blink to finish on its own" reasoning as
        // ArmWrongSolutionDismissWhenBlinkFinishes below, for the rule-break pulse instead of the
        // Wrong Solution blink. Not gated on IsSequencing/HasLevel - if the level is
        // cleared/reloaded mid-wait, ftueDriver itself is reconfigured by then and both calls
        // below become no-ops (or point at a target that's no longer meaningful but harmless).
        private IEnumerator NotifyFtueWhenRuleBreakBlinkFinishes(int row, int column)
        {
            if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                while (spriteBoardView.IsAnyRuleBreakBlinking())
                {
                    yield return null;
                }
            }

            ftueDriver?.ShowMistakeTip(row, column);

            // Same "only once BOTH dimensions have settled" guard as FinishLineOutcome's own call
            // to this - by the time this coroutine resumes here, the OTHER dimension's own
            // independent timer (see StartDelayedValidation) may since have started (or still be
            // running); checked fresh here rather than when this coroutine was first scheduled.
            if (!rowValidations.ContainsKey(row) && !columnValidations.ContainsKey(column))
            {
                ftueDriver?.NotifyLineOutcomeResolved(row, column, ruleBroken: true);
            }
        }

        // Waits until the Wrong Solution blink (started synchronously inside the ValidateBoard/
        // HasWrongSequenceAtCell call above, via CatFlapRuntimeController.VisualState's
        // PlayWarningBlink) has run its fixed number of cycles and stopped on its own, then arms
        // the HUD text's dismiss - see CatFlapRuntimeController.ArmWrongSolutionTextDismiss. Not
        // gated on IsSequencing/HasLevel the way ValidateLineAfterDelay's own waits are: if the
        // level is cleared/reloaded mid-wait, ArmWrongSolutionTextDismiss's own wrongSequenceCells
        // check (reset to empty by then) makes the call a no-op anyway. If boardView isn't the
        // sprite adapter (no per-cell blink to track), arms immediately.
        private IEnumerator ArmWrongSolutionDismissWhenBlinkFinishes()
        {
            if (boardView is CatFlapSpriteBoardViewAdapter spriteBoardView)
            {
                while (spriteBoardView.IsAnyWarningBlinking())
                {
                    yield return null;
                }
            }

            controller?.ArmWrongSolutionTextDismiss();
        }

        // Sound/vibration/half-progress feedback shared between the fast Complete checkpoint and
        // the final authoritative check below - factored out purely to avoid repeating it twice.
        private void ApplyLineCompleteFeedback()
        {
            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
            PlaySound(controller.LastNewlyCompletedLineCount >= 2
                ? global::SoundName.LineCompleteDouble
                : global::SoundName.LineComplete);

            if (controller.JustReachedHalfBoardProgress)
            {
                // PlaySound(global::SoundName.LineProgressHalf);
            }
        }

        // Cleanup shared between the fast Complete checkpoint and the final authoritative check's
        // Complete/WrongSolution/neither branch.
        private void FinishLineOutcome(int row, int column, bool checkRow, bool checkColumn)
        {
            // ValidateBoard() may have just locked a completed row/column (cells turn green),
            // which can make undo entries pointing at those cells no longer undoable - refresh
            // the Undo button now rather than leaving it clickable until the next booster use.
            sharedBoosterManager?.RefreshAllBoosterUI();
            // Also re-check the FTUE highlight now, not just right after the tap - a target
            // cell whose correct value was already placed only counts as "done" once it's
            // actually locked/green (see CatFlapFtueDriver.Refresh), which only just happened
            // above.
            ftueDriver?.Refresh();
            SaveAll();
            RemoveDelayedValidation(row, column, checkRow, checkColumn);

            // Also lets the rules walkthrough's last-cell free tap react once BOTH this cell's row
            // and column outcomes have resolved - not just whichever of the two independent
            // timers (see StartDelayedValidation) happened to finish first. A cell sits at the
            // intersection of one row and one column; if, say, its row is the one that actually
            // breaks a rule (slow wrongCheckSeconds wait) while its column resolves as an
            // unrelated fast Complete, calling this the instant the column finishes would react
            // before the row's own rule-break blink has even started - see
            // CatFlapFtueDriver.NotifyLineOutcomeResolved. This is only ever reached from a
            // Complete/Wrong-Solution outcome (see ValidateLineAfterDelay), never a rule break -
            // ruleBroken is always false here.
            if (!rowValidations.ContainsKey(row) && !columnValidations.ContainsKey(column))
            {
                ftueDriver?.NotifyLineOutcomeResolved(row, column, ruleBroken: false);
            }
        }

        // Note: Apply All no longer uses a delayed-validation coroutine like single-cell taps do.
        // It must read as one atomic action (one solution check, one set of red cells, one life
        // deduction pass) rather than the single-cell "find one rule violation and stop" path
        // that used to live here - that logic now lives in
        // CatFlapRuntimeController.ApplyAllNotes, which runs synchronously.

        // Starts (or restarts) this tap's row timer and column timer independently of each other -
        // exactly one still-pending timer per row index and one per column index at any time,
        // board-wide. Retapping a cell in row R only ever cancels/restarts rowValidations[R]; it
        // never touches columnValidations for any column, including the one just tapped before if
        // this is a different cell in the same row. Same the other way for a shared column. This
        // is what lets an unrelated line's in-flight feedback (e.g. a correct-placement scale
        // animation) survive a fast second tap elsewhere in this cell's row or column.
        //
        // Each dimension's timer only actually starts if controller.HasPendingRowOutcome/
        // HasPendingColumnOutcome reports something worth eventually confirming for it (Complete,
        // Fail/rule-break, or Warning/wrong-solution) right now, immediately, synchronously, the
        // instant this cell is placed - a line that's still safely mid-fill with none of those
        // three showing gets no timer at all, since nothing would change for it by waiting. Once
        // a timer does start, it still re-checks the same three outcomes again when its delay
        // elapses (see ValidateLineAfterDelay) before actually applying the effect - the delay
        // exists to let a fast follow-up edit to the SAME line settle first, not to decide
        // whether something is happening in the first place.
        private void StartDelayedValidation(int row, int column)
        {
            // This tap already just filled the board's last remaining empty cell (see
            // CatFlapRuntimeSession.IsBoardComplete) - checked once, synchronously, right now,
            // not re-evaluated when the timer(s) below actually fire.
            bool isLastCell = controller != null && controller.Session.IsBoardComplete;
            float correctCheckSeconds = isLastCell ? activeRulesConfig.LastCellCorrectValidationDelaySeconds : correctValidationDelaySeconds;

            if (rowValidations.TryGetValue(row, out Coroutine pendingRow) && pendingRow != null)
            {
                StopCoroutine(pendingRow);
            }

            rowValidations.Remove(row);
            bool startRowTimer = controller.HasPendingRowOutcome(row);
            if (startRowTimer)
            {
                rowValidations[row] = StartCoroutine(ValidateLineAfterDelay(row, column, checkRow: true, checkColumn: false, correctCheckSeconds));
            }

            if (columnValidations.TryGetValue(column, out Coroutine pendingColumn) && pendingColumn != null)
            {
                StopCoroutine(pendingColumn);
            }

            columnValidations.Remove(column);
            bool startColumnTimer = controller.HasPendingColumnOutcome(column);
            if (startColumnTimer)
            {
                columnValidations[column] = StartCoroutine(ValidateLineAfterDelay(row, column, checkRow: false, checkColumn: true, correctCheckSeconds));
            }
        }

        private void RemoveDelayedValidation(int row, int column, bool checkRow, bool checkColumn)
        {
            if (checkRow)
            {
                rowValidations.Remove(row);
            }

            if (checkColumn)
            {
                columnValidations.Remove(column);
            }
        }

        // Cancels every pending validation, for global resets (level load/restart/undo/clear/quit)
        // where nothing in-flight should be allowed to resolve.
        private void StopDelayedValidation()
        {
            foreach (Coroutine routine in rowValidations.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            rowValidations.Clear();

            foreach (Coroutine routine in columnValidations.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            columnValidations.Clear();
        }
    }
}
