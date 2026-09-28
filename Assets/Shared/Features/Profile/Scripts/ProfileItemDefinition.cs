using System;
using UnityEngine;

namespace Shared.Profile
{
    /// <summary>
    /// One pickable profile icon or frame. Icons and frames are the same shape - they differ only
    /// in which list of the catalog they sit in and which slot they render into - so one definition
    /// serves both rather than two near-identical classes drifting apart.
    ///
    /// Public fields and CloneNormalized follow StoreItemDefinition, the closest existing
    /// equivalent in the project.
    /// </summary>
    [Serializable]
    public sealed class ProfileItemDefinition
    {
        /// <summary>
        /// Stable identity, and the ONLY part of this that persists (into
        /// PlayerData.ProfilePictureId / ProfileFrameId and the owned list).
        ///
        /// Never reuse or renumber an id that has shipped: a player currently wearing it would
        /// silently drop back to the default, and anyone who owns it would lose it.
        /// </summary>
        public string Id = string.Empty;

        public Sprite Art;

        /// <summary>Shown in the picker. Optional - falls back to Id when blank.</summary>
        public string DisplayName = string.Empty;

        /// <summary>
        /// Hides the item from the picker entirely without deleting it, so a seasonal or
        /// not-yet-released item can sit finished in the catalog. A player already wearing a
        /// disabled item keeps wearing it - this gates the picker, not what is equipped.
        /// </summary>
        public bool Enabled = true;

        public ProfileUnlockKind Unlock = ProfileUnlockKind.Free;

        /// <summary>
        /// Unlock == Level only. Same convention as BoosterConfigData.UnLockLevel: 0 or lower is
        /// always unlocked, a positive value gates until the player reaches that level NUMBER
        /// (1-based, not the 0-based PlayerData.CurrentLevelIndex - see
        /// ProfileManager.CurrentLevelNumber).
        /// </summary>
        public int UnlockLevel = 0;

        /// <summary>Unlock == Purchase only. The product id handed to InAppManager.Purchase.</summary>
        public string ProductId = string.Empty;

        /// <summary>
        /// Unlock == Purchase only. Authored fallback shown until the store answers with a real
        /// localized price - see InAppManager.ResolveDisplayPrice, same fallback contract the
        /// store cards already use.
        /// </summary>
        public string PriceLabel = string.Empty;

        /// <summary>
        /// Unlock == Reward only. Names where it comes from ("Spin the Wheel", "Level 50 chest")
        /// so a locked cell tells the player what to chase. Purely presentational - the grant
        /// itself comes from whichever system calls ProfileManager.Grant.
        /// </summary>
        public string RewardHint = string.Empty;

        public ProfileItemDefinition CloneNormalized()
        {
            return new ProfileItemDefinition
            {
                Id = Id ?? string.Empty,
                Art = Art,
                DisplayName = DisplayName ?? string.Empty,
                Enabled = Enabled,
                Unlock = Unlock,
                UnlockLevel = Mathf.Max(0, UnlockLevel),
                ProductId = ProductId ?? string.Empty,
                PriceLabel = PriceLabel ?? string.Empty,
                RewardHint = RewardHint ?? string.Empty
            };
        }

        /// <summary>
        /// Usable at all - a blank id can never be selected or persisted, so it is treated as an
        /// unfinished authoring row rather than an item.
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(Id);

        public string ResolveDisplayName()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
        }
    }
}
