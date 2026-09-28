using System;
using CatFlap.Levels;

namespace CatFlap.Models
{
    public sealed class CatFlapBoardModel
    {
        private readonly CatFlapCellModel[,] cells;

        private CatFlapBoardModel(CatFlapCellModel[,] cells)
        {
            this.cells = cells;
            RowCount = cells.GetLength(0);
            ColumnCount = cells.GetLength(1);
        }

        public int RowCount { get; }
        public int ColumnCount { get; }

        public CatFlapCellModel GetCell(int row, int column)
        {
            EnsureInside(row, column);
            return cells[row, column];
        }

        public bool IsFullIgnoringNotes()
        {
            for (int row = 0; row < RowCount; row++)
            {
                for (int column = 0; column < ColumnCount; column++)
                {
                    CatFlapCellModel cell = cells[row, column];
                    if (cell.Value == CatFlapCellValue.Empty || cell.IsNote)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public CatFlapCellValue GetEffectiveValue(int row, int column)
        {
            CatFlapCellModel cell = GetCell(row, column);
            return cell.IsNote ? CatFlapCellValue.Empty : cell.Value;
        }

        public void SetCellValue(int row, int column, CatFlapCellValue value, bool isNote)
        {
            GetCell(row, column).SetValue(value, isNote);
        }

        public void LockRow(int row)
        {
            EnsureRow(row);
            for (int column = 0; column < ColumnCount; column++)
            {
                cells[row, column].SetLocked(true);
            }
        }

        public void LockColumn(int column)
        {
            EnsureColumn(column);
            for (int row = 0; row < RowCount; row++)
            {
                cells[row, column].SetLocked(true);
            }
        }

        public CatFlapBoardSnapshot CreateSnapshot()
        {
            var values = new CatFlapCellValue[RowCount, ColumnCount];
            var locked = new bool[RowCount, ColumnCount];
            var notes = new bool[RowCount, ColumnCount];

            for (int row = 0; row < RowCount; row++)
            {
                for (int column = 0; column < ColumnCount; column++)
                {
                    CatFlapCellModel cell = cells[row, column];
                    values[row, column] = cell.Value;
                    locked[row, column] = cell.IsLocked;
                    notes[row, column] = cell.IsNote;
                }
            }

            return new CatFlapBoardSnapshot(values, locked, notes);
        }

        public void Restore(CatFlapBoardSnapshot snapshot)
        {
            if (snapshot.Values.GetLength(0) != RowCount || snapshot.Values.GetLength(1) != ColumnCount)
            {
                throw new ArgumentException("Snapshot dimensions do not match the board.");
            }

            for (int row = 0; row < RowCount; row++)
            {
                for (int column = 0; column < ColumnCount; column++)
                {
                    cells[row, column].Restore(
                        snapshot.Values[row, column],
                        snapshot.Locked[row, column],
                        snapshot.Notes[row, column]);
                }
            }
        }

        public static CatFlapBoardModel FromLevel(CatFlapLevelData level)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            int size = level.Size;
            var cells = new CatFlapCellModel[size, size];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    CatFlapCellValue value = CatFlapCellValue.Empty;
                    int? clue = level.Clues[row].Cells[column];
                    if (clue == 0)
                    {
                        value = CatFlapCellValue.Fish;
                    }
                    else if (clue == 1)
                    {
                        value = CatFlapCellValue.Cat;
                    }

                    cells[row, column] = new CatFlapCellModel(row, column, value, clue.HasValue, false, clue.HasValue);
                }
            }

            return new CatFlapBoardModel(cells);
        }

        private void EnsureInside(int row, int column)
        {
            EnsureRow(row);
            EnsureColumn(column);
        }

        private void EnsureRow(int row)
        {
            if (row < 0 || row >= RowCount)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }
        }

        private void EnsureColumn(int column)
        {
            if (column < 0 || column >= ColumnCount)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }
        }
    }
}
