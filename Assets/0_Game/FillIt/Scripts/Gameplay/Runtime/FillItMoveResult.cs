using System.Collections.Generic;
using FillIt.Rules;

namespace FillIt.Runtime
{
    public enum FillItMoveKind
    {
        None,
        Drag,
        TapPlaceOne,
        TapClear,
        NumberPad,
        Hint,
        Fix,
        Undo
    }

    /// <summary>What one committed player action did to the board - drives feedback (sounds, pops, lives).</summary>
    public sealed class FillItMoveResult
    {
        public static readonly FillItMoveResult Nothing = new FillItMoveResult(FillItMoveKind.None, new List<FillItCellChange>(), new List<FillItRegion>(), new List<FillItRegion>(), false);

        public FillItMoveResult(
            FillItMoveKind kind,
            List<FillItCellChange> changes,
            List<FillItRegion> newlyCompletedRegions,
            List<FillItRegion> newViolations,
            bool solvedBoard)
        {
            Kind = kind;
            Changes = changes ?? new List<FillItCellChange>();
            NewlyCompletedRegions = newlyCompletedRegions ?? new List<FillItRegion>();
            NewViolations = newViolations ?? new List<FillItRegion>();
            SolvedBoard = solvedBoard;
        }

        public FillItMoveKind Kind { get; }
        public List<FillItCellChange> Changes { get; }
        public bool Changed => Changes.Count > 0;
        public List<FillItRegion> NewlyCompletedRegions { get; }
        /// <summary>Broken regions that didn't exist before this move (candidates for a life charge).</summary>
        public List<FillItRegion> NewViolations { get; }
        public bool SolvedBoard { get; }

        /// <summary>The cell the move "ended" on - anchors feedback like the heart break FX.</summary>
        public int LastChangedIndex => Changes.Count > 0 ? Changes[Changes.Count - 1].Index : -1;
    }
}
