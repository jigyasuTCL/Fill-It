using System;

namespace Shared.Ftue
{
    // Plain tutorial-step content - just a description. No knowledge of any particular game's
    // board, cells, or levels; a game's own step-sequencing code decides which FtueStepData to
    // show and when.
    [Serializable]
    public sealed class FtueStepData
    {
        public string Description = string.Empty;
    }
}
