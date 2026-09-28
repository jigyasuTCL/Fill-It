namespace CatFlap.Models
{
    public readonly struct CatFlapCellPosition
    {
        public CatFlapCellPosition(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }
    }
}
