using System;
using UnityEngine;

namespace Shared.Store
{
    [Serializable]
    public sealed class StoreItemDefinition
    {
        public string ItemId = string.Empty;
        public StoreItemType Type = StoreItemType.CoinPack;
        public bool Enabled = true;

        public Sprite Icon;
        public string IconKey = string.Empty;
        public string Title = string.Empty;
        public string Description = string.Empty;

        public int CoinAmount = 0;

        public string ProductId = string.Empty;
        public string PriceLabel = string.Empty;

        public bool HasDiscount = false;
        public string OldPriceLabel = string.Empty;

        public bool IsTimerBased = false;
        public float TimerHours = 4f;

        public bool IsRepeatable = true;
        public int MaxPurchaseCount = 0;

        public bool HasCooldown = false;
        public float CooldownHours = 0f;

        public StoreItemDefinition CloneNormalized()
        {
            return new StoreItemDefinition
            {
                ItemId = ItemId ?? string.Empty,
                Type = Type,
                Enabled = Enabled,
                Icon = Icon,
                IconKey = IconKey ?? string.Empty,
                Title = Title ?? string.Empty,
                Description = Description ?? string.Empty,
                CoinAmount = Mathf.Max(0, CoinAmount),
                ProductId = ProductId ?? string.Empty,
                PriceLabel = PriceLabel ?? string.Empty,
                HasDiscount = HasDiscount,
                OldPriceLabel = OldPriceLabel ?? string.Empty,
                IsTimerBased = IsTimerBased,
                TimerHours = Mathf.Max(0f, TimerHours),
                IsRepeatable = IsRepeatable,
                MaxPurchaseCount = Mathf.Max(0, MaxPurchaseCount),
                HasCooldown = HasCooldown,
                CooldownHours = Mathf.Max(0f, CooldownHours)
            };
        }

        // IconKey takes priority (looked up in the shared IconDatabaseConfig, same convention as
        // CollectScreenController.iconDatabase) - falls back to Icon, the sprite authored directly
        // on this item in the Store Config asset, when there's no key/database or no match.
        public Sprite ResolveIcon(IconDatabaseConfig iconDatabase)
        {
            if (iconDatabase != null && !string.IsNullOrEmpty(IconKey) &&
                iconDatabase.TryGetIcon(IconKey, out Sprite icon))
            {
                return icon;
            }

            return Icon;
        }
    }
}
