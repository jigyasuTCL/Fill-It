using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Shared.FileStorage;
using SharedFileStorage = Shared.FileStorage.FileStorage;

namespace Shared.GameConfig
{
    public sealed class GlobalConfigJsonReader
    {
        private readonly SharedFileStorage storage;

        public GlobalConfigJsonReader()
            : this(new SharedFileStorage())
        {
        }

        public GlobalConfigJsonReader(SharedFileStorage storage)
        {
            this.storage = storage ?? new SharedFileStorage();
        }

        public GlobalConfigReadResult Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return GlobalConfigReadResult.Failed(new[] { "Global config JSON is empty." });
            }

            try
            {
                GlobalConfig config = JsonConvert.DeserializeObject<GlobalConfig>(json);
                if (config == null)
                {
                    return GlobalConfigReadResult.Failed(new[] { "Global config JSON did not contain a config object." });
                }

                GlobalConfig normalized = config.CloneNormalized();
                var warnings = new List<string>();
                if (config.TotalLevels < 0)
                {
                    warnings.Add("totalLevels was below zero and was clamped to zero.");
                }

                return GlobalConfigReadResult.Loaded(normalized, warnings);
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                return GlobalConfigReadResult.Failed(new[] { $"Global config JSON could not be parsed: {exception.Message}" });
            }
        }

        public GlobalConfigReadResult ReadFromFilePath(string path)
        {
            FileReader result = storage.ReadAllText(path);
            if (!result.IsLoaded)
            {
                return GlobalConfigReadResult.Failed(result.Errors);
            }

            return Read(result.Text);
        }
    }
}
