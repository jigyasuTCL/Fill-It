using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Models;
using CatFlap.Progress;
using CatFlap.Validation;
using Shared.Boosters;
using UnityEngine;

namespace CatFlap.Views
{
    public interface ICatFlapStatusView
    {
        // isFtueLevel: true for levels 1-4 (the FTUE tutorial sequence) - shows a combined
        // "Level 1-4" label instead of this level's own individual number.
        void SetLevel(string id, string name, string difficulty, bool isFtueLevel = false);
        // Non-null while a Daily Challenge level is active: SetLevel then displays this date
        // (e.g. "Jan 5") instead of the level number. Pass null to go back to normal level display.
        void SetDailyChallengeDate(System.DateTime? date);
        void SetLevelMistakes(int remainingMistakes, int maxMistakes, bool animateHeartLoss = false, bool featureVisible = true);
        void SetState(CatFlapGameState state);
        void SetProgress(CatFlapProgressSnapshot progress);
        void SetBoosterEconomy(int coins, int undoCount, int undoCost, int hintCount, int hintCost, int fixCount, int fixCost);
        void ShowMistake(int row, int column);
        void ShowRuleResult(CatFlapRuleValidationResult result, bool boardComplete);
        void PlayRuleBreakBlink();
        // Wrong Solution HUD text: fades in and loops while at least one row/column is currently
        // flagged wrong-sequence; fades out and disables the instant none are left.
        void ShowWrongSolutionWarning();
        void HideWrongSolutionWarning();
        // Instantly disables the Wrong Solution indicator with no fade/animation - use at level
        // load (LoadLevel/Clear/Restart) so a level that starts clean never plays HideWrongSolutionWarning's
        // fade-out just because it happened to still be showing from whatever was on screen before
        // this level loaded. Safe to call even if it's not currently showing.
        void ForceHideWrongSolutionWarning();
        // Screen-pixel position of the wrong-solution warning text's own on-screen spot - lets
        // world-space popups (e.g. the appreciation text) anchor at the exact same visual spot
        // instead of wherever the triggering row/column happens to sit on the board.
        bool TryGetWrongSolutionScreenPosition(out Vector2 screenPosition);
        void ShowBoosterResult(CatFlapCellRevealResult result);
        void ShowHintResult(CatFlapCellRevealResult result, int stars, int difficultyRemoved);
        void ShowBoosterResult(BoosterUseResult result);
        void ShowLoadErrors(IReadOnlyList<string> errors);
        // Plays the HUD's top-bar pop-in - called by CatFlapGameplayManager.PlayBoardEnterAnimation
        // alongside the board's own enter animation, every regular level load/restart and Daily
        // Challenge load/restart.
        void PlayEntryAnimation();
        // Instantly hides the Hard Level HUD indicator - called at the start of every
        // LoadLevel/Clear/Restart (see CatFlapRuntimeController.VisualState.ClearErrorCells), same
        // convention as ForceHideWrongSolutionWarning, so it never lingers on screen from a
        // previous hard level once HardLevelPopupController.OnMoveToTargetArrived re-enables it.
        void DisableHardLevelUI();
        // Re-enables the Info button - called at the start of every LoadLevel/Clear/Restart, same
        // convention as DisableHardLevelUI, so it's never left disabled from the previous level's
        // win/loss (see CatFlapGameplayManager.HandleLevelWon/HandleLevelFailed).
        void EnableInfoButton();
    }
}
