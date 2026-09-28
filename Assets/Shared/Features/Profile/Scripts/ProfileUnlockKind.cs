namespace Shared.Profile
{
    /// <summary>
    /// How a profile icon or frame is obtained.
    ///
    /// The kind decides where "does the player have this?" is answered from, which is the only
    /// thing that actually differs between them:
    /// - Free and Level are DERIVED - recomputed every time from nothing at all, or from the
    ///   player's level - and persist nothing.
    /// - Purchase and Reward are GRANTED, so the id is written into the owned list and stays there
    ///   for the life of the install.
    ///
    /// That split is why a level-gated item can never be "lost" by a save wipe of the owned list,
    /// and why re-pricing or re-gating an item in the catalog takes effect immediately without a
    /// migration.
    /// </summary>
    public enum ProfileUnlockKind
    {
        /// <summary>Always available. The common section every player can pick from.</summary>
        Free = 0,

        /// <summary>Gated on the player reaching UnlockLevel. Nothing to grant or persist.</summary>
        Level = 1,

        /// <summary>Bought through InAppManager with ProductId, then granted permanently.</summary>
        Purchase = 2,

        /// <summary>
        /// Handed out by some other system (spin the wheel, daily reward, an event) calling
        /// ProfileManager.Grant. Locked and unbuyable until then - the picker only ever shows
        /// RewardHint, never a way to obtain it directly.
        /// </summary>
        Reward = 3
    }
}
