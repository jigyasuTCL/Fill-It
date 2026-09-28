using System.Collections.Generic;
using UnityEngine;

namespace Shared.Profile
{
    /// <summary>
    /// Every profile avatar and frame in the game, grouped into the titled bands the picker renders
    /// ("Classic", and whatever follows). Content lives here - in an asset a designer can edit -
    /// rather than in GlobalConfig's JSON, for the same reason LeaderboardRivalPoolAsset does:
    /// these are sprites plus authored copy, not tunable numbers.
    ///
    /// Order is meaningful twice over. The picker renders categories and their items in list order,
    /// and the FIRST valid Free item in a slot is the default a player starts with - so keep the
    /// plain starter avatar and frame at the top of the first category.
    /// </summary>
    [CreateAssetMenu(fileName = "ProfileCatalog", menuName = "MangraGames/Profile Catalog")]
    public sealed class ProfileCatalogAsset : ScriptableObject
    {
        [SerializeField] private List<ProfileCategory> avatarCategories = new List<ProfileCategory>();
        [SerializeField] private List<ProfileCategory> frameCategories = new List<ProfileCategory>();

        // Every item of a slot in authored order, ignoring category boundaries. Built on demand and
        // kept, because the two callers that want it - id lookup and the leaderboard's per-rival
        // index - run per row of a board that can be twenty rows long, and neither cares about
        // grouping. Cleared by OnValidate so editing the asset in play mode is still picked up.
        private List<ProfileItemDefinition> flatAvatars;
        private List<ProfileItemDefinition> flatFrames;

        public IReadOnlyList<ProfileCategory> GetCategories(ProfileSlot slot)
        {
            if (slot == ProfileSlot.Frame)
            {
                return frameCategories ?? (frameCategories = new List<ProfileCategory>());
            }

            return avatarCategories ?? (avatarCategories = new List<ProfileCategory>());
        }

        /// <summary>
        /// Every valid item of a slot, flattened across categories. Includes disabled items and
        /// disabled categories on purpose: a player already wearing something that was later held
        /// back must keep rendering it, and a rival's frozen index must keep resolving. The PICKER
        /// is what filters on Enabled, because that is the only place the flag means anything.
        /// </summary>
        public IReadOnlyList<ProfileItemDefinition> GetAll(ProfileSlot slot)
        {
            if (slot == ProfileSlot.Frame)
            {
                return flatFrames ?? (flatFrames = Flatten(frameCategories));
            }

            return flatAvatars ?? (flatAvatars = Flatten(avatarCategories));
        }

        public ProfileItemDefinition FindById(ProfileSlot slot, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            IReadOnlyList<ProfileItemDefinition> all = GetAll(slot);
            for (int index = 0; index < all.Count; index++)
            {
                if (string.Equals(all[index].Id, id, System.StringComparison.Ordinal))
                {
                    return all[index];
                }
            }

            return null;
        }

        /// <summary>
        /// What a player wears before they have ever opened the picker, and what an unknown saved
        /// id falls back to. The first valid Free item rather than simply the first - a default the
        /// player cannot actually own would leave them stuck looking locked.
        /// </summary>
        public ProfileItemDefinition ResolveDefault(ProfileSlot slot)
        {
            IReadOnlyList<ProfileItemDefinition> all = GetAll(slot);
            for (int index = 0; index < all.Count; index++)
            {
                if (all[index].Unlock == ProfileUnlockKind.Free)
                {
                    return all[index];
                }
            }

            // No free item authored at all - fall back to the first valid one, so the board still
            // renders something rather than an empty avatar.
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Indexed access for the leaderboard's simulated rivals, which pick a look by index once
        /// per install and keep it forever (see LeaderboardManager.CreateRival). Wraps rather than
        /// throwing, exactly like LeaderboardRivalPoolAsset.GetName, so a saved rival that outlived
        /// a shortened catalog still renders.
        ///
        /// Deliberately ignores every unlock rule: a rival is set dressing, not a player, and
        /// gating their look on the local player's purchases would be meaningless.
        /// </summary>
        public ProfileItemDefinition GetByIndex(ProfileSlot slot, int index)
        {
            IReadOnlyList<ProfileItemDefinition> all = GetAll(slot);
            if (all.Count == 0)
            {
                return null;
            }

            int wrapped = ((index % all.Count) + all.Count) % all.Count;
            return all[wrapped];
        }

        private static List<ProfileItemDefinition> Flatten(List<ProfileCategory> categories)
        {
            var flat = new List<ProfileItemDefinition>();
            if (categories == null)
            {
                return flat;
            }

            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                ProfileCategory category = categories[categoryIndex];
                if (category?.Items == null)
                {
                    continue;
                }

                for (int itemIndex = 0; itemIndex < category.Items.Count; itemIndex++)
                {
                    ProfileItemDefinition item = category.Items[itemIndex];
                    if (item != null && item.IsValid)
                    {
                        flat.Add(item);
                    }
                }
            }

            return flat;
        }

        private void OnValidate()
        {
            // Authoring the asset while playing otherwise keeps serving the list flattened before
            // the edit.
            flatAvatars = null;
            flatFrames = null;
        }
    }
}
