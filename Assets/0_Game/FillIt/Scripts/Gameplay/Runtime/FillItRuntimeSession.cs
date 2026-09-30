using System;
using System.Collections.Generic;
using FillIt.Levels;
using FillIt.Models;
using FillIt.Rules;
using Shared.Boosters;
using Shared.LevelMistakeLimit;

namespace FillIt.Runtime
{
    /// <summary>
    /// View-agnostic state of one Fillomino attempt: board, region analysis, lives, wallet, undo
    /// history and game state. Every board edit funnels through <see cref="Commit"/>, which
    /// re-analyses the regions and reports what changed.
    /// </summary>
    public sealed class FillItRuntimeSession
    {
        private readonly FillItProgressTracker progressTracker = new FillItProgressTracker();
        private readonly List<List<FillItCellChange>> undoHistory = new List<List<FillItCellChange>>();
        private BoosterConfig currentEconomyConfig;
        private bool completionEventRaised;

        public FillItRuntimeSession()
        {
            GameState = new FillItGameStateModel();
        }

        public FillItLevelData Level { get; private set; }
        public FillItBoardModel Board { get; private set; }
        public FillItBoardAnalysis Analysis { get; private set; }
        public LevelMistakeLimitModel LevelMistakeLimit { get; private set; }
        public BoosterWallet BoosterWallet { get; private set; }
        public FillItGameStateModel GameState { get; }
        public FillItProgressSnapshot Progress => progressTracker.CreateSnapshot(Board);
        public bool HasLevel => Level != null && Board != null;
        public bool IsBoardComplete => HasLevel && Board.IsFull();
        public bool IsPlaying => HasLevel && GameState.Current == FillItGameState.Playing;
        public int LastInteractedIndex { get; private set; } = -1;
        public bool CanUndo => undoHistory.Count > 0;
        public int UndoCount => undoHistory.Count;

        public event EventHandler StateChanged = delegate { };
        public event EventHandler LevelWon = delegate { };
        public event EventHandler LevelFailed = delegate { };
        public event EventHandler OutOfMistakes = delegate { };
        /// <summary>Timer mode only: the countdown just reached zero (raised once until time is added).</summary>
        public event EventHandler TimeUp = delegate { };

        /// <summary>Timer gameplay mode: this level has a countdown instead of (visible) hearts.</summary>
        public bool TimerEnabled { get; private set; }
        public float TimeLimitSeconds { get; private set; }
        public float RemainingSeconds { get; private set; }
        public bool IsOutOfTime => TimerEnabled && RemainingSeconds <= 0f;
        /// <summary>
        /// The countdown waits at full time until the player's first input on the level
        /// (see StartTimer) - reset on every Load/Restart and on resuming a saved board.
        /// </summary>
        public bool TimerStarted { get; private set; }
        private bool timeUpRaised;

        /// <summary>First input of the level: lets the countdown run. No-op once started.</summary>
        public void StartTimer()
        {
            if (TimerEnabled && IsPlaying)
            {
                TimerStarted = true;
            }
        }

        /// <summary>
        /// Sets the countdown used by this and every following Load/Restart until changed. Call
        /// before Load so the fresh level starts with the full time.
        /// </summary>
        public void ConfigureTimer(bool enabled, float limitSeconds)
        {
            TimerEnabled = enabled;
            TimeLimitSeconds = enabled ? Math.Max(1f, limitSeconds) : 0f;
            RemainingSeconds = TimeLimitSeconds;
            TimerStarted = false;
            timeUpRaised = false;
        }

