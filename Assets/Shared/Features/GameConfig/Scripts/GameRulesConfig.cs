using System;
using UnityEngine;

namespace Shared.GameConfig
{
    [Serializable]
    public sealed class GameRulesConfig
    {
        public int LevelMistakeLimit = 5;
        public float WrongValidationDelaySeconds = 0.75f;
        public float CorrectValidationDelaySeconds = 0.4f;
        // Faster "correct" checkpoint used only when a tap fills the board's last remaining empty
        // cell (see CatFlapGameplayManager.Validation.StartDelayedValidation) - normal cells still
        // use CorrectValidationDelaySeconds above.
        public float LastCellCorrectValidationDelaySeconds = 0.2f;
        public float AutosaveIntervalSeconds = 5f;
        public float BoosterInfoVisibleSeconds = 2f;
        public float BoosterInfoFadeSeconds = 0.35f;
        public string HintOneMissingLineText = "Hint: completed the row/column with one cell left.";
        public string HintMaxTwoAdjacentText = "Hint: used the Max 2 Adjacent rule.";
        public string HintLeastEmptyLineText = "Hint: picked the row/column with fewer empty cells.";
        public string HintRandomText = "Hint: picked a random available cell.";
        public string HintFallbackText = "Hint revealed.";
        public Color CellCompletedColor = new Color(0.82f, 0.96f, 0.86f, 1f);
        public Color CellErrorColor = new Color(1f, 0.84f, 0.84f, 1f);
        public Color CellWarningColor = new Color(1f, 0.84f, 0.42f, 1f);
        public Color MistakeBlinkColor = new Color(1f, 0.24f, 0.24f, 1f);
        public float MistakeBlinkDurationSeconds = 0.45f;
        public int MistakeBlinkCount = 3;
        // Seconds of no board input (and no hint use) while the Hint booster is available before
        // its idle-attention pulse starts - see CatFlapHintHighlightAnimator.
        public float HintHighlightIdleSeconds = 10f;

        public GameRulesConfig CloneNormalized()
        {
            return new GameRulesConfig
            {
                LevelMistakeLimit = Math.Max(1, LevelMistakeLimit),
                WrongValidationDelaySeconds = Mathf.Max(0f, WrongValidationDelaySeconds),
                CorrectValidationDelaySeconds = Mathf.Max(0f, CorrectValidationDelaySeconds),
                LastCellCorrectValidationDelaySeconds = Mathf.Max(0f, LastCellCorrectValidationDelaySeconds),
                AutosaveIntervalSeconds = Mathf.Max(0f, AutosaveIntervalSeconds),
                BoosterInfoVisibleSeconds = Mathf.Max(0f, BoosterInfoVisibleSeconds),
                BoosterInfoFadeSeconds = Mathf.Max(0f, BoosterInfoFadeSeconds),
                HintOneMissingLineText = NormalizeText(HintOneMissingLineText, "Hint: completed the row/column with one cell left."),
                HintMaxTwoAdjacentText = NormalizeText(HintMaxTwoAdjacentText, "Hint: used the Max 2 Adjacent rule."),
                HintLeastEmptyLineText = NormalizeText(HintLeastEmptyLineText, "Hint: picked the row/column with fewer empty cells."),
                HintRandomText = NormalizeText(HintRandomText, "Hint: picked a random available cell."),
                HintFallbackText = NormalizeText(HintFallbackText, "Hint revealed."),
                CellCompletedColor = CellCompletedColor,
                CellErrorColor = CellErrorColor,
                CellWarningColor = CellWarningColor,
                MistakeBlinkColor = MistakeBlinkColor,
                MistakeBlinkDurationSeconds = Mathf.Max(0f, MistakeBlinkDurationSeconds),
                MistakeBlinkCount = Math.Max(1, MistakeBlinkCount),
                HintHighlightIdleSeconds = Mathf.Max(0f, HintHighlightIdleSeconds)
            };
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
