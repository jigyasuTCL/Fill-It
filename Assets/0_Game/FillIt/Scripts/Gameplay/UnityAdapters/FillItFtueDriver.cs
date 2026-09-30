using System.Collections;
using System.Collections.Generic;
using FillIt.Runtime;
using Shared.Ftue;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Fill It's FTUE sequencing on top of the generic Shared.Ftue module: for each guided move it
    /// spotlights the move's cells, shows the step text, draws a ghost path of the drag, and
    /// restricts input to those cells (FillItRuntimeController.AllowedCells). A move is done once
    /// every one of its empty cells holds its solution number; the next one then follows.
    /// </summary>
    public sealed class FillItFtueDriver : MonoBehaviour
    {
        [SerializeField] private FtueController ftueController;
        [SerializeField] private FillItBoardView boardView;
        [Tooltip("Pause between a guided move being completed and the next one appearing.")]
        [SerializeField] private float advanceDelaySeconds = 0.5f;

        private readonly List<FtueStepData> steps = new List<FtueStepData>();
        private readonly List<FillItFtueMove> moves = new List<FillItFtueMove>();
        private FillItRuntimeController controller;
        private Coroutine advanceRoutine;
        private int currentMove = -1;

        public bool IsActive { get; private set; }
        public bool IsShowingUiTip { get; private set; }

        /// <summary>1-based step the player is on (analytics: ftue_id).</summary>
        public int CurrentStepNumber => Mathf.Max(1, currentMove + 1);

        /// <summary>The cells the current guided move lets the player fill, or null when free play.</summary>
        public ISet<int> CurrentTargets { get; private set; }

        public void ShowUiTip(RectTransform targetUIRect, FtueStepData step)
        {
            IsShowingUiTip = true;
            ftueController?.ShowDismissOnTap(targetUIRect, step, () => StartCoroutine(ClearShowingUiTipNextFrame()));
        }

        // The dismiss catcher's click and the board's own pointer handling can resolve the same
        // physical tap in the same frame - clearing a frame later keeps that tap blocked.
        private IEnumerator ClearShowingUiTipNextFrame()
        {
            yield return null;
            IsShowingUiTip = false;
        }

        public void Configure(bool active, IReadOnlyList<FtueStepData> ftueSteps, IReadOnlyList<FillItFtueMove> ftueMoves, FillItRuntimeController runtimeController)
        {
            StopAdvance();
            controller = runtimeController;
            steps.Clear();
            moves.Clear();
            currentMove = -1;

            if (ftueSteps != null)
            {
                steps.AddRange(ftueSteps);
            }

            if (ftueMoves != null)
            {
                for (int index = 0; index < ftueMoves.Count; index++)
                {
                    if (ftueMoves[index] != null && ftueMoves[index].Path != null && ftueMoves[index].Path.Length > 0)
                    {
                        moves.Add(ftueMoves[index]);
                    }
                }
            }

            IsActive = active && controller != null && controller.Session.HasLevel && moves.Count > 0;
            if (!IsActive)
            {
                EndTutorial();
                return;
            }

            if (boardView != null && boardView.TryGetBoardWorldBounds(out Bounds boardBounds))
            {
                ftueController?.SetBoardClipBounds(boardBounds, boardView.InputCamera);
            }

            ShowMove(FirstUnfinishedMove(0));
        }

        /// <summary>Call after every committed board edit - advances once the current move is done.</summary>
        public void Refresh()
        {
            if (!IsActive || currentMove < 0 || advanceRoutine != null)
            {
                return;
            }

            if (IsMoveComplete(currentMove))
            {
                ftueController?.HideSpotlightOnly();
                boardView?.ClearGuidePath();
                advanceRoutine = StartCoroutine(AdvanceAfterDelay());
            }
        }

        /// <summary>Instant hide (Skip Tutorial) - no fade that would hang over a sliding board.</summary>
        public void ForceHideImmediate()
        {
            StopAdvance();
            ftueController?.HideImmediate();
            boardView?.ClearGuidePath();
        }

        private IEnumerator AdvanceAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, advanceDelaySeconds));
            advanceRoutine = null;
            ShowMove(FirstUnfinishedMove(currentMove + 1));
        }

        private void ShowMove(int moveIndex)
        {
            if (moveIndex < 0 || moveIndex >= moves.Count)
            {
                EndTutorial();
                return;
            }

            currentMove = moveIndex;
            int[] path = moves[moveIndex].Path;
            var targets = new HashSet<int>();
            FillItRuntimeSession session = controller.Session;
            for (int index = 0; index < path.Length; index++)
            {
                if (session.Board.IsValidIndex(path[index]) && !session.Board.IsGiven(path[index]))
                {
                    targets.Add(path[index]);
                }
            }

            CurrentTargets = targets;
            controller.AllowedCells = targets;

            FtueStepData step = moveIndex < steps.Count ? steps[moveIndex] : new FtueStepData();
            if (boardView != null && boardView.TryGetCellsWorldBounds(path, out Bounds highlight))
            {
                Bounds? handBounds = boardView.TryGetCellWorldBounds(path[0], out Bounds first) ? first : (Bounds?)null;
                ftueController?.Show(highlight, boardView.InputCamera, step, isDoubleTap: false, showHandPointer: true, handWorldBounds: handBounds);
            }
            else
            {
                ftueController?.ShowBanner(step);
            }

            boardView?.ShowGuidePath(path);
        }

        private int FirstUnfinishedMove(int from)
        {
            for (int index = Mathf.Max(0, from); index < moves.Count; index++)
            {
                if (!IsMoveComplete(index))
                {
                    return index;
                }
            }

            return -1;
        }

        private bool IsMoveComplete(int moveIndex)
        {
            FillItRuntimeSession session = controller?.Session;
            if (session == null || !session.HasLevel)
            {
                return true;
            }

            int[] path = moves[moveIndex].Path;
            for (int index = 0; index < path.Length; index++)
            {
                int cell = path[index];
                if (!session.Board.IsValidIndex(cell) || session.Board.IsGiven(cell))
                {
                    continue;
                }

                if (session.Board.GetValue(cell) != session.Level.GetSolutionValue(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private void EndTutorial()
        {
            bool wasActive = IsActive;
            IsActive = false;
            currentMove = -1;
            CurrentTargets = null;
            if (controller != null)
            {
                controller.AllowedCells = null;
            }

            boardView?.ClearGuidePath();
            if (wasActive || (ftueController != null && ftueController.IsShowing && !IsShowingUiTip))
            {
                ftueController?.Hide();
            }
        }

        private void StopAdvance()
        {
            if (advanceRoutine != null)
            {
                StopCoroutine(advanceRoutine);
                advanceRoutine = null;
            }
        }
    }
}
