namespace FillIt.Models
{
    /// <summary>Copy of a board's player-editable state (values + which cells a hint placed).</summary>
    public sealed class FillItBoardSnapshot
    {
        public FillItBoardSnapshot(int size, int[] values, bool[] hinted)
        {
            Size = size;
            Values = values;
            Hinted = hinted;
        }

        public int Size { get; }
        public int[] Values { get; }
        public bool[] Hinted { get; }
    }
}
