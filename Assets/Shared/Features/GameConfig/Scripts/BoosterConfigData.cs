using System;
using Newtonsoft.Json;

namespace Shared.GameConfig
{
    [Serializable]
    public sealed class BoosterConfigData
    {
        [JsonProperty("unLockLevel")]
        public int UnLockLevel = -1;

        [JsonProperty("unLockCount")]
        public int UnLockCount = 3;

        [JsonProperty("buyCoinCost")]
        public int BuyCoinCost = 50;

        [JsonProperty("buyCount")]
        public int BuyCount = 1;

        [JsonProperty("adBuyCount")]
        public int AdBuyCount = 1;

        public BoosterConfigData CloneNormalized()
        {
            return new BoosterConfigData
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
