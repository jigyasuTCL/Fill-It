namespace Shared.RemoteConfig
{
    using System.Collections.Generic;

#if !DEV_MODE
    using Coredian;
    using Coredian.Firebase;
#endif

    using UnityEngine;

    /// <summary>
    /// Single shared entry point for Firebase Remote Config. Applies safe local defaults to every
    /// registered *ConfigController first (AdsConfigController, EconomyConfigController, ...), then
    /// fetches/activates Remote Config through Coredian's existing IFirebaseRemoteConfigService and
    /// pushes the result into those same controllers. Gameplay/ads code never talks to Firebase
    /// directly - only to a *ConfigController's static properties - so this manager can gain more
    /// controllers later without any other call site changing.
    ///
    /// Each controller owns one Remote Config parameter whose value is a JSON object grouping all
    /// of that category's values (Firebase Console parameters: "adsConfig", "economyConfig", ...) -
    /// not one flat Remote Config key per value. See AdsConfigData/EconomyConfigData for the JSON
    /// shape each parameter's value must match.
    ///
    /// Reuses Coredian's existing Firebase initialization (the same IFirebaseRemoteConfigService
    /// Core's own boot pipeline uses to patch its Core Config diff) instead of starting a second
    /// Firebase init flow - see FirebaseAnalyticsHandler/AdsManager for the same
    /// Core.GetService&lt;T&gt;() convention this follows.
    /// </summary>
    public sealed class FirebaseRemoteConfigManager : MonoBehaviour
    {
        private static FirebaseRemoteConfigManager instance;

        public static FirebaseRemoteConfigManager Instance
        {
            get
            {
                if (instance)
                {
                    return instance;
                }

                instance = FindFirstObjectByType<FirebaseRemoteConfigManager>();
                if (!instance)
                {
                    // Falls back to a bare self-created instance (Inspector fields below just keep
                    // their C# literal defaults then) - same as AnalyticsManager. To actually tune
                    // the local defaults below from the Inspector, place this component on a
                    // GameObject in BootScene instead, same convention AdsManager already uses
                    // (see its useFakeAds/loadingLabelText fields).
                    instance = new GameObject(nameof(FirebaseRemoteConfigManager)).AddComponent<FirebaseRemoteConfigManager>();
                }

                return instance;
            }
        }

        [Header("Ads Config - Local Defaults")]
        [Tooltip("Used until Remote Config overrides it. No dedicated local flag existed before Remote Config, so this defaults to on.")]
        [SerializeField] private bool interstitialAdsEnabled = true;
        [Tooltip("Local default for the \"adsConfig\" parameter's interstitialStartLevel field.")]
        [SerializeField] private int interstitialStartLevel = 18;
        [Tooltip("Local default for the \"adsConfig\" parameter's interstitialDelayBetweenAds field, in seconds.")]
        [SerializeField] private float interstitialDelayBetweenAds = 180f;

        [Header("Economy Config - Local Defaults")]
        [Tooltip("Local default for the \"economyConfig\" parameter's levelWinCoins field.")]
        [SerializeField] private int levelWinCoins = 200;
        [Tooltip("Local default for the \"economyConfig\" parameter's hardLevelWinCoins field.")]
        [SerializeField] private int hardLevelWinCoins = 20;
        [Tooltip("Local default for the \"economyConfig\" parameter's levelReviveCoins field - the Out of Lives refill coin cost.")]
        [SerializeField] private int outOfLivesRefillCoinCost = 100;

        private bool remoteConfigInitialized;

        /// <summary>
        /// True once Initialize() has settled - either a fetch attempt completed (success or
        /// failure) or DEV_MODE skipped straight to defaults. Does NOT mean Remote Config values
        /// were actually applied; check AdsConfigController/EconomyConfigController's own
        /// properties for that - they're always safe to read regardless of IsReady.
        /// </summary>
        public bool IsReady { get; private set; }

#if !DEV_MODE
        private IFirebaseRemoteConfigService remoteConfigService;
#endif

        private void Awake()
        {
            if (instance && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // Called explicitly by BootLoader once BootScene's own work is done (same pattern/reasoning
        // as AdsManager.InitializeAdvertising/AnalyticsManager.InitializeAnalytics/
        // InAppManager.InitializeInApp - Core.GetService<T>() triggers Coredian's own
        // Core.Initialize() and shouldn't compete with BootScene's own setup work). Local defaults
        // are applied synchronously below BEFORE any Firebase call, so AdsConfigController/
        // EconomyConfigController are always usable immediately - the first scene never blocks on
        // Remote Config.
        public void Initialize()
        {
            if (remoteConfigInitialized)
            {
                return;
            }

            remoteConfigInitialized = true;

            ApplyDefaultsToAllControllers();

#if DEV_MODE
            // DEV_MODE never touches Coredian.Firebase at all - some dev machines have a
            // broken/missing Firebase SDK setup. Defaults applied above are all this build uses.
            IsReady = true;
#else
            remoteConfigService = Core.GetService<IFirebaseRemoteConfigService>();
            if (remoteConfigService == null)
            {
                IsReady = true;
                return;
            }

            // Core's own boot pipeline (FirebaseRemoteConfigHandler) already fetches Remote Config
            // once during Core.Initialize() for its own Core Config diff patch - if that's already
            // landed by the time we get here, reuse its result immediately instead of waiting on a
            // second fetch to come back.
            if (remoteConfigService.IsRemoteConfigLoaded)
            {
                ApplyRemoteConfigToAllControllers(remoteConfigService.RemoteConfig);
            }

            FetchAndActivate();
#endif
        }

#if !DEV_MODE
        /// <summary>
        /// Triggers a fresh Remote Config fetch + activation. Safe to call again later (e.g. a
        /// manual refresh) - a failed fetch always leaves whatever values are already applied in
        /// place, it never clears them or blocks gameplay.
        /// </summary>
        public void FetchAndActivate()
        {
            if (remoteConfigService == null)
            {
                return;
            }

            remoteConfigService.FetchRemoteConfig(OnFetchSucceeded, OnFetchFailed);
        }

        private void OnFetchSucceeded(IReadOnlyDictionary<string, string> remoteConfig)
        {
            // remoteConfig's static type is IReadOnlyDictionary (the fetch callback's contract),
            // while every *ConfigController reads IDictionary (matching IFirebaseRemoteConfigService.
            // RemoteConfig's own property type) - both call sites hand it a concrete Dictionary
            // under the hood, so this cast always succeeds; a null result (defensive only) just
            // means the controllers keep their current values, same as a missing key would.
            ApplyRemoteConfigToAllControllers(remoteConfig as IDictionary<string, string>);
            IsReady = true;
        }

        private void OnFetchFailed()
        {
            IsReady = true;
        }
#endif

        // Add one line here, and one in ApplyRemoteConfigToAllControllers below, for every new
        // *ConfigController (EconomyConfigController was added this same way) - no other file
        // needs to change. Values come from the Inspector fields above (editable when this
        // component is placed in BootScene), not GlobalConfig - keeps every configured value
        // controllable from one place instead of scattered across GlobalConfig/GameRulesConfig.
        private void ApplyDefaultsToAllControllers()
        {
            AdsConfigController.ApplyDefaults(interstitialAdsEnabled, interstitialStartLevel, interstitialDelayBetweenAds);
            EconomyConfigController.ApplyDefaults(levelWinCoins, hardLevelWinCoins, outOfLivesRefillCoinCost);
        }

        private static void ApplyRemoteConfigToAllControllers(IDictionary<string, string> remoteConfig)
        {
            if (remoteConfig == null)
            {
                return;
            }

            AdsConfigController.ApplyRemoteConfig(remoteConfig);
            EconomyConfigController.ApplyRemoteConfig(remoteConfig);
        }
    }
}
