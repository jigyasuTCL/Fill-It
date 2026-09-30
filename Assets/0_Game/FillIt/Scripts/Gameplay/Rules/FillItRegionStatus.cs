namespace FillIt.Rules
{
    public enum FillItRegionStatus
    {
        /// <summary>Cell holds no number.</summary>
        Empty,
        /// <summary>Region is smaller than its number but still touches an empty cell it can grow into.</summary>
        Open,
        /// <summary>Region contains exactly as many cells as its number.</summary>
        Done,
        /// <summary>Region contains more cells than its number.</summary>
        Over,
        /// <summary>Region is smaller than its number and is fully enclosed - it can never be completed as-is.</summary>
        Stuck
    }
}
