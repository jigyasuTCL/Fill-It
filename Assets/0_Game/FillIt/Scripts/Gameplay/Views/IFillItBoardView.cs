using System.Collections.Generic;
using UnityEngine;

namespace FillIt.Views
{
    public interface IFillItBoardView
    {
        void Build(int size);
        /// <param name="animateValueChange">True for a live player edit (pop the number in); false for load/restore repaints.</param>
        void SetCell(int index, FillItCellVisualState state, bool animateValueChange);
        /// <summary>Line through the active drag chain's cells, as (from, to) cell-index pairs.</summary>
        void SetDragTrail(IReadOnlyList<Vector2Int> segments, int trailValue);
        void ClearDragTrail();
        /// <summary>Region-complete celebration pop across the region's cells.</summary>
        void PlayRegionComplete(IReadOnlyList<int> cells);
        /// <summary>Red shake on a broken region.</summary>
        void PlayMistake(IReadOnlyList<int> cells);
        void PlayHeartBreak(int index);
        void PlayLockedTapShake(int index);
        void ShowHintTarget(int index, int value);
        void ClearHintTarget();
        void Clear();
        bool TryGetCellWorldPosition(int index, out Vector3 position);
    }
}
