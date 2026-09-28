using System;
using Newtonsoft.Json;

namespace Shared.PlayerData
{
    public sealed class PlayerDataJsonReader
    {
        public PlayerDataReadResult Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return PlayerDataReadResult.Failed(new[] { "Player data JSON is empty." });
            }

            try
            {
                PlayerData data = JsonConvert.DeserializeObject<PlayerData>(json);
                if (data == null)
                {
                    return PlayerDataReadResult.Failed(new[] { "Player data JSON did not contain a data object." });
                }

                return PlayerDataReadResult.Loaded(data.CloneNormalized());
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                return PlayerDataReadResult.Failed(new[] { $"Player data JSON could not be parsed: {exception.Message}" });
            }
        }
    }
}
