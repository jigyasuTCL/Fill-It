namespace Shared.RemoteConfig
{
    using System.Collections.Generic;

    using Newtonsoft.Json;

    using UnityEngine;

    /// <summary>
    /// Ads-related Remote Config values. Gameplay (CatFlapGameplayManager) and any future ads code
    /// read the static properties here directly - they never touch Firebase for these values.
    /// Backed by a single Remote Config parameter ("adsConfig") whose value is a JSON object (see
    /// AdsConfigData), not one flat key per value.
    /// </summary>
    public static class AdsConfigController
    {
        private const string ConfigKey = "adsConfig";

        /// <summary>
        /// No dedicated local flag existed for this before Remote Config - interstitials were only
        /// ever gated by InterstitialStartLevel/cooldown - so the default is simply "on", preserving
        /// existing behavior until Remote Config explicitly turns it off.
        /// </summary>
        public static bool InterstitialAdsEnabled { get; private set; } = true;

        public static int InterstitialStartLevel { get; private set; } = 18;

        public static float InterstitialDelayBetweenAds { get; private set; } = 180f;

        /// <summary>
        /// Applies safe local defaults, sourced from FirebaseRemoteConfigManager's own Inspector
        /// fields (see its "Ads Config - Local Defaults" header) - called once before any Firebase
        /// call is made, so these properties are always usable immediately at boot.
        /// </summary>
        public static void ApplyDefaults(bool adsEnabled, int startLevel, float delayBetweenAds)
        {
            InterstitialAdsEnabled = adsEnabled;
            InterstitialStartLevel = Mathf.Max(0, startLevel);
            InterstitialDelayBetweenAds = Mathf.Max(0f, delayBetweenAds);
        }

        /// <summary>
        /// Overlays the "adsConfig" Remote Config parameter's JSON on top of the current values. A
        /// missing parameter, empty value, or JSON that fails to parse leaves every property
        /// untouched. JsonConvert.PopulateObject only overwrites fields actually present in the
        /// JSON (starting from a snapshot of the current values), so a payload missing one field -
        /// e.g. just {"interstitialAdsEnable": false} - still only changes that one property.
        /// </summary>
        public static void ApplyRemoteConfig(IDictionary<string, string> remoteConfig)
        {
            if (remoteConfig == null || !remoteConfig.TryGetValue(ConfigKey, out string json) || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var data = new AdsConfigData
            {
                InterstitialAdsEnable = InterstitialAdsEnabled,
                InterstitialStartLevel = InterstitialStartLevel,
                InterstitialDelayBetweenAds = InterstitialDelayBetweenAds
            };

            try
            {
                JsonConvert.PopulateObject(json, data);
            }
            catch (JsonException)
            {
                return;
            }

            InterstitialAdsEnabled = data.InterstitialAdsEnable;
            InterstitialStartLevel = Mathf.Max(0, data.InterstitialStartLevel);
            InterstitialDelayBetweenAds = Mathf.Max(0f, data.InterstitialDelayBetweenAds);
        }
    }
}
