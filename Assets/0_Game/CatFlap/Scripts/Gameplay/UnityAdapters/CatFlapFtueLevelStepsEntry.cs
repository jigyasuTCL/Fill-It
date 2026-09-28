using System;
using Shared.Ftue;

namespace CatFlap.UnityAdapters
{
    // Maps a single 1-based level number to the FtueStepsAsset holding that level's tutorial
    // text - see CatFlapGameplayManager.ftueLevelSteps. Kept as a small Inspector-editable list
    // rather than a Dictionary since Unity can't serialize dictionaries directly.
    [Serializable]
    public sealed class CatFlapFtueLevelStepsEntry
    {
        public int LevelNumber;
        public FtueStepsAsset Steps;

        // 0 (default) - every step in Steps.Steps becomes a walkthrough target, same as before.
        // A positive value N walks through only the first N steps' cells at level start; any
        // step AFTER that (e.g. Steps.Steps[N]) is reserved instead as a "mistake reaction" tip -
        // shown only if the player breaks a rule after the walkthrough ends, never as a normal
        // target (see CatFlapFtueDriver.Configure/ShowMistakeTip).
        public int WalkthroughStepCount;

        // Level 4 only: replaces the generic empty-cell walkthrough above with a bespoke sequence
        // that teaches the board's actual rules (highlight a whole row, place the first fish,
        // move the FTUE popup onto the Rules panel to reveal Equal Counts, place the second fish,
        // wait for the row to lock, move the popup to reveal Max Two Adjacent, then a normal free
        // tap followed by one guided tap for the last cell) - see
        // CatFlapFtueDriver.ConfigureRulesWalkthrough. WalkthroughStepCount/Steps above are
        // ignored while this is true; Steps.Steps is still used, but only its first two entries
        // (the two rules' own explanation text).
        public bool EnableRulesWalkthrough;
    }
}