        /// <summary>Counts the timer down; only called while the player can actually play (see FillItGameplayManager).</summary>
        public void TickTimer(float deltaSeconds)
        {
            if (!TimerEnabled || !TimerStarted || !IsPlaying || timeUpRaised || deltaSeconds <= 0f)
            {
                return;
            }

            RemainingSeconds = Math.Max(0f, RemainingSeconds - deltaSeconds);
            if (RemainingSeconds <= 0f)
            {
                timeUpRaised = true;
                TimeUp?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Timer revive: extra seconds on the clock (re-arms TimeUp).</summary>
        public void AddTime(float seconds)
        {
            if (!TimerEnabled || seconds <= 0f)
            {
                return;
            }

            RemainingSeconds += seconds;
            timeUpRaised = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RestoreRemainingTime(float seconds)
        {
            if (TimerEnabled && seconds > 0f)
            {
                RemainingSeconds = seconds;
                timeUpRaised = false;
            }
        }

        public void ShowMainMenu()
        {
            if (GameState.Current == FillItGameState.Uninitialized)
            {
                GameState.TransitionTo(FillItGameState.MainMenu);
            }
            else if (GameState.Current != FillItGameState.MainMenu)
            {
                GameState.TryTransitionTo(FillItGameState.MainMenu);
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Load(FillItLevelData level, BoosterConfig economyConfig = null, int levelMistakeLimit = 5)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            if (GameState.Current == FillItGameState.Uninitialized)
            {
                GameState.TransitionTo(FillItGameState.Initializing);
            }
            else
            {
                GameState.TryTransitionTo(FillItGameState.Initializing);
            }

            Level = level;
            Board = FillItBoardModel.FromLevel(level);
            Analysis = FillItRegionAnalyzer.Analyze(Board);
            LevelMistakeLimit = new LevelMistakeLimitModel(levelMistakeLimit);
            currentEconomyConfig = (economyConfig ?? currentEconomyConfig ?? new BoosterConfig()).Clone();
            BoosterWallet = new BoosterWallet(currentEconomyConfig);
            undoHistory.Clear();
            LastInteractedIndex = -1;
            progressTracker.Reset();
            completionEventRaised = false;
            RemainingSeconds = TimeLimitSeconds;
            TimerStarted = false;
            timeUpRaised = false;

            GameState.TransitionTo(FillItGameState.Playing);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Restart()
        {
            EnsureLoaded();
            Load(Level, currentEconomyConfig, LevelMistakeLimit?.MaxMistakes ?? 5);
        }

        /// <summary>Clears every player number back to the level's starting clues (lives untouched).</summary>
        public void Clear()
        {
            EnsureLoaded();
            Board = FillItBoardModel.FromLevel(Level);
            Analysis = FillItRegionAnalyzer.Analyze(Board);
            undoHistory.Clear();
            LastInteractedIndex = -1;
            progressTracker.Reset();
        }

        public void RestoreLevelProgress(FillItBoardSnapshot snapshot, int remainingMistakes, int moves, float elapsedSeconds)
        {
            EnsureLoaded();
            Board.Restore(snapshot);
            Analysis = FillItRegionAnalyzer.Analyze(Board);
            LevelMistakeLimit?.RestoreState(remainingMistakes);
            progressTracker.Restore(moves, elapsedSeconds);
            undoHistory.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Tick(float deltaSeconds)
        {
            if (IsPlaying)
            {
                progressTracker.Tick(deltaSeconds);
            }
        }

        /// <summary>
        /// Call after the board already holds the new values (drag chains write live). Re-analyses
        /// regions, records Undo, counts the move, and raises LevelWon when the board is solved.
        /// </summary>
        public FillItMoveResult Commit(FillItMoveKind kind, List<FillItCellChange> changes, bool recordUndo = true)
        {
            EnsureLoaded();
            if (changes == null || changes.Count == 0)
            {
                return FillItMoveResult.Nothing;
            }

            FillItBoardAnalysis before = Analysis;
            Analysis = FillItRegionAnalyzer.Analyze(Board);

            if (recordUndo)
            {
                undoHistory.Add(new List<FillItCellChange>(changes));
            }

            if (kind != FillItMoveKind.Undo)
            {
                progressTracker.AddMove();
            }
            else
            {
                progressTracker.RemoveMove();
            }

            LastInteractedIndex = changes[changes.Count - 1].Index;
            List<FillItRegion> completed = FindNewlyCompleted(before, Analysis);
            List<FillItRegion> violations = FindNewViolations(before, Analysis);
            bool solved = Analysis.IsSolved;
            if (solved && GameState.TryTransitionTo(FillItGameState.Won))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                RaiseCompletionOnce(FillItGameState.Won);
            }

            return new FillItMoveResult(kind, changes, completed, violations, solved);
        }

        /// <summary>Sets one cell (number pad input). Returns Nothing if the cell can't change.</summary>
        public FillItMoveResult SetCellValue(int index, int value, FillItMoveKind kind)
        {
            EnsureLoaded();
            if (!IsPlaying || !Board.IsValidIndex(index) || Board.IsGiven(index) || Board.GetValue(index) == value)
            {
                return FillItMoveResult.Nothing;
            }

            bool wasHinted = Board.IsHinted(index);
            int previous = Board.GetValue(index);
            Board.SetValue(index, value);
            return Commit(kind, new List<FillItCellChange> { new FillItCellChange(index, previous, value, wasHinted) });
        }

        /// <summary>Reverts the most recent recorded move (drag, tap, pad input or hint).</summary>
        public FillItMoveResult Undo()
        {
            EnsureLoaded();
            if (!IsPlaying || undoHistory.Count == 0)
            {
                return FillItMoveResult.Nothing;
            }

            List<FillItCellChange> move = undoHistory[undoHistory.Count - 1];
            undoHistory.RemoveAt(undoHistory.Count - 1);
            var reverted = new List<FillItCellChange>(move.Count);
            for (int index = move.Count - 1; index >= 0; index--)
            {
                FillItCellChange change = move[index];
                Board.SetValue(change.Index, change.Before);
                Board.SetHinted(change.Index, change.BeforeHinted);
                reverted.Add(new FillItCellChange(change.Index, change.After, change.Before, change.AfterHinted, change.BeforeHinted));
            }

            return Commit(FillItMoveKind.Undo, reverted, recordUndo: false);
        }

        public void ClearUndoHistory()
        {
            undoHistory.Clear();
        }

        public FillItHintResult FindHint(int selectedIndex)
        {
            EnsureLoaded();
            return FillItHintService.FindHint(Board, Level, Analysis, selectedIndex);
        }

        public FillItMoveResult ApplyHint(int index, int value)
        {
            EnsureLoaded();
            if (!IsPlaying || !Board.IsValidIndex(index) || Board.IsGiven(index))
            {
                return FillItMoveResult.Nothing;
            }

            int previous = Board.GetValue(index);
            bool wasHinted = Board.IsHinted(index);
            Board.SetValue(index, value);
            Board.SetHinted(index, true);
            return Commit(FillItMoveKind.Hint, new List<FillItCellChange> { new FillItCellChange(index, previous, value, wasHinted, true) });
        }

        /// <summary>Fix booster: clears every placed number that doesn't match the solution.</summary>
        public FillItMoveResult ClearWrongCells()
        {
            EnsureLoaded();
            if (!IsPlaying)
            {
                return FillItMoveResult.Nothing;
            }

            List<int> wrong = FillItHintService.FindWrongCells(Board, Level);
            var changes = new List<FillItCellChange>(wrong.Count);
            for (int index = 0; index < wrong.Count; index++)
            {
                int cell = wrong[index];
                changes.Add(new FillItCellChange(cell, Board.GetValue(cell), FillItBoardModel.EmptyValue, Board.IsHinted(cell)));
                Board.SetValue(cell, FillItBoardModel.EmptyValue);
            }

            // Not recorded for Undo - undoing a Fix would just put the mistakes back.
            return Commit(FillItMoveKind.Fix, changes, recordUndo: false);
        }

        public int CountWrongCells()
        {
            return HasLevel ? FillItHintService.FindWrongCells(Board, Level).Count : 0;
        }

        public bool TrySpendBooster(BoosterType boosterType, out string failureMessage)
        {
            EnsureLoaded();
            int cost = BoosterWallet.Config.GetCost(boosterType);
            if (BoosterWallet.TrySpend(boosterType))
            {
                failureMessage = string.Empty;
                return true;
            }

            failureMessage = $"Not enough coins. {boosterType} costs {cost}.";
            return false;
        }

        public void RefundBoosterCharge(BoosterType boosterType)
        {
            BoosterWallet?.RefundCharge(boosterType);
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

        public void AddLevelMistakes(int amount)
        {
            EnsureLoaded();
            LevelMistakeLimit.AddMistakes(amount);
            StateChanged?.Invoke(this, EventArgs.Empty);
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

        /// <summary>Cheat/Skip-Tutorial: fills the stored solution and wins immediately.</summary>
        public bool ForceWin()
        {
            EnsureLoaded();
            for (int index = 0; index < Board.CellCount; index++)
            {
                if (!Board.IsGiven(index))
                {
                    Board.SetValue(index, Level.GetSolutionValue(index));
                }
            }

            Analysis = FillItRegionAnalyzer.Analyze(Board);
            if (GameState.TryTransitionTo(FillItGameState.Won))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                RaiseCompletionOnce(FillItGameState.Won);
                return true;
            }

            return false;
        }

        public bool ForceFail()
        {
            EnsureLoaded();
            if (GameState.TryTransitionTo(FillItGameState.Failed))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                RaiseCompletionOnce(FillItGameState.Failed);
                return true;
            }

            return false;
        }

        private static List<FillItRegion> FindNewlyCompleted(FillItBoardAnalysis before, FillItBoardAnalysis after)
        {
            var previous = CollectSignatures(before, done: true);
            var result = new List<FillItRegion>();
            for (int index = 0; index < after.Regions.Count; index++)
            {
                FillItRegion region = after.Regions[index];
                if (region.Status == FillItRegionStatus.Done && !previous.Contains(region.Signature))
                {
                    result.Add(region);
                }
            }

            return result;
        }

        private static List<FillItRegion> FindNewViolations(FillItBoardAnalysis before, FillItBoardAnalysis after)
        {
            var previous = CollectSignatures(before, done: false);
            var result = new List<FillItRegion>();
            for (int index = 0; index < after.Regions.Count; index++)
            {
                FillItRegion region = after.Regions[index];
                if (region.IsViolation && !previous.Contains(region.Signature))
                {
                    result.Add(region);
                }
            }

            return result;
        }

        private static HashSet<string> CollectSignatures(FillItBoardAnalysis analysis, bool done)
        {
            var signatures = new HashSet<string>();
            if (analysis == null)
            {
                return signatures;
            }

            for (int index = 0; index < analysis.Regions.Count; index++)
            {
                FillItRegion region = analysis.Regions[index];
                if (done ? region.Status == FillItRegionStatus.Done : region.IsViolation)
                {
                    signatures.Add(region.Signature);
                }
            }

            return signatures;
        }

        private void RaiseCompletionOnce(FillItGameState state)
        {
            if (completionEventRaised)
            {
                return;
            }

            completionEventRaised = true;
            if (state == FillItGameState.Won)
            {
                LevelWon?.Invoke(this, EventArgs.Empty);
            }
            else if (state == FillItGameState.Failed)
            {
                LevelFailed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void EnsureLoaded()
        {
            if (!HasLevel)
            {
                throw new InvalidOperationException("No Fill It level is loaded.");
            }
        }
    }
}
