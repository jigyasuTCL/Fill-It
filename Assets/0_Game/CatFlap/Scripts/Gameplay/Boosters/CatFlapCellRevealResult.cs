using CatFlap.Models;

namespace CatFlap.Boosters
{
    public readonly struct CatFlapCellRevealResult
    {
        public CatFlapCellRevealResult(bool success, int row, int column, CatFlapCellValue value, string message)
            : this(success, row, column, value, message, string.Empty)
        {
        }

        public CatFlapCellRevealResult(bool success, int row, int column, CatFlapCellValue value, string message, string reasonKey)
        {
            Success = success;
            Row = row;
            Column = column;
            Value = value;
            Message = message ?? string.Empty;
            ReasonKey = reasonKey ?? string.Empty;
        }

        public bool Success { get; }
        public int Row { get; }
        public int Column { get; }
        public CatFlapCellValue Value { get; }
        public string Message { get; }
        public string ReasonKey { get; }

        public static CatFlapCellRevealResult Failed(string message)
        {
            return new CatFlapCellRevealResult(false, -1, -1, CatFlapCellValue.Empty, message);
        }
    }
}
