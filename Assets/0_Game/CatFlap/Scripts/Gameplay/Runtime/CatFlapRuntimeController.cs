using System;
using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Levels;
using CatFlap.Models;
using CatFlap.Validation;
using CatFlap.Views;
using Shared.AppreciationText;
using Shared.Boosters;
using Shared.GameFlow;
using UnityEngine;

namespace CatFlap.Runtime
{
    public sealed partial class CatFlapRuntimeController : IDisposable
    {
        private readonly CatFlapLevelRepository levelRepository;
        private readonly CatFlapRuntimeSession session;
        private readonly ICatFlapBoardView boardView;
        private readonly ICatFlapStatusView statusView;
        private readonly IGameFlowView gameFlowView;
        private readonly ICatFlapAnimationView animationView;
        private readonly CatFlapAnimationSequencer animationSequencer;
        // What RenderCell actually last painted for each cell (allocated/reset whenever RenderLines'
        // full-board branch rebuilds the view - see RenderLines) - lets the scoped-repaint skip
        // below tell a genuinely-unchanged locked cell (safe to skip, preserves an in-flight flip)
        // apart from a locked cell whose Error/Warning/Completed status just changed (must repaint
        // even though it's locked, or a resolved mistake/rule-violation/wrong-sequence highlight
        // would keep showing forever once its cell is locked and no future pass ever revisits it).
        private CatFlapCellVisualState?[,] lastRenderedVisualState;
        private readonly List<CatFlapCellPosition> mistakeCells = new List<CatFlapCellPosition>();
        // Signature (sorted CellKeys joined) of the last violation actually charged per rule - lets
        // ResolveMistake dedupe two fast taps that resolve into the SAME still-broken violation,
        // without also suppressing a later, genuinely different violation of the same rule (e.g. a
        // 4th adjacent fish placed after a 3rd already charged a mistake). See
        // IsViolationAlreadyChargedAtCell.
        private readonly Dictionary<CatFlapRule, string> lastChargedViolationSignatureByRule = new Dictionary<CatFlapRule, string>();
        // session.Progress.Moves at the moment a mistake was last actually spent - caps a single
        // tap to costing AT MOST one life even when it breaks its row AND its column at once (two
        // DIFFERENT rule violations, so lastChargedViolationSignatureByRule's per-rule dedupe above
        // doesn't catch it on its own). A single tap's row-check and column-check run as two fully
        // independent delayed-validation coroutines (see CatFlapGameplayManager.
        // StartDelayedValidation) that both call ResolveMistake separately for the SAME move -
        // without this, a row violation and a column violation from that one move each spent their
        // own mistake. Moves only increments once per genuine tap, so a later, truly separate tap
        // always has a different value here and is never wrongly suppressed.
        private int lastMistakeChargedMoveCount = -1;
        private readonly List<CatFlapCellPosition> ruleErrorCells = new List<CatFlapCellPosition>();
        private readonly List<CatFlapCellPosition> wrongSequenceCells = new List<CatFlapCellPosition>();
        // Tracks which rows/columns were already flagged wrong-sequence as of the last
        // UpdateWrongSequenceCells call, so the Warning appreciation popup only fires once per
        // newly-wrong line rather than every re-validation while it stays wrong.
        private readonly HashSet<int> previousWrongRows = new HashSet<int>();
        private readonly HashSet<int> previousWrongColumns = new HashSet<int>();
        // Same idea as previousWrongRows/Columns, but for rule-violation (Error) lines - lets
        // LogRuleErrorLine (diagnostic-only) log a newly-broken line's input-vs-expected values
        // once, instead of on every re-render while it stays broken.
        private readonly HashSet<int> previousErrorRows = new HashSet<int>();
        private readonly HashSet<int> previousErrorColumns = new HashSet<int>();
        // Set once the Wrong Solution blink (see PlayWarningBlink) has run its fixed number of
        // cycles and stopped on its own - the HUD text stays visible until the player's NEXT tap
        // anywhere on the board, at which point ConsumeWrongSolutionTextDismissIfArmed hides it
        // regardless of whether that tap fixed the wrong line. See SyncWrongSolutionStatusText.
        private bool wrongSolutionDismissArmed;
        // Set once that dismiss has actually happened - keeps the text hidden on every later
        // re-validation while the SAME wrong line(s) remain unfixed, instead of
        // SyncWrongSolutionStatusText's normal count&gt;0 check bringing it right back. Cleared the
        // instant a genuinely NEW line goes wrong (fresh PlayWarningBlink) or the board has nothing
        // wrong left at all (see SyncWrongSolutionStatusText's count == 0 branch).
        private bool wrongSolutionTextSuppressed;
        private readonly List<CatFlapCellPosition> pendingCompletedVisualCells = new List<CatFlapCellPosition>();
        private CatFlapCellPosition? activeHintCell;
        private CatFlapCellRevealResult? activeHintResult;
        private bool disposed;
        // The game state PlayPageState last actually animated for - see PlayPageState.
        private CatFlapGameState lastPlayedPageState = CatFlapGameState.Uninitialized;

        public CatFlapRuntimeController(ICatFlapBoardView boardView, ICatFlapStatusView statusView = null)
            : this(new CatFlapLevelRepository(), new CatFlapRuntimeSession(), boardView, statusView, null, null)
        {
        }

        public CatFlapRuntimeController(
            CatFlapLevelRepository levelRepository,
            CatFlapRuntimeSession session,
            ICatFlapBoardView boardView,
            ICatFlapStatusView statusView = null,
            IGameFlowView gameFlowView = null,
            ICatFlapAnimationView animationView = null)
        {
            this.levelRepository = levelRepository;
            this.session = session;
            this.boardView = boardView;
            this.statusView = statusView;
            this.gameFlowView = gameFlowView;
            this.animationView = animationView;
            animationSequencer = new CatFlapAnimationSequencer();
            this.session.LevelWon += HandleLevelWon;
            this.session.LevelFailed += HandleLevelFailed;
            this.session.OutOfMistakes += HandleOutOfMistakes;
        }

        public CatFlapRuntimeSession Session => session;
        public bool IsSequencing => animationSequencer.IsBusy;

        // Gates the Level Attempt (hearts/mistake-limit) feature's visuals: while false, the
        // attempt-pips HUD stays hidden and a wrong placement never plays the heart break FX.
        // Set by CatFlapGameplayManager from GlobalConfig.LevelAttemptUnlockLevel vs the
        // current level number. The underlying mistake tracking/out-of-mistakes logic is
        // unaffected - only these two visuals are suppressed until unlocked.
        public bool LevelAttemptFeatureEnabled { get; set; } = true;

        // Gates ALL "wrong answer" rendering board-wide - both the red rule-error cell outline
        // (ruleErrorCells, via UpdateRuleErrorCells) and the HUD rule-chip red/X (via the
        // ShowRuleResult wrapper below) - regardless of which internal path triggered validation
        // (ValidateBoard, ApplyAllNotes, RefreshValidationVisuals, UseFix all funnel through
        // those two). Set by CatFlapGameplayManager for FTUE levels 1-3, where a rule can go
        // transiently "wrong" mid-way through a guided multi-tap cell (e.g. two Fish placeholders
        // sitting adjacent before either becomes its final Cat) without ever being a real player
        // mistake. A currently-VALID state still renders normally (green/checkmark) while
        // suppressed - only the red/invalid rendering is held back.
        public bool SuppressWrongIndication { get; set; }

        // Suppresses the Appreciation Text popup entirely (both the normal per-line
        // PlayAppreciationText and the win-line PlayVeryHardAppreciationText) - set by
        // CatFlapGameplayManager for every FTUE level (1..FtueEnabledUntilLevel, today 1-4), so
        // the guided tutorial never gets interrupted by an unrelated callout popup. First appears
        // starting the level right after the FTUE range ends (today, level 5).
        public bool SuppressAppreciationText { get; set; }

        /// <summary>Cell currently highlighted by an active, undismissed hint (highlight-only - never auto-placed).</summary>
        public CatFlapCellPosition? ActiveHintCell => activeHintCell;
        public event System.EventHandler LevelWon = delegate { };
        public event System.EventHandler LevelFailed = delegate { };
        public event System.EventHandler OutOfMistakes = delegate { };
        // Fires once per mistake that actually spends a life (see ResolveMistake's own
        // IsViolationAlreadyChargedAtCell dedupe) - lets CatFlapGameplayManager log the
        // lives_lost analytics event without this lower-level controller needing to know about
        // level numbers or analytics at all.
        public event System.EventHandler MistakeSpent = delegate { };
        // Fires once per row/column that newly becomes "wrong sequence" (filled, no rule
        // violation, but doesn't match the level's solution) - see
        // CatFlapRuntimeController.VisualState.LogWrongSequenceLine. Feeds the level_complete/
        // level_fail analytics event's wrong_solution_count.
        public event System.EventHandler WrongSolutionDetected = delegate { };
        // Fires once per rule violation newly charged against a life (see ResolveMistake's own
        // touchedViolations/IsViolationAlreadyChargedAtCell dedupe) - feeds the level_complete/
        // level_fail analytics event's rule_1_break_count/rule_2_break_count.
        public event System.Action<CatFlapRule> RuleViolationCharged = delegate { };

