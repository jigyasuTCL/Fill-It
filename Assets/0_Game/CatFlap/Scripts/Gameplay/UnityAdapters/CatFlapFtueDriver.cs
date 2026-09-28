using System.Collections;
using System.Collections.Generic;
using CatFlap.Models;
using CatFlap.Runtime;
using CatFlap.Validation;
using Shared.Ftue;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    // Cat Flap-specific FTUE sequencing: picks which board cell to highlight and drives the
    // standalone Shared.Ftue.FtueController to show/hide it. Everything game-specific (which
    // cells are targets, when the highlight moves on, level gating, tap-count rules) lives here
    // or in CatFlapGameplayManager - the shared FtueController itself only knows "highlight this
    // world-space box and show this text, or don't."
    //
    // Deliberately stateless about "how many taps have happened": what to show is recomputed
    // from the board's actual current cell values every time Refresh() runs, rather than tracked
    // with counters incremented once per tap. Counters drift if a tap ever fires the selection
    // event more than once (or gets missed); reading the board directly can't drift - it's
    // always exactly what's really placed. Targets are the level's originally-empty cells (in
    // row-major order), one FtueStepData per target (content authors write one message per
    // tile, not per tap). How many taps a target needs before the highlight moves on is derived
    // from the level's own solution data (a tile whose solution wants a Cat needs 2 taps -
    // Empty -> Fish -> Cat; anything else needs 1), not authored separately - so it can never
    // drift out of sync with what the puzzle actually requires.
    public sealed class CatFlapFtueDriver : MonoBehaviour
    {
        [SerializeField] private FtueController ftueController;
        [SerializeField] private CatFlapSpriteBoardViewAdapter boardView;

        [SerializeField] private float advanceDelaySeconds = 0.5f;

        [Header("Level 4 Rules Walkthrough")]
        // Halved speed (i.e. doubled duration) for the banner's fly-to-rule-chip move.
        [SerializeField] private float rulesBannerMoveDurationSeconds = 0.8f;
        // Fixed shrink target for the banner's fly-to-rule-chip move (see RevealRuleThenAdvance) -
        // always 0.75x by the time it reaches the chip, rather than dynamically fitting the chip's
        // own on-screen size (which varied visibly between rows/chip widths).
        [SerializeField] private float rulesBannerTargetScale = 0.75f;
        // The banner only dims to this alpha by the time it reaches the chip, rather than fading
        // out completely - it stays partly visible sitting on the rule chip instead of vanishing.
        [SerializeField] private float rulesBannerFadeToAlpha = 0.5f;
        // Held at the banner's normal size/position, showing the rule's own explanation text,
        // BEFORE it flies over and fades into that rule's HUD chip (see RevealRuleThenAdvance) -
        // long enough for the player to actually read it before it shrinks away.
        [SerializeField] private float rulesRevealHoldSeconds = 0.6f;
        // Overlay strips/corners are normally black @ 0.72 alpha (see GamePlayUIRoot.prefab) -
        // darker only for this level, via FtueSpotlightController.SetOverlayDimAlpha.
        // 250/255 (0-255 byte scale, matching how alpha is usually specified in the Editor's
        // color picker) - Image.color.a is a 0-1 float under the hood, so expressed as that
        // fraction here.
        [SerializeField] private float rulesWalkthroughOverlayDimAlpha = 250f / 255f;

        private IReadOnlyList<FtueStepData> steps;
        private CatFlapRuntimeSession session;
        private readonly List<(int Row, int Column)> targets = new List<(int, int)>();

        // Index into targets of whichever one is currently displayed (or was, mid-advance-wait) -
        // -1 before the very first Refresh() ever shows anything. Lets Refresh tell "still on the
        // same target, just re-showing its updated step" apart from "this target just finished,
        // time to move on to the next one".
        private int displayedTargetIndex = -1;

        // Non-null while hiding the just-completed target and waiting out advanceDelaySeconds
        // before revealing the next one - see Refresh/AdvanceToTargetAfterDelay. CurrentTarget is
        // null for this whole window, so CatFlapGameplayManager.HandleCellSelected's "only the
        // highlighted cell takes input" gate rejects every tap on every cell until it resolves.
        private Coroutine advanceRoutine;

        // The step reserved by Configure's walkthroughStepCount parameter (the first step AFTER
        // the walkthrough targets, if any) - null for every level that walks through all of its
        // steps, as before. Consumed only by ShowMistakeTip.
        private FtueStepData mistakeStepData;

        // See ConfigureRulesWalkthrough - Level 4's bespoke sequence entirely replaces the
        // generic empty-cell walkthrough above while true. Refresh() branches to
        // RefreshRulesWalkthrough() instead of its normal body whenever this is set.
        private bool rulesWalkthroughActive;
        private CatFlapStatusViewAdapter rulesStatusView;

        private enum RulesWalkthroughPhase
        {
            FirstFishTapping,
            FirstFishRevealing,
            SecondFishTapping,
            SecondFishWaitingForLock,
            SecondFishRevealing,
            LastCellFreeTap,
            LastCellGuidedTap,
            Done
        }

        private RulesWalkthroughPhase rulesPhase;
        private (int Row, int Column) rwFirstTarget;
        private (int Row, int Column) rwSecondTarget;
        private (int Row, int Column) rwThirdTarget;
        private readonly List<(int Row, int Column)> rwFirstHighlight = new List<(int, int)>();
        private readonly List<(int Row, int Column)> rwSecondHighlight = new List<(int, int)>();
        // Kept SEPARATE (row cells, column cells) rather than merged into one list - a single
        // encapsulating Bounds over both would touch all 4 screen edges (a full row already spans
        // the whole width, a full column the whole height) and degenerate into highlighting the
        // WHOLE board. See ShowThirdTargetCrossHighlight/FtueSpotlightController.ShowCross.
        private readonly List<(int Row, int Column)> rwThirdRowHighlight = new List<(int, int)>();
        private readonly List<(int Row, int Column)> rwThirdColumnHighlight = new List<(int, int)>();
        private Coroutine rulesTransitionRoutine;

        public bool IsActive { get; private set; }

        // 1-based "which step is the player currently on" - feeds ftue_level_skip's ftue_id (see
        // CatFlapGameplayManager.SkipFtueTutorial). Levels 1-3's plain walkthrough numbers steps
        // by target index; Level 4's bespoke rules walkthrough numbers them by its own phase
        // instead, since it isn't driven by the targets list at all (see rulesPhase).
        public int CurrentStepNumber
        {
            get
            {
                if (rulesWalkthroughActive)
                {
                    switch (rulesPhase)
                    {
                        case RulesWalkthroughPhase.FirstFishTapping:
                        case RulesWalkthroughPhase.FirstFishRevealing:
                            return 1;
                        case RulesWalkthroughPhase.SecondFishTapping:
                        case RulesWalkthroughPhase.SecondFishWaitingForLock:
                        case RulesWalkthroughPhase.SecondFishRevealing:
                            return 2;
                        case RulesWalkthroughPhase.LastCellFreeTap:
                        case RulesWalkthroughPhase.LastCellGuidedTap:
                            return 3;
                        default:
                            return 4;
                    }
                }

                return displayedTargetIndex >= 0 ? displayedTargetIndex + 1 : 1;
            }
        }

        // The one cell currently highlighted - while IsActive, this is the only cell that should
        // accept tap input at all (see CatFlapGameplayManager.HandleCellSelected). Null once
        // hidden/inactive.
        public (int Row, int Column)? CurrentTarget { get; private set; }

        // One-off UI-targeted tip (e.g. "Beware! Life gets reduced on wrong moves" pointing at
        // the hearts HUD) - unrelated to the board-cell tutorial sequence above, so it
        // deliberately never touches IsActive/CurrentTarget/targets. Dismissed by a tap anywhere
        // on screen (see FtueController.ShowDismissOnTap), not tied to any specific cell.

        // While true, ALL board input is blocked (see CatFlapGameplayManager.HandleCellSelected)
        // regardless of which cell was tapped - unlike the board-cell tutorial above, this tip
        // isn't pointing at a tappable target, so nothing on the board should react to a tap
        // while it's up; the tap should only dismiss the tip.
        public bool IsShowingUiTip { get; private set; }

        public void ShowUiTip(RectTransform targetUIRect, FtueStepData step)
        {
            IsShowingUiTip = true;
            ftueController?.ShowDismissOnTap(targetUIRect, step, () => StartCoroutine(ClearShowingUiTipNextFrame()));
        }

        // The dismiss-catcher's UI click and the board's own independent tap-detection (it isn't
        // routed through Unity's UI event system) can both resolve the same physical tap within
        // the same frame - if IsShowingUiTip cleared synchronously here, the board's handler could
        // end up checking it AFTER it's already false and let that same tap fall through to a
        // tile. Clearing it a frame later guarantees this tap is still blocked, mirroring
        // CatFlapGameplayManager.EnableGameplayInputNextFrame's identical one-frame-late pattern.
        private IEnumerator ClearShowingUiTipNextFrame()
        {
            yield return null;
            IsShowingUiTip = false;
        }

        // walkthroughStepCount: 0 (default) walks through every step in ftueSteps, same as
        // before. A positive value N walks through only the first N steps' cells; step N (if
        // ftueSteps has one) is reserved as the mistake-reaction tip instead of a normal target -
        // see ShowMistakeTip.
        public void Configure(bool active, IReadOnlyList<FtueStepData> ftueSteps, CatFlapRuntimeSession runtimeSession, int walkthroughStepCount = 0, bool enableRulesWalkthrough = false, CatFlapStatusViewAdapter rulesStatusViewAdapter = null)
        {
            if (advanceRoutine != null)
            {
                StopCoroutine(advanceRoutine);
                advanceRoutine = null;
            }

            if (rulesTransitionRoutine != null)
            {
                StopCoroutine(rulesTransitionRoutine);
                rulesTransitionRoutine = null;
            }

            // Undoes ConfigureRulesWalkthrough's own darker override from a previous level (see
            // ftueController.SetOverlayDimAlpha there) - every OTHER FTUE level uses the normal,
            // prefab-authored dim strength, so this must reset on every Configure() call, not
            // just when leaving the rules-walkthrough level specifically.
            ftueController?.ResetOverlayDimAlpha();
            boardView?.SetFtueLastStepOverlayActive(false);

            displayedTargetIndex = -1;
            steps = ftueSteps;
            session = runtimeSession;
            targets.Clear();
            mistakeStepData = null;
            rulesWalkthroughActive = false;
            rulesStatusView = rulesStatusViewAdapter;

            // Scopes the PRECISE per-target hole/cross confinement to just the board area (see
            // FtueSpotlightController.SetBoardClipBounds) - computed once per level here rather
            // than on every Refresh(), since the board's own on-screen position/size doesn't
            // change mid-level. The dim overlay itself still covers the WHOLE screen (see
            // FtueSpotlightController's own outer strips) - this only confines where the
            // highlighted-cell/cross hole is allowed to cut through.
            if (active && ftueController != null && boardView != null && boardView.TryGetBoardWorldBounds(out Bounds boardBounds))
            {
                ftueController.SetBoardClipBounds(boardBounds, boardView.InputCamera);
            }

            if (active && enableRulesWalkthrough && runtimeSession?.Board != null && ftueSteps != null && ftueSteps.Count > 0)
            {
                ConfigureRulesWalkthrough(runtimeSession);
                return;
            }

            int walkthroughCount = ftueSteps != null
                ? (walkthroughStepCount > 0 ? Mathf.Min(walkthroughStepCount, ftueSteps.Count) : ftueSteps.Count)
                : 0;

            // Only as many tiles as walkthroughCount get guided - e.g. level 4 walks through just
            // its first empty cell (row-major); its second empty cell is left alone entirely and
            // plays as a normal tap once the tutorial ends, instead of also getting highlighted.
            if (active && runtimeSession?.Board != null && walkthroughCount > 0)
            {
                FindEmptyCellsRowMajor(runtimeSession, targets, walkthroughCount);
            }

            if (ftueSteps != null && walkthroughCount < ftueSteps.Count)
            {
                mistakeStepData = ftueSteps[walkthroughCount];
            }

            IsActive = active && steps != null && steps.Count > 0 && targets.Count > 0;

            Refresh();
        }

        // Instant teardown - see FtueController.HideImmediate. Called by
        // CatFlapGameplayManager.SkipFtueTutorial right before it starts sliding the board away:
        // the normal Configure(false, ...)/Refresh() path hides via FtueController.Hide(), whose
        // fade-out takes real time and, since the dim overlay is a screen-anchored UI element (not
        // a child of the world-space board), would otherwise visibly hang in its old on-screen
        // position while the board is already sliding out from underneath it.
        public void ForceHideImmediate()
        {
            if (advanceRoutine != null)
            {
                StopCoroutine(advanceRoutine);
                advanceRoutine = null;
            }

            if (rulesTransitionRoutine != null)
            {
                StopCoroutine(rulesTransitionRoutine);
                rulesTransitionRoutine = null;
            }

            rulesWalkthroughActive = false;
            IsActive = false;
            CurrentTarget = null;
            displayedTargetIndex = -1;
            ftueController?.HideImmediate();
            boardView?.SetFtueLastStepOverlayActive(false);
        }

        // Sets up Level 4's bespoke rules-teaching sequence: highlight the whole row while
        // placing the first fish (columns other than the second target), reveal Equal Counts once
        // it lands; highlight the whole row (now including the second target) for the second fish,
        // wait for the row to actually lock (see RefreshRulesWalkthrough), reveal Max Two Adjacent;
        // then a normal, unguided tap on the last empty cell (so the player sees the engine's own
        // red rule-violation feedback first-hand) followed by one guided tap to finish it. Targets
        // are the level's 3 originally-empty cells in row-major order, EXCEPT the first two (both
        // in the same row) are taught in reverse column order - see rwFirstTarget/rwSecondTarget.
        private void ConfigureRulesWalkthrough(CatFlapRuntimeSession runtimeSession)
        {
            var emptyCells = new List<(int, int)>();
            FindEmptyCellsRowMajor(runtimeSession, emptyCells, int.MaxValue);
            if (emptyCells.Count < 3)
            {
                IsActive = false;
                return;
            }

            // Level 4 only - a noticeably darker dim overlay than every other FTUE level, since
            // its rules-walkthrough highlights (a whole row/column, or the row+column cross for
            // the last cell) already cover much more of the screen than a single-cell highlight
            // does; reset in Configure() whenever a different level loads.
            ftueController?.SetOverlayDimAlpha(rulesWalkthroughOverlayDimAlpha);

            rwFirstTarget = emptyCells[1];
            rwSecondTarget = emptyCells[0];
            rwThirdTarget = emptyCells[2];

            rwFirstHighlight.Clear();
            rwSecondHighlight.Clear();
            rwThirdRowHighlight.Clear();
            rwThirdColumnHighlight.Clear();
            int row = rwFirstTarget.Row;
            int columnCount = runtimeSession.Board.ColumnCount;
            int rowCount = runtimeSession.Board.RowCount;
            for (int column = 0; column < columnCount; column++)
            {
                rwSecondHighlight.Add((row, column));
                if (column != rwSecondTarget.Column)
                {
                    rwFirstHighlight.Add((row, column));
                }
            }

            // The last cell's guided fix tap highlights its WHOLE row and column (a cross
            // centered on the cell itself), not just that single cell - kept as two SEPARATE
            // lists (see their own field comment for why) rather than one merged list.
            for (int column = 0; column < columnCount; column++)
            {
                rwThirdRowHighlight.Add((rwThirdTarget.Row, column));
            }

            for (int thirdRow = 0; thirdRow < rowCount; thirdRow++)
            {
                rwThirdColumnHighlight.Add((thirdRow, rwThirdTarget.Column));
            }

            rulesWalkthroughActive = true;
            rulesPhase = RulesWalkthroughPhase.FirstFishTapping;
            IsActive = true;
            ShowRulesWalkthroughStep(rwFirstTarget, rwFirstHighlight, steps[0], isDoubleTap: false);
        }

        // One-off reaction tip shown when the player breaks a rule AFTER the board-cell
        // walkthrough above has already finished (IsActive false) - reuses the same
        // FtueController.Show(...) mechanism as a normal target, but is entirely independent of
        // IsActive/CurrentTarget/targets, and only ever plays the step reserved by Configure's
        // walkthroughStepCount (null on any level that doesn't reserve one, so this silently
        // no-ops there). Dismissed by the player's next tap - see IsShowingMistakeTip/
        // DismissMistakeTip and CatFlapGameplayManager.HandleCellSelected.
        public bool IsShowingMistakeTip { get; private set; }

        public void ShowMistakeTip(int row, int column)
        {
            if (mistakeStepData == null || ftueController == null || boardView == null ||
                !boardView.TryGetCellWorldBounds(row, column, out Bounds bounds))
            {
                return;
            }

            IsShowingMistakeTip = true;
            ftueController.Show(bounds, boardView.InputCamera, mistakeStepData, isDoubleTap: false, showHandPointer: false);
        }

        // Same one-frame-late clearing reasoning as ClearShowingUiTipNextFrame - the board's own
        // tap detection isn't routed through Unity's UI event system, so clearing this
        // synchronously could let the SAME tap that dismissed the tip also register as a normal
        // board move within the same frame.
        public void DismissMistakeTip()
        {
            if (!IsShowingMistakeTip)
            {
                return;
            }

            ftueController?.Hide();
            StartCoroutine(ClearShowingMistakeTipNextFrame());
        }

        private IEnumerator ClearShowingMistakeTipNextFrame()
        {
            yield return null;
            IsShowingMistakeTip = false;
        }

        // Call after any tap that might have changed the board, AND again once that tap's
        // delayed row/column validation resolves (see
        // CatFlapGameplayManager.Validation.ValidateLineAfterDelay) - safe to call as often as
        // needed, harmlessly a no-op when not active. Re-derives the current target/step purely
        // from the board's cell values rather than trusting a tap counter.
        public void Refresh()
        {
            if (rulesWalkthroughActive)
            {
                RefreshRulesWalkthrough();
                return;
            }

            if (!IsActive)
            {
                CurrentTarget = null;
                displayedTargetIndex = -1;
                ftueController?.Hide();
                return;
            }

            // Already paused between two targets, waiting out advanceDelaySeconds (see below) -
            // the coroutine itself calls back into Refresh() once that wait elapses, so a
            // redundant call landing mid-wait (e.g. both the tap handler and the delayed-
            // validation callback firing close together) must not re-enter this logic.
            if (advanceRoutine != null)
            {
                return;
            }

            // If a target is currently "owned" (shown, or already hidden while waiting for its
            // lock/row-column animation), re-check ITS status first, before considering any
            // other target - this is what lets the highlight/input-block react to the exact
            // moment its correct value lands, rather than only noticing on whichever LATER
            // Refresh() call happens to move on to a different target index.
            if (displayedTargetIndex >= 0 && displayedTargetIndex < targets.Count)
            {
                (int ownedRow, int ownedColumn) = targets[displayedTargetIndex];
                CatFlapCellModel ownedCell = session.Board.GetCell(ownedRow, ownedColumn);
                int ownedRequiredTaps = GetRequiredTaps(ownedRow, ownedColumn);
                int ownedTapsConsumed = CountTapsConsumed(ownedCell.Value);
                bool ownedValueReached = ownedTapsConsumed >= ownedRequiredTaps;
                // Always waits for the real lock - including the very last target - so a correct
                // tap gets the exact same validation delay (and matching line-complete animation/
                // sound) a normal, non-FTUE level's last tap gets. This used to skip the wait for
                // the last target ("nothing left to reveal after it"), which hid the FTUE overlay
                // and ended the tutorial instantly on tap, before the real correct-check delay had
                // even elapsed.
                bool ownedTrulyComplete = ownedValueReached && ownedCell.IsLocked;

                if (ownedTrulyComplete)
                {
                    // Truly done (locked). Hide (a no-op if the step below already did) and pause
                    // for advanceDelaySeconds - giving this line's own row/column complete
                    // animation room to actually finish - before deciding what comes next (another
                    // target, or the tutorial ending).
                    ftueController?.Hide();
                    CurrentTarget = null;
                    displayedTargetIndex = -1;
                    advanceRoutine = StartCoroutine(AdvanceAfterDelay());
                    return;
                }

                if (ownedValueReached)
                {
                    // Correct value just landed but hasn't locked yet (row/column validation
                    // still pending) - hide the highlight and block ALL board input immediately,
                    // rather than waiting for the lock to do so. Only actually touches anything
                    // the FIRST time this is seen (CurrentTarget already null on every later
                    // redundant Refresh() call while still waiting).
                    if (CurrentTarget.HasValue)
                    {
                        ftueController?.Hide();
                        CurrentTarget = null;
                    }

                    return;
                }

                // Still needs more taps - fall through to the loop below, which re-shows this
                // same target with its updated step/hand state.
            }

            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                (int row, int column) = targets[targetIndex];
                CatFlapCellModel cell = session.Board.GetCell(row, column);
                int requiredTaps = GetRequiredTaps(row, column);
                int tapsConsumed = CountTapsConsumed(cell.Value);
                // Always requires the real lock, including the last target - see the "owned"
                // branch above for why.
                bool trulyComplete = tapsConsumed >= requiredTaps && cell.IsLocked;

                if (trulyComplete)
                {
                    continue;
                }

                ShowTargetStep(targetIndex, row, column, cell, requiredTaps, tapsConsumed);
                return;
            }

            // Every target has reached its solution value - the tutorial for this level is done.
            IsActive = false;
            CurrentTarget = null;
            displayedTargetIndex = -1;
            ftueController?.Hide();
        }

        private IEnumerator AdvanceAfterDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, advanceDelaySeconds));
            advanceRoutine = null;
            Refresh();
        }

        // Level 4's bespoke sequence - see ConfigureRulesWalkthrough for the overall shape.
        // Re-derives progress from the board's actual cell values every call, same "never trust a
        // counter" philosophy as the generic Refresh() above.
        private void RefreshRulesWalkthrough()
        {
            // Already mid-transition (banner moving onto a rule, or waiting out the reveal hold) -
            // the coroutine itself calls back into the next phase once it finishes, so a redundant
            // call landing mid-transition must not re-enter this logic.
            if (rulesTransitionRoutine != null)
            {
                return;
            }

            switch (rulesPhase)
            {
                case RulesWalkthroughPhase.FirstFishTapping:
                {
                    CatFlapCellModel cell = session.Board.GetCell(rwFirstTarget.Row, rwFirstTarget.Column);
                    if (CountTapsConsumed(cell.Value) < GetRequiredTaps(rwFirstTarget.Row, rwFirstTarget.Column))
                    {
                        break;
                    }

                    // CurrentTarget = null immediately disables touch on this cell (see
                    // CatFlapGameplayManager.HandleCellSelected's CurrentTarget gate) - happens
                    // right away, before anything else below even starts.
                    CurrentTarget = null;
                    ftueController?.HideSpotlightOnly();
                    rulesPhase = RulesWalkthroughPhase.FirstFishRevealing;
                    // Step 1 reveals Max Two Adjacent (rule 2) - but keeps showing steps[0] text,
                    // same as the tap-guidance banner already up for this step (see
                    // ConfigureRulesWalkthrough's ShowRulesWalkthroughStep call) - the banner must
                    // NOT visibly change text right before it flies to the rule chip, only move.
                    rulesTransitionRoutine = StartCoroutine(PlayRowCompletePulseThenRevealRule(rwFirstTarget.Row, CatFlapRule.MaxTwoAdjacent, steps[0], ShowSecondFishStep));
                    break;
                }

                case RulesWalkthroughPhase.SecondFishTapping:
                {
                    CatFlapCellModel cell = session.Board.GetCell(rwSecondTarget.Row, rwSecondTarget.Column);
                    if (CountTapsConsumed(cell.Value) < GetRequiredTaps(rwSecondTarget.Row, rwSecondTarget.Column))
                    {
                        break;
                    }

                    // Correct value landed but the row hasn't locked/validated yet - hide the
                    // highlight and block input immediately, same as the generic driver's
                    // ownedValueReached branch, then wait for the row's own natural
                    // complete-line animation (see SecondFishWaitingForLock) before moving on.
                    if (CurrentTarget.HasValue)
                    {
                        ftueController?.HideSpotlightOnly();
                        CurrentTarget = null;
                    }

                    rulesPhase = RulesWalkthroughPhase.SecondFishWaitingForLock;
                    break;
                }

                case RulesWalkthroughPhase.SecondFishWaitingForLock:
                {
                    CatFlapCellModel cell = session.Board.GetCell(rwSecondTarget.Row, rwSecondTarget.Column);
                    if (!cell.IsLocked)
                    {
                        break;
                    }

                    rulesPhase = RulesWalkthroughPhase.SecondFishRevealing;
                    // Step 2 reveals Equal Counts (rule 1) - keeps showing steps[1] text, same as
                    // ShowSecondFishStep's own tap-guidance banner, so it doesn't visibly change
                    // right before flying to the rule chip.
                    rulesTransitionRoutine = StartCoroutine(RevealRuleThenAdvance(CatFlapRule.EqualCounts, steps[Mathf.Min(1, steps.Count - 1)], ShowLastCellFreeTapStep));
                    break;
                }

                case RulesWalkthroughPhase.LastCellFreeTap:
                {
                    // The cross highlight/hand pointer are already showing (see
                    // ShowLastCellFreeTapStep), but no banner yet - the player free-taps the last
                    // empty cell and sees the engine's own red rule-violation feedback untouched,
                    // with no text telling them what's about to happen. Deliberately does NOT
                    // advance here just because a value landed (Refresh() runs synchronously
                    // right after the tap,
                    // long before CatFlapGameplayManager.Validation's delayed row/column check -
                    // and its rule-break blink - has even started) - advancing this early used to
                    // pop the guided-tap banner in instantly, on top of the still-pending/blinking
                    // violation. See NotifyLineOutcomeResolved, called once that check has
                    // actually finished (blink included), for what really drives the transition.
                    break;
                }

                case RulesWalkthroughPhase.LastCellGuidedTap:
                {
                    CatFlapCellModel cell = session.Board.GetCell(rwThirdTarget.Row, rwThirdTarget.Column);
                    if (CountTapsConsumed(cell.Value) < GetRequiredTaps(rwThirdTarget.Row, rwThirdTarget.Column))
                    {
                        break;
                    }

                    // Tutorial is fully done - the normal engine takes it from here (board
                    // complete -> level win, see CatFlapGameplayManager).
                    rulesPhase = RulesWalkthroughPhase.Done;
                    rulesWalkthroughActive = false;
                    IsActive = false;
                    CurrentTarget = null;
                    ftueController?.Hide();
                    boardView?.SetFtueLastStepOverlayActive(false);
                    break;
                }
            }
        }

        // Called by CatFlapGameplayManager.Validation once a tap's row/column outcome has fully
        // resolved - Complete or Wrong Solution immediately (ruleBroken: false), or Rule Break
        // only after its blink (see CatFlapSpriteBoardViewAdapter.IsAnyRuleBreakBlinking) has
        // actually finished playing (ruleBroken: true) - so this is the ONLY thing that advances
        // LastCellFreeTap; see that phase's own comment in RefreshRulesWalkthrough for why
        // advancing on the tap alone (via the generic Refresh() call right after SelectCell) was
        // wrong. The guided-fix banner (steps[0], pointing back at this same cell) is reserved
        // for a GENUINE rule break only - two fast taps that land on the correct value before any
        // violation is ever confirmed must finish (or keep silently waiting for more taps) with no
        // FTUE banner at all, exactly like an ordinary tap would.
        public void NotifyLineOutcomeResolved(int row, int column, bool ruleBroken)
        {
            if (!rulesWalkthroughActive || rulesPhase != RulesWalkthroughPhase.LastCellFreeTap)
            {
                return;
            }

            if (row != rwThirdTarget.Row && column != rwThirdTarget.Column)
            {
                return;
            }

            CatFlapCellModel cell = session.Board.GetCell(rwThirdTarget.Row, rwThirdTarget.Column);
            if (CountTapsConsumed(cell.Value) < 1)
            {
                return;
            }

            if (!ruleBroken)
            {
                // Resolved clean (Complete or Wrong Solution, but no rule violated). If the
                // cell's own full solution value hasn't landed yet (e.g. a Wrong Solution
                // read on the Fish-only halfway state of a Cat target), just keep waiting
                // silently for the remaining tap(s) - no banner, no phase change. Once it
                // truly has, the walkthrough is simply done - nothing to guide the player
                // back to fix.
                if (CountTapsConsumed(cell.Value) < GetRequiredTaps(rwThirdTarget.Row, rwThirdTarget.Column))
                {
                    return;
                }

                rulesPhase = RulesWalkthroughPhase.Done;
                rulesWalkthroughActive = false;
                IsActive = false;
                CurrentTarget = null;
                ftueController?.Hide();
                boardView?.SetFtueLastStepOverlayActive(false);
                return;
            }

            rulesPhase = RulesWalkthroughPhase.LastCellGuidedTap;
            // Spotlight/hand are already showing from ShowLastCellFreeTapStep - only the banner
            // is new here, revealed now that the player has actually broken the rule.
            ftueController?.ShowBanner(steps[0]);
        }

        private void ShowSecondFishStep()
        {
            rulesPhase = RulesWalkthroughPhase.SecondFishTapping;
            // steps[1] ("every row/column should have equal...") - matches the text
            // SecondFishWaitingForLock's own reveal shows below, so the banner never visibly
            // changes text mid-step, only moves.
            ShowRulesWalkthroughStep(rwSecondTarget, rwSecondHighlight, steps[Mathf.Min(1, steps.Count - 1)], isDoubleTap: false);
        }

        private void ShowLastCellFreeTapStep()
        {
            rulesPhase = RulesWalkthroughPhase.LastCellFreeTap;
            // Only this cell takes input (see CatFlapGameplayManager.HandleCellSelected's
            // CurrentTarget gate). The cross highlight + hand pointer show right away for this
            // free tap, same as they will for the guided retry - but the banner stays hidden
            // until the player actually breaks the rule (see NotifyLineOutcomeResolved), so a
            // clean tap that resolves the board on the first try never flashes text it doesn't
            // need.
            ShowThirdTargetCrossHighlight(showBanner: false);
        }

        // Waits out the just-placed first fish's own tap-driven entry animation (tile flip +
        // idle-settle) before playing the whole-row completion pulse, then hands off to
        // RevealRuleThenAdvance as before. CatFlapSpriteBoardViewAdapter.PlayRowCompletePulse
        // reads each cell's CURRENT scale as its own pulse's starting point - firing it
        // immediately, while the just-tapped cell's own flip/settle animation is still mid-flight,
        // read a mid-squash/mid-settle scale instead of its true resting one, producing a visibly
        // jerky pulse instead of a clean scale-up-and-settle.
        private IEnumerator PlayRowCompletePulseThenRevealRule(int row, CatFlapRule rule, FtueStepData ruleExplanationStep, System.Action nextStep)
        {
            if (boardView != null)
            {
                while (boardView.IsAnyEntryAnimationPlaying())
                {
                    yield return null;
                }
            }

            boardView?.PlayRowCompletePulse(row);
            yield return RevealRuleThenAdvance(rule, ruleExplanationStep, nextStep);
        }

        // Switches the popup's text to that rule's own explanation and holds it there (at its
        // normal size/position, fully readable) for rulesRevealHoldSeconds, then flies it onto
        // ruleRow's own on-screen chip - shrinking to rulesBannerTargetScale (fixed, not fit to
        // the chip's own size) and fading out along the way (see
        // FtueBannerController.MoveFadeAndFitTo), continuing to scale all the way until it
        // actually reaches the chip, so it visually "delivers" itself into the rule rather than
        // sitting on top of it - reveals the rule the instant it arrives, then resets the (now
        // invisible) popup back to its normal position/scale/alpha and hands off to nextStep.
        private IEnumerator RevealRuleThenAdvance(CatFlapRule rule, FtueStepData ruleExplanationStep, System.Action nextStep)
        {
            ftueController?.UpdateBannerText(ruleExplanationStep);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, rulesRevealHoldSeconds));

            RectTransform ruleRect = rulesStatusView != null ? rulesStatusView.GetRuleRowRect(rule) : null;
            if (ftueController != null && ruleRect != null)
            {
                bool arrived = false;
                ftueController.MoveBannerToTarget(ruleRect, rulesBannerMoveDurationSeconds, () => arrived = true, rulesBannerTargetScale, rulesBannerFadeToAlpha);
                while (!arrived)
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning($"CatFlapFtueDriver: rules walkthrough couldn't find a HUD rect for {rule} - " +
                    $"is rulesStatusView null ({rulesStatusView == null}) or is its {rule} rule row RectTransform unassigned in the Inspector? " +
                    "Skipping the popup move/fade, revealing the rule in place instead.");
            }

            rulesStatusView?.SetRuleRowVisible(rule, true);

            // The banner already faded to invisible on arrival (or never moved at all, if
            // ruleRect was missing above) - reset its position/scale/alpha instantly here rather
            // than animating, since there's nothing left on screen to animate. The next step (if
            // any) re-shows it fresh via ftueController.Show(), which replays the normal open pop
            // since Hide() below clears isShowing/currentDescription.
            ftueController?.ResetBannerPositionAndScale();
            ftueController?.Hide();

            rulesTransitionRoutine = null;
            nextStep?.Invoke();
        }

        private void ShowRulesWalkthroughStep((int Row, int Column) target, List<(int Row, int Column)> highlightCells, FtueStepData step, bool isDoubleTap)
        {
            CurrentTarget = target;
            if (ftueController == null || boardView == null)
            {
                return;
            }

            if (!TryEncapsulateCellBounds(highlightCells, out Bounds highlightBounds) ||
                !boardView.TryGetCellWorldBounds(target.Row, target.Column, out Bounds handBounds))
            {
                return;
            }

            ftueController.Show(highlightBounds, boardView.InputCamera, step, isDoubleTap, showHandPointer: true, handWorldBounds: handBounds);
        }

        // LastCellGuidedTap's own highlight - the target's whole row AND whole column (a cross),
        // not just that one cell. Deliberately does NOT go through ShowRulesWalkthroughStep/
        // TryEncapsulateCellBounds's single-Bounds path - encapsulating a full row's cells and a
        // full column's cells together into ONE Bounds would touch all 4 screen edges (the row
        // already spans the whole width, the column the whole height) and degenerate into
        // highlighting the entire board. FtueSpotlightController.ShowCross instead dims only the
        // 4 corners OUTSIDE the row/column union, so the two bounds must stay separate all the
        // way through.
        // showBanner = false for the initial free-tap reveal (highlight/hand only, no text box
        // yet - see ShowLastCellFreeTapStep); true once the player actually breaks the rule (see
        // NotifyLineOutcomeResolved, which instead just calls ftueController.ShowBanner directly
        // rather than re-entering here, since the spotlight/hand are already showing by then).
        private void ShowThirdTargetCrossHighlight(bool showBanner = true)
        {
            CurrentTarget = rwThirdTarget;
            if (ftueController == null || boardView == null)
            {
                return;
            }

            if (!TryEncapsulateCellBounds(rwThirdRowHighlight, out Bounds rowBounds) ||
                !TryEncapsulateCellBounds(rwThirdColumnHighlight, out Bounds columnBounds) ||
                !boardView.TryGetCellWorldBounds(rwThirdTarget.Row, rwThirdTarget.Column, out Bounds handBounds))
            {
                return;
            }

            // Last step: previously showed the scene's own world-space "Overlay" sprite (see
            // CatFlapSpriteBoardViewAdapter.SetFtueLastStepOverlayActive) in place of the usual
            // dim strips/cross-highlight INSIDE the board - that sprite overlay and the UI dim
            // overlay outside the board were two independently-positioned visuals that could go
            // out of alignment at some resolutions. Now uses only the UI-based cross-highlight
            // (showSpotlight: true) for both inside and outside the board, so there's a single
            // overlay system driving the whole highlight. The hand pointer and (once revealed)
            // banner keep working exactly as before. The Rules panel still reads on top of
            // everything (see RulesPanelRoot's own overriding Canvas sorting order).
            boardView.SetFtueLastStepOverlayActive(false);
            ftueController.ShowCross(rowBounds, columnBounds, boardView.InputCamera, steps[0], isDoubleTap: false, showHandPointer: true, handWorldBounds: handBounds, showBanner: showBanner, showSpotlight: true);
        }

        // Combines every listed cell's own world bounds into one - used to dim/highlight a whole
        // group of cells (e.g. a row) as a single rectangular window, per FtueSpotlightController's
        // one-hole-per-Show design, while the hand pointer still points at just one cell within it
        // (see ShowRulesWalkthroughStep's separate handWorldBounds).
        private bool TryEncapsulateCellBounds(List<(int Row, int Column)> cells, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach ((int row, int column) in cells)
            {
                if (!boardView.TryGetCellWorldBounds(row, column, out Bounds cellBounds))
                {
                    continue;
                }

                if (!any)
                {
                    bounds = cellBounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(cellBounds);
                }
            }

            return any;
        }

        // A multi-tap target (e.g. Empty -> Fish -> Cat) cycles through its own "before"/"after
        // first tap" steps by tap PROGRESS, resetting for every such target (level 3's two Cat
        // tiles each replay the same 2-step text) - a single-tap target instead picks its step by
        // which TILE it is, since each single-tap tile only ever shows one state and needs its
        // own distinct text (level 2's two Fish tiles each have their own line).
        private void ShowTargetStep(int targetIndex, int row, int column, CatFlapCellModel cell, int requiredTaps, int tapsConsumed)
        {
            displayedTargetIndex = targetIndex;
            CurrentTarget = (row, column);
            int stepIndex = requiredTaps > 1
                ? Mathf.Min(tapsConsumed, steps.Count - 1)
                : Mathf.Min(targetIndex, steps.Count - 1);
            FtueStepData step = steps[stepIndex];
            // How many MORE taps this target needs from here, not its total requirement - a Cat
            // target shows the double-tap animation while still empty, then switches to
            // single-tap once the Fish placeholder is down and only the final tap remains.
            bool isDoubleTap = requiredTaps - tapsConsumed >= 2;
            // The hand disappears the instant a tap is registered (tapsConsumed reaches
            // requiredTaps) - it doesn't linger while this target is only waiting to lock/turn
            // green. It only reappears once the NEXT target's step actually starts.
            bool showHandPointer = tapsConsumed < requiredTaps;
            if (ftueController != null && boardView != null && boardView.TryGetCellWorldBounds(row, column, out Bounds bounds))
            {
                ftueController.Show(bounds, boardView.InputCamera, step, isDoubleTap, showHandPointer);
            }
        }

        // 1 tap (Empty -> Fish) unless the level's solution actually wants a Cat here, in which
        // case it takes 2 (Empty -> Fish -> Cat).
        private int GetRequiredTaps(int row, int column)
        {
            var solutionRows = session.Level?.Solution;
            int? solutionValue = solutionRows != null && row < solutionRows.Length ? solutionRows[row].Cells[column] : null;
            return solutionValue == (int)CatFlapCellValue.Cat ? 2 : 1;
        }

        // How many taps (from Empty) it takes to reach this value, given the board's own
        // Empty -> Fish -> Cat -> Empty cycle (CatFlapRuntimeSession.NextValue).
        private static int CountTapsConsumed(CatFlapCellValue value)
        {
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return 1;
                case CatFlapCellValue.Cat:
                    return 2;
                default:
                    return 0;
            }
        }

        private static void FindEmptyCellsRowMajor(CatFlapRuntimeSession session, List<(int, int)> result, int maxCount)
        {
            var board = session.Board;
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    if (result.Count >= maxCount)
                    {
                        return;
                    }

                    if (board.GetCell(row, column).Value == CatFlapCellValue.Empty)
                    {
                        result.Add((row, column));
                    }
                }
            }
        }
    }
}
