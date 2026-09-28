using UnityEngine;

namespace Shared.AppreciationText
{
    /// <summary>
    /// One appreciation "card" - a pre-authored text image plus an optional dedicated sound.
    /// Leaving sfx unassigned falls back to AppreciationTextConfig.defaultSfx.
    /// </summary>
    [System.Serializable]
    public sealed class AppreciationTextEntry
    {
        [SerializeField] private Sprite textImage;
        [SerializeField] private AudioClip sfx;
        // Matched to this entry's own text image color (not its tier) - two entries in the same
        // tier can use different colored art and therefore different particle VFX.
        [SerializeField] private AppreciationParticleColor particleColor = AppreciationParticleColor.Peach;

        public Sprite TextImage => textImage;
        public AudioClip Sfx => sfx;
        public AppreciationParticleColor ParticleColor => particleColor;
    }
}
