namespace Shared.RemoteConfig
{
    using System;
    using Newtonsoft.Json;

    /// <summary>
    /// JSON shape of the single "adsConfig" Remote Config parameter - see AdsConfigController.
    /// </summary>
    [Serializable]
    public sealed class AdsConfigData
    {
        [JsonProperty("interstitialAdsEnable")]
        public bool InterstitialAdsEnable;

        [JsonProperty("interstitialStartLevel")]
        public int InterstitialStartLevel;

        [JsonProperty("interstitialDelayBetweenAds")]
        public float InterstitialDelayBetweenAds;
    }

    /// <summary>
    /// JSON shape of the single "economyConfig" Remote Config parameter - see
    /// EconomyConfigController.
    /// </summary>
    [Serializable]
    public sealed class EconomyConfigData
    {
        [JsonProperty("levelWinCoins")]
        public int LevelWinCoins;

        [JsonProperty("hardLevelWinCoins")]
        public int HardLevelWinCoins;

        // Out of Lives refill coin cost - kept as "levelReviveCoins" in JSON for continuity with
        // the original Firebase key naming (see EconomyConfigController.OutOfLivesRefillCoinCost).
        [JsonProperty("levelReviveCoins")]
        public int LevelReviveCoins;
    }
}
