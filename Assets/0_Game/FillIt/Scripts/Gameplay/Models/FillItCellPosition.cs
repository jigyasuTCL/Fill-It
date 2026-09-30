using System;

namespace FillIt.Models
{
    public readonly struct FillItCellPosition : IEquatable<FillItCellPosition>
    {
        public FillItCellPosition(int row, int column)
        {
            Row = row;
            Column = column;
        }

        public int Row { get; }
        public int Column { get; }

        public bool Equals(FillItCellPosition other)
        {
            return Row == other.Row && Column == other.Column;
        }

        public override bool Equals(object obj)
        {
            return obj is FillItCellPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Row * 397) ^ Column;
        }

        public override string ToString()
        {
            return $"({Row},{Column})";
        }
    }
}
