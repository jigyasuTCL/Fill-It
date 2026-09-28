using System;

namespace CatFlap.Levels
{
    [Serializable]
    public sealed class CatFlapLevelRow
    {
        public int?[] Cells;

        public CatFlapLevelRow(int?[] cells)
        {
            Cells = cells;
        }
    }
}
