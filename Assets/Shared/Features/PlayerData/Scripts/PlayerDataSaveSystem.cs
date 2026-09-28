using Newtonsoft.Json;
using Shared.FileStorage;
using UnityEngine;
using SharedFileStorage = Shared.FileStorage.FileStorage;

namespace Shared.PlayerData
{
    public sealed class PlayerDataSaveSystem
    {
        private readonly PlayerDataJsonReader reader = new PlayerDataJsonReader();
        private readonly SharedFileStorage storage;

        public PlayerDataSaveSystem()
            : this(new SharedFileStorage())
        {
        }

        public PlayerDataSaveSystem(SharedFileStorage storage)
        {
            this.storage = storage ?? new SharedFileStorage();
        }

        public PlayerData LoadOrCreate(string fileName, int initialCoins)
        {
            string path = CreatePath(fileName);
            FileReader fileResult = storage.ReadAllText(path);
            if (!fileResult.IsLoaded)
            {
                return CreateNew(initialCoins);
            }

            PlayerDataReadResult result = reader.Read(fileResult.Text);
            if (result.IsLoaded)
            {
                return result.Data;
            }

            return CreateNew(initialCoins);
        }

        public void Save(string fileName, PlayerData data)
        {
            if (data == null)
            {
                return;
            }

            data.Touch();
            string path = CreatePath(fileName);
            storage.WriteAllText(path, JsonConvert.SerializeObject(data.CloneNormalized(), Formatting.Indented));
        }

        public string CreatePath(string fileName)
        {
            return storage.CreatePersistentDataPath(fileName, PlayerDataDefaults.FileName);
        }

        private static PlayerData CreateNew(int initialCoins)
        {
            return new PlayerData
            {
                CurrentLevelIndex = 0,
                TotalCoins = Mathf.Max(0, initialCoins)
            }.CloneNormalized();
        }
    }
}
