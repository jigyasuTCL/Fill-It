using System;
using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Levels;
using CatFlap.Models;
using CatFlap.Progress;
using CatFlap.Validation;
using Shared.Boosters;
using Shared.LevelMistakeLimit;

namespace CatFlap.Runtime
{
    public sealed class CatFlapRuntimeSession
    {
        private readonly CatFlapRuleValidator ruleValidator;
        private readonly CatFlapProgressTracker progressTracker;
        private readonly CatFlapHintService hintService;
        private readonly CatFlapFixService fixService;
        private CatFlapBoardSnapshot initialSnapshot;
        private BoosterConfig currentEconomyConfig;
        private bool completionEventRaised;

        // Explicit selection-order history of currently-tentative (note) cells: newest edit is
        // always last. Apply All commits in this order so the undo stack (which pops
        // last-pushed-first) naturally reverses in last-selected-first order. Never derive this
        // order from board scan/dictionary/hierarchy order - only from actual edit sequence.
        private readonly List<CatFlapCellPosition> tentativeEditOrder = new List<CatFlapCellPosition>();

        public CatFlapRuntimeSession()
            : this(new CatFlapRuleValidator(), new CatFlapProgressTracker(), new CatFlapHintService(), new CatFlapFixService())
        {
        }

        public CatFlapRuntimeSession(
            CatFlapRuleValidator ruleValidator,
            CatFlapProgressTracker progressTracker,
            CatFlapHintService hintService,
            CatFlapFixService fixService)
        {
            this.ruleValidator = ruleValidator;
            this.progressTracker = progressTracker;
            this.hintService = hintService;
            this.fixService = fixService;
            GameState = new CatFlapGameStateModel();
        }

        public CatFlapLevelData Level { get; private set; }
        public CatFlapBoardModel Board { get; private set; }
        public LevelMistakeLimitModel LevelMistakeLimit { get; private set; }
        public BoosterWallet BoosterWallet { get; private set; }
        public CatFlapGameStateModel GameState { get; }
        public CatFlapRuleValidationResult LastRuleResult { get; private set; } = new CatFlapRuleValidationResult();

        /// <summary>Difficulty removed (before minus after) by the most recent successful hint. See <see cref="CatFlapBoardDifficultyScorer"/>.</summary>
        public int LastHintDifficultyRemoved { get; private set; }

        /// <summary>Star rating (1-3) for the most recent successful hint, derived from <see cref="LastHintDifficultyRemoved"/>.</summary>
        public int LastHintStars { get; private set; }
        public CatFlapProgressSnapshot Progress => progressTracker.CreateSnapshot(Board);
        public bool HasLevel => Level != null && Board != null;
        public bool IsBoardComplete => HasLevel && IsBoardFilled();

        /// <summary>
        /// Last cell the player tapped/cycled (note or committed). Used only as the final
        /// tie-break ("nearest to last player tap") in <see cref="CatFlapSolutionHintProvider"/>;
        /// null until the player has made at least one move on this level.
        /// </summary>
        public CatFlapCellPosition? LastInteractedCell { get; private set; }

        /// <summary>Currently-tentative (note) cells in last-edited order, oldest first. See <see cref="tentativeEditOrder"/>.</summary>
        public IReadOnlyList<CatFlapCellPosition> TentativeEditOrder => tentativeEditOrder;

        /// <summary>Apply All button availability per spec: unavailable below 2 tentative cells.</summary>
        public int TentativeNoteCount => tentativeEditOrder.Count;

        public event EventHandler BoardChanged = delegate { };
        public event EventHandler StateChanged = delegate { };
        public event EventHandler LevelWon = delegate { };
        public event EventHandler LevelFailed = delegate { };
        public event EventHandler OutOfMistakes = delegate { };
        public event EventHandler<CatFlapRuleValidationResult> RulesValidated = delegate { };

