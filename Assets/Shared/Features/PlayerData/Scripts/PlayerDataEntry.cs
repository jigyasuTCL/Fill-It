using System;

namespace Shared.PlayerData
{
    [Serializable]
    public sealed class PlayerDataEntry
    {
        public string Key;
        public string Value;

        public PlayerDataEntry()
        {
        }

        public PlayerDataEntry(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
