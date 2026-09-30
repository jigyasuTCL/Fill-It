using System;
using System.Collections.Generic;
using FillIt.Levels;
using FillIt.Models;
using FillIt.Rules;
using FillIt.Views;
using Shared.AppreciationText;
using Shared.Boosters;
using Shared.GameFlow;
using UnityEngine;

namespace FillIt.Runtime
{
    /// <summary>
    /// View-agnostic Fill It core: owns the <see cref="FillItRuntimeSession"/> and the drag gesture,
    /// turns player input into board edits, and paints the board/status views through their
    /// interfaces. Knows nothing about popups, ads, economy screens or level progression - that's
    /// FillItGameplayManager's job, driven by the events below.
    /// </summary>
    public sealed class FillItRuntimeController : IDisposable
    {
        // Fraction of the board that must be in finished regions before a move can trigger the
        // Hard appreciation tier (once per level).
        private const float HardTierFillThreshold = 0.7f;

        private readonly FillItLevelRepository levelRepository;
        private readonly FillItRuntimeSession session;
        private readonly IFillItBoardView boardView;
        private readonly IFillItStatusView statusView;
        private readonly IGameFlowView gameFlowView;
        private readonly FillItDragChain dragChain = new FillItDragChain();
        private readonly List<Vector2Int> trailScratch = new List<Vector2Int>();
        private FillItCellVisualState[] lastRendered = Array.Empty<FillItCellVisualState>();
        private bool[] hasRendered = Array.Empty<bool>();
        private FillItHintResult? activeHint;
        private bool pendingLevelWonNotification;
        private bool disposed;
        private int regionStreak;
        private bool hasFiredHardTier;

        public FillItRuntimeController(
            FillItLevelRepository levelRepository,
            FillItRuntimeSession session,
            IFillItBoardView boardView,
            IFillItStatusView statusView = null,
            IGameFlowView gameFlowView = null)
        {
            this.levelRepository = levelRepository ?? new FillItLevelRepository();
            this.session = session ?? new FillItRuntimeSession();
            this.boardView = boardView;
            this.statusView = statusView;
            this.gameFlowView = gameFlowView;
            this.session.LevelWon += HandleSessionLevelWon;
            this.session.LevelFailed += HandleSessionLevelFailed;
            this.session.OutOfMistakes += HandleSessionOutOfMistakes;
            this.session.TimeUp += HandleSessionTimeUp;
        }

        public FillItRuntimeSession Session => session;
        public bool IsDragging => dragChain.IsActive;
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>
        /// Gates the Level Attempt (hearts) visuals - while false the hearts HUD stays hidden and a
        /// mistake never plays the heart break FX. Mistakes are still tracked underneath.
        /// </summary>
        public bool LevelAttemptFeatureEnabled { get; set; } = true;

        /// <summary>Hides all red "wrong region" rendering - FTUE levels, where the tutorial keeps the player on track.</summary>
        public bool SuppressWrongIndication { get; set; }

        /// <summary>Suppresses the Appreciation Text popup (FTUE levels).</summary>
        public bool SuppressAppreciationText { get; set; }

        /// <summary>Game-wide on/off switch for the Appreciation Text ("Nice!", "Great!"...) popups.</summary>
        public bool AppreciationTextEnabled { get; set; } = true;

        /// <summary>FTUE guided moves: only these cells may be started on / filled. Null = unrestricted.</summary>
        public ISet<int> AllowedCells { get; set; }

        public FillItHintResult? ActiveHint => activeHint;
        public FillItCellPosition? ActiveHintCell => activeHint.HasValue ? new FillItCellPosition(activeHint.Value.Row, activeHint.Value.Column) : (FillItCellPosition?)null;

