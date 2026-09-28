using Newtonsoft.Json;
using Shared.PlayerData;

// Shared.PlayerData.PlayerData collides with its own namespace - alias it rather than scattering
// fully-qualified names, same problem LeaderboardSaveManager/VibrationManager already hit.
using PlayerDataModel = Shared.PlayerData.PlayerData;

namespace Shared.Profile
{
    /// <summary>
    /// Reads/writes everything the profile persists, following LeaderboardSaveManager's structure
    /// exactly.
    ///
    /// Split across two storage styles on purpose:
    /// - name, equipped avatar and equipped frame go into PlayerData's own Username /
    ///   ProfilePictureId / ProfileFrameId FIELDS, which have existed on the model since before
    ///   this feature and were simply never written to. Anything reading a player's identity
    ///   (analytics, a future backend) finds them where it would expect, not buried in a blob.
    /// - the owned list goes into the Variables list as JSON, the string-key convention every
    ///   other feature uses, because it is this feature's business alone.
    /// </summary>
    public sealed class ProfileSaveManager
    {
        private const string OwnedKey = "CatFlap.Profile.Owned";

        private readonly PlayerDataSaveSystem saveSystem = new PlayerDataSaveSystem();

        public string LoadName()
        {
            return LoadPlayerData().Username;
        }

        public string LoadEquippedId(ProfileSlot slot)
        {
            PlayerDataModel data = LoadPlayerData();
            return slot == ProfileSlot.Frame ? data.ProfileFrameId : data.ProfilePictureId;
        }

        public void SaveName(string name)
        {
            PlayerDataModel data = LoadPlayerData();
            data.Username = name;
            data.Touch();
            SavePlayerData(data);
        }

        public void SaveEquippedId(ProfileSlot slot, string id)
        {
            PlayerDataModel data = LoadPlayerData();
            if (slot == ProfileSlot.Frame)
            {
                data.ProfileFrameId = id ?? string.Empty;
            }
            else
            {
                data.ProfilePictureId = id ?? string.Empty;
            }

            data.Touch();
            SavePlayerData(data);
        }

        /// <summary>
        /// Never null - a first run, an empty value, corrupt JSON or a foreign schema version all
        /// return an empty owned list rather than failing. Same four-case fallback as
        /// LeaderboardSaveManager.LoadState, except the caller here has nothing to regenerate:
        /// owning nothing is a perfectly valid state.
        /// </summary>
        public ProfileOwnedData LoadOwned()
        {
            PlayerDataModel data = LoadPlayerData();
            if (!data.TryGetString(OwnedKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return new ProfileOwnedData();
            }

            try
            {
                ProfileOwnedData owned = JsonConvert.DeserializeObject<ProfileOwnedData>(json);
                if (owned == null || owned.Version != ProfileOwnedData.CurrentVersion)
                {
                    return new ProfileOwnedData();
                }

                // Deserialising JSON that omitted either array leaves the field null, which every
                // caller would then have to guard. Normalise once, here.
                owned.GetList(ProfileSlot.Avatar);
                owned.GetList(ProfileSlot.Frame);
                return owned;
            }
            catch (JsonException)
            {
                return new ProfileOwnedData();
            }
        }

        public void SaveOwned(ProfileOwnedData owned)
        {
            if (owned == null)
            {
                return;
            }

            PlayerDataModel data = LoadPlayerData();
            data.SetString(OwnedKey, JsonConvert.SerializeObject(owned));
            data.Touch();
            SavePlayerData(data);
        }

        private PlayerDataModel LoadPlayerData()
        {
            if (!PlayerDataRuntimeContext.HasData)
            {
                PlayerDataModel created = saveSystem.LoadOrCreate(PlayerDataDefaults.FileName, 0);
                PlayerDataModel.Init(created, PlayerDataDefaults.FileName);
            }

            return PlayerDataModel.Instance;
        }

        private void SavePlayerData(PlayerDataModel data)
        {
            string fileName = string.IsNullOrWhiteSpace(PlayerDataRuntimeContext.FileName)
                ? PlayerDataDefaults.FileName
                : PlayerDataRuntimeContext.FileName;

            saveSystem.Save(fileName, data);
        }
    }
}
