using Newtonsoft.Json;
using System;

namespace Shared.GameConfig
{
    /// <summary>
    /// Gating for the profile feature (Assets/Shared/Features/Profile) - whether the main menu
    /// shows it at all, and from which level. Content - the avatars and frames themselves - lives
    /// in ProfileCatalogAsset instead, so designers edit art and unlock rules per item without
    /// touching config JSON.
    ///
    /// Per-ITEM unlocks (level/purchase/reward) are a separate thing entirely, handled by
    /// ProfileItemDefinition. This is the gate on the whole feature.
    /// </summary>
    [Serializable]
    public sealed class ProfileConfigData
    {
        // Off hides the profile from the main menu outright - not "coming soon", just absent, as
        // though the feature did not ship.
        [JsonProperty("enabled")]
        public bool Enabled = true;

        /// <summary>
        /// Same -1/0/positive convention as BoosterConfigData.UnLockLevel and
        /// SpinTheWheelConfigData.UnlockLevel:
        /// -1 = built but not live yet, shown locked as "Coming Soon" with no level to reach.
        ///  0 or 1 = always available.
        ///  positive = locked until the player reaches that level NUMBER (1-based).
        /// </summary>
        [JsonProperty("unlockLevel")]
        public int UnlockLevel = 0;

        public ProfileConfigData CloneNormalized()
        {
            return new ProfileConfigData
            {
                Enabled = Enabled,
                // Floored at -1, NOT 0 like the other feature blocks here: -1 is a MEANINGFUL
                // value in this field (coming soon), and clamping it to 0 would silently flip the
                // feature from "not live yet" to "available to everyone".
                UnlockLevel = Math.Max(-1, UnlockLevel)
            };
        }
    }
}
