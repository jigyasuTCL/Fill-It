using System.Collections.Generic;
using FillIt.Models;
using FillIt.Rules;
using FillIt.Runtime;
using Shared.Boosters;
using UnityEngine;

namespace FillIt.Views
{
    public interface IFillItStatusView
    {
        // isFtueLevel: true for the FTUE tutorial levels - shows a combined "Level 1-4" label
        // instead of this level's own individual number.
        void SetLevel(string id, string name, string difficulty, bool isFtueLevel = false);
        // Non-null while a Daily Challenge level is active: SetLevel then displays this date
        // (e.g. "Jan 5") instead of the level number. Pass null to go back to normal level display.
        void SetDailyChallengeDate(System.DateTime? date);
        void SetLevelMistakes(int remainingMistakes, int maxMistakes, bool animateHeartLoss = false, bool featureVisible = true);
        // Timer gameplay mode countdown (shown where the hearts sit); visible false hides it.
        void SetTimer(bool visible, float remainingSeconds);
        void SetState(FillItGameState state);
        void SetProgress(FillItProgressSnapshot progress);
        void SetBoosterEconomy(int coins, int undoCount, int undoCost, int hintCount, int hintCost, int fixCount, int fixCost);
        void ShowRuleResult(FillItBoardAnalysis analysis, bool boardComplete);
        void PlayRuleBreakBlink();
        // Screen-pixel anchor the appreciation text pops from (the old wrong-solution HUD spot).
        bool TryGetWrongSolutionScreenPosition(out Vector2 screenPosition);
        void ShowHintResult(FillItHintResult result);
        void ShowBoosterResult(BoosterUseResult result);
        void ShowLoadErrors(IReadOnlyList<string> errors);
        // Plays the HUD's top-bar pop-in alongside the board's own enter animation.
        void PlayEntryAnimation();
        void DisableHardLevelUI();
        void EnableInfoButton();
    }
}
