namespace CatFlap.Boosters
{
    public sealed class CatFlapUndoMoveState
    {
        public CatFlapUndoMoveState(int row, int column, bool wasMoveCounted)
        {
            Row = row;
            Column = column;
            MoveCount = wasMoveCounted ? 1 : 0;
        }

        public int Row { get; }
        public int Column { get; }

        // Consecutive taps on this same cell (e.g. Empty -> Fish -> Cat) collapse into this
        // one entry, since undoing always just clears the cell regardless of how many taps
        // it took to get there. This tracks how many of those taps counted as a real move,
        // so undoing this single entry decrements the move counter by the right amount.
        public int MoveCount { get; private set; }

        public void AddMove(bool wasMoveCounted)
        {
            if (wasMoveCounted)
            {
                MoveCount++;
            }
        }
    }
}
