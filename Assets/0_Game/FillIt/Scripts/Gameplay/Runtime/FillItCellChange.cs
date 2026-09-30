namespace FillIt.Runtime
{
    /// <summary>One cell's value before/after a player action - the unit Undo reverts.</summary>
    public readonly struct FillItCellChange
    {
        public FillItCellChange(int index, int before, int after, bool beforeHinted, bool afterHinted = false)
        {
            Index = index;
            Before = before;
            After = after;
            BeforeHinted = beforeHinted;
            AfterHinted = afterHinted;
        }

        public int Index { get; }
        public int Before { get; }
        public int After { get; }
        public bool BeforeHinted { get; }
        public bool AfterHinted { get; }
    }
}