        public void ShowMainMenu()
        {
            if (GameState.Current == CatFlapGameState.MainMenu)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (GameState.Current == CatFlapGameState.Uninitialized)
            {
                GameState.TransitionTo(CatFlapGameState.MainMenu);
            }
            else
            {
                GameState.TryTransitionTo(CatFlapGameState.MainMenu);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Load(
            CatFlapLevelData level,
            BoosterConfig economyConfig = null,
            int levelMistakeLimit = 5)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            if (GameState.Current == CatFlapGameState.Uninitialized)
            {
                GameState.TransitionTo(CatFlapGameState.Initializing);
            }
            else
            {
                GameState.TryTransitionTo(CatFlapGameState.Initializing);
            }

            Level = level;
            Board = CatFlapBoardModel.FromLevel(level);
            // Any row/column that's already fully correct from given clues alone must be locked
            // now, before the player's first move - otherwise it stays unlocked until whatever
            // move happens to trigger the first ValidateBoard() call, at which point it gets
            // reported as "newly locked" alongside (or instead of) whatever the player actually
            // just completed, firing LineComplete/LineCompleteDouble for a line the player never
            // touched.
            LockCompletedCorrectLines();
            LevelMistakeLimit = new LevelMistakeLimitModel(levelMistakeLimit);
            currentEconomyConfig = (economyConfig ?? currentEconomyConfig ?? new BoosterConfig()).Clone();
            BoosterWallet = new BoosterWallet(currentEconomyConfig);
            initialSnapshot = Board.CreateSnapshot();
            LastRuleResult = new CatFlapRuleValidationResult();
            LastInteractedCell = null;
            tentativeEditOrder.Clear();
            progressTracker.Reset();
            completionEventRaised = false;

            GameState.TransitionTo(CatFlapGameState.Playing);
            StateChanged?.Invoke(this, EventArgs.Empty);
            BoardChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            EnsureLoaded();
            Board.Restore(initialSnapshot);
            LastRuleResult = new CatFlapRuleValidationResult();
            LastInteractedCell = null;
            tentativeEditOrder.Clear();
            progressTracker.Reset();
            BoardChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Restart()
        {
            EnsureLoaded();
            Load(Level, currentEconomyConfig, LevelMistakeLimit?.MaxMistakes ?? 5);
        }

        public void RestoreLevelProgress(CatFlapBoardSnapshot boardSnapshot, int remainingMistakes, int moves, float elapsedSeconds)
        {
            EnsureLoaded();
            if (boardSnapshot != null)
            {
                Board.Restore(boardSnapshot);
            }

            RebuildCompletedLineLocks();
            LevelMistakeLimit?.RestoreState(remainingMistakes);
            RebuildTentativeEditOrderFromBoard();
            progressTracker.Restore(moves, elapsedSeconds);
            BoardChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TrySetCell(int row, int column, CatFlapCellValue value, bool isNote)
        {
            EnsureLoaded();
            if (GameState.Current != CatFlapGameState.Playing)
            {
                return false;
            }

            CatFlapCellModel cell = Board.GetCell(row, column);
            if (cell.IsLocked)
            {
                return false;
            }

            Board.SetCellValue(row, column, value, isNote);
            LastInteractedCell = new CatFlapCellPosition(row, column);
            UpdateTentativeOrder(row, column);
            BoardChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool TryCycleCell(int row, int column, bool isNote)
        {
            EnsureLoaded();
            CatFlapCellModel cell = Board.GetCell(row, column);
            if (!isNote && cell.IsNote)
            {
                bool noteApplied = TrySetCell(row, column, cell.Value, false);
                if (noteApplied)
                {
                    progressTracker.AddMove();
                }

                return noteApplied;
            }

            CatFlapCellValue next = NextValue(cell.Value);
            bool changed = TrySetCell(row, column, next, isNote);
            if (changed && !isNote)
            {
                progressTracker.AddMove();
            }

            return changed;
        }

        public bool UndoCell(int row, int column, int moveCount)
        {
            EnsureLoaded();
            CatFlapCellModel cell = Board.GetCell(row, column);
            if (cell.IsLocked)
            {
                return false;
            }

            Board.SetCellValue(row, column, CatFlapCellValue.Empty, false);
            RemoveTentativeEntry(row, column);
            progressTracker.RemoveMoves(moveCount);
            BoardChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Commits every currently-tentative cell to a normal placed value, in the order the
        /// player last edited them (see <see cref="tentativeEditOrder"/>). Does not validate or
        /// deduct lives - callers (see <see cref="CatFlapRuntimeController.ApplyAllNotes"/>) run
        /// solution/rule checks once against the returned batch so Apply All reads as one action.
        /// </summary>
        public List<CatFlapCellPosition> ApplyAllNotes()
        {
            EnsureLoaded();
            var appliedPositions = new List<CatFlapCellPosition>();
            if (GameState.Current != CatFlapGameState.Playing)
            {
                return appliedPositions;
            }

            // Snapshot first: committing a cell below removes it from tentativeEditOrder, which
            // would otherwise skip entries while iterating the live list.
            var orderedCandidates = new List<CatFlapCellPosition>(tentativeEditOrder);
            for (int index = 0; index < orderedCandidates.Count; index++)
            {
                CatFlapCellPosition position = orderedCandidates[index];
                CatFlapCellModel cell = Board.GetCell(position.Row, position.Column);
                if (cell.IsLocked || !cell.IsNote || cell.Value == CatFlapCellValue.Empty)
                {
                    continue;
                }

                Board.SetCellValue(position.Row, position.Column, cell.Value, false);
                RemoveTentativeEntry(position.Row, position.Column);
                appliedPositions.Add(position);
            }

            if (appliedPositions.Count > 0)
            {
                progressTracker.AddMove();
                BoardChanged?.Invoke(this, EventArgs.Empty);
            }

            return appliedPositions;
        }

        public void Tick(float deltaSeconds)
        {
            if (HasLevel && GameState.Current == CatFlapGameState.Playing)
            {
                progressTracker.Tick(deltaSeconds);
            }
        }

        /// <summary>
        /// Highlight-only hint lookup: finds the best candidate via the existing solution-driven
        /// priority selector and spends the booster charge on success, but NEVER mutates the
        /// board - the player must still tap the target cell themselves. The star rating is
        /// still computed from the difficulty the hinted move would remove, using a
        /// non-destructive simulate-then-restore (the same snapshot/restore pattern already used
        /// by <see cref="CatFlapForcedCellAnalyzer"/>/<see cref="CatFlapSolutionHintProvider"/>),
        /// so no observable board state changes as a result of calling this method.
        /// </summary>
        public CatFlapCellRevealResult RevealHint()
        {
            EnsureLoaded();
            CatFlapCellRevealResult result = hintService.RevealNext(Board, Level, LastInteractedCell);
            if (!result.Success)
            {
                return result;
            }

            if (!TrySpendBooster(BoosterType.Hint, out string failureMessage))
            {
                return CatFlapCellRevealResult.Failed(failureMessage);
            }

            CatFlapBoardSnapshot snapshotBeforeSimulation = Board.CreateSnapshot();
            int difficultyBefore = CatFlapBoardDifficultyScorer.Score(Board, Level);
            Board.SetCellValue(result.Row, result.Column, result.Value, false);
            int difficultyAfter = CatFlapBoardDifficultyScorer.Score(Board, Level);
            Board.Restore(snapshotBeforeSimulation);
            LastHintDifficultyRemoved = difficultyBefore - difficultyAfter;
            LastHintStars = ComputeHintStars(LastHintDifficultyRemoved);
            return result;
        }

        /// <summary>
        /// Refunds the Hint charge spent by <see cref="RevealHint"/>. Only for the case where the
        /// player presses Undo while a hint is active but never actually tapped the hinted cell -
        /// see <see cref="CatFlapRuntimeController.DismissActiveHint"/>. Normal hint dismissal
        /// (tapping elsewhere, a future "X" button) still never refunds.
        /// </summary>
        public void RefundHintCharge()
        {
            BoosterWallet?.RefundCharge(BoosterType.Hint);
        }

        private static int ComputeHintStars(int difficultyRemoved)
        {
            const int ThreeStarThreshold = 10;
            const int TwoStarThreshold = 4;
            if (difficultyRemoved >= ThreeStarThreshold)
            {
                return 3;
            }

            return difficultyRemoved >= TwoStarThreshold ? 2 : 1;
        }

        public CatFlapCellRevealResult FixFirstIncorrect()
        {
            EnsureLoaded();
            CatFlapCellRevealResult result = fixService.FixFirstIncorrect(Board, Level);
            if (!result.Success)
            {
                return result;
            }

            if (!TrySpendBooster(BoosterType.Fix, out string failureMessage))
            {
                return CatFlapCellRevealResult.Failed(failureMessage);
            }

            Board.SetCellValue(result.Row, result.Column, result.Value, false);
            RefreshLastRuleResult();
            BoardChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        public int AddCoins(int amount)
        {
            EnsureLoaded();
            return BoosterWallet.AddCoins(amount);
        }

        public bool TrySpendCoins(int amount)
        {
            EnsureLoaded();
            return BoosterWallet.TrySpendCoins(amount);
        }

        public void AddLevelMistakes(int amount)
        {
            EnsureLoaded();
            LevelMistakeLimit.AddMistakes(amount);
            StateChanged?.Invoke(this, EventArgs.Empty);

            // Same check TrySpendLevelMistake already does after ITS own deduction - a negative
            // amount (e.g. CatFlapGameplayManager.Cheat's CheatFailLevel depleting the remaining
            // count to 0 to force the real Out of Lives flow) should notify exactly the same way a
            // real mistake spend that lands on 0 does. A positive amount (granting an extra
            // attempt, the only other existing caller) never satisfies IsOutOfMistakes, so this is
            // a no-op for that case.
            if (LevelMistakeLimit.IsOutOfMistakes)
            {
                OutOfMistakes?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RefillLevelMistakes()
        {
            EnsureLoaded();
            LevelMistakeLimit.Refill();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsCellCorrectAgainstSolution(int row, int column)
        {
            EnsureLoaded();
            CatFlapCellModel cell = Board.GetCell(row, column);
            if (cell.Value == CatFlapCellValue.Empty || cell.IsNote)
            {
                return true;
            }

            int? solutionValue = Level.Solution[row].Cells[column];
            return solutionValue.HasValue && (int)cell.Value == solutionValue.Value;
        }

        public CatFlapRuleValidationResult ValidateCurrentBoard()
        {
            EnsureLoaded();
            LastRuleResult = ruleValidator.Validate(Board);
            RulesValidated?.Invoke(this, LastRuleResult);
            return FinalizeValidation();
        }

        /// <summary>
        /// Same as the parameterless overload, but only re-checks the given row and/or column
        /// instead of rescanning the whole board - every other line's violation state is carried
        /// over unchanged from <see cref="LastRuleResult"/> via
        /// <see cref="CatFlapRuleValidationResult.ReplaceLines"/>. Pass -1 for row or column to
        /// skip rechecking that one dimension entirely (e.g. an independent per-column timer that
        /// must never touch row violation state), or -1 for both (or use the parameterless
        /// overload) when there's no line to check at all, e.g. a manual/cheat re-validate.
        /// </summary>
        public CatFlapRuleValidationResult ValidateCurrentBoard(int row, int column)
        {
            if (row < 0 && column < 0)
            {
                return ValidateCurrentBoard();
            }

            EnsureLoaded();
            CatFlapRuleValidationResult scoped = ruleValidator.ValidateLines(Board, row, column);
            LastRuleResult = LastRuleResult.ReplaceLines(scoped, row, column);
            RulesValidated?.Invoke(this, LastRuleResult);
            return FinalizeValidation();
        }

        private CatFlapRuleValidationResult FinalizeValidation()
        {
            bool boardSolved = IsBoardSolvedAgainstSolution();
            bool lockedLines = false;
            if (LastRuleResult.IsValid || boardSolved)
            {
                lockedLines = LockCompletedCorrectLines();
            }

            if (boardSolved)
            {
                if (GameState.TryTransitionTo(CatFlapGameState.Won))
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    RaiseCompletionOnce(CatFlapGameState.Won);
                }
            }

            if (lockedLines)
            {
                BoardChanged?.Invoke(this, EventArgs.Empty);
            }

            return LastRuleResult;
        }

        private void RefreshLastRuleResult()
        {
            EnsureLoaded();
            LastRuleResult = ruleValidator.Validate(Board);
            RulesValidated?.Invoke(this, LastRuleResult);
        }

        public CatFlapRuleValidationResult PreviewRuleValidation()
        {
            EnsureLoaded();
            return ruleValidator.Validate(Board);
        }

        /// <summary>
        /// Same as the parameterless overload, but only checks the given row and/or column instead
        /// of the whole board. Safe for point-in-time containment checks against a single tapped
        /// cell (e.g. "does this cell sit in a broken rule right now?"), since that cell's
        /// violation state can only ever depend on its own row and column. Pass -1 for row or
        /// column to skip checking that one dimension (e.g. a per-column timer that must ignore
        /// row state owned by a different, still-independent timer). Does not update
        /// <see cref="LastRuleResult"/> - callers that need the persisted board-wide result should
        /// use <see cref="ValidateCurrentBoard(int, int)"/> instead.
        /// </summary>
        public CatFlapRuleValidationResult PreviewRuleValidation(int row, int column)
        {
            if (row < 0 && column < 0)
            {
                return PreviewRuleValidation();
            }

            EnsureLoaded();
            return ruleValidator.ValidateLines(Board, row, column);
        }

        public bool TrySpendLevelMistake()
        {
            EnsureLoaded();
            bool spent = LevelMistakeLimit.TrySpendMistake();
            if (LevelMistakeLimit.IsOutOfMistakes)
            {
                OutOfMistakes?.Invoke(this, EventArgs.Empty);
            }

            return spent;
        }

        public bool ForceWin()
        {
            EnsureLoaded();
            if (GameState.TryTransitionTo(CatFlapGameState.Won))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                RaiseCompletionOnce(CatFlapGameState.Won);
                return true;
            }

            return false;
        }

        public bool ForceFail()
        {
            EnsureLoaded();
            if (GameState.TryTransitionTo(CatFlapGameState.Failed))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                RaiseCompletionOnce(CatFlapGameState.Failed);
                return true;
            }

            return false;
        }

        private void EnsureLoaded()
        {
            if (!HasLevel)
            {
                throw new InvalidOperationException("No Cat Flap level is loaded.");
            }
        }

        private bool TrySpendBooster(BoosterType boosterType, out string failureMessage)
        {
            int cost = BoosterWallet.Config.GetCost(boosterType);
            if (BoosterWallet.TrySpend(boosterType))
            {
                failureMessage = string.Empty;
                return true;
            }

            failureMessage = $"Not enough coins. {boosterType} costs {cost}.";
            return false;
        }

        private void RaiseCompletionOnce(CatFlapGameState state)
        {
            if (completionEventRaised)
            {
                return;
            }

            completionEventRaised = true;
            if (state == CatFlapGameState.Won)
            {
                LevelWon?.Invoke(this, EventArgs.Empty);
            }
            else if (state == CatFlapGameState.Failed)
            {
                LevelFailed?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool LockCompletedCorrectLines()
        {
            bool changed = false;
            for (int row = 0; row < Board.RowCount; row++)
            {
                if (IsCompletedCorrectRow(row) && !IsRowLocked(row))
                {
                    Board.LockRow(row);
                    changed = true;
                }
            }

            for (int column = 0; column < Board.ColumnCount; column++)
            {
                if (IsCompletedCorrectColumn(column) && !IsColumnLocked(column))
                {
                    Board.LockColumn(column);
                    changed = true;
                }
            }

            return changed;
        }

        private void RebuildCompletedLineLocks()
        {
            for (int row = 0; row < Board.RowCount; row++)
            {
                for (int column = 0; column < Board.ColumnCount; column++)
                {
                    bool shouldLock = IsClueCell(row, column) ||
                                      IsCompletedCorrectRow(row) ||
                                      IsCompletedCorrectColumn(column);
                    Board.GetCell(row, column).SetLocked(shouldLock);
                }
            }
        }

        private bool IsClueCell(int row, int column)
        {
            return Level.Clues[row].Cells[column].HasValue;
        }

        private bool IsCompletedCorrectRow(int row)
        {
            for (int column = 0; column < Board.ColumnCount; column++)
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
            for (int row = 0; row < Board.RowCount; row++)
            {
                if (!IsSolvedCell(row, column))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSolvedCell(int row, int column)
        {
            CatFlapCellModel cell = Board.GetCell(row, column);
            int? solution = Level.Solution[row].Cells[column];
            return solution.HasValue && !cell.IsNote && cell.Value == (CatFlapCellValue)solution.Value;
        }

        private bool IsBoardSolvedAgainstSolution()
        {
            for (int row = 0; row < Board.RowCount; row++)
            {
                for (int column = 0; column < Board.ColumnCount; column++)
                {
                    if (!IsSolvedCell(row, column))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsBoardFilled()
        {
            for (int row = 0; row < Board.RowCount; row++)
            {
                for (int column = 0; column < Board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = Board.GetCell(row, column);
                    if (cell.Value == CatFlapCellValue.Empty || cell.IsNote)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsRowLocked(int row)
        {
            for (int column = 0; column < Board.ColumnCount; column++)
            {
                if (!Board.GetCell(row, column).IsLocked)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsColumnLocked(int column)
        {
            for (int row = 0; row < Board.RowCount; row++)
            {
                if (!Board.GetCell(row, column).IsLocked)
                {
                    return false;
                }
            }

            return true;
        }

        // Central chokepoint for tentative-order bookkeeping: called after every cell write that
        // can change note state. Removes any stale entry for this cell then, if it is still a
        // non-empty note, re-adds it as the newest (last) entry - this is what makes "Fish ->
        // Cat" on an existing note bump it to the front of the undo-reversal queue, per spec.
        private void UpdateTentativeOrder(int row, int column)
        {
            RemoveTentativeEntry(row, column);
            CatFlapCellModel cell = Board.GetCell(row, column);
            if (cell.IsNote && cell.Value != CatFlapCellValue.Empty)
            {
                tentativeEditOrder.Add(new CatFlapCellPosition(row, column));
            }
        }

        private void RemoveTentativeEntry(int row, int column)
        {
            for (int index = tentativeEditOrder.Count - 1; index >= 0; index--)
            {
                CatFlapCellPosition position = tentativeEditOrder[index];
                if (position.Row == row && position.Column == column)
                {
                    tentativeEditOrder.RemoveAt(index);
                }
            }
        }

        // Used only when restoring a previously-saved board (see RestoreLevelProgress): the true
        // edit order isn't persisted, so tentative cells are re-seeded in board scan order as a
        // reasonable fallback rather than leaving the undo-reversal history empty/wrong.
        private void RebuildTentativeEditOrderFromBoard()
        {
            tentativeEditOrder.Clear();
            for (int row = 0; row < Board.RowCount; row++)
            {
                for (int column = 0; column < Board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = Board.GetCell(row, column);
                    if (!cell.IsLocked && cell.IsNote && cell.Value != CatFlapCellValue.Empty)
                    {
                        tentativeEditOrder.Add(new CatFlapCellPosition(row, column));
                    }
                }
            }
        }

        private static CatFlapCellValue NextValue(CatFlapCellValue value)
        {
            switch (value)
            {
                case CatFlapCellValue.Empty:
                    return CatFlapCellValue.Fish;
                case CatFlapCellValue.Fish:
                    return CatFlapCellValue.Cat;
                default:
                    return CatFlapCellValue.Empty;
            }
        }
    }
}
