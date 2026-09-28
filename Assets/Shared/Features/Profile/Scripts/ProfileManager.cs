using MangraGames;
// GlobalConfigRuntimeContext lives in Shared.Boot, not Shared.GameConfig - same pair of usings
// LeaderboardManager needs.
using Shared.Boot;
using Shared.GameConfig;
using System;
using UnityEngine;

using PlayerDataModel = Shared.PlayerData.PlayerData;

namespace Shared.Profile
{
    /// <summary>
    /// The player's identity - display name, profile icon, profile frame - and the single place
    /// that decides whether a given icon or frame is available to them.
    ///
    /// Deliberately NOT a singleton: assign this in the Inspector wherever it is needed (the
    /// leaderboard panel, the profile picker), the same way LeaderboardManager now works. A
    /// singleton here would hit exactly the bug that one had - this component naturally lives on a
    /// popup that ships inactive, and a duplicate guard on an object that only wakes when the
    /// popup opens destroys the popup.
    ///
    /// Everything routes through here rather than reading PlayerData directly, so the four unlock
    /// kinds (see ProfileUnlockKind) are answered in one place and a caller can never equip
    /// something the player does not have.
    /// </summary>
    public sealed class ProfileManager : MonoBehaviour
    {
        [SerializeField] private ProfileCatalogAsset catalog;

        [Header("Name Rules")]
        // Trimmed length. Enforced here rather than only on the input field, since Grant/rename
        // can also arrive from code paths that never touch the UI.
        [SerializeField] private int minNameLength = 2;
        [SerializeField] private int maxNameLength = 14;
        [SerializeField] private string fallbackName = "Player";

        private readonly ProfileSaveManager saveManager = new ProfileSaveManager();

        private ProfileOwnedData owned;
        private bool initialized;

        // Set by SetLevelProvider - see CurrentLevelNumber.
        private Func<int> levelNumberProvider;

        /// <summary>
        /// Fired after ANY change - rename, equip, or grant. The picker and the leaderboard row
        /// both just re-read state rather than being told what specifically moved; there is not
        /// enough of it for a finer-grained event to earn its keep.
        /// </summary>
        public event Action ProfileChanged;

        public ProfileCatalogAsset Catalog => catalog;

        public int MinNameLength => Mathf.Max(1, minNameLength);

        public int MaxNameLength => Mathf.Max(MinNameLength, maxNameLength);

        // =========================================================
        // FEATURE STATE
        // =========================================================

        /// <summary>
        /// Config gate on the whole feature, off means the main menu shows nothing at all - not
        /// "coming soon", absent.
        /// </summary>
        public bool IsFeatureEnabled => ResolveConfig().Enabled;

        /// <summary>-1 = coming soon, 0/1 = always on, positive = the level number to reach.</summary>
        public int UnlockLevel => ResolveConfig().UnlockLevel;

        /// <summary>
        /// Built but not live yet: shown locked with no level to chase. Same reading as
        /// SpinWheelManager.IsComingSoon and DailyChallengeButtonUI's own -1 check.
        /// </summary>
        public bool IsComingSoon => UnlockLevel == -1;

        /// <summary>
        /// The one answer to "can the player open the profile at all?". Note this is a different
        /// question from ProfileItemDefinition's per-item unlocks - a player can have the feature
        /// unlocked and still not own a particular frame.
        /// </summary>
        public bool IsFeatureUnlocked => IsFeatureEnabled && !IsComingSoon && CurrentLevelNumber >= UnlockLevel;

        /// <summary>
        /// 1-based level number, matching what UnlockLevel is authored against and what
        /// CatFlapGameplayManager.CurrentLevelNumber reports.
        ///
        /// Prefers the provider a menu handed over via SetLevelProvider - during a session the
        /// gameplay manager's live number is ahead of what PlayerData has been saved with - and
        /// falls back to the saved index, which is what matters on a cold main menu before any
        /// level has loaded. PlayerData stores the 0-based index, so an UnlockLevel of 5 must not
        /// fire on index 5 (level 6).
        /// </summary>
        public int CurrentLevelNumber
        {
            get
            {
                if (levelNumberProvider != null)
                {
                    return levelNumberProvider.Invoke();
                }

                return Mathf.Max(0, PlayerDataModel.Instance.CurrentLevelIndex) + 1;
            }
        }

        /// <summary>
        /// Lets the main menu supply the live level number, same handshake
        /// SpinWheelMenuButtonUIController.SetLevel and DailyChallengeButtonUI.Configure use.
        /// Optional - without it the saved PlayerData index is used.
        /// </summary>
        public void SetLevelProvider(Func<int> provider)
        {
            levelNumberProvider = provider;
        }

