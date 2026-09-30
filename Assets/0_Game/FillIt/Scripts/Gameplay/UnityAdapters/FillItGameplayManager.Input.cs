using FillIt.Runtime;
using Shared.Analytics;
using Shared.Boosters;
using Shared.Vibration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FillIt.UnityAdapters
{
    public partial class FillItGameplayManager
    {
        private bool pressStartedOnGivenCell;
        private int pressStartIndex = -1;

        private void BindBoardInput()
        {
            if (boardView != null)
            {
                boardView.CellPressed = HandleCellPressed;
                boardView.CellDragEntered += HandleCellDragEntered;
                boardView.CellReleased += HandleCellReleased;
            }

            if (numberPad != null)
            {
                numberPad.NumberPressed += HandleNumberPadPressed;
            }
        }

        private void UnbindBoardInput()
        {
            if (boardView != null)
            {
                boardView.CellPressed = null;
                boardView.CellDragEntered -= HandleCellDragEntered;
                boardView.CellReleased -= HandleCellReleased;
            }

            if (numberPad != null)
            {
                numberPad.NumberPressed -= HandleNumberPadPressed;
            }
        }

        // Returns true to accept the press as the start of a drag/tap gesture.
        private bool HandleCellPressed(int index)
        {
            // Any board touch postpones the Hint idle-attention highlight.
            hintHighlightAnimator?.ResetTimer();

            // The one-time hearts tip isn't pointing at a cell - a tap only dismisses it.
            if (ftueDriver != null && ftueDriver.IsShowingUiTip)
            {
                return false;
            }

            if (!Shared.GameFlow.InputManager.GamePlayInputEnabled)
            {
                if (controller != null && controller.Session.HasLevel &&
                    (controller.Session.IsOutOfTime || (!controller.Session.TimerEnabled && controller.Session.LevelMistakeLimit.IsOutOfMistakes)))
                {
                    PlaySound(global::SoundName.ActionBlocked);
                }

                return false;
            }

            if (controller == null || !controller.Session.IsPlaying)
            {
                return false;
            }

            // While a hint is showing, only the hinted cell takes input - tapping it commits the hint.
            if (controller.ActiveHint.HasValue)
            {
                if (controller.ActiveHint.Value.Index == index)
                {
                    ApplyActiveHint();
                }

                return false;
            }

            if (!controller.BeginDrag(index))
            {
                // FTUE: a cell outside the guided move.
                PlaySound(global::SoundName.ActionBlocked);
                return false;
            }

            pressStartIndex = index;
            pressStartedOnGivenCell = controller.IsGivenCell(index);
            PlaySound(global::SoundName.CellTap);
            return true;
        }

        private void HandleCellDragEntered(int index)
        {
            if (controller == null || !controller.IsDragging)
            {
                return;
            }

            if (controller.DragEnter(index))
            {
                VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Light);
                PlaySound(global::SoundName.TapFish);
            }
        }

        private void HandleCellReleased()
        {
            if (controller == null || !controller.IsDragging)
            {
                return;
            }

            bool wasGivenTap = pressStartedOnGivenCell;
            int startIndex = pressStartIndex;
            pressStartedOnGivenCell = false;
            pressStartIndex = -1;
            FillItMoveResult result = controller.EndDrag();
            if (!result.Changed && wasGivenTap)
            {
                // A plain tap on a given number - it can't be changed.
                attemptPrePlacedTileTap++;
                PlaySound(global::SoundName.CellLockedTap);
                VibrationManager.Instance.PlayVibration(global::NotificationFeedbackStyle.Error);
                boardView?.PlayLockedTapShake(startIndex);
                return;
            }

            HandleMoveResult(result);
        }

        private void HandleNumberPadPressed(int value)
        {
            if (!Shared.GameFlow.InputManager.GamePlayInputEnabled || controller == null || !controller.Session.IsPlaying || controller.IsDragging)
            {
                return;
            }

            hintHighlightAnimator?.ResetTimer();
            if (controller.ActiveHint.HasValue)
            {
                return;
            }

            if (controller.IsGivenCell(controller.SelectedIndex))
            {
                attemptPrePlacedTileTap++;
                PlaySound(global::SoundName.CellLockedTap);
                boardView?.PlayLockedTapShake(controller.SelectedIndex);
                return;
            }

            HandleMoveResult(controller.InputNumber(value));
        }

        // Editor/desktop convenience: 1-9 type into the selected cell, 0/Backspace/Delete erase,
        // arrows move the selection, Ctrl/Cmd+Z undoes.
        private void TickKeyboardInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || controller == null || !controller.Session.IsPlaying || !Shared.GameFlow.InputManager.GamePlayInputEnabled)
            {
                return;
            }

            for (int digit = 1; digit <= 9; digit++)
            {
                if (keyboard[Key.Digit1 + (digit - 1)].wasPressedThisFrame || keyboard[Key.Numpad1 + (digit - 1)].wasPressedThisFrame)
                {
                    HandleNumberPadPressed(digit);
                    return;
                }
            }

            if (keyboard.digit0Key.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame)
            {
                HandleNumberPadPressed(0);
                return;
            }

            if (keyboard.zKey.wasPressedThisFrame && (keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed))
            {
                UseUndo();
                return;
            }

            int size = controller.Session.Board.Size;
            int selected = controller.SelectedIndex;
            int row = selected >= 0 ? selected / size : 0;
            int column = selected >= 0 ? selected % size : 0;
            bool moved = true;
            if (keyboard.upArrowKey.wasPressedThisFrame) row = Mathf.Max(0, row - 1);
            else if (keyboard.downArrowKey.wasPressedThisFrame) row = Mathf.Min(size - 1, row + 1);
            else if (keyboard.leftArrowKey.wasPressedThisFrame) column = Mathf.Max(0, column - 1);
            else if (keyboard.rightArrowKey.wasPressedThisFrame) column = Mathf.Min(size - 1, column + 1);
            else moved = false;

            if (moved)
            {
                controller.SelectCell(row * size + column);
            }
        }

        /// <summary>
        /// Shared feedback for every committed move: placement/clear sounds, region-complete cues,
        /// schedules the life charge for a newly broken region, advances FTUE, saves.
        /// </summary>
        private void HandleMoveResult(FillItMoveResult result)
        {
            if (result == null || !result.Changed)
            {
                return;
            }

            ftueDriver?.Refresh();

            if (!result.SolvedBoard)
            {
                if (result.NewlyCompletedRegions.Count > 0)
                {
                    VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
                    PlaySound(result.NewlyCompletedRegions.Count >= 2 ? global::SoundName.LineCompleteDouble : global::SoundName.LineComplete);
                }
                else if (result.Kind == FillItMoveKind.TapClear || (result.Kind == FillItMoveKind.NumberPad && result.Changes[0].After == 0))
                {
                    VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Light);
                    PlaySound(global::SoundName.TapEmpty);
                }
                else if (result.Kind != FillItMoveKind.Drag && result.Kind != FillItMoveKind.Undo)
                {
                    VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Light);
                    PlaySound(global::SoundName.TapCat);
                }

                // Hint/Fix/Undo results are ignored inside - only the player's own wrong numbers count.
                if (ScheduleMistakeCheck(result))
                {
                    PlaySound(global::SoundName.MoveErrorSoft);
                }
            }

            SaveAll();
            sharedBoosterManager?.RefreshAllBoosterUI();
        }

        private bool IsFtueLevelSession()
        {
            return !isDailyChallengeSession && IsFtueLevel(CurrentLevelNumber);
        }

        // ----- boosters -----

        /// <summary>
        /// While a hint is active, only the hint's Apply button and the hinted cell take input -
        /// every other booster button lives under this CanvasGroup.
        /// </summary>
        private void SetBoosterPanelInputEnabled(bool enabled)
        {
            if (boosterPanelCanvasGroup != null)
            {
                boosterPanelCanvasGroup.interactable = enabled;
                boosterPanelCanvasGroup.blocksRaycasts = enabled;
            }

            numberPad?.SetInteractable(enabled);
        }

        private void HideHintUi()
        {
            sharedBoosterManager?.HideBoosterInfo();
            hintBannerController?.Hide();
            hintSpotlightController?.Hide();
            hintApplyButton?.gameObject.SetActive(false);
            SetBoosterPanelInputEnabled(true);
        }

        public void ApplyActiveHint()
        {
            HideHintUi();
            if (controller == null || !controller.ActiveHint.HasValue)
            {
                return;
            }

            int index = controller.ActiveHint.Value.Index;
            FillItMoveResult result = controller.ApplyHint();
            if (!result.Changed)
            {
                return;
            }

            VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Rigid);
            boardView?.PlayHintParticle(index);
            HandleMoveResult(result);
        }

        public void UseHint()
        {
            if (IsDuplicateBoosterUse(BoosterType.Hint) || controller == null || !controller.Session.IsPlaying)
            {
                return;
            }

            FillItHintResult result = controller.RevealHint();
            statusView?.ShowHintResult(result);
            if (result.Success)
            {
                attemptHintUsedCount++;
                // Highlight only - the player commits it by tapping the cell or Apply.
                VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Rigid);
                PlaySound(global::SoundName.HintUsed);
                hintBannerController?.Show(result.Value, result.ReasonKey);
                hintSpotlightController?.Show(result.Row, result.Column);
                hintApplyButton?.gameObject.SetActive(true);
                SetBoosterPanelInputEnabled(false);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveAll();
        }

        public void UseUndo()
        {
            if (IsDuplicateBoosterUse(BoosterType.Undo) || controller == null || !controller.Session.IsPlaying)
            {
                return;
            }

            StopPendingMistake();
            if (boosterManager == null)
            {
                statusView?.ShowLoadErrors(new[] { "BoosterManager is not assigned." });
                return;
            }

            HideHintUi();
            // Undo before the hinted cell was ever used cancels the hint and refunds its charge.
            controller.DismissActiveHint(refundCharge: true);
            BoosterUseResult result = boosterManager.UseUndo(out FillItMoveResult moveResult);
            statusView?.ShowBoosterResult(result);
            if (result.Success)
            {
                attemptUndoUsedCount++;
                PlaySound(global::SoundName.UndoUsed);
                HandleMoveResult(moveResult);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveAll();
        }

        public void UseFix()
        {
            if (IsDuplicateBoosterUse(BoosterType.Fix) || controller == null || !controller.Session.IsPlaying)
            {
                return;
            }

            StopPendingMistake();
            FillItMoveResult result = controller.UseFix(out string failureMessage);
            if (result.Changed)
            {
                PlaySound(global::SoundName.SolveStep);
                VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Medium);
                HandleMoveResult(result);
            }
            else if (!string.IsNullOrEmpty(failureMessage))
            {
                statusView?.ShowBoosterResult(BoosterUseResult.Failed(BoosterType.Fix, failureMessage));
                PlaySound(global::SoundName.ActionBlocked);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveAll();
        }

        private bool IsDuplicateBoosterUse(BoosterType boosterType)
        {
            if (lastBoosterUseFrame == Time.frameCount && lastUsedBoosterType == boosterType)
            {
                return true;
            }

            lastBoosterUseFrame = Time.frameCount;
            lastUsedBoosterType = boosterType;
            return false;
        }

        private void BindSharedBoosterEvents()
        {
            ResolveSharedBoosterManager();
            if (sharedBoosterManager == null)
            {
                return;
            }

            sharedBoosterManager.ApplyRulesConfig(activeRulesConfig);
            sharedBoosterManager.HintImplementationRequested.RemoveListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.RemoveListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.RemoveListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.RemoveListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.RemoveListener(HandleBoosterImplementationStarted);
            sharedBoosterManager.HintImplementationRequested.AddListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.AddListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.AddListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.AddListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.AddListener(HandleBoosterImplementationStarted);
        }

        private void UnbindSharedBoosterEvents()
        {
            if (sharedBoosterManager == null)
            {
                return;
            }

            sharedBoosterManager.HintImplementationRequested.RemoveListener(UseHint);
            sharedBoosterManager.FixImplementationRequested.RemoveListener(UseFix);
            sharedBoosterManager.UndoImplementationRequested.RemoveListener(UseUndo);
            sharedBoosterManager.BoosterPurchaseRequested.RemoveListener(HandleBoosterPurchaseRequested);
            sharedBoosterManager.BoosterImplementationStarted.RemoveListener(HandleBoosterImplementationStarted);
        }

        private void HandleBoosterImplementationStarted(BoosterType boosterType)
        {
            if (boosterType == BoosterType.Hint)
            {
                hintHighlightAnimator?.ResetTimer();
            }
        }

        // (Re)points the Hint idle highlight at the Hint button - after every Configure.
        private void ConfigureHintHighlight()
        {
            if (hintHighlightAnimator == null || sharedBoosterManager == null)
            {
                return;
            }

            BoosterButtonUI hintButton = sharedBoosterManager.GetBoosterButton(BoosterType.Hint);
            RectTransform hintTransform = hintButton != null ? hintButton.transform as RectTransform : null;
            hintHighlightAnimator.Configure(
                hintTransform,
                activeRulesConfig.HintHighlightIdleSeconds,
                () => sharedBoosterManager != null && sharedBoosterManager.IsBoosterAvailable(BoosterType.Hint));
        }
    }
}
