using CatFlap.Models;

namespace CatFlap.Progress
{
    public sealed class CatFlapProgressTracker
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

        public void RemoveMoves(int count)
        {
            if (count <= 0)
            {
                return;
            }

            Moves = Moves > count ? Moves - count : 0;
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

        public CatFlapProgressSnapshot CreateSnapshot(CatFlapBoardModel board)
        {
            if (board == null)
            {
                return new CatFlapProgressSnapshot(0, 0, Moves, ElapsedSeconds);
            }

            int playable = 0;
            int filled = 0;
            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = board.GetCell(row, column);
                    if (cell.IsLocked)
                    {
                        continue;
                    }

                    playable++;
                    if (cell.Value != CatFlapCellValue.Empty && !cell.IsNote)
                    {
                        filled++;
                    }
                }
            }

            return new CatFlapProgressSnapshot(filled, playable, Moves, ElapsedSeconds);
        }
    }
}
