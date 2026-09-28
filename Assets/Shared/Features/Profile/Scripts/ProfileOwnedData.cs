using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Shared.Profile
{
    /// <summary>
    /// The ids the player has been GRANTED - bought (Purchase) or awarded (Reward). Free and
    /// Level items are never listed here: those are derived every time from the catalog and the
    /// player's level, so re-gating them in a later build takes effect immediately and no
    /// migration is possible to get wrong.
    ///
    /// Stored as JSON under a single PlayerData key, same shape and reasoning as
    /// LeaderboardStateData. Property names are one character because this re-serialises in full
    /// on every save.
    /// </summary>
    [Serializable]
    public sealed class ProfileOwnedData
    {
        // Bumped only when the shape below changes. An unknown version is discarded rather than
        // partially migrated - same call LeaderboardSaveManager.LoadState makes.
        //
        // NOTE: discarding this list takes real purchases with it. If this is ever bumped, add a
        // migration instead, or have the player restore purchases (InAppManager.RestorePurchases)
        // on the next launch.
        public const int CurrentVersion = 1;

        [JsonProperty("v")]
        public int Version = CurrentVersion;

        [JsonProperty("i")]
        public List<string> OwnedAvatarIds = new List<string>();

        [JsonProperty("f")]
        public List<string> OwnedFrameIds = new List<string>();

        public List<string> GetList(ProfileSlot slot)
        {
            if (slot == ProfileSlot.Frame)
            {
                return OwnedFrameIds ?? (OwnedFrameIds = new List<string>());
            }

            return OwnedAvatarIds ?? (OwnedAvatarIds = new List<string>());
        }

        public bool Contains(ProfileSlot slot, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            List<string> list = GetList(slot);
            for (int index = 0; index < list.Count; index++)
            {
                if (string.Equals(list[index], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns true only when this actually changed something, so the caller can skip a pointless save.</summary>
        public bool Add(ProfileSlot slot, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || Contains(slot, id))
            {
                return false;
            }

            GetList(slot).Add(id);
            return true;
        }
    }
}
