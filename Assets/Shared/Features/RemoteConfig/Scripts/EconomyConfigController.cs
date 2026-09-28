namespace Shared.RemoteConfig
{
    using System.Collections.Generic;

    using Newtonsoft.Json;

    using UnityEngine;

    /// <summary>
    /// Economy-related Remote Config values. Gameplay reads the static properties here directly -
    /// it never touches Firebase for these values. Backed by a single Remote Config parameter
    /// ("economyConfig") whose value is a JSON object (see EconomyConfigData), not one flat key
    /// per value.
    /// </summary>
    public static class EconomyConfigController
    {
        private const string ConfigKey = "economyConfig";

        public static int LevelWinCoins { get; private set; } = 10;

        /// <summary>
        /// Granted instead of LevelWinCoins when the level just won is listed in
        /// GlobalConfig.HardLevels - see CatFlapGameplayManager.ResolveLevelWinCoinReward.
        /// </summary>
        public static int HardLevelWinCoins { get; private set; } = 20;

        /// <summary>
        /// Coin cost to refill attempts from the Out of Lives popup - see
        /// CatFlapGameplayManager.PlayHeartBreakThenShowOutOfLivesPopup/HandleOutOfLivesRefill.
        /// Was GlobalConfig.OutOfLivesRefillCoinCost before this was migrated to Remote Config
        /// (LFRetryCoinCost/levelReviveCoins was a separate, never-consumed field - this property
        /// replaces the one that's actually used in gameplay, not that dead one).
        /// </summary>
        public static int OutOfLivesRefillCoinCost { get; private set; }

        /// <summary>
        /// Applies safe local defaults, sourced from FirebaseRemoteConfigManager's own Inspector
        /// fields (see its "Economy Config - Local Defaults" header) - called once before any
        /// Firebase call is made, so these properties are always usable immediately at boot.
        /// </summary>
        public static void ApplyDefaults(int winCoins, int hardWinCoins, int outOfLivesRefillCoinCost)
        {
            LevelWinCoins = Mathf.Max(0, winCoins);
            HardLevelWinCoins = Mathf.Max(0, hardWinCoins);
            OutOfLivesRefillCoinCost = Mathf.Max(0, outOfLivesRefillCoinCost);
        }

        /// <summary>
        /// Overlays the "economyConfig" Remote Config parameter's JSON on top of the current
        /// values. A missing parameter, empty value, or JSON that fails to parse leaves every
        /// property untouched. JsonConvert.PopulateObject only overwrites fields actually present
        /// in the JSON (starting from a snapshot of the current values), so a payload missing one
        /// field - e.g. just {"levelWinCoins": 250} - still only changes that one property.
        /// </summary>
        public static void ApplyRemoteConfig(IDictionary<string, string> remoteConfig)
        {
            if (remoteConfig == null || !remoteConfig.TryGetValue(ConfigKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var data = new EconomyConfigData
            {
                LevelWinCoins = LevelWinCoins,
                HardLevelWinCoins = HardLevelWinCoins,
                LevelReviveCoins = OutOfLivesRefillCoinCost
            };

            try
            {
                JsonConvert.PopulateObject(json, data);
            }
            catch (JsonException)
            {
                return;
            }

            LevelWinCoins = Mathf.Max(0, data.LevelWinCoins);
            HardLevelWinCoins = Mathf.Max(0, data.HardLevelWinCoins);
            OutOfLivesRefillCoinCost = Mathf.Max(0, data.LevelReviveCoins);
        }
    }
}
