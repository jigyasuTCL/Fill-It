using Shared.CheatUI;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public partial class CatFlapGameplayManager
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
                StopDelayedValidation();
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
            StopDelayedValidation();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            controller?.ForceWin();
            SaveAll();
        }

        // Opens Out of Lives, not the Level Fail popup directly - depletes the remaining mistake
        // count to 0 via the same AddLevelMistakes(negative) path a real mistake spend uses (see
        // CatFlapRuntimeSession.AddLevelMistakes), so this drives the exact real production
        // Out-of-Lives flow (CatFlapGameplayManager.HandleOutOfMistakes) rather than a separate
        // cheat-only shortcut. A no-op if the Level Attempt feature isn't enabled for this level
        // (HandleOutOfMistakes itself skips the popup then) or there's no level loaded.
        public void CheatFailLevel()
        {
            StopDelayedValidation();

            if (controller == null || !controller.Session.HasLevel)
            {
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
            StopDelayedValidation();
        }

        public void CheatSetCorrectValidationDelay(float delaySeconds)
        {
            correctValidationDelaySeconds = Mathf.Max(0f, delaySeconds);
            StopDelayedValidation();
        }

        public void CheatSetValidationDelays(float wrongDelaySeconds, float correctDelaySeconds)
        {
            wrongValidationDelaySeconds = Mathf.Max(0f, wrongDelaySeconds);
            correctValidationDelaySeconds = Mathf.Max(0f, correctDelaySeconds);
            cheatPanel?.SetValidationDelayValues(wrongValidationDelaySeconds, correctValidationDelaySeconds);
            StopDelayedValidation();
        }

        public void CheatJumpToLevel(int levelNumber)
        {
            StopDelayedValidation();
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
