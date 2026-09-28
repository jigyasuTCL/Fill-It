using System.Collections.Generic;
using CatFlap.Models;
using UnityEngine;

namespace CatFlap.Views
{
    public interface ICatFlapBoardView
    {
        void Build(int rows, int columns);
        // speedMultiplier scales up the tile-flip's playback speed (1x = normal tap speed) -
        // passed by a completed-line reveal's per-step speed-up, always 1x for a normal tap.
        // suppressPostFlipPop skips the completed-line character pop (scale up/hold/scale down)
        // that would otherwise follow the flip - the level-winning move's own reveal passes true,
        // so its flip goes straight into the board-wide win celebration instead of playing that
        // pop first (see CatFlapRuntimeController.PlayWinningLineReveal).
        void SetCell(int row, int column, CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor, CatFlapCellVisualState visualState, bool isGiven, bool suppressValueChangeAnimation = false, bool forcePlayFlip = false, float speedMultiplier = 1f, bool suppressPostFlipPop = false);
        // Reveals a completed row/column (same tap-flip animation as a normal placement, plus a
        // settle-overshoot flourish - see CatFlapSpriteCellViewAdapter.AnimateTileFlip) group by
        // group instead of all at once - every cell within one group reveals together, in the same
        // instant (so a row and column completed by the same move flip in lockstep rather than one
        // line finishing before the other starts); staggerSeconds only pauses BETWEEN groups.
        // revealCell's float parameter is the speed multiplier for that group's step (see
        // speedIncreasePerStep/maxSpeedMultiplier) - group 0 plays at 1x, each following group
        // plays faster, capped at maxSpeedMultiplier. onComplete fires once every cell in every
        // group has been revealed.
        void PlayStaggeredReveal(IReadOnlyList<IReadOnlyList<CatFlapCellPosition>> groups, float staggerSeconds, float speedIncreasePerStep, float maxSpeedMultiplier, System.Action<CatFlapCellPosition, float> revealCell, System.Action onComplete);
        // Stops an in-flight PlayStaggeredReveal without invoking its onComplete - used when a
        // level Restart/Clear/Home happens mid-reveal (see CatFlapRuntimeController.CancelSequencing)
        // so its completion callback never fires against a board that's since been reset/reloaded.
        void CancelStaggeredReveal();
        void PlayCompletedParticle(int row, int column);
        void PlayMistakeBlink(int row, int column);
        void PlayRuleBreakBlink(IReadOnlyList<CatFlapCellPosition> cells);
        // Wrong Solution feedback (complete line, no rule violation, but wrong order): the amber
        // highlight fades in/out on a loop until StopWarningBlink is called (the line is
        // edited/fixed). playShake additionally plays a one-shot shake alongside the blink start -
        // pass false for a silent load/resume refresh so the shake doesn't replay for a line that
        // was already wrong before this session started.
        void PlayWarningBlink(IReadOnlyList<CatFlapCellPosition> cells, bool playShake = true);
        void StopWarningBlink(int row, int column);
        // One-shot shake (visually the same technique as PlayWarningBlink's shake) for tapping a
        // pre-placed/locked cell that isn't part of a completed line - see
        // CatFlapGameplayManager.HandleCellSelected.
        void PlayLockedTapShake(int row, int column);
        void PlayHeartBreak(int row, int column);
        // Highlights the hint target cell AND shows a note-style preview icon of the correct
        // character (Cat/Fish) that belongs there - purely a view-side overlay, independent of the
        // cell's real model value/note, so it never conflicts with the player's own note-cycling.
        // ClearHintTarget removes both.
        void ShowHintTarget(int row, int column, CatFlapCellValue value);
        void ClearHintTarget();
        void Clear();
        bool TryGetCellWorldPosition(int row, int column, out Vector3 position);
    }
}
