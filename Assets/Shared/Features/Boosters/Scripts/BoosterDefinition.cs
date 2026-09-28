using System;

namespace Shared.Boosters
{
    [Serializable]
    public sealed class BoosterDefinition
    {
        public int UnLockLevel = -1;
        public int UnLockCount = 3;
        public int BuyCoinCost = 50;
        public int BuyCount = 1;
        public int AdBuyCount = 1;

        public BoosterDefinition CloneNormalized()
        {
            return new BoosterDefinition
            {
                UnLockLevel = UnLockLevel,
                UnLockCount = Math.Max(0, UnLockCount),
                BuyCoinCost = Math.Max(0, BuyCoinCost),
                BuyCount = Math.Max(1, BuyCount),
                AdBuyCount = Math.Max(1, AdBuyCount)
            };
        }
    }
}
