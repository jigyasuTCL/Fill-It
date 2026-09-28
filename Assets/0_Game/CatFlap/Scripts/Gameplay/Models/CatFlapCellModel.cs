using System;

namespace CatFlap.Models
{
    [Serializable]
    public sealed class CatFlapCellModel
    {
        public CatFlapCellModel(int row, int column, CatFlapCellValue value, bool isLocked, bool isNote, bool isGiven = false)
        {
            Row = row;
            Column = column;
            Value = value;
            IsLocked = isLocked;
            IsNote = isNote;
            IsGiven = isGiven;
        }

        public int Row { get; }
        public int Column { get; }
        public CatFlapCellValue Value { get; private set; }
        public bool IsLocked { get; private set; }
        public bool IsNote { get; private set; }
        public bool IsGiven { get; }

        public void SetValue(CatFlapCellValue value, bool isNote)
        {
            if (IsLocked)
            {
                throw new InvalidOperationException("Locked cells cannot be changed.");
            }

            Value = value;
            IsNote = isNote && value != CatFlapCellValue.Empty;
        }

        public void SetLocked(bool isLocked)
        {
            IsLocked = isLocked;
            if (isLocked)
            {
                IsNote = false;
            }
        }

        internal void Restore(CatFlapCellValue value, bool isLocked, bool isNote)
        {
            Value = value;
            IsLocked = isLocked;
            IsNote = isNote && value != CatFlapCellValue.Empty;
        }
    }
}