        public void ShowMainMenu()
        {
            CancelSequencing();
            session.ShowMainMenu();
            boardView?.Clear();
            gameFlowView?.ShowState(GameFlowState.MainMenu);
            PlayAnimation(complete => animationView?.PlayPageState(CatFlapGameState.MainMenu, complete));
        }

        public void ClearBoardView()
        {
            ClearErrorCells();
            ResetRuleView();
            DismissActiveHint();
            boardView?.Clear();
        }

        public CatFlapRuntimeLevelLoadResult LoadJson(string json, BoosterConfig economyConfig = null, int levelMistakeLimit = 5)
        {
            CatFlapRuntimeLevelLoadResult result = ParseLevelJson(json);
            if (!result.IsLoaded)
            {
                statusView?.ShowLoadErrors(result.Errors);
                return result;
            }

            LoadLevel(result.Level, economyConfig, levelMistakeLimit);
            return result;
        }

        public CatFlapRuntimeLevelLoadResult ParseLevelJson(string json)
        {
            return levelRepository.LoadFromJson(json);
        }

        public void LoadLevel(CatFlapLevelData level, BoosterConfig economyConfig = null, int levelMistakeLimit = 5)
        {
            CancelSequencing();
            ClearErrorCells();
            DismissActiveHint();
            session.Load(level, economyConfig, levelMistakeLimit);
            ResetRuleView();
            hasReachedHalfBoardProgress = false;
            easySingleLineStreak = 0;
            hasFiredHardTier = false;
            RenderAll(suppressValueChangeAnimation: true);
        }

        public void Clear()
        {
            if (!EnsureLevelLoadedForCommand())
            {
                return;
            }

            CancelSequencing();
            ClearErrorCells();
            DismissActiveHint();
            session.Clear();
            ResetRuleView();
            hasReachedHalfBoardProgress = false;
            easySingleLineStreak = 0;
            hasFiredHardTier = false;
            RenderAll(suppressValueChangeAnimation: true);
        }

        public void Restart()
        {
            if (!EnsureLevelLoadedForCommand())
            {
                return;
            }

            CancelSequencing();
            ClearErrorCells();
            DismissActiveHint();
            session.Restart();
            ResetRuleView();
            hasReachedHalfBoardProgress = false;
            easySingleLineStreak = 0;
            hasFiredHardTier = false;
            RenderAll(suppressValueChangeAnimation: true);
        }

        public bool SelectCell(int row, int column, bool notesMode)
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                return false;
            }

            if (session.TryCycleCell(row, column, notesMode))
            {
                ClearValidationFeedbackForCellEdit(row, column);
                RenderAll();
                CatFlapCellModel cell = session.Board.GetCell(row, column);
                PlayAnimation(complete => animationView?.PlayCellChanged(
                    new CatFlapCellPosition(row, column),
                    cell.Value,
                    cell.IsNote,
                    complete));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Directly commits the active hint's target value to its cell (no tap-cycling) - used by
        /// the "Apply" button while a hint is active. Clears the hint highlight either way.
        /// </summary>
        public bool ApplyHint(out CatFlapCellPosition position)
        {
            position = default;
            if (!session.HasLevel || animationSequencer.IsBusy || !activeHintCell.HasValue || !activeHintResult.HasValue)
            {
                return false;
            }

            CatFlapCellPosition targetPosition = activeHintCell.Value;
            CatFlapCellValue value = activeHintResult.Value.Value;
            position = targetPosition;
            DismissActiveHint();

            if (!session.TrySetCell(targetPosition.Row, targetPosition.Column, value, false))
            {
                return false;
            }

            ClearValidationFeedbackForCellEdit(targetPosition.Row, targetPosition.Column);
            RenderAll();
            CatFlapCellModel cell = session.Board.GetCell(targetPosition.Row, targetPosition.Column);
            PlayAnimation(complete => animationView?.PlayCellChanged(
                targetPosition,
                cell.Value,
                cell.IsNote,
                complete));
            return true;
        }

        public bool UndoCell(int row, int column, int moveCount)
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                return false;
            }

            if (!session.UndoCell(row, column, moveCount))
            {
                return false;
            }

