namespace CatFlap.Progress
{
    public readonly struct CatFlapProgressSnapshot
    {
        public CatFlapProgressSnapshot(int filledCells, int playableCells, int moves, float elapsedSeconds)
        {
            FilledCells = filledCells;
            PlayableCells = playableCells;
            Moves = moves;
            ElapsedSeconds = elapsedSeconds;
        }

        public int FilledCells { get; }
        public int PlayableCells { get; }
        public int Moves { get; }
        public float ElapsedSeconds { get; }
        public float CompletionPercent => PlayableCells <= 0 ? 100f : (float)FilledCells / PlayableCells * 100f;
    }
}
