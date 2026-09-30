using System;
using Shared.Ftue;

namespace FillIt.UnityAdapters
{
    // Maps a single 1-based level number to its tutorial - see FillItGameplayManager.ftueLevelSteps.
    // Kept as a small Inspector-editable list rather than a Dictionary since Unity can't serialize
    // dictionaries directly. Designer text lives in the FtueStepsAsset; which cells each step
    // guides the player through lives in Moves (one Move per step, same order).
    [Serializable]
    public sealed class FillItFtueLevelStepsEntry
    {
        public int LevelNumber;
        public FtueStepsAsset Steps;
        public FillItFtueMove[] Moves = Array.Empty<FillItFtueMove>();
    }

    // One guided move: the cells the finger is shown dragging through, in order (row-major cell
    // indices). A single cell is a tap. The path may start on a given number (drag out of it);
    // only its empty cells are "targets" the player must fill to finish the step.
    [Serializable]
    public sealed class FillItFtueMove
    {
        public int[] Path = Array.Empty<int>();
    }
}
