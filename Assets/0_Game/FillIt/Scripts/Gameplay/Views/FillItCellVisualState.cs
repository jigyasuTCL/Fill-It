using FillIt.Rules;

namespace FillIt.Views
{
    /// <summary>Everything a cell view needs to paint itself for one frame of board state.</summary>
    public readonly struct FillItCellVisualState
    {
        public FillItCellVisualState(
            int value,
            bool isGiven,
            bool isHinted,
            FillItRegionStatus status,
            bool showError,
            bool isSelected,
            bool isInDragChain,
            bool connectRight,
            bool connectDown)
        {
            Value = value;
            IsGiven = isGiven;
            IsHinted = isHinted;
            Status = status;
            ShowError = showError;
            IsSelected = isSelected;
            IsInDragChain = isInDragChain;
            ConnectRight = connectRight;
            ConnectDown = connectDown;
        }

        public int Value { get; }
        public bool IsGiven { get; }
        public bool IsHinted { get; }
        public FillItRegionStatus Status { get; }
        /// <summary>Region is broken and wrong-answer rendering isn't suppressed (FTUE).</summary>
        public bool ShowError { get; }
        public bool IsSelected { get; }
        public bool IsInDragChain { get; }
        /// <summary>Right/bottom neighbour belongs to the same region - the view bridges the gap between them.</summary>
        public bool ConnectRight { get; }
        public bool ConnectDown { get; }
        public bool IsEmpty => Value <= 0;
        public bool IsDone => Status == FillItRegionStatus.Done;

        public bool Equals(FillItCellVisualState other)
        {
            return Value == other.Value &&
                   IsGiven == other.IsGiven &&
                   IsHinted == other.IsHinted &&
                   Status == other.Status &&
                   ShowError == other.ShowError &&
                   IsSelected == other.IsSelected &&
                   IsInDragChain == other.IsInDragChain &&
                   ConnectRight == other.ConnectRight &&
                   ConnectDown == other.ConnectDown;
        }
    }
}
