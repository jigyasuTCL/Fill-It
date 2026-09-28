using System;
using System.Collections.Generic;

namespace Shared.PlayerData
{
    [Serializable]
    public sealed class PlayerData
    {
        public string PlayerId = Guid.NewGuid().ToString("N");
        public string Username = "Player";
        public string ProfilePictureId = string.Empty;
        public string ProfileFrameId = string.Empty;
        public int CurrentLevelIndex;
        public int TotalCoins;
        public bool IsSoundOn = true;
        public bool IsMusicOn = false;
        public bool IsHapticOn = true;
        public bool NoAdsPurchased;
        public string CreatedUtc = DateTime.UtcNow.ToString("O");
        public string LastUpdatedUtc = DateTime.UtcNow.ToString("O");
        public List<PlayerDataEntry> Variables = new List<PlayerDataEntry>();
        public PlayerAnalyticsData Analytics = new PlayerAnalyticsData();

        public static PlayerData Instance
        {
            get
            {
                if (PlayerDataRuntimeContext.HasData)
                {
                    return PlayerDataRuntimeContext.Data;
                }

                Init(new PlayerData());
                return PlayerDataRuntimeContext.Data;
            }
        }

        public static void Init(PlayerData data, string fileName = PlayerDataDefaults.FileName)
        {
            PlayerDataRuntimeContext.Set((data ?? new PlayerData()).CloneNormalized(), fileName);
        }

        public PlayerData CloneNormalized()
        {
            var clone = new PlayerData
            {
                PlayerId = string.IsNullOrWhiteSpace(PlayerId) ? Guid.NewGuid().ToString("N") : PlayerId,
                Username = string.IsNullOrWhiteSpace(Username) ? "Player" : Username,
                ProfilePictureId = ProfilePictureId ?? string.Empty,
                ProfileFrameId = ProfileFrameId ?? string.Empty,
                CurrentLevelIndex = Math.Max(0, CurrentLevelIndex),
                TotalCoins = Math.Max(0, TotalCoins),
                IsSoundOn = IsSoundOn,
                IsMusicOn = IsMusicOn,
                IsHapticOn = IsHapticOn,
                NoAdsPurchased = NoAdsPurchased,
                CreatedUtc = string.IsNullOrWhiteSpace(CreatedUtc) ? DateTime.UtcNow.ToString("O") : CreatedUtc,
                LastUpdatedUtc = string.IsNullOrWhiteSpace(LastUpdatedUtc) ? DateTime.UtcNow.ToString("O") : LastUpdatedUtc,
                Variables = new List<PlayerDataEntry>(),
                Analytics = (Analytics ?? new PlayerAnalyticsData()).Clone()
            };

            if (Variables != null)
            {
                foreach (PlayerDataEntry entry in Variables)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Key))
                    {
                        clone.SetString(entry.Key, entry.Value ?? string.Empty);
                    }
                }
            }

            return clone;
        }

        public bool TryGetString(string key, out string value)
        {
            PlayerDataEntry entry = FindEntry(key);
            value = entry?.Value;
            return entry != null;
        }

        public void SetString(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            PlayerDataEntry entry = FindEntry(key);
            if (entry == null)
            {
                Variables.Add(new PlayerDataEntry(key, value ?? string.Empty));
                return;
            }

            entry.Value = value ?? string.Empty;
        }

        public bool TryGetInt(string key, out int value)
        {
            value = 0;
            return TryGetString(key, out string rawValue) && int.TryParse(rawValue, out value);
        }

        public void SetInt(string key, int value)
        {
            SetString(key, value.ToString());
        }

        public void Touch()
        {
            LastUpdatedUtc = DateTime.UtcNow.ToString("O");
        }

        private PlayerDataEntry FindEntry(string key)
        {
            if (Variables == null)
            {
                Variables = new List<PlayerDataEntry>();
            }

            for (int index = 0; index < Variables.Count; index++)
            {
                PlayerDataEntry entry = Variables[index];
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