        public event EventHandler LevelWon = delegate { };
        public event EventHandler LevelFailed = delegate { };
        public event EventHandler OutOfMistakes = delegate { };
        /// <summary>Timer mode: the level's countdown ran out.</summary>
        public event EventHandler TimeUp = delegate { };
        /// <summary>Fires once per mistake that actually spends a life.</summary>
        public event EventHandler MistakeSpent = delegate { };
        /// <summary>Fires once per broken rule charged against a life (analytics).</summary>
        public event Action<FillItRule> RuleViolationCharged = delegate { };

        public void ShowMainMenu()
        {
            CancelDrag();
            session.ShowMainMenu();
            boardView?.Clear();
            gameFlowView?.ShowState(GameFlowState.MainMenu);
        }

        public void ClearBoardView()
        {
            CancelDrag();
            DismissActiveHint();
            ResetStatusIndicators();
            boardView?.Clear();
        }

        public FillItLevelLoadResult ParseLevelJson(string json)
        {
            return levelRepository.LoadFromJson(json);
        }

        public void LoadLevel(FillItLevelData level, BoosterConfig economyConfig = null, int levelMistakeLimit = 5)
        {
            CancelDrag();
            DismissActiveHint();
            session.Load(level, economyConfig, levelMistakeLimit);
            ResetPerLevelState();
            RenderAll(animate: false, rebuild: true);
        }

        public void Clear()
        {
            if (!session.HasLevel)
            {
                return;
            }

            CancelDrag();
            DismissActiveHint();
            session.Clear();
            ResetPerLevelState();
            RenderAll(animate: false, rebuild: true);
        }

        public void Restart()
        {
            if (!session.HasLevel)
            {
                return;
            }

            CancelDrag();
            DismissActiveHint();
            session.Restart();
            ResetPerLevelState();
            RenderAll(animate: false, rebuild: true);
        }

        public void RefreshViews(bool rebuild = false)
        {
            RenderAll(animate: false, rebuild: rebuild);
        }

