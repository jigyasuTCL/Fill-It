using System;
using FillIt.Levels;

namespace FillIt.Models
{
    /// <summary>
    /// Square Fillomino grid, stored row-major. A value of 0 is an empty cell, 1-9 a placed number.
    /// Given (clue) cells can never be changed by the player.
    /// </summary>
    public sealed class FillItBoardModel
    {
        public const int EmptyValue = 0;
        public const int MaxValue = 9;

        private readonly int[] values;
        private readonly bool[] given;
        private readonly bool[] hinted;
        private readonly int[][] neighbors;

        public FillItBoardModel(int size, int[] clues)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            Size = size;
            int count = size * size;
            values = new int[count];
            given = new bool[count];
            hinted = new bool[count];
            neighbors = BuildNeighbors(size);

            for (int index = 0; index < count; index++)
            {
                int clue = clues != null && index < clues.Length ? clues[index] : EmptyValue;
                values[index] = clue;
                given[index] = clue != EmptyValue;
            }
        }

        public int Size { get; }
        public int RowCount => Size;
        public int ColumnCount => Size;
        public int CellCount => values.Length;

        public static FillItBoardModel FromLevel(FillItLevelData level)
        {
            return new FillItBoardModel(level.Size, level.Clues);
        }

        public int Index(int row, int column) => row * Size + column;
        public int RowOf(int index) => index / Size;
        public int ColumnOf(int index) => index % Size;
        public FillItCellPosition PositionOf(int index) => new FillItCellPosition(index / Size, index % Size);

        public bool IsInside(int row, int column)
        {
            return row >= 0 && row < Size && column >= 0 && column < Size;
        }

        public bool IsValidIndex(int index)
        {
            return index >= 0 && index < values.Length;
        }

        public int GetValue(int index) => values[index];
        public int GetValue(int row, int column) => values[Index(row, column)];
        public bool IsGiven(int index) => given[index];
        public bool IsHinted(int index) => hinted[index];
        public bool IsEmpty(int index) => values[index] == EmptyValue;
        public int[] Neighbors(int index) => neighbors[index];

        /// <summary>Areas the player can type into - everything that wasn't a clue.</summary>
        public bool IsEditable(int index) => !given[index];

        public bool SetValue(int index, int value)
        {
            if (given[index] || values[index] == value)
            {
                return false;
            }

            values[index] = value;
            hinted[index] = false;
            return true;
        }

        public void SetHinted(int index, bool value)
        {
            hinted[index] = value && !given[index];
        }

        public bool IsFull()
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == EmptyValue)
                {
                    return false;
                }
            }

            return true;
        }

        public int CountPlayerFilled()
        {
            int filled = 0;
            for (int index = 0; index < values.Length; index++)
            {
                if (!given[index] && values[index] != EmptyValue)
                {
                    filled++;
                }
            }

            return filled;
        }

        public int CountEditable()
        {
            int editable = 0;
            for (int index = 0; index < given.Length; index++)
            {
                if (!given[index])
                {
                    editable++;
                }
            }

            return editable;
        }

        public int[] CopyValues()
        {
            return (int[])values.Clone();
        }

        public FillItBoardSnapshot CreateSnapshot()
        {
            return new FillItBoardSnapshot(Size, (int[])values.Clone(), (bool[])hinted.Clone());
        }

        /// <summary>Restores player cells only - clue cells always keep their given value.</summary>
        public void Restore(FillItBoardSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Size != Size || snapshot.Values == null || snapshot.Values.Length != values.Length)
            {
                return;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (given[index])
                {
                    continue;
                }

                int value = snapshot.Values[index];
                values[index] = value < EmptyValue || value > MaxValue ? EmptyValue : value;
                hinted[index] = snapshot.Hinted != null && index < snapshot.Hinted.Length && snapshot.Hinted[index] && values[index] != EmptyValue;
            }
        }

        private static int[][] BuildNeighbors(int size)
        {
            var result = new int[size * size][];
            var scratch = new int[4];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int count = 0;
                    if (row > 0) scratch[count++] = (row - 1) * size + column;
                    if (row < size - 1) scratch[count++] = (row + 1) * size + column;
                    if (column > 0) scratch[count++] = row * size + column - 1;
                    if (column < size - 1) scratch[count++] = row * size + column + 1;

                    var cellNeighbors = new int[count];
                    Array.Copy(scratch, cellNeighbors, count);
                    result[row * size + column] = cellNeighbors;
                }
            }

            return result;
        }
    }
}
