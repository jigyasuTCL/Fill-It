using CatFlap.Levels;
using CatFlap.Models;

namespace CatFlap.Boosters
{
    public sealed class CatFlapFixService
    {
        public CatFlapCellRevealResult FixFirstIncorrect(CatFlapBoardModel board, CatFlapLevelData level)
        {
            if (board == null || level == null)
            {
                return CatFlapCellRevealResult.Failed("Cannot use Fix before a level is loaded.");
            }

            for (int row = 0; row < board.RowCount; row++)
            {
                for (int column = 0; column < board.ColumnCount; column++)
                {
                    CatFlapCellModel cell = board.GetCell(row, column);
                    if (cell.IsLocked || cell.Value == CatFlapCellValue.Empty || cell.IsNote)
                    {
                        continue;
                    }

                    int? solution = level.Solution[row].Cells[column];
                    if (solution.HasValue && cell.Value != (CatFlapCellValue)solution.Value)
                    {
                        return new CatFlapCellRevealResult(true, row, column, (CatFlapCellValue)solution.Value, "Cell fixed.");
                    }
                }
            }

            return CatFlapCellRevealResult.Failed("No incorrect filled cell is available to fix.");
        }
    }
}
