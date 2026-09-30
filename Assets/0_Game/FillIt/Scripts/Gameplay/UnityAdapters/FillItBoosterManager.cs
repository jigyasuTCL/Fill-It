using FillIt.Runtime;
using Shared.Boosters;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Undo booster. Undo is free and unlimited once unlocked: every committed move (drag, tap,
    /// number pad, hint) is one step, reverted most-recent first.
    /// </summary>
    public sealed class FillItBoosterManager : MonoBehaviour
    {
        private FillItRuntimeController runtimeController;
        private System.Func<int> currentLevelNumberProvider;

        public void Initialize(FillItRuntimeController controller, System.Func<int> levelNumberProvider)
        {
            runtimeController = controller;
            currentLevelNumberProvider = levelNumberProvider;
        }

        public void ClearUndoHistory()
        {
            runtimeController?.Session?.ClearUndoHistory();
        }

        public bool CanUndo()
        {
            return IsUnlocked() && runtimeController != null && runtimeController.CanUndo();
        }

        public BoosterUseResult UseUndo(out FillItMoveResult moveResult)
        {
            moveResult = FillItMoveResult.Nothing;
            if (runtimeController == null || !runtimeController.Session.HasLevel)
            {
                return BoosterUseResult.Failed(BoosterType.Undo, "No level is loaded.");
            }

            if (!IsUnlocked())
            {
                int unlockLevel = runtimeController.Session.BoosterWallet?.Config.GetUnlockLevel(BoosterType.Undo) ?? -1;
                return BoosterUseResult.Failed(BoosterType.Undo, unlockLevel < 0 ? "Undo is not active." : $"Undo unlocks at level {unlockLevel}.");
            }

            moveResult = runtimeController.Undo();
            return moveResult.Changed
                ? BoosterUseResult.Used(BoosterType.Undo, "Undo used.")
                : BoosterUseResult.Failed(BoosterType.Undo, "No move is available to undo.");
        }

        private bool IsUnlocked()
        {
            BoosterWallet wallet = runtimeController?.Session?.BoosterWallet;
            if (wallet == null)
            {
                return false;
            }

            int currentLevelNumber = currentLevelNumberProvider?.Invoke() ?? 1;
            int unlockLevel = wallet.Config.GetUnlockLevel(BoosterType.Undo);
            return unlockLevel >= 0 && currentLevelNumber >= unlockLevel;
        }
    }
}
