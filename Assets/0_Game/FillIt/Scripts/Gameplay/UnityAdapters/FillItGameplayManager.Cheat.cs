using Shared.CheatUI;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    public partial class FillItGameplayManager
    {
        public void CheatApplyPlayerData()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            SyncSessionEconomyFromPlayerData();
            sharedBoosterManager?.RefreshAllBoosterUI();
            MainMenuHandler?.RefreshCoinDisplay();
            int targetLevelIndex = progressSaveManager?.LoadCurrentLevelIndex(currentLevelIndex, GetLevelCount()) ?? currentLevelIndex;
            if (targetLevelIndex != currentLevelIndex)
            {
                StopPendingMistake();
                boardSaveManager?.Clear(controller?.Session);
                boosterManager?.ClearUndoHistory();
                currentLevelIndex = targetLevelIndex;
                LoadAssignedLevel();
                return;
            }

            controller.RefreshViews();
        }

        public void CheatWinLevel()
        {
            StopPendingMistake();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            controller?.ForceWin();
            SaveAll();
        }

        // Drains the remaining lives through the same path a real mistake uses, so this drives
        // the real Out of Lives flow rather than a cheat-only shortcut. Timer mode ignores hearts,
        // so there it runs the clock out instead (real Time's Up flow).
        public void CheatFailLevel()
        {
            StopPendingMistake();

            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            if (controller.Session.TimerEnabled)
            {
                controller.Session.StartTimer();
                controller.TickTimer(controller.Session.RemainingSeconds + 1f);
                SaveAll();
                return;
            }

            int remaining = controller.Session.LevelMistakeLimit.RemainingMistakes;
            if (remaining > 0)
            {
                controller.Session.AddLevelMistakes(-remaining);
            }

            SaveAll();
        }

        public void CheatSetWrongValidationDelay(float delaySeconds)
        {
            wrongValidationDelaySeconds = Mathf.Max(0f, delaySeconds);
        }

        public void CheatSetCorrectValidationDelay(float delaySeconds)
        {
            correctValidationDelaySeconds = Mathf.Max(0f, delaySeconds);
        }

        public void CheatSetValidationDelays(float wrongDelaySeconds, float correctDelaySeconds)
        {
            wrongValidationDelaySeconds = Mathf.Max(0f, wrongDelaySeconds);
            correctValidationDelaySeconds = Mathf.Max(0f, correctDelaySeconds);
            cheatPanel?.SetValidationDelayValues(wrongValidationDelaySeconds, correctValidationDelaySeconds);
        }

        public void CheatJumpToLevel(int levelNumber)
        {
            StopPendingMistake();
            int levelCount = GetLevelCount();
            if (levelCount <= 0)
            {
                return;
            }

            int targetIndex = Mathf.Clamp(levelNumber - 1, 0, levelCount - 1);
            boardSaveManager?.Clear(controller?.Session);
            boosterManager?.ClearUndoHistory();
            currentLevelIndex = targetIndex;
            SaveProgress();
            LoadAssignedLevel();
        }

        private void BindCheatPanel()
        {
            cheatPanel = FindFirstObjectByType<CheatPanelController>(FindObjectsInactive.Include);
            if (cheatPanel == null)
            {
                return;
            }

            cheatPanel.WrongValidationDelayChanged.RemoveListener(CheatSetWrongValidationDelay);
            cheatPanel.CorrectValidationDelayChanged.RemoveListener(CheatSetCorrectValidationDelay);
            cheatPanel.WrongValidationDelayChanged.AddListener(CheatSetWrongValidationDelay);
            cheatPanel.CorrectValidationDelayChanged.AddListener(CheatSetCorrectValidationDelay);
            cheatPanel.SetValidationDelayValues(wrongValidationDelaySeconds, correctValidationDelaySeconds);
            cheatPanel.SetCurrentLevelIndexKey(activeConfig.CurrentLevelIndexSaveKey);
            cheatPanel.SetLoopLevelReadout(CurrentLevelNumber, lastLoadedActualLevelNumber);
        }

        private void UnbindCheatPanel()
        {
            if (cheatPanel == null)
            {
                return;
            }

            cheatPanel.WrongValidationDelayChanged.RemoveListener(CheatSetWrongValidationDelay);
            cheatPanel.CorrectValidationDelayChanged.RemoveListener(CheatSetCorrectValidationDelay);
            cheatPanel = null;
        }
    }
}
