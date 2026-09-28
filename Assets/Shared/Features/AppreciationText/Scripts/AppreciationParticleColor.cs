namespace Shared.AppreciationText
{
    /// <summary>
    /// Which of the appreciation popup's pre-authored particle VFX (see
    /// AppreciationTextItemView) matches a given AppreciationTextEntry's own text image color -
    /// set per entry, not per tier, since entries within the same tier can use different colors.
    /// </summary>
    public enum AppreciationParticleColor
    {
        Yellow,
        Purple,
        Blue,
        Peach
    }
}
