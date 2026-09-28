namespace CatFlap.Models
{
    public sealed class CatFlapBoardSnapshot
    {
        public CatFlapBoardSnapshot(CatFlapCellValue[,] values, bool[,] locked, bool[,] notes)
        {
            Values = values;
            Locked = locked;
            Notes = notes;
        }

        public CatFlapCellValue[,] Values { get; }
        public bool[,] Locked { get; }
        public bool[,] Notes { get; }
    }
}
