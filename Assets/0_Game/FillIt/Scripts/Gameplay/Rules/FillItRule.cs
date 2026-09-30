namespace FillIt.Rules
{
    /// <summary>
    /// The two ways a Fillomino region can be wrong. Every region must contain exactly as many
    /// cells as its number, so a region is either too big, or too small with no room left to grow.
    /// </summary>
    public enum FillItRule
    {
        RegionTooBig,
        RegionTooSmall
    }
}