        /// <summary>
        /// Loads the owned list. Safe to call more than once.
        ///
        /// Nothing has to call this: every public entry point below runs EnsureInitialized first,
        /// for the same reason LeaderboardManager does - this component is expected to sit on a
        /// popup that ships inactive, so neither Awake nor Start is guaranteed to have run by the
        /// time a leaderboard row asks for the player's icon.
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            owned = saveManager.LoadOwned();
        }

        // =========================================================
        // NAME
        // =========================================================

        public string PlayerName
        {
            get
            {
                EnsureInitialized();
                string stored = saveManager.LoadName();
                return string.IsNullOrWhiteSpace(stored) ? fallbackName : stored;
            }
        }

        /// <summary>
        /// Checks a name WITHOUT saving it, and hands back the trimmed form that would be stored.
        ///
        /// Split out from TrySetPlayerName so the picker can tell the player a name is too short
        /// the moment they stop typing, while still writing nothing until they press Confirm.
        /// </summary>
        public bool ValidateName(string candidate, out string trimmed, out string rejectionReason)
        {
            trimmed = (candidate ?? string.Empty).Trim();

            if (trimmed.Length < MinNameLength)
            {
                rejectionReason = $"Name must be at least {MinNameLength} characters.";
                return false;
            }

            if (trimmed.Length > MaxNameLength)
            {
                rejectionReason = $"Name must be {MaxNameLength} characters or fewer.";
                return false;
            }

            rejectionReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Validates and SAVES a rename. Returns false without writing anything when the name is
        /// refused, so a caller can show the reason and leave the field for the player to fix.
        /// </summary>
        public bool TrySetPlayerName(string candidate, out string rejectionReason)
        {
            EnsureInitialized();

            if (!ValidateName(candidate, out string trimmed, out rejectionReason))
            {
                return false;
            }

            // No-op renames must not write: this saves the whole PlayerData file, and the picker
            // calls it on every Confirm whether or not the field was touched.
            if (string.Equals(trimmed, PlayerName, StringComparison.Ordinal))
            {
                return true;
            }

            saveManager.SaveName(trimmed);
            ProfileChanged?.Invoke();
            return true;
        }

        // =========================================================
        // EQUIPPED
        // =========================================================

        /// <summary>
        /// What the player is currently wearing. Falls back to the catalog default when the saved
        /// id is blank (a fresh install) or names something the catalog no longer has (an item
        /// removed in a later build) - never returns a dangling id.
        /// </summary>
        public ProfileItemDefinition GetEquipped(ProfileSlot slot)
        {
            EnsureInitialized();
            if (catalog == null)
            {
                return null;
            }

            ProfileItemDefinition stored = catalog.FindById(slot, saveManager.LoadEquippedId(slot));
            return stored ?? catalog.ResolveDefault(slot);
        }

        public Sprite GetEquippedArt(ProfileSlot slot)
        {
            return GetEquipped(slot)?.Art;
        }

        public bool IsEquipped(ProfileSlot slot, ProfileItemDefinition item)
        {
            if (item == null || !item.IsValid)
            {
                return false;
            }

            ProfileItemDefinition equipped = GetEquipped(slot);
            return equipped != null && string.Equals(equipped.Id, item.Id, StringComparison.Ordinal);
        }

        /// <summary>
        /// Equips an item the player has. Refuses anything still locked, so a mis-wired button
        /// cannot hand out a paid frame - the picker's own locked/unlocked display is presentation,
        /// this is the actual gate.
        /// </summary>
        public bool TryEquip(ProfileSlot slot, ProfileItemDefinition item)
        {
            EnsureInitialized();

            if (item == null || !item.IsValid || !IsUnlocked(slot, item))
            {
                return false;
            }

            if (IsEquipped(slot, item))
            {
                return true;
            }

            saveManager.SaveEquippedId(slot, item.Id);
            ProfileChanged?.Invoke();
            return true;
        }

        // =========================================================
        // UNLOCKS
        // =========================================================

        /// <summary>
        /// The one answer to "can the player wear this?". Free is always yes; Level compares
        /// against their progression; Purchase and Reward are yes only once granted.
        /// </summary>
        public bool IsUnlocked(ProfileSlot slot, ProfileItemDefinition item)
        {
            EnsureInitialized();

            if (item == null || !item.IsValid)
            {
                return false;
            }

            switch (item.Unlock)
            {
                case ProfileUnlockKind.Free:
                    return true;

                case ProfileUnlockKind.Level:
                    // Same -1/0/positive reading as BoosterConfig.IsActive: 0 or lower is always
                    // unlocked, a positive value is the level number to reach.
                    return item.UnlockLevel <= 0 || CurrentLevelNumber >= item.UnlockLevel;

                case ProfileUnlockKind.Purchase:
                case ProfileUnlockKind.Reward:
                    return owned != null && owned.Contains(slot, item.Id);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Grants an item permanently - the completion path for both a finished purchase and any
        /// reward system handing one out (spin the wheel, a daily reward, an event).
        ///
        /// Public and unconditional by design: the caller has already decided the player earned
        /// it. Granting a Free or Level item is harmless but pointless - those never read the
        /// owned list - so it is ignored rather than written.
        /// </summary>
        public bool Grant(ProfileSlot slot, string itemId)
        {
            EnsureInitialized();

            ProfileItemDefinition item = catalog != null ? catalog.FindById(slot, itemId) : null;
            if (item == null || !item.IsValid)
            {
                return false;
            }

            if (item.Unlock != ProfileUnlockKind.Purchase && item.Unlock != ProfileUnlockKind.Reward)
            {
                return false;
            }

            if (!owned.Add(slot, item.Id))
            {
                return false;
            }

            saveManager.SaveOwned(owned);
            ProfileChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Buys a Purchase item and grants it on success. onComplete reports the final outcome
        /// (false on a store failure, a cancel, or a misconfigured item), so a caller can restore
        /// its button state either way.
        ///
        /// Nothing is granted until the store says yes - InAppManager owns the loading overlay and
        /// the success/failure popups, exactly as the store cards use it.
        /// </summary>
        public void Purchase(ProfileSlot slot, ProfileItemDefinition item, Action<bool> onComplete = null)
        {
            EnsureInitialized();

            if (item == null || !item.IsValid || item.Unlock != ProfileUnlockKind.Purchase || string.IsNullOrWhiteSpace(item.ProductId))
            {
                onComplete?.Invoke(false);
                return;
            }

            if (IsUnlocked(slot, item))
            {
                onComplete?.Invoke(true);
                return;
            }

            InAppManager.Instance.Purchase(item.ProductId, success =>
            {
                if (success)
                {
                    Grant(slot, item.Id);
                }

                onComplete?.Invoke(success);
            });
        }

        /// <summary>
        /// Price to show on a Purchase item: the store's own localized string when it has one,
        /// otherwise the authored PriceLabel. Same contract as the store cards
        /// (InAppManager.ResolveDisplayPrice).
        /// </summary>
        public string ResolvePriceLabel(ProfileItemDefinition item)
        {
            if (item == null || item.Unlock != ProfileUnlockKind.Purchase)
            {
                return string.Empty;
            }

            return InAppManager.Instance.ResolveDisplayPrice(item.ProductId, item.PriceLabel);
        }

        // =========================================================
        // RIVALS
        // =========================================================

        /// <summary>
        /// A look for one of the leaderboard's simulated rivals, by an index frozen in their saved
        /// state (see LeaderboardManager.CreateRival). Bypasses every unlock rule on purpose -
        /// rivals are set dressing, and gating their appearance on the local player's purchases
        /// would be meaningless.
        /// </summary>
        public Sprite ResolveArtByIndex(ProfileSlot slot, int index)
        {
            return catalog != null ? catalog.GetByIndex(slot, index)?.Art : null;
        }

        /// <summary>
        /// The art for a catalog item id, ignoring whether it is unlocked or equipped.
        ///
        /// For anything that recorded WHAT WAS WORN at some past moment and has to draw it again
        /// later - a finished leaderboard's podium, a replay, a screenshot. Reading the live
        /// equipment instead would redraw history with today's outfit.
        /// </summary>
        public Sprite ResolveArtById(ProfileSlot slot, string id)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return catalog.FindById(slot, id)?.Art;
        }

        // =========================================================
        // INTERNAL
        // =========================================================

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Never null - a project whose GlobalConfig has no profile section yet falls back to the
        /// defaults (enabled, no level gate), same as LeaderboardManager.ResolveConfig does.
        /// </summary>
        private static ProfileConfigData ResolveConfig()
        {
            GlobalConfig config = GlobalConfigRuntimeContext.Config;
            return config?.Profile ?? new ProfileConfigData();
        }
    }
}
