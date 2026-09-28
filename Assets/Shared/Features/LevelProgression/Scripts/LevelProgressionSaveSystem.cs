using Newtonsoft.Json;
using Shared.FileStorage;
using UnityEngine;
using SharedFileStorage = Shared.FileStorage.FileStorage;

namespace Shared.LevelProgression
{
    public sealed class LevelProgressionSaveSystem
    {
        private readonly SharedFileStorage storage;

        public LevelProgressionSaveSystem()
            : this(new SharedFileStorage())
        {
        }

        public LevelProgressionSaveSystem(SharedFileStorage storage)
        {
            this.storage = storage ?? new SharedFileStorage();
        }

        public bool TryLoadRecord(string fileName, string scope, string progressionId, string levelId, out LevelProgressionRecord record)
        {
            LevelProgressionData data = Load(fileName);
            return data.TryGet(scope, progressionId, levelId, out record);
        }

        public void SaveRecord(string fileName, LevelProgressionRecord record)
        {
            SaveRecord(fileName, record, true);
        }

        public void SaveRecord(string fileName, LevelProgressionRecord record, bool keepHistory)
        {
            if (record == null)
            {
                return;
            }

            LevelProgressionData data = Load(fileName);
            LevelProgressionRecord normalized = record.CloneNormalized();
            normalized.Touch();
            if (!keepHistory)
            {
                data.RemoveAllExcept(normalized.Scope, normalized.ProgressionId, normalized.LevelId);
            }

            data.Upsert(normalized);
            Save(fileName, data);
        }

        public void ClearRecord(string fileName, string scope, string progressionId, string levelId)
        {
            LevelProgressionData data = Load(fileName);
            if (data.Remove(scope, progressionId, levelId))
            {
                Save(fileName, data);
            }
        }

        public LevelProgressionData Load(string fileName)
        {
            FileReader result = storage.ReadAllText(CreatePath(fileName));
            if (!result.IsLoaded)
            {
                return new LevelProgressionData();
            }

            LevelProgressionData data = JsonConvert.DeserializeObject<LevelProgressionData>(result.Text);
            return (data ?? new LevelProgressionData()).CloneNormalized();
        }

        public void Save(string fileName, LevelProgressionData data)
        {
            storage.WriteAllText(CreatePath(fileName), JsonConvert.SerializeObject((data ?? new LevelProgressionData()).CloneNormalized(), Formatting.Indented));
        }

        public string CreatePath(string fileName)
        {
            return storage.CreatePersistentDataPath(fileName, "level-progression.json");
        }
    }
}
