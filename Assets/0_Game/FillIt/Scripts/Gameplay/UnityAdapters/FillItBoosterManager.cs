using CatFlap.Boosters;
using CatFlap.Runtime;
using Shared.Boosters;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapBoosterManager : MonoBehaviour
    {
        private readonly CatFlapBoosterController controller = new CatFlapBoosterController();
        private CatFlapRuntimeController runtimeController;

        public CatFlapBoosterController Controller => controller;

        public void Initialize(CatFlapRuntimeController runtimeController, System.Func<int> currentLevelNumberProvider)
        {
            this.runtimeController = runtimeController;
            controller.Configure(runtimeController?.Session?.BoosterWallet, currentLevelNumberProvider);
        }

        public void ClearUndoHistory()
        {
            controller.ClearUndoHistory();
        }

        public void RebuildUndoHistoryFromBoard()
        {
            controller.RebuildFromBoard(runtimeController?.Session);
        }

        public void RecordMove(int row, int column, bool wasMoveCounted)
        {
            controller.RecordMove(row, column, wasMoveCounted);
        }

        public bool CanUndo()
        {
            return controller.CanUndo(runtimeController);
        }

        public BoosterUseResult UseUndo()
        {
            return controller.UseUndo(runtimeController);
        }
    }
}