            // Undo dismisses any active hint highlight (never refunded - just hidden).
            DismissActiveHint();
            RemoveErrorCell(row, column);
            PlayAnimation(complete => animationView?.PlayCellChanged(
                new CatFlapCellPosition(row, column),
                CatFlapCellValue.Empty,
                false,
                complete));
            return true;
        }

        public bool ApplyAllNotes()
        {
            return ApplyAllNotes(out _);
        }

        /// <summary>
        /// Commits every tentative cell (see <see cref="CatFlapRuntimeSession.ApplyAllNotes"/>) to
        /// a real placed value - mirrors exactly what a normal tap's own <see cref="SelectCell"/>
        /// does for its single cell, just batched over every committed cell at once (one shared
        /// render pass, not one per tile). Deliberately does NOT validate rules, spend lives, or
        /// play any completed-line reveal here - CatFlapGameplayManager.ApplyAllNotes instead
        /// starts the exact same per-row/per-column delayed-validation timers a real tap starts
        /// (see CatFlapGameplayManager.Validation.StartDelayedValidation) for every committed
        /// cell, so each one's eventual Complete/Rule Break/Wrong Solution outcome - and its life
        /// cost, sound, and reveal animation - resolves through precisely the same code path and
        /// timing a tap uses, rather than a separate batch-only rule.
        /// </summary>
        public bool ApplyAllNotes(out IReadOnlyList<CatFlapCellPosition> appliedPositions)
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                appliedPositions = Array.Empty<CatFlapCellPosition>();
                return false;
            }

            List<CatFlapCellPosition> positions = session.ApplyAllNotes();
            appliedPositions = positions;
            if (positions.Count <= 0)
            {
                return false;
            }

            for (int index = 0; index < positions.Count; index++)
            {
                ClearValidationFeedbackForCellEdit(positions[index].Row, positions[index].Column);
            }

            RenderAll();
            return true;
        }

        // Set by ValidateBoard() to the number of distinct rows+columns that call just newly
        // completed (0, 1, or 2 - a move can complete at most one row and one column at once).
        // Read via LastNewlyCompletedLineCount to tell a single-line complete from a double.
        private int lastNewlyCompletedLineCount;

        public int LastNewlyCompletedLineCount => lastNewlyCompletedLineCount;

        // Latched true the first time this level's locked (confirmed-correct) cell count reaches
        // half the board, so LineProgressHalf can play once per level instead of on every
        // subsequent correct line.
        private bool hasReachedHalfBoardProgress;

        // True only on the ValidateBoard() call where the board's locked-cell count just crossed
        // the halfway point - a one-shot flag for that call, not a running "is past half" state.
        public bool JustReachedHalfBoardProgress { get; private set; }

        // Appreciation Text tiers (see ResolveAppreciationTier) - Easy's "N continuous taps, each
        // completing exactly one line" streak. Extended by a move that completes exactly one line,
        // broken back to zero by any other move (zero lines, or a two-line corner completion).
        private int easySingleLineStreak;

        // Fraction of the board that must be locked before a completed line can trigger Hard tier.
        private const float HardTierFillThreshold = 0.7f;

        // Latches true the first time a move's completed line leaves the board at/above
        // HardTierFillThreshold, so Hard fires once per level rather than on every later
        // completion once the board is already that full. Reset on level load/restart/clear.
        private bool hasFiredHardTier;

        // Set by ValidateBoard() to whether THIS move is the one that just crossed
        // HardTierFillThreshold - read moments later by ResolveAppreciationTier for the same move,
        // same pattern as JustReachedHalfBoardProgress above.
        private bool justReachedHardTierFill;

        public bool ValidateBoard()
        {
            return ValidateBoard(-1, -1);
        }

        /// <summary>
        /// Same as the parameterless overload, but row/column identify the cell that triggered
        /// this validation (the just-tapped cell) - passed through to Appreciation Text so its
        /// popup anchors there instead of a fixed board-wide spot. Pass -1,-1 (or use the
        /// parameterless overload) when there's no single triggering cell, e.g. a manual/cheat
        /// re-validate. checkRow/checkColumn let a caller that owns only one dimension of this
        /// cell right now (an independent per-row or per-column timer - see
        /// CatFlapGameplayManager.Validation) skip rechecking the other, so it never disturbs
        /// violation state that a different, still-running timer for that other line still owns.
        /// </summary>
        public bool ValidateBoard(int triggerRow, int triggerColumn, bool checkRow = true, bool checkColumn = true)
        {
            if (!EnsureLevelLoadedForCommand() || animationSequencer.IsBusy)
            {
                JustReachedHalfBoardProgress = false;
                return false;
            }

            List<CatFlapCellPosition> lockedBefore = GetLockedCells();
            CatFlapRuleValidationResult ruleResult = session.ValidateCurrentBoard(checkRow ? triggerRow : -1, checkColumn ? triggerColumn : -1);
            LogRuleResult("ValidateBoard", triggerRow, triggerColumn, ruleResult);
            UpdateRuleErrorCells(ruleResult);
            UpdateWrongSequenceCells(ruleResult, checkRow ? triggerRow : -1, checkColumn ? triggerColumn : -1, playFeedback: true);
            ShowRuleResult(ruleResult, session.IsBoardComplete);
            List<CatFlapCellPosition> newlyLocked = GetNewlyLockedCells(lockedBefore);
            lastNewlyCompletedLineCount = CountNewlyCompletedLines(newlyLocked);

            // Only touch the Easy streak when THIS call actually newly locked something. A single
            // tap starts two fully independent delayed-validation coroutines - one for its row, one
            // for its column (see CatFlapGameplayManager.Validation.StartDelayedValidation) - and
            // each calls ValidateBoard once when its own delay elapses. Whichever dimension didn't
            // just complete a line reports 0 newly locked cells here, even though nothing actually
            // went wrong this move - the OTHER dimension's call already completed and consumed the
            // line. Previously that harmless "other half" call still reset easySingleLineStreak
            // back to 0 via UpdateEasyStreak(0), silently eating a streak the sibling call had just
            // built up. Genuine streak-breaking (a wrong/rule-broken placement) is instead reset
            // explicitly from ResolveMistake and the wrong-solution branch in
            // CatFlapGameplayManager.Validation - not inferred from a 0 count here.
            if (newlyLocked.Count > 0)
            {
                UpdateEasyStreak(lastNewlyCompletedLineCount);
            }

            justReachedHardTierFill = UpdateHardTierFill(lockedBefore.Count + newlyLocked.Count);
            JustReachedHalfBoardProgress = UpdateHalfBoardProgress(lockedBefore.Count + newlyLocked.Count);

            if (session.GameState.Current == CatFlapGameState.Won)
            {
                // The line that wins the level - Very Hard tier. PlayLockedCellsThenPageState's own
                // particle/appreciation-text beat is skipped for this move (the win popup owns that
                // beat instead), so this is fired directly here rather than through the usual
                // anchoring path.
                if (newlyLocked.Count > 0)
                {
                    PlayVeryHardAppreciationText(triggerRow, triggerColumn);
                }

                // The tile-flip reveal itself, however, must still play - CatFlapGameplayManager's
                // win-celebration coroutine waits for it via boardView.IsAnyEntryAnimationPlaying,
                // so LevelWon (deferred by HandleLevelWon above) is only raised once this has
                // actually been kicked off, not before.
                //
                // Won can be reached with ruleErrorCells/wrongSequenceCells/mistakeCells still
                // wrongly non-empty: checkRow/checkColumn let a single line's own independent timer
                // (see CatFlapGameplayManager.Validation) recheck only ITS dimension, so the
                // OTHER dimension's rule violation - already fixed by an earlier edit, but not
                // rechecked by THIS call - can still be sitting in the merged LastRuleResult
                // (CatFlapRuleValidationResult.ReplaceLines only overwrites the line(s) actually
                // rechecked this pass) when this pass is the one whose value-vs-solution check
                // happens to satisfy Won. UpdateRuleErrorCells above then re-populates
                // ruleErrorCells straight from that stale merged violation. But Won is only ever
                // reached when every cell matches the level's own (rule-valid) solution, so at this
                // exact point no real violation can still exist - any leftover error/warning
                // bookkeeping here is definitionally stale and safe to force-clear outright, rather
                // than trying to fix the underlying partial-recheck merge. The HUD rule chips
                // (statusView.ShowRuleResult) have the exact same staleness problem - only the
                // dimension(s) THIS pass actually rechecked get a fresh ShowRuleResult call, so a
                // rule broken by an earlier edit and fixed by a LATER one that only rechecked a
                // different line can leave a chip showing red/X straight through the win
                // celebration. Force every chip to a fresh, fully-valid result here too, for the
                // same reason ClearValidationErrorStateForWin force-clears the board-level state.
                ClearValidationErrorStateForWin();
                ShowRuleResult(new CatFlapRuleValidationResult(), boardComplete: true);

                // This Won branch never calls RenderLines - PlayWinningLineReveal below only ever
                // repaints cells inside the newly-completing line(s) themselves. A stale highlight
                // sitting on some OTHER cell (locked and unrelated to the row/column this exact tap
                // rechecked) would otherwise never get revisited at all and stay visibly red/orange
                // straight through the win celebration. One plain repaint pass over the whole board
                // first corrects every cell's true state; PlayWinningLineReveal's own flip afterward
                // still plays exactly as before for the cells that are actually newly completing.
                RepaintAllCellVisualStates(suppressValueChangeAnimation: true);
                PlayWinningLineReveal(newlyLocked);
                RaisePendingLevelWonIfNeeded();
                return newlyLocked.Count > 0;
            }

            SetPendingCompletedVisualCells(GetCompletedLineCells(newlyLocked));
            RenderLines(checkRow ? triggerRow : -1, checkColumn ? triggerColumn : -1, newlyLocked);
            PlayLockedCellsThenPageState(newlyLocked, triggerRow, triggerColumn);

            return newlyLocked.Count > 0;
        }

        private bool UpdateHalfBoardProgress(int lockedCellCount)
        {
            if (hasReachedHalfBoardProgress)
            {
                return false;
            }

            int totalCells = session.Board.RowCount * session.Board.ColumnCount;
            if (totalCells <= 0 || lockedCellCount * 2 < totalCells)
            {
                return false;
            }

            hasReachedHalfBoardProgress = true;
            return true;
        }

        // Extends Easy tier's streak on a move that completes exactly one line; a two-line corner
        // completion breaks it back to zero (only ever called with newlyCompletedLineCount > 0 -
        // see ValidateBoard). A genuinely failed/wrong placement breaks it via ResetEasyStreak
        // instead, called explicitly from ResolveMistake and the wrong-solution branch in
        // CatFlapGameplayManager.Validation.
        private void UpdateEasyStreak(int newlyCompletedLineCount)
        {
            easySingleLineStreak = newlyCompletedLineCount == 1 ? easySingleLineStreak + 1 : 0;
        }

        // Called when a placement is confirmed wrong (rule violation) or doesn't match the
        // solution (wrong-sequence) after the validation delay - a genuine break in the "each tap
        // completes one line" streak, as opposed to the harmless 0-count ValidateBoard call the
        // other dimension's own independent timer produces for the very same successful move (see
        // ValidateBoard's own comment on newlyLocked.Count > 0).
        public void ResetEasyStreak()
        {
            easySingleLineStreak = 0;
        }

        // Board length (2/4/6/8/10, same metric CatFlapSpriteBoardViewAdapter's sizeBounds table
        // and CatFlapCharacterIdleThrottle use) determines how many continuous single-line taps
        // Easy tier requires - 2 on anything smaller than 8x8, 3 on 8x8 and up.
        private int EasyStreakRequirement()
        {
            int boardLength = Mathf.Max(session.Board.RowCount, session.Board.ColumnCount);
            return boardLength >= 8 ? 3 : 2;
        }

        // One-shot per level - true only on the ValidateBoard() call where the board's locked-cell
        // count just crossed HardTierFillThreshold, same "compute now, read later" pattern as
        // UpdateHalfBoardProgress/JustReachedHalfBoardProgress above.
        private bool UpdateHardTierFill(int lockedCellCount)
        {
            if (hasFiredHardTier)
            {
                return false;
            }

            int totalCells = session.Board.RowCount * session.Board.ColumnCount;
            if (totalCells <= 0 || lockedCellCount < totalCells * HardTierFillThreshold)
            {
                return false;
            }

            hasFiredHardTier = true;
            return true;
        }

        // Counts distinct rows and distinct columns that newlyLocked just completed - mirrors the
        // completedRows/completedColumns dedupe in PlayCompletedLineParticles below, but as a
        // count rather than a particle trigger.
        private int CountNewlyCompletedLines(List<CatFlapCellPosition> newlyLocked)
        {
            if (newlyLocked == null || newlyLocked.Count == 0)
            {
                return 0;
            }

            List<int> completedRows = new List<int>();
            List<int> completedColumns = new List<int>();

            for (int index = 0; index < newlyLocked.Count; index++)
            {
                CatFlapCellPosition position = newlyLocked[index];

                if (IsValidatedCompletedRow(position.Row) && !completedRows.Contains(position.Row))
                {
                    completedRows.Add(position.Row);
                }

                if (IsValidatedCompletedColumn(position.Column) && !completedColumns.Contains(position.Column))
                {
                    completedColumns.Add(position.Column);
                }
            }

            return completedRows.Count + completedColumns.Count;
        }

        public bool HasRuleViolation()
        {
            return EnsureLevelLoadedForCommand() &&
                   !session.PreviewRuleValidation().IsValid;
        }

        /// <summary>
        /// True when the cell at (row, column) currently sits in a broken rule. checkRow/checkColumn
        /// let a caller that's only re-checking one dimension of this cell (see the independent
        /// per-row/per-column timers in CatFlapGameplayManager.Validation) skip the other dimension
        /// entirely, so it never reports a violation owned by a different, still-independent timer.
        /// </summary>
        public bool HasRuleViolationAtCell(int row, int column, bool checkRow = true, bool checkColumn = true)
        {
            if (!EnsureLevelLoadedForCommand())
            {
                return false;
            }

            CatFlapRuleValidationResult result = session.PreviewRuleValidation(checkRow ? row : -1, checkColumn ? column : -1);
            LogRuleResult("HasRuleViolationAtCell", row, column, result);
            return ContainsViolationCell(result, row, column);
        }

        /// <summary>
        /// True when row `row`, right now (synchronously, no delay), already has something worth
        /// eventually confirming: an active rule violation, a fresh not-yet-locked complete+correct
        /// state, or a wrong-solution (filled, rule-legal, but doesn't match the level's solution)
        /// state. Used as the immediate gate right when a cell is placed - see
        /// CatFlapGameplayManager.StartDelayedValidation - to decide whether this row's independent
        /// timer needs to start at all. A row that's still safely mid-fill (no violation yet, not
        /// full) reports false, since nothing will change for it once a delay elapses with no
        /// further edits - only a row already showing one of these three outcomes needs a delayed
        /// re-check to actually apply the effect (mistake charge / lock+celebrate / warning sound).
        /// </summary>
        public bool HasPendingRowOutcome(int row)
        {
            return EnsureLevelLoadedForCommand() && LineHasPendingOutcome(row, isRow: true);
        }

        /// <summary>Same as <see cref="HasPendingRowOutcome"/>, but for column `column`.</summary>
        public bool HasPendingColumnOutcome(int column)
        {
            return EnsureLevelLoadedForCommand() && LineHasPendingOutcome(column, isRow: false);
        }

        // Reads the wrong-sequence set computed by the most recent ValidateBoard() call - true
        // when (row, column)'s line is fully filled, has no rule violation, but doesn't match the
        // solution (the "yellow-orange" highlight case).
        public bool HasWrongSequenceAtCell(int row, int column)
        {
            return Contains(wrongSequenceCells, row, column);
        }

        // True when the row or column through (row, column) has no empty cells left - i.e. a
        // rule violation there is now locked in (line is fully placed and broken), rather than a
        // violation on a line that's still mid-fill and could still be fixed by more taps.
        public bool IsLineFullAtCell(int row, int column)
        {
            return IsFilledRow(row) || IsFilledColumn(column);
        }

        public bool TryGetRuleViolationCell(IReadOnlyList<CatFlapCellPosition> positions, out CatFlapCellPosition position)
        {
            position = default;
            if (!EnsureLevelLoadedForCommand() || positions == null || positions.Count == 0)
            {
                return false;
            }

            CatFlapRuleValidationResult result = session.PreviewRuleValidation();
            for (int index = 0; index < positions.Count; index++)
            {
                CatFlapCellPosition candidate = positions[index];
                if (ContainsViolationCell(result, candidate.Row, candidate.Column))
                {
                    position = candidate;
                    return true;
                }
            }

            return false;
        }

        public void RefreshViews(bool suppressValueChangeAnimation = false)
        {
            RenderAll(suppressValueChangeAnimation);
        }

        // HUD/status only (level label, mistakes/hearts, coins, ShowState gate) - deliberately
        // never touches board cells, unlike RefreshViews/RenderAll. Used by
        // CatFlapGameplayManager.HandleLevelWon right after a win: the winning line's own reveal
        // cascade (see PlayWinningLineReveal, kicked off moments earlier in ValidateBoard) is
        // still mid-flip at that point, and a full RenderAll there would immediately force-stop
        // those still-in-flight tile-flip coroutines (see RenderLines' own doc comment on this
        // exact collision) - right before the win-celebration coroutine ever got to see them
        // playing.
        public void RefreshStatusOnly()
        {
            RenderStatus();
        }

        public void RefreshValidationVisuals(bool suppressValueChangeAnimation = false)
        {
            if (!EnsureLevelLoadedForCommand())
            {
                return;
            }

            ClearErrorCells();
            CatFlapRuleValidationResult ruleResult = session.ValidateCurrentBoard();
            UpdateRuleErrorCells(ruleResult);
            UpdateWrongSequenceCells(ruleResult);
            ShowRuleResult(ruleResult, session.IsBoardComplete);
            RenderAll(suppressValueChangeAnimation);
            // Safety net: session.ValidateCurrentBoard can (rarely - a restored save that was
            // already solved) transition straight to Won here, which only sets
            // pendingLevelWonNotification (see HandleLevelWon) rather than firing LevelWon
            // immediately - a no-op whenever the flag isn't set.
            RaisePendingLevelWonIfNeeded();
        }

        public void Tick(float deltaSeconds)
        {
            if (disposed)
            {
                return;
            }

            session.Tick(deltaSeconds);
            RenderStatus();
        }

        /// <summary>
        /// Highlight-only hint request. Finds the best candidate via
        /// <see cref="CatFlapRuntimeSession.RevealHint"/> (which spends the booster charge on
        /// success but never mutates the board) and shows it as a highlight on the target cell -
        /// the player must still tap it themselves. While a hint is already active, re-requesting
        /// is a no-op: it returns the existing result as-is without re-spending, re-picking a
        /// candidate, or restarting the highlight animation.
        /// </summary>
        public CatFlapCellRevealResult RevealHint()
        {
            if (!session.HasLevel)
            {
                return CatFlapCellRevealResult.Failed("No Cat Flap level is loaded.");
            }

            if (animationSequencer.IsBusy)
            {
                return CatFlapCellRevealResult.Failed("A Cat Flap animation is still resolving.");
            }

            if (activeHintCell.HasValue && activeHintResult.HasValue)
            {
                return activeHintResult.Value;
            }

            CatFlapCellRevealResult result = session.RevealHint();
            statusView?.ShowBoosterResult(result);
            if (result.Success)
            {
                activeHintCell = new CatFlapCellPosition(result.Row, result.Column);
                activeHintResult = result;
                boardView?.ShowHintTarget(result.Row, result.Column, result.Value);
                PlayAnimation(complete => animationView?.PlayBooster(BoosterType.Hint, result, complete));
            }

            return result;
        }

        /// <summary>
        /// Clears the active hint highlight, if any. By default this never refunds the
        /// currency/charge that was already spent when the hint was found (tapping elsewhere, a
        /// future hint-card "X" button). Pass <paramref name="refundCharge"/> true only for the
        /// Undo-before-ever-tapping-the-hinted-cell case: the player never acted on the hint, so
        /// pressing Undo cancels it and gives the charge back, in addition to clearing the
        /// highlight. Called on cell tap, level load/reset, and Undo.
        /// </summary>
        public void DismissActiveHint(bool refundCharge = false)
        {
            if (!activeHintCell.HasValue)
            {
                return;
            }

            if (refundCharge)
            {
                session.RefundHintCharge();
            }

            boardView?.ClearHintTarget();
            activeHintCell = null;
            activeHintResult = null;
        }

        public CatFlapCellRevealResult UseFix()
        {
            if (!session.HasLevel)
            {
                return CatFlapCellRevealResult.Failed("No Cat Flap level is loaded.");
            }

            if (animationSequencer.IsBusy)
            {
                return CatFlapCellRevealResult.Failed("A Cat Flap animation is still resolving.");
            }

            List<CatFlapCellPosition> lockedBefore = GetLockedCells();
            CatFlapCellRevealResult result = session.FixFirstIncorrect();
            statusView?.ShowBoosterResult(result);
            if (result.Success)
            {
                RemoveErrorCell(result.Row, result.Column);
                ShowRuleResult(session.LastRuleResult, session.IsBoardComplete);
            }

            List<CatFlapCellPosition> newlyLocked = result.Success ? GetNewlyLockedCells(lockedBefore) : new List<CatFlapCellPosition>();
            SetPendingCompletedVisualCells(GetCompletedLineCells(newlyLocked));
            RenderAll();
            if (result.Success)
            {
                PlayAnimation(
                    complete => animationView?.PlayBooster(BoosterType.Fix, result, complete),
                    () => PlayLockedCellsThenPageState(newlyLocked, result.Row, result.Column));
            }

            return result;
        }

        /// <summary>
        /// checkRow/checkColumn let a caller that owns only one dimension of (row, column) right
        /// now (an independent per-row or per-column timer - see CatFlapGameplayManager.Validation)
        /// skip rechecking the other, so it never disturbs or double-charges a violation that a
        /// different, still-running timer for that other line still owns.
        /// </summary>
        public void ResolveMistake(int row, int column, bool checkRow = true, bool checkColumn = true)
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                return;
            }

            // Two fast taps that both land inside the same still-broken rule (e.g. two placements
            // that together push a row to 3 adjacent fish) each run their own independent
            // delayed-validation coroutine and would otherwise each call ResolveMistake - only the
            // first one to arrive should actually cost a life; see
            // IsViolationAlreadyChargedAtCell for why.
            bool spentMistake = false;
            if (!IsViolationAlreadyChargedAtCell(row, column, checkRow, checkColumn, out List<(CatFlapRule rule, string signature)> touchedViolations))
            {
                // Caps a single tap at costing at most one life - see lastMistakeChargedMoveCount's
                // own comment for why this is needed on top of the per-rule dedupe above (a row
                // violation and a column violation from the SAME move are different rules/
                // signatures, so that dedupe alone doesn't catch this case).
                int currentMoveCount = session.Progress.Moves;
                bool alreadyChargedThisMove = currentMoveCount == lastMistakeChargedMoveCount;
                spentMistake = !alreadyChargedThisMove && session.TrySpendLevelMistake();
                if (spentMistake)
                {
                    lastMistakeChargedMoveCount = currentMoveCount;
                }

                for (int index = 0; index < touchedViolations.Count; index++)
                {
                    lastChargedViolationSignatureByRule[touchedViolations[index].rule] = touchedViolations[index].signature;
                    if (spentMistake)
                    {
                        RuleViolationCharged(touchedViolations[index].rule);
                    }
                }
            }

            if (spentMistake)
            {
                MistakeSpent(this, System.EventArgs.Empty);
            }

            RenderStatus(animateHeartLoss: spentMistake && LevelAttemptFeatureEnabled);
            AddMistakeCell(row, column);
            var ruleErrorCellsBefore = new List<CatFlapCellPosition>(ruleErrorCells);
            CatFlapRuleValidationResult ruleResult = session.ValidateCurrentBoard(checkRow ? row : -1, checkColumn ? column : -1);
            LogRuleResult("ResolveMistake", row, column, ruleResult);
            UpdateRuleErrorCells(ruleResult);
            // Must also refresh wrongSequenceCells here, not just ruleErrorCells - otherwise a
            // line that goes straight from "wrong solution" (Warning) to an actual rule violation
            // in one edit (e.g. one more Fish tips a row from 2-fish-wrong-order to 3-fish-over-
            // count) leaves every cell NOT in the violation's own cell-key list (an EqualCounts
            // violation only lists the over-counted value's cells, never the other value's) still
            // reading its stale pre-violation Warning membership forever, since nothing else ever
            // clears wrongSequenceCells for this row/column once ValidateBoard stops being the
            // method that resolves it.
            UpdateWrongSequenceCells(ruleResult, checkRow ? row : -1, checkColumn ? column : -1);
            // No extraCells needed here (unlike ValidateBoard) - a rule violation means
            // LastRuleResult.IsValid is false, so LockCompletedCorrectLines' gate inside
            // FinalizeValidation never runs this pass and nothing new can lock anywhere.
            RenderLines(checkRow ? row : -1, checkColumn ? column : -1, null);
            boardView?.PlayMistakeBlink(row, column);
            boardView?.PlayRuleBreakBlink(GetNewlyBrokenRuleCells(ruleErrorCellsBefore));
            ShowRuleResult(ruleResult, session.IsBoardComplete);
            if (spentMistake)
            {
                if (LevelAttemptFeatureEnabled)
                {
                    boardView?.PlayHeartBreak(row, column);
                }

                statusView?.PlayRuleBreakBlink();
            }

            statusView?.ShowMistake(row, column);
            PlayAnimation(complete => animationView?.PlayMistake(new CatFlapCellPosition(row, column), complete), PlayPageState);
            // Safety net (see RefreshValidationVisuals) - a rule violation can never itself lock a
            // line/win the level, so this is always a no-op here in practice, but costs nothing to
            // guard against pendingLevelWonNotification getting stuck.
            RaisePendingLevelWonIfNeeded();
        }

        public void ForceWin()
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                return;
            }

            session.ForceWin();
            RenderAll();
            PlayPageState();
            // Cheat/debug entry point - no reveal cascade to kick off first, so raise the deferred
            // LevelWon notification (see HandleLevelWon) right away.
            RaisePendingLevelWonIfNeeded();
        }

        public void ForceFail()
        {
            if (!session.HasLevel || animationSequencer.IsBusy)
            {
                return;
            }

            session.ForceFail();
            RenderAll();
            PlayPageState();
        }

        public void CancelActiveSequence()
        {
            CancelSequencing();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CancelSequencing();
            session.LevelWon -= HandleLevelWon;
            session.LevelFailed -= HandleLevelFailed;
            session.OutOfMistakes -= HandleOutOfMistakes;
        }

        private void RenderAll(bool suppressValueChangeAnimation = false)
        {
            RenderLines(-1, -1, null, suppressValueChangeAnimation);
        }

        /// <summary>
        /// Renders just row `scopedRow` and/or column `scopedColumn` (each -1 to skip) plus any
        /// explicitly-listed extraCells, instead of repainting the whole board. Pass -1 for both
        /// with no extraCells (or use the parameterless RenderAll) for a genuine full-board
        /// repaint - level load/restart/clear, ApplyAllNotes, and RefreshValidationVisuals still
        /// need that, since they genuinely touch or re-derive state for the whole board.
        ///
        /// A single tap's own confirm-pass (ValidateBoard/ResolveMistake) only ever changes
        /// ruleErrorCells/wrongSequenceCells/lock state for its own checked line(s) - see
        /// CatFlapRuleValidationResult.ReplaceLines and the scoped UpdateRuleErrorCells/
        /// UpdateWrongSequenceCells overloads - so repainting every other cell was pure waste.
        /// Worse: per CatFlapSpriteCellViewAdapter.SetState's interruptedFlip handling, that
        /// waste could collide with and force-stop some UNRELATED cell's own still-in-flight
        /// tap-driven flip animation just because an entirely different row/column's timer
        /// happened to fire around the same time - the more taps in flight, the more likely.
        /// Scoping the render to only the cells that could have actually changed removes that
        /// collision at the source instead of just recovering gracefully from it.
        ///
        /// extraCells covers the one case scoping alone can miss: CatFlapRuntimeSession.
        /// LockCompletedCorrectLines is a whole-board sweep (locking ANY line that's newly
        /// complete+correct, not just the checked one) - pass its result (via
        /// GetNewlyLockedCells) so a rare unrelated line that happens to lock in the same pass
        /// still gets repainted too.
        /// </summary>
        private void RenderLines(int scopedRow, int scopedColumn, IReadOnlyList<CatFlapCellPosition> extraCells, bool suppressValueChangeAnimation = false)
        {
            if (!session.HasLevel)
            {
                return;
            }

            bool fullBoard = scopedRow < 0 && scopedColumn < 0 && (extraCells == null || extraCells.Count == 0);
            if (fullBoard)
            {
                boardView?.Build(session.Board.RowCount, session.Board.ColumnCount);
                lastRenderedVisualState = new CatFlapCellVisualState?[session.Board.RowCount, session.Board.ColumnCount];
                for (int row = 0; row < session.Board.RowCount; row++)
                {
                    for (int column = 0; column < session.Board.ColumnCount; column++)
                    {
                        RenderCell(row, column, suppressValueChangeAnimation);
                    }
                }
            }
            else
            {
                // skipIfAlreadyLocked: true - a locked cell's appearance only ever changes through
                // the dedicated reveal path (RevealCompletedCell's forcePlayFlip), never a passive
                // scoped repaint like this one. Without this, a corner tap that completes both a
                // row and a column raced two independent ValidateBoard calls (one per dimension,
                // both firing the same frame - see CatFlapGameplayManager.Validation) - the second
                // call's own scoped render of ITS checked line plainly repaints the shared corner
                // cell (already locked by the first call), landing on
                // CatFlapSpriteCellViewAdapter.SetState's "nothing changed" branch, which force-
                // stops and snaps whatever tileFlipAnimation the first call's reveal had just
                // started for that exact cell - the corner visibly never flips while every other
                // cell in the line does. RenderCell only actually honors this skip when the cell's
                // visual state truly hasn't changed since it was last painted (see
                // lastRenderedVisualState) - a locked cell whose Error/Warning status just changed
                // (newly joined a violated/wrong-sequence line, OR just got resolved by an edit
                // elsewhere in its row/column) always repaints regardless of lock.
                //
                // pendingCompletedVisualCells (not extraCells) is what actually protects a cell
                // that ANOTHER dimension's own independent call already locked+queued for reveal
                // moments earlier in the SAME frame: it's set once, in ValidateBoard, and only
                // removed cell-by-cell as each one's OWN RevealCompletedCell actually plays - so
                // it stays true across the second dimension's own separate ValidateBoard/
                // RenderLines call even though THAT call's own extraCells (its own newlyLocked) is
                // empty by then (nothing new for IT to lock, since the first call's board-wide
                // LockCompletedCorrectLines sweep already locked everything). Checking extraCells
                // alone missed exactly this: the shared/tapped cell, mid-flip from the FIRST
                // dimension's reveal, still got touched by the SECOND dimension's own scoped pass
                // (since it wasn't in THAT call's own, empty, extraCells) and had its in-flight
                // flip snapped short - while a cell in the same completed line whose OWN reveal
                // hadn't started yet (still waiting out its stagger delay) settled quietly early
                // and then played its real reveal flip cleanly, since nothing was interrupting it.
                if (scopedRow >= 0)
                {
                    for (int column = 0; column < session.Board.ColumnCount; column++)
                    {
                        if (Contains(pendingCompletedVisualCells, scopedRow, column))
                        {
                            continue;
                        }

                        RenderCell(scopedRow, column, suppressValueChangeAnimation, skipIfAlreadyLocked: true);
                    }
                }

                if (scopedColumn >= 0)
                {
                    for (int row = 0; row < session.Board.RowCount; row++)
                    {
                        if (row == scopedRow)
                        {
                            continue;
                        }

                        if (Contains(pendingCompletedVisualCells, row, scopedColumn))
                        {
                            continue;
                        }

                        RenderCell(row, scopedColumn, suppressValueChangeAnimation, skipIfAlreadyLocked: true);
                    }
                }

                if (extraCells != null)
                {
                    for (int index = 0; index < extraCells.Count; index++)
                    {
                        CatFlapCellPosition position = extraCells[index];
                        if (position.Row == scopedRow || position.Column == scopedColumn)
                        {
                            continue;
                        }

                        if (Contains(pendingCompletedVisualCells, position.Row, position.Column))
                        {
                            continue;
                        }

                        RenderCell(position.Row, position.Column, suppressValueChangeAnimation, skipIfAlreadyLocked: true);
                    }
                }
            }

            RenderStatus();
        }

        // skipIfAlreadyLocked is only ever passed true from RenderLines' scoped/incremental paths
        // above, never from its full-board path - a fresh level load/restart/resume still needs to
        // paint every locked clue/pre-solved cell normally the first time.
        private void RenderCell(int row, int column, bool suppressValueChangeAnimation, bool skipIfAlreadyLocked = false)
        {
            CatFlapCellModel cell = session.Board.GetCell(row, column);
            CatFlapCellVisualState visualState = ResolveVisualState(row, column);
            if (skipIfAlreadyLocked && cell.IsLocked &&
                lastRenderedVisualState != null &&
                lastRenderedVisualState[row, column] == visualState)
            {
                return;
            }

            boardView?.SetCell(row, column, cell.Value, cell.IsLocked, cell.IsNote, cell.IsLocked && ShouldUseLockedColor(row, column), visualState, cell.IsGiven, suppressValueChangeAnimation);

            if (lastRenderedVisualState != null)
            {
                lastRenderedVisualState[row, column] = visualState;
            }
        }

        // Plain, unscoped repaint of every cell - unlike RenderLines' full-board branch, never
        // calls boardView.Build (doesn't recreate cell views/interrupt anything in flight). Used
        // by ValidateBoard's Won branch (see above) to correct any stale highlight left behind on
        // a cell outside the winning line(s) before the win reveal/celebration takes over - there,
        // skipIfAlreadyLocked stays false (the default) since the whole board is confirmed valid
        // at that point, so a genuine, unconditional repaint is exactly what's wanted.
        // skipIfAlreadyLocked: true is passed instead by PlayLockedCellsThenPageState/
        // PlayWinningLineReveal's own completion callbacks - those fire once THIS dimension's own
        // reveal cascade finishes, but a shared cell (e.g. a corner completing both a row and a
        // column from the same tap) can still be mid-reveal via the OTHER dimension's own
        // independent timer/cascade at that exact moment; repainting it unconditionally here would
        // land on CatFlapSpriteCellViewAdapter.SetState's "nothing changed" branch and force-stop/
        // snap that still-in-flight flip short, exactly like RenderLines' own scoped branch already
        // guards against for the analogous per-tap case (see its comment).
        private void RepaintAllCellVisualStates(bool suppressValueChangeAnimation, bool skipIfAlreadyLocked = false)
        {
            if (!session.HasLevel)
            {
                return;
            }

            for (int row = 0; row < session.Board.RowCount; row++)
            {
                for (int column = 0; column < session.Board.ColumnCount; column++)
                {
                    RenderCell(row, column, suppressValueChangeAnimation, skipIfAlreadyLocked);
                }
            }
        }

        // animateHeartLoss must stay false for every load/restart/continue/periodic-refresh render
        // path (RenderAll, Tick) - the heart-break FX should only play for a mistake the player
        // just made during live gameplay, not when the HUD is first painted after entering or
        // resuming a level with hearts already spent. Only ResolveMistake passes true.
        private void RenderStatus(bool animateHeartLoss = false)
        {
            if (!session.HasLevel)
            {
                gameFlowView?.ShowState(ToGameFlowState(session.GameState.Current));
                return;
            }

            // SuppressAppreciationText is set by CatFlapGameplayManager to exactly
            // IsFtueLevel(CurrentLevelNumber) right before LoadLevel - reused here rather than a
            // second dedicated field, since it already means precisely "is this an FTUE level".
            statusView?.SetLevel(session.Level.Id, session.Level.Name, session.Level.Difficulty, isFtueLevel: SuppressAppreciationText);
            statusView?.SetLevelMistakes(session.LevelMistakeLimit.RemainingMistakes, session.LevelMistakeLimit.MaxMistakes, animateHeartLoss, LevelAttemptFeatureEnabled);
            statusView?.SetState(session.GameState.Current);
            statusView?.SetProgress(session.Progress);
            statusView?.SetBoosterEconomy(
                session.BoosterWallet.CurrentCoins,
                session.BoosterWallet.GetCount(BoosterType.Undo),
                session.BoosterWallet.Config.GetCost(BoosterType.Undo),
                session.BoosterWallet.GetCount(BoosterType.Hint),
                session.BoosterWallet.Config.GetCost(BoosterType.Hint),
                session.BoosterWallet.GetCount(BoosterType.Fix),
                session.BoosterWallet.Config.GetCost(BoosterType.Fix));
            gameFlowView?.ShowState(ToGameFlowState(session.GameState.Current));
        }

        private bool EnsureLevelLoadedForCommand()
        {
            if (session.HasLevel)
            {
                return true;
            }

            statusView?.ShowLoadErrors(new[] { "No Cat Flap level is loaded." });
            return false;
        }

        // animationView.PlayPageState is purely a fixed pageStateDelaySeconds pause (see
        // CatFlapAnimationViewAdapter) that blocks input for its duration via animationSequencer -
        // it renders nothing itself. Every board-changing action (mistake, hint, fix, row/column
        // completion, ApplyAllNotes...) used to chain it unconditionally, so completing a
        // row/column while the game state stays Playing paid this same input-blocking pause a
        // second time right on top of the reveal/appreciation-text beat, on every single move.
        // Only genuinely worth paying when the state actually just changed (e.g. into Won/Failed,
        // where a real page transition follows) - skip it otherwise.
        private void PlayPageState()
        {
            CatFlapGameState currentState = session.GameState.Current;
            if (currentState == lastPlayedPageState)
            {
                return;
            }

            lastPlayedPageState = currentState;
            PlayAnimation(complete => animationView?.PlayPageState(currentState, complete));
        }

        /// <summary>
        /// Seconds to pause between each cell's flip when a completed row/column reveals - mirrors
        /// the tap-driven flip timing rather than repainting every cell at once. Settable so
        /// CatFlapGameplayManager can tune it like the other animation delays on
        /// CatFlapAnimationViewAdapter.
        /// </summary>
        public float CompletedLineFlipStaggerSeconds { get; set; } = 0.08f;

        /// <summary>
        /// How much faster each successive reveal step in a completed-line flip plays than the
        /// last - step 0 plays at 1x, step 1 at (1 + this) x, step 2 at (1 + 2*this) x, and so on,
        /// capped at CompletedLineFlipMaxSpeedMultiplier. Settable so CatFlapGameplayManager can
        /// tune it like CompletedLineFlipStaggerSeconds. Never applied to a normal tap flip - only
        /// PlayLockedCellsThenPageState's staggered reveal passes a multiplier above 1x.
        /// </summary>
        public float CompletedLineFlipSpeedIncreasePerStep { get; set; } = 0.1f;

        /// <summary>Upper bound on the per-step speed-up above, so a long line doesn't ramp into an
        /// absurdly fast flip by its last few cells.</summary>
        public float CompletedLineFlipMaxSpeedMultiplier { get; set; } = 3f;

        // Deliberately does NOT go through PlayAnimation/animationSequencer - a completed-line
        // reveal only ever touches cells that are already locked (session.ValidateCurrentBoard
        // already ran and locked them before this is called), so no OTHER cell needs to be
        // blocked while it plays. Routing it through the shared sequencer used to make every
        // still-open cell on the board reject taps for the whole reveal+particle+appreciation-text
        // duration, which is unrelated to those cells. It also means two separate moves that each
        // complete a different line can have their reveals genuinely run in parallel instead of
        // the second cancelling the first (CatFlapSpriteBoardViewAdapter.PlayStaggeredReveal tracks
        // every concurrently-active reveal rather than a single routine). CancelSequencing
        // (Restart/Clear/Home) stops every underlying coroutine directly via
        // boardView.CancelStaggeredReveal instead, so a completion callback below never fires late
        // against a since-reset board.
        private void PlayLockedCellsThenPageState(List<CatFlapCellPosition> newlyLocked, int triggerRow = -1, int triggerColumn = -1)
        {
            if (newlyLocked.Count > 0 && boardView != null)
            {
                // A row and a column completed by the same move (a corner tap) are interleaved
                // cell-for-cell into groups that each reveal in the same instant - see
                // GetCompletedLineRevealGroups - so both lines visibly flip together instead of the
                // whole row finishing before the column starts. A plain single-line completion just
                // produces one cell per group, same as before.
                List<List<CatFlapCellPosition>> revealGroups = GetCompletedLineRevealGroups(newlyLocked);

                // Fired now, before the reveal starts, not from the reveal's completion callback -
                // session.ValidateCurrentBoard already locked these cells earlier in ValidateBoard,
                // so the completed-row/column data this reads is already final. The popup should
                // read as "you just did that", so it needs to appear the instant the tile flip
                // begins rather than waiting out the whole staggered flip first.
                PlayCompletedLineParticles(newlyLocked, triggerRow, triggerColumn);

                boardView.PlayStaggeredReveal(
                    revealGroups,
                    CompletedLineFlipStaggerSeconds,
                    CompletedLineFlipSpeedIncreasePerStep,
                    CompletedLineFlipMaxSpeedMultiplier,
                    (position, speedMultiplier) => RevealCompletedCell(position.Row, position.Column, speedMultiplier),
                    () =>
                    {
                        // NOT RenderAll() - see RepaintAllCellVisualStates' own comment on
                        // skipIfAlreadyLocked: a corner tap completing both a row and a column
                        // runs this same callback independently for each dimension, and an
                        // unconditional full-board RenderAll() here (which also calls
                        // boardView.Build()) could snap the OTHER dimension's own still-in-flight
                        // reveal for their shared cell short instead of letting it finish.
                        RepaintAllCellVisualStates(suppressValueChangeAnimation: true, skipIfAlreadyLocked: true);
                        RenderStatus();
                        PlayPageState();
                    });
                return;
            }

            // Either nothing newly completed this call, or there's no boardView to drive
            // RevealCompletedCell (which normally removes each of THIS batch's own cells from
            // pendingCompletedVisualCells one by one as its reveal plays) - remove this batch's own
            // cells here so they don't get stuck "pending" forever. Never a blanket Clear(): a
            // completely different batch's cells can genuinely still be mid-reveal in this same
            // list right now (see SetPendingCompletedVisualCells), and must be left alone.
            for (int index = 0; index < newlyLocked.Count; index++)
            {
                RemoveCell(pendingCompletedVisualCells, newlyLocked[index].Row, newlyLocked[index].Column);
            }

            PlayPageState();
        }

        // Winning-move counterpart to PlayLockedCellsThenPageState: plays the same tile-flip
        // reveal cascade for the line that just won the level, but WITHOUT
        // PlayCompletedLineParticles' normal appreciation-text beat - PlayVeryHardAppreciationText
        // (called just before this, in ValidateBoard's Won branch) already covers that popup for a
        // win, and firing both would show two conflicting callouts on the same move. PlayPageState
        // still runs once the reveal completes (or immediately, if nothing needs revealing), same
        // timing PlayLockedCellsThenPageState uses.
        private void PlayWinningLineReveal(List<CatFlapCellPosition> newlyLocked)
        {
            if (newlyLocked.Count == 0 || boardView == null)
            {
                PlayPageState();
                return;
            }

            SetPendingCompletedVisualCells(GetCompletedLineCells(newlyLocked));
            List<List<CatFlapCellPosition>> revealGroups = GetCompletedLineRevealGroups(newlyLocked);
            boardView.PlayStaggeredReveal(
                revealGroups,
                CompletedLineFlipStaggerSeconds,
                CompletedLineFlipSpeedIncreasePerStep,
                CompletedLineFlipMaxSpeedMultiplier,
                (position, speedMultiplier) => RevealCompletedCell(position.Row, position.Column, speedMultiplier, suppressPostFlipPop: true),
                () =>
                {
                    // Same "don't stomp a still-in-flight sibling reveal" reasoning as
                    // PlayLockedCellsThenPageState's own completion callback - see there.
                    RepaintAllCellVisualStates(suppressValueChangeAnimation: true, skipIfAlreadyLocked: true);
                    RenderStatus();
                    PlayPageState();
                });
        }

        // Reveals a single cell of a just-completed row/column: drops it out of
        // pendingCompletedVisualCells (so ShouldUseLockedColor now reports it locked/green) and
        // repaints just that cell with forcePlayFlip so it plays the same tile-flip as a tap.
        // suppressPostFlipPop is only ever true from PlayWinningLineReveal - the winning move's
        // flip goes straight into the board-wide win celebration instead of playing the
        // completed-line character pop first.
        private void RevealCompletedCell(int row, int column, float speedMultiplier = 1f, bool suppressPostFlipPop = false)
        {
            RemoveCell(pendingCompletedVisualCells, row, column);
            CatFlapCellModel cell = session.Board.GetCell(row, column);
            boardView?.SetCell(
                row,
                column,
                cell.Value,
                cell.IsLocked,
                cell.IsNote,
                cell.IsLocked && ShouldUseLockedColor(row, column),
                ResolveVisualState(row, column),
                cell.IsGiven,
                suppressValueChangeAnimation: false,
                forcePlayFlip: true,
                speedMultiplier: speedMultiplier,
                suppressPostFlipPop: suppressPostFlipPop);
        }

        /// <summary>
        /// Fires the Appreciation Text popup once for this move, even when both a row and a
        /// column complete together - the player only ever sees a single popup per move. The
        /// completed-line sparkle/particle itself no longer plays from here - each cell now plays
        /// its own completed particle the moment its own reveal flip finishes (see
        /// CatFlapSpriteCellViewAdapter.PlayCompletedParticle via forcePlayFlip/
        /// playCompletedParticleOnFinish), rather than every cell in the line firing at once,
        /// after the fact, once the whole staggered reveal has already finished.
        /// </summary>
        private void PlayCompletedLineParticles(List<CatFlapCellPosition> newlyLocked, int triggerRow = -1, int triggerColumn = -1)
        {
            if (newlyLocked == null || boardView == null)
            {
                return;
            }

            List<int> completedRows = new List<int>();
            List<int> completedColumns = new List<int>();

            for (int index = 0; index < newlyLocked.Count; index++)
            {
                CatFlapCellPosition position = newlyLocked[index];

                if (IsValidatedCompletedRow(position.Row) && !completedRows.Contains(position.Row))
                {
                    completedRows.Add(position.Row);
                }

                if (IsValidatedCompletedColumn(position.Column) && !completedColumns.Contains(position.Column))
                {
                    completedColumns.Add(position.Column);
                }
            }

            if (completedRows.Count == 0 && completedColumns.Count == 0)
            {
                return;
            }

            // Prefer anchoring on whichever line the tapped cell itself completed, so a
            // single-line completion still reads exactly as before; otherwise just fall back to
            // whichever line completed first.
            if (completedRows.Contains(triggerRow))
            {
                PlayAppreciationTextForRow(triggerRow);
            }
            else if (completedColumns.Contains(triggerColumn))
            {
                PlayAppreciationTextForColumn(triggerColumn);
            }
            else if (completedRows.Count > 0)
            {
                PlayAppreciationTextForRow(completedRows[0]);
            }
            else
            {
                PlayAppreciationTextForColumn(completedColumns[0]);
            }
        }

        // Anchors on the middle cell of the just-completed row (ColumnCount/2, rounding down for
        // an even count) rather than wherever the player happened to tap, so the popup always
        // reads as belonging to that row instead of jumping around with the trigger cell.
        private void PlayAppreciationTextForRow(int row)
        {
            PlayAppreciationText(row, session.Board.ColumnCount / 2);
        }

        // Same as PlayAppreciationTextForRow, anchored on the completed column's middle cell
        // (RowCount/2) instead.
        private void PlayAppreciationTextForColumn(int column)
        {
            PlayAppreciationText(session.Board.RowCount / 2, column);
        }

        // Fires the shared Appreciation Text popup (Assets/Shared/Features/AppreciationText) once
        // per completed row/column, anchored at the same on-screen spot as the "wrong solution"
        // warning text (see ICatFlapStatusView.TryGetWrongSolutionScreenPosition) rather than the
        // completed line's own cells, so it always reads from one consistent, familiar spot
        // instead of jumping around the board. Falls back to the line's middle cell, then to
        // AppreciationTextManager's own default spot, only if that HUD position can't be
        // resolved. Entirely optional - AppreciationTextManager.Play() is a no-op when no manager
        // instance exists in the scene or the feature is disabled on it. Suppressed on the move
        // that wins the level - that gets the win popup and Very Hard tier instead, fired
        // separately from PlayVeryHardAppreciationText. Also suppressed entirely (no popup at all)
        // when ResolveAppreciationTier finds none of the four tier conditions met for this move -
        // not every line completion is meant to be a callout anymore, only a notable one.
        private void PlayAppreciationText(int row, int column)
        {
            if (SuppressAppreciationText)
            {
                return;
            }

            if (session.GameState.Current == CatFlapGameState.Won)
            {
                return;
            }

            AppreciationTextTier? tier = ResolveAppreciationTier();
            if (tier == null)
            {
                return;
            }

            PlayAppreciationTextAtAnchor(tier.Value, row, column);
        }

        // The line that wins the level - Very Hard tier. Fired directly from ValidateBoard's own
        // Won branch, since the normal PlayLockedCellsThenPageState/PlayCompletedLineParticles
        // pipeline (which would otherwise reach PlayAppreciationText above) is intentionally
        // skipped once the level is won - the win popup owns that beat instead, but the player
        // still gets this one callout for the line that actually finished the board.
        private void PlayVeryHardAppreciationText(int triggerRow, int triggerColumn)
        {
            if (SuppressAppreciationText)
            {
                return;
            }

            int row = triggerRow >= 0 ? triggerRow : session.Board.RowCount / 2;
            int column = triggerColumn >= 0 ? triggerColumn : session.Board.ColumnCount / 2;
            PlayAppreciationTextAtAnchor(AppreciationTextTier.VeryHard, row, column);
        }

        // 2x2 boards are trivial (a single row/column each) so every callout tier would fire on
        // essentially every move - suppressed entirely for that board size rather than letting the
        // tier thresholds try to filter it, since both PlayAppreciationText's normal path and
        // PlayVeryHardAppreciationText's win-line path converge here.
        private bool IsBoardTooSmallForAppreciationText()
        {
            return session.Board.RowCount <= 2 && session.Board.ColumnCount <= 2;
        }

        private void PlayAppreciationTextAtAnchor(AppreciationTextTier tier, int row, int column)
        {
            if (IsBoardTooSmallForAppreciationText())
            {
                return;
            }

            if (AppreciationTextManager.Instance == null)
            {
                return;
            }

            if (TryGetWrongSolutionTextWorldPosition(out Vector3 anchorPosition))
            {
                AppreciationTextManager.Instance.Play(tier, anchorPosition);
                return;
            }

            if (boardView != null && boardView.TryGetCellWorldPosition(row, column, out Vector3 worldPosition))
            {
                AppreciationTextManager.Instance.Play(tier, worldPosition);
                return;
            }

            AppreciationTextManager.Instance.Play(tier);
        }

        // Converts the wrong-solution warning text's screen-pixel position into a world position
        // in the board's own camera space, at the same depth as the board itself, so it can be
        // handed to AppreciationTextManager.Play(tier, worldPosition) - which expects a
        // board-world-space position, not a UI screen position - and land it visually right on
        // top of that HUD spot.
        private bool TryGetWrongSolutionTextWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            Camera camera = Camera.main;
            if (statusView == null || camera == null ||
                !statusView.TryGetWrongSolutionScreenPosition(out Vector2 screenPosition) ||
                boardView == null || !boardView.TryGetCellWorldPosition(0, 0, out Vector3 referenceWorldPosition))
            {
                return false;
            }

            float depth = camera.WorldToScreenPoint(referenceWorldPosition).z;
            worldPosition = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
            return true;
        }


        // Per-move, not per-level: unlike the old static session.Level.Difficulty lookup, each of
        // the four tiers now corresponds to a specific noteworthy situation for THIS move, checked
        // in priority order (VeryHard is handled separately - see PlayVeryHardAppreciationText,
        // never reached through here since ValidateBoard returns before this runs on a win).
        // Returns null when none of the three apply, meaning an ordinary line completion shows no
        // popup at all rather than falling back to a generic tier.
        //   Priority 0 - Medium: this move's tap completed both its row AND column at once
        //     (lastNewlyCompletedLineCount >= 2, a "corner" completion) - always wins regardless of
        //     streak/fill state.
        //   Hard: this move is the one that first left the board at/above HardTierFillThreshold
        //     (70%) - once per level (see UpdateHardTierFill).
        //   Easy: this move extended the "continuous taps, each completing exactly one line"
        //     streak to EasyStreakRequirement() (2 on boards smaller than 8x8, 3 on 8x8+) - the
        //     streak resets back to zero once consumed here, so it takes a fresh streak to fire
        //     again.
        private AppreciationTextTier? ResolveAppreciationTier()
        {
            if (lastNewlyCompletedLineCount >= 2)
            {
                return AppreciationTextTier.Medium;
            }

            if (justReachedHardTierFill)
            {
                return AppreciationTextTier.Hard;
            }

            if (easySingleLineStreak >= EasyStreakRequirement())
            {
                easySingleLineStreak = 0;
                return AppreciationTextTier.Easy;
            }

            return null;
        }

        private void PlayAnimation(Action<System.Action> playAnimation, System.Action onComplete = null)
        {
            if (animationView == null)
            {
                onComplete?.Invoke();
                return;
            }

            animationSequencer.Play(playAnimation, onComplete);
        }

        private void CancelSequencing()
        {
            animationSequencer.Cancel();
            animationView?.CancelAll();
            // The completed-line staggered reveal (see PlayLockedCellsThenPageState) runs outside
            // animationSequencer so unrelated cells stay tappable while it plays - it must be
            // stopped explicitly here, or a Restart/Clear/Home mid-reveal would let its completion
            // callback fire later against a board that's since been reset/reloaded (stale
            // newlyLocked/trigger-cell closure).
            boardView?.CancelStaggeredReveal();
            pendingCompletedVisualCells.Clear();
        }

        // Set here instead of immediately re-firing LevelWon - session.ValidateCurrentBoard raises
        // its own LevelWon synchronously, still nested inside ValidateBoard, before ValidateBoard
        // has even computed newlyLocked/reached its Won branch (see ValidateBoard). Re-firing
        // LevelWon immediately here let CatFlapGameplayManager's win-celebration coroutine (which
        // waits on boardView.IsAnyEntryAnimationPlaying before playing the Happy mood/board pulse)
        // run its very first check before the winning line's own tile-flip reveal had even started
        // - finding nothing playing yet, it skipped straight to the celebration every time.
        // ValidateBoard's Won branch now kicks off that reveal first, then calls
        // RaisePendingLevelWonIfNeeded once it has actually started, so the celebration's own
        // IsAnyEntryAnimationPlaying check finally sees it in progress.
        private bool pendingLevelWonNotification;

        private void HandleLevelWon(object sender, EventArgs eventArgs)
        {
            pendingLevelWonNotification = true;
        }

        private void RaisePendingLevelWonIfNeeded()
        {
            if (!pendingLevelWonNotification)
            {
                return;
            }

            pendingLevelWonNotification = false;
            LevelWon?.Invoke(this, EventArgs.Empty);
        }

        private void HandleLevelFailed(object sender, EventArgs eventArgs)
        {
            LevelFailed?.Invoke(this, EventArgs.Empty);
        }

        private void HandleOutOfMistakes(object sender, EventArgs eventArgs)
        {
            OutOfMistakes?.Invoke(this, EventArgs.Empty);
        }
    }
}
