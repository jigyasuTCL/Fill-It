using System;

namespace Shared.Boosters
{
    [Serializable]
    public sealed class BoosterConfig
    {
        private const int BoosterCount = 3;

        public int InitialCoins = 100;
        public BoosterDefinition[] BoosterConfigList =
        {
            new BoosterDefinition { UnLockLevel = 5, UnLockCount = 3, BuyCoinCost = 50, BuyCount = 1, AdBuyCount = 1 },
            new BoosterDefinition { UnLockLevel = 7, UnLockCount = 3, BuyCoinCost = 75, BuyCount = 1, AdBuyCount = 1 },
            new BoosterDefinition { UnLockLevel = 10, UnLockCount = 3, BuyCoinCost = 100, BuyCount = 1, AdBuyCount = 1 }
        };

        public int GetCost(BoosterType boosterType)
        {
            return GetDefinition(boosterType).BuyCoinCost;
        }

        public int GetFreeCount(BoosterType boosterType)
        {
            return GetDefinition(boosterType).UnLockCount;
        }

        public int GetUnlockLevel(BoosterType boosterType)
        {
            return GetDefinition(boosterType).UnLockLevel;
        }

        public bool IsActive(BoosterType boosterType)
        {
            return GetUnlockLevel(boosterType) >= 0;
        }

        public int GetBuyCount(BoosterType boosterType)
        {
            return GetDefinition(boosterType).BuyCount;
        }
        public int GetAdBuyCount(BoosterType boosterType)
        {
            return GetDefinition(boosterType).AdBuyCount;
        }

        public BoosterConfig Clone()
        {
            return new BoosterConfig
            {
                InitialCoins = Math.Max(0, InitialCoins),
                BoosterConfigList = NormalizeBoosterConfigList(BoosterConfigList)
            };
        }

        private BoosterDefinition GetDefinition(BoosterType boosterType)
        {
            int index = (int)boosterType;
            if (index < 0 || index >= BoosterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(boosterType), boosterType, null);
            }

            BoosterDefinition[] normalized = NormalizeBoosterConfigList(BoosterConfigList);
            return normalized[index];
        }

        private static BoosterDefinition[] NormalizeBoosterConfigList(BoosterDefinition[] configs)
        {
            var normalized = new BoosterDefinition[BoosterCount];
            for (int index = 0; index < BoosterCount; index++)
            {
                BoosterDefinition source = configs != null && index < configs.Length && configs[index] != null
                    ? configs[index]
                    : CreateDefault(index);
                normalized[index] = source.CloneNormalized();
            }

            return normalized;
        }

        private static BoosterDefinition CreateDefault(int index)
        {
            switch (index)
            {
                case 0:
                    return new BoosterDefinition { UnLockLevel = 5, UnLockCount = 3, BuyCoinCost = 50, BuyCount = 1, AdBuyCount = 1 };
                case 1:
                    return new BoosterDefinition { UnLockLevel = 7, UnLockCount = 3, BuyCoinCost = 75, BuyCount = 1, AdBuyCount = 1 };
                default:
                    return new BoosterDefinition { UnLockLevel = 10, UnLockCount = 3, BuyCoinCost = 100, BuyCount = 1, AdBuyCount = 1 };
            }
        }
    }
}
