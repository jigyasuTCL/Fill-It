namespace FillIt.Models
{
    public readonly struct FillItProgressSnapshot
    {
        public FillItProgressSnapshot(int filledCells, int playableCells, int moves, float elapsedSeconds)
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

    public sealed class FillItProgressTracker
    {
        public int Moves { get; private set; }
        public float ElapsedSeconds { get; private set; }

        public void Reset()
        {
            Moves = 0;
            ElapsedSeconds = 0f;
        }

        public void AddMove()
        {
            Moves++;
        }

        public void RemoveMove()
        {
            if (Moves > 0)
            {
                Moves--;
            }
        }

        public void Restore(int moves, float elapsedSeconds)
        {
            Moves = moves < 0 ? 0 : moves;
            ElapsedSeconds = elapsedSeconds < 0f ? 0f : elapsedSeconds;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                ElapsedSeconds += deltaSeconds;
            }
        }

        public FillItProgressSnapshot CreateSnapshot(FillItBoardModel board)
        {
            if (board == null)
            {
                return new FillItProgressSnapshot(0, 0, Moves, ElapsedSeconds);
            }

            return new FillItProgressSnapshot(board.CountPlayerFilled(), board.CountEditable(), Moves, ElapsedSeconds);
        }
    }
}