        public void RefreshStatusOnly()
        {
            RenderStatus();
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

        /// <summary>Timer mode countdown - the manager only calls this while the player can act.</summary>
        public void TickTimer(float deltaSeconds)
        {
            if (!disposed)
            {
                session.TickTimer(deltaSeconds);
            }
        }

        // ----- selection & drag input -----

        public void SelectCell(int index)
        {
            if (!session.HasLevel || !session.Board.IsValidIndex(index))
            {
                return;
            }

            SelectedIndex = index;
            RenderAll(animate: false);
        }

        /// <summary>Starts a drag/tap gesture on a cell. Returns false when the cell can't be started on.</summary>
        public bool BeginDrag(int index)
        {
            if (!session.IsPlaying || !session.Board.IsValidIndex(index))
            {
                return false;
            }

            FillItBoardModel board = session.Board;
            if (AllowedCells != null && !board.IsGiven(index) && !AllowedCells.Contains(index))
            {
                return false;
            }

            SelectedIndex = index;
            session.StartTimer();
            dragChain.Begin(board, index, AllowedCells);
            RenderAll(animate: false);
            return true;
        }

        /// <summary>Feeds the next cell under the finger. Returns true when the chain changed any value.</summary>
        public bool DragEnter(int index)
        {
            if (!dragChain.IsActive || !session.Board.IsValidIndex(index))
            {
                return false;
            }

            bool changed = dragChain.Enter(index);
            SelectedIndex = dragChain.LastIndex;
            RenderAll(animate: changed, liveDrag: true);
            return changed;
        }

        public FillItMoveResult EndDrag()
        {
            if (!dragChain.IsActive)
            {
                return FillItMoveResult.Nothing;
            }

            List<FillItCellChange> changes = dragChain.End(out FillItMoveKind kind);
            boardView?.ClearDragTrail();
            if (changes.Count == 0)
            {
                RenderAll(animate: false);
                return FillItMoveResult.Nothing;
            }

            DismissActiveHint();
            return CommitAndRender(kind, changes);
        }

        public void CancelDrag()
        {
            if (!dragChain.IsActive)
            {
                return;
            }

            dragChain.Cancel();
            boardView?.ClearDragTrail();
            RenderAll(animate: false);
        }

        /// <summary>
        /// Number pad: writes value (0 = erase) into the selected cell, or the first empty cell
        /// when nothing is selected. Returns Nothing (and changes nothing) for a given cell.
        /// </summary>
        public FillItMoveResult InputNumber(int value)
        {
            if (!session.IsPlaying || dragChain.IsActive)
            {
                return FillItMoveResult.Nothing;
            }

            FillItBoardModel board = session.Board;
            if (!board.IsValidIndex(SelectedIndex))
            {
                SelectedIndex = FirstEditableEmptyIndex();
            }

            if (!board.IsValidIndex(SelectedIndex) || board.IsGiven(SelectedIndex))
            {
                return FillItMoveResult.Nothing;
            }

            if (AllowedCells != null && !AllowedCells.Contains(SelectedIndex))
            {
                return FillItMoveResult.Nothing;
            }

            int previous = board.GetValue(SelectedIndex);
            bool wasHinted = board.IsHinted(SelectedIndex);
            if (previous == value)
            {
                return FillItMoveResult.Nothing;
            }

            DismissActiveHint();
            board.SetValue(SelectedIndex, value);
            return CommitAndRender(FillItMoveKind.NumberPad, new List<FillItCellChange> { new FillItCellChange(SelectedIndex, previous, value, wasHinted) });
        }

        public bool IsGivenCell(int index)
        {
            return session.HasLevel && session.Board.IsValidIndex(index) && session.Board.IsGiven(index);
        }

        // ----- boosters -----

        /// <summary>
        /// Highlight-only hint: picks a cell, spends a Hint charge, and shows the correct number as a
        /// preview. The player commits it by tapping the cell (or the Apply button). Re-requesting
        /// while a hint is showing returns the same hint without spending again.
        /// </summary>
        public FillItHintResult RevealHint()
        {
            if (!session.HasLevel)
            {
                return FillItHintResult.Failed("No Fill It level is loaded.");
            }

            if (activeHint.HasValue)
            {
                return activeHint.Value;
            }

            CancelDrag();
            FillItHintResult result = session.FindHint(SelectedIndex);
            if (!result.Success)
            {
                return result;
            }

            if (!session.TrySpendBooster(BoosterType.Hint, out string failureMessage))
            {
                return FillItHintResult.Failed(failureMessage);
            }

            activeHint = result;
            session.StartTimer();
            SelectedIndex = result.Index;
            boardView?.ShowHintTarget(result.Index, result.Value);
            RenderAll(animate: false);
            return result;
        }

        public FillItMoveResult ApplyHint()
        {
            if (!session.IsPlaying || !activeHint.HasValue)
            {
                return FillItMoveResult.Nothing;
            }

            FillItHintResult hint = activeHint.Value;
            DismissActiveHint();
            FillItMoveResult result = session.ApplyHint(hint.Index, hint.Value);
            SelectedIndex = hint.Index;
            return RenderMoveResult(result);
        }

        /// <summary>Clears the hint preview. refundCharge gives the charge back (Undo before the hint was used).</summary>
        public void DismissActiveHint(bool refundCharge = false)
        {
            if (!activeHint.HasValue)
            {
                return;
            }

            if (refundCharge)
            {
                session.RefundBoosterCharge(BoosterType.Hint);
            }

            activeHint = null;
            boardView?.ClearHintTarget();
        }

        /// <summary>Fix booster: removes every wrong number. Fails (no charge) when nothing is wrong.</summary>
        public FillItMoveResult UseFix(out string failureMessage)
        {
            failureMessage = string.Empty;
            if (!session.IsPlaying)
            {
                failureMessage = "No Fill It level is loaded.";
                return FillItMoveResult.Nothing;
            }

            if (session.CountWrongCells() == 0)
            {
                failureMessage = "Nothing to fix - every number is right so far.";
                return FillItMoveResult.Nothing;
            }

            if (!session.TrySpendBooster(BoosterType.Fix, out failureMessage))
            {
                return FillItMoveResult.Nothing;
            }

            CancelDrag();
            DismissActiveHint();
            FillItMoveResult result = session.ClearWrongCells();
            return RenderMoveResult(result);
        }

        public bool CanUndo()
        {
            return session.IsPlaying && session.CanUndo && !dragChain.IsActive;
        }

        public FillItMoveResult Undo()
        {
            if (!CanUndo())
            {
                return FillItMoveResult.Nothing;
            }

            FillItMoveResult result = session.Undo();
            if (result.Changed)
            {
                SelectedIndex = result.LastChangedIndex;
            }

            return RenderMoveResult(result);
        }

        // ----- mistakes -----

        /// <summary>
        /// The cells a move could cost a life for: numbers the player just placed themselves (drag,
        /// tap, number pad) that are wrong against the level's solution AND sit in a broken region.
        /// Hints, Fix and Undo never cost a life, nor does clearing a cell, nor does a correct number
        /// that merely touches someone else's broken region.
        /// </summary>
        public List<int> FindMistakeCells(FillItMoveResult result)
        {
            var cells = new List<int>();
            if (result == null || !result.Changed || !session.IsPlaying ||
                (result.Kind != FillItMoveKind.Drag && result.Kind != FillItMoveKind.TapPlaceOne && result.Kind != FillItMoveKind.NumberPad))
            {
                return cells;
            }

            for (int index = 0; index < result.Changes.Count; index++)
            {
                int cell = result.Changes[index].Index;
                if (IsChargeableMistake(cell) && !cells.Contains(cell))
                {
                    cells.Add(cell);
                }
            }

            return cells;
        }

        /// <summary>
        /// Charges at most one life, for whichever of candidateCells are still a real mistake after
        /// the grace period (a mistake fixed or undone in the meantime is free). Returns true when a
        /// life was actually spent.
        /// </summary>
        public bool ResolveMistakes(IEnumerable<int> candidateCells, int anchorIndex)
        {
            if (!session.IsPlaying || candidateCells == null)
            {
                return false;
            }

            var stillWrong = new List<int>();
            foreach (int cell in candidateCells)
            {
                if (IsChargeableMistake(cell))
                {
                    stillWrong.Add(cell);
                }
            }

            if (stillWrong.Count == 0)
            {
                return false;
            }

            var broken = new List<FillItRegion>();
            for (int index = 0; index < stillWrong.Count; index++)
            {
                FillItRegion region = session.Analysis.GetRegionAt(stillWrong[index]);
                if (region != null && !broken.Contains(region))
                {
                    broken.Add(region);
                }
            }

            bool spent = session.TrySpendLevelMistake();
            regionStreak = 0;

            if (spent)
            {
                bool tooBig = false;
                bool tooSmall = false;
                for (int index = 0; index < broken.Count; index++)
                {
                    tooBig |= broken[index].BrokenRule == FillItRule.RegionTooBig;
                    tooSmall |= broken[index].BrokenRule == FillItRule.RegionTooSmall;
                }

                if (tooBig)
                {
                    RuleViolationCharged(FillItRule.RegionTooBig);
                }

                if (tooSmall)
                {
                    RuleViolationCharged(FillItRule.RegionTooSmall);
                }

                MistakeSpent(this, EventArgs.Empty);
            }

            RenderStatus(animateHeartLoss: spent && LevelAttemptFeatureEnabled);
            var cells = new List<int>();
            for (int index = 0; index < broken.Count; index++)
            {
                cells.AddRange(broken[index].Cells);
            }

            boardView?.PlayMistake(cells);
            if (spent)
            {
                int anchor = stillWrong.Contains(anchorIndex) ? anchorIndex : stillWrong[stillWrong.Count - 1];
                if (LevelAttemptFeatureEnabled)
                {
                    boardView?.PlayHeartBreak(anchor);
                }

                statusView?.PlayRuleBreakBlink();
            }

            return spent;
        }

        // A player number that doesn't match the solution and is part of a visibly broken region.
        private bool IsChargeableMistake(int cell)
        {
            FillItBoardModel board = session.Board;
            if (!board.IsValidIndex(cell) || board.IsGiven(cell) || board.IsEmpty(cell) || board.IsHinted(cell))
            {
                return false;
            }

            return board.GetValue(cell) != session.Level.GetSolutionValue(cell) && session.Analysis.IsViolationCell(cell);
        }

        public void ForceWin()
        {
            if (!session.HasLevel)
            {
                return;
            }

            CancelDrag();
            DismissActiveHint();
            session.ForceWin();
            RenderAll(animate: true);
            RaisePendingLevelWonIfNeeded();
        }

        public void ForceFail()
        {
            if (!session.HasLevel)
            {
                return;
            }

            CancelDrag();
            session.ForceFail();
            RenderAll(animate: false);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            session.LevelWon -= HandleSessionLevelWon;
            session.LevelFailed -= HandleSessionLevelFailed;
            session.OutOfMistakes -= HandleSessionOutOfMistakes;
            session.TimeUp -= HandleSessionTimeUp;
        }

        // ----- internals -----

        private FillItMoveResult CommitAndRender(FillItMoveKind kind, List<FillItCellChange> changes)
        {
            // Any board change (number pad, Fix...) also counts as the first input.
            session.StartTimer();
            FillItMoveResult result = session.Commit(kind, changes);
            return RenderMoveResult(result);
        }

        private FillItMoveResult RenderMoveResult(FillItMoveResult result)
        {
            if (!result.Changed)
            {
                RenderAll(animate: false);
                return result;
            }

            RenderAll(animate: true);
            for (int index = 0; index < result.NewlyCompletedRegions.Count; index++)
            {
                boardView?.PlayRegionComplete(result.NewlyCompletedRegions[index].Cells);
            }

            if (result.Kind != FillItMoveKind.Undo && result.Kind != FillItMoveKind.Fix)
            {
                PlayAppreciationText(result);
            }

            RaisePendingLevelWonIfNeeded();
            return result;
        }

        private void PlayAppreciationText(FillItMoveResult result)
        {
            if (result.NewlyCompletedRegions.Count == 0)
            {
                if (result.NewViolations.Count > 0)
                {
                    regionStreak = 0;
                }

                return;
            }

            regionStreak++;
            if (!AppreciationTextEnabled || SuppressAppreciationText || session.Board.Size <= 3)
            {
                return;
            }

            AppreciationTextTier? tier = null;
            if (result.SolvedBoard)
            {
                tier = AppreciationTextTier.VeryHard;
            }
            else if (result.NewlyCompletedRegions.Count >= 2)
            {
                tier = AppreciationTextTier.Medium;
            }
            else if (!hasFiredHardTier && session.Analysis.CountDoneCells() >= session.Board.CellCount * HardTierFillThreshold)
            {
                hasFiredHardTier = true;
                tier = AppreciationTextTier.Hard;
            }
            else if (regionStreak >= (session.Board.Size >= 8 ? 4 : 3))
            {
                regionStreak = 0;
                tier = AppreciationTextTier.Easy;
            }

            if (tier.HasValue)
            {
                PlayAppreciationTextAt(tier.Value, result.LastChangedIndex);
            }
        }

        private void PlayAppreciationTextAt(AppreciationTextTier tier, int anchorIndex)
        {
            if (AppreciationTextManager.Instance == null)
            {
                return;
            }

            if (TryGetHudAnchorWorldPosition(out Vector3 anchor))
            {
                AppreciationTextManager.Instance.Play(tier, anchor);
                return;
            }

            if (boardView != null && boardView.TryGetCellWorldPosition(anchorIndex, out Vector3 cellPosition))
            {
                AppreciationTextManager.Instance.Play(tier, cellPosition);
                return;
            }

            AppreciationTextManager.Instance.Play(tier);
        }

        // Converts the HUD anchor's screen position into board world space, at the board's depth.
        private bool TryGetHudAnchorWorldPosition(out Vector3 worldPosition)
        {
            worldPosition = default;
            Camera camera = Camera.main;
            if (statusView == null || camera == null || boardView == null ||
                !statusView.TryGetWrongSolutionScreenPosition(out Vector2 screenPosition) ||
                !boardView.TryGetCellWorldPosition(0, out Vector3 reference))
            {
                return false;
            }

            float depth = camera.WorldToScreenPoint(reference).z;
            worldPosition = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
            return true;
        }

        private void RenderAll(bool animate, bool rebuild = false, bool liveDrag = false)
        {
            if (!session.HasLevel)
            {
                RenderStatus();
                return;
            }

            FillItBoardModel board = session.Board;
            // A live drag writes values straight into the board, so regions are re-read every
            // step to colour the chain as it grows.
            FillItBoardAnalysis analysis = liveDrag || dragChain.IsActive ? FillItRegionAnalyzer.Analyze(board) : session.Analysis;
            int count = board.CellCount;
            if (rebuild || lastRendered.Length != count)
            {
                boardView?.Build(board.Size);
                lastRendered = new FillItCellVisualState[count];
                hasRendered = new bool[count];
            }

            HashSet<int> chain = dragChain.IsActive ? new HashSet<int>(dragChain.Path) : null;
            if (chain != null && dragChain.LockCell >= 0)
            {
                chain.Add(dragChain.LockCell);
            }

            for (int index = 0; index < count; index++)
            {
                FillItCellVisualState state = BuildVisualState(board, analysis, index, chain);
                if (hasRendered[index] && lastRendered[index].Equals(state))
                {
                    continue;
                }

                bool valueChanged = !hasRendered[index] || lastRendered[index].Value != state.Value;
                boardView?.SetCell(index, state, animate && valueChanged);
                lastRendered[index] = state;
                hasRendered[index] = true;
            }

            RenderDragTrail();
            statusView?.ShowRuleResult(SuppressWrongIndication ? null : analysis, analysis.IsSolved);
            RenderStatus();
        }

        private FillItCellVisualState BuildVisualState(FillItBoardModel board, FillItBoardAnalysis analysis, int index, HashSet<int> chain)
        {
            int size = board.Size;
            int region = analysis.RegionOfCell[index];
            int row = index / size;
            int column = index % size;
            bool connectRight = region >= 0 && column < size - 1 && analysis.RegionOfCell[index + 1] == region;
            bool connectDown = region >= 0 && row < size - 1 && analysis.RegionOfCell[index + size] == region;
            FillItRegionStatus status = analysis.GetStatus(index);
            bool showError = !SuppressWrongIndication && (status == FillItRegionStatus.Over || status == FillItRegionStatus.Stuck);
            return new FillItCellVisualState(
                board.GetValue(index),
                board.IsGiven(index),
                board.IsHinted(index),
                status,
                showError,
                index == SelectedIndex && session.IsPlaying,
                chain != null && chain.Contains(index),
                connectRight,
                connectDown);
        }

        private void RenderDragTrail()
        {
            if (boardView == null)
            {
                return;
            }

            if (!dragChain.IsActive || !dragChain.Moved)
            {
                boardView.ClearDragTrail();
                return;
            }

            trailScratch.Clear();
            IReadOnlyList<int> path = dragChain.Path;
            for (int index = 0; index < path.Count; index++)
            {
                if (dragChain.TryGetCameFrom(path[index], out int from) && from >= 0)
                {
                    trailScratch.Add(new Vector2Int(from, path[index]));
                }
            }

            // Connect the given number the chain took its value from, when it's adjacent to the chain end.
            if (dragChain.LockCell >= 0 && path.Count > 0 && path[0] != dragChain.LockCell)
            {
                int last = path[path.Count - 1];
                if (AreNeighbors(last, dragChain.LockCell) && !(dragChain.TryGetCameFrom(path[0], out int first) && first == dragChain.LockCell))
                {
                    trailScratch.Add(new Vector2Int(last, dragChain.LockCell));
                }
            }

            int trailValue = dragChain.LockedValue > 0 ? dragChain.LockedValue : Mathf.Min(path.Count, FillItDragChain.MaxCountLength);
            boardView.SetDragTrail(trailScratch, trailValue);
        }

        private bool AreNeighbors(int a, int b)
        {
            int[] neighbors = session.Board.Neighbors(a);
            for (int index = 0; index < neighbors.Length; index++)
            {
                if (neighbors[index] == b)
                {
                    return true;
                }
            }

            return false;
        }

        // animateHeartLoss must stay false for every load/restore/periodic refresh - the heart
        // break FX only plays for a mistake the player just made (see ResolveMistakes).
        private void RenderStatus(bool animateHeartLoss = false)
        {
            if (!session.HasLevel)
            {
                gameFlowView?.ShowState(ToGameFlowState(session.GameState.Current));
                return;
            }

            statusView?.SetLevel(session.Level.Id, session.Level.Name, session.Level.Difficulty, isFtueLevel: SuppressAppreciationText);
            // Timer mode shows the countdown in place of the hearts.
            statusView?.SetLevelMistakes(session.LevelMistakeLimit.RemainingMistakes, session.LevelMistakeLimit.MaxMistakes, animateHeartLoss, LevelAttemptFeatureEnabled && !session.TimerEnabled);
            statusView?.SetTimer(session.TimerEnabled, session.RemainingSeconds);
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

            // Won is never pushed from here: the win page only appears once the celebration (and
            // leaderboard) beat is over - FillItGameplayManager shows it explicitly. Pushing it on
            // the winning move's own render would swap the gameplay page out mid-celebration.
            if (session.GameState.Current != FillItGameState.Won)
            {
                gameFlowView?.ShowState(ToGameFlowState(session.GameState.Current));
            }
        }

        private void ResetPerLevelState()
        {
            SelectedIndex = -1;
            regionStreak = 0;
            hasFiredHardTier = false;
            pendingLevelWonNotification = false;
            ResetStatusIndicators();
        }

        private void ResetStatusIndicators()
        {
            statusView?.ShowRuleResult(null, false);
            // A fresh/restarted level never starts with a previous hard level's HUD or a
            // previous win's disabled Info button.
            statusView?.DisableHardLevelUI();
            statusView?.EnableInfoButton();
        }

        private int FirstEditableEmptyIndex()
        {
            FillItBoardModel board = session.Board;
            for (int index = 0; index < board.CellCount; index++)
            {
                if (!board.IsGiven(index) && board.IsEmpty(index) && (AllowedCells == null || AllowedCells.Contains(index)))
                {
                    return index;
                }
            }

            return -1;
        }

        private static GameFlowState ToGameFlowState(FillItGameState state)
        {
            switch (state)
            {
                case FillItGameState.MainMenu:
                case FillItGameState.Uninitialized:
                    return GameFlowState.MainMenu;
                case FillItGameState.Won:
                    return GameFlowState.Win;
                case FillItGameState.Failed:
                    return GameFlowState.Fail;
                default:
                    return GameFlowState.Gameplay;
            }
        }

        // The session raises LevelWon synchronously from inside Commit, before the winning move has
        // been painted - deferred so listeners (the win celebration) only run once the final
        // region's pop animation has actually started.
        private void HandleSessionLevelWon(object sender, EventArgs eventArgs)
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

        private void HandleSessionLevelFailed(object sender, EventArgs eventArgs)
        {
            LevelFailed?.Invoke(this, EventArgs.Empty);
        }

        private void HandleSessionOutOfMistakes(object sender, EventArgs eventArgs)
        {
            OutOfMistakes?.Invoke(this, EventArgs.Empty);
        }

        private void HandleSessionTimeUp(object sender, EventArgs eventArgs)
        {
            RenderStatus();
            TimeUp?.Invoke(this, EventArgs.Empty);
        }
    }
}
