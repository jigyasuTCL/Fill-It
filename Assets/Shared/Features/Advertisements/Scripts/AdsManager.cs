namespace MangraGames
{
    using System;

#if !DEV_MODE
    using Coredian;
    using Coredian.Advertisements;
#endif

    using Shared.Analytics;
    using Shared.GameFlow;
    using Shared.NoAdsPopup;

    using TMPro;

    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Single shared entry point for showing rewarded/interstitial ads. Every gameplay call site
    /// goes through here instead of talking to Coredian or FakeAdViewerController directly, so:
    /// - placements live in one place (RewardedPlacement/InterstitialPlacement)
    /// - the real-vs-fake decision (useFakeAds) is made once, not per call site
    /// - the ad-loading overlay and per-ad state are managed consistently
    /// </summary>
    public sealed class AdsManager : MonoBehaviour
    {
        public enum AdState
        {
            Idle,
            Loading,
            Success,
            Failed,
        }

        public static class RewardedPlacement
        {
            public const string DoubleCoinNormalLevel = "RV_DoubleCoinNormalLevel";
            public const string DoubleCoinDcLevel = "RV_DoubleCoinDcLevel";
            public const string ReviveLevel = "RV_ReviveLevel";
            public const string ReviveDcLevel = "RV_ReviveDcLevel";
            public const string BuyHintBooster = "RV_BuyHintBooster";
            public const string DailyChallengeRestart = "RV_DailyChallengeRestart";
            public const string DailyChallengePlay = "RV_DailyChallengePlay";
            public const string DailyReward = "RV_DailyReward";
        }

        public static class InterstitialPlacement
        {
            public const string LevelCompleteNormal = "I_LevelCompleteNormal";
            public const string LevelCompleteDc = "I_LevelCompleteDc";
            public const string OutOfLivesNormal = "I_OutOfLivesNormal";
            public const string OutOfLivesDc = "I_OutOfLivesDc";
        }

        private static AdsManager instance;

        public static AdsManager Instance
        {
            get
            {
                if (instance)
                {
                    return instance;
                }

                instance = FindFirstObjectByType<AdsManager>();
                if (!instance)
                {
                    instance = Instantiate(Resources.Load<AdsManager>(nameof(AdsManager)));
                }

                return instance;
            }
        }

        [Header("Ad Source")]
        [Tooltip("When on, ad requests are routed to fakeAdViewer instead of the real Coredian SDK.")]
        [SerializeField] private bool useFakeAds;

        // AdsManager lives on a DontDestroyOnLoad object created from BootScene, so it can't hold
        // an Inspector reference to FakeAdViewerController - that dialog lives inside the GamePlay
        // scene's UI and gets destroyed/recreated on every scene load. Whoever owns that reference
        // in-scene (currently CatFlapGameplayManager) must call SetFakeAdViewer with it once it's
        // available, and again after every GamePlay scene (re)load.
        private FakeAdViewerController fakeAdViewer;

        public void SetFakeAdViewer(FakeAdViewerController viewer)
        {
            fakeAdViewer = viewer;
        }

        [Tooltip("Text shown on the shared loading overlay (see SetLoadingScreen) while an ad request is in flight - kept genuinely distinct from InAppManager's own store-specific message on the same overlay.")]
        [SerializeField] private string loadingLabelText = "Ads loading...";

        // Same story as fakeAdViewer above - the loading overlay lives in the GamePlay scene's UI
        // (under GameFlowViewAdapter), not on this DontDestroyOnLoad object, so it's assigned at
        // runtime via SetLoadingScreen rather than serialized here.
        private GameObject loadingRoot;
        private TMP_Text loadingLabel;
        private string originalLoadingLabelText;

        // Shares this exact GameObject with InAppManager.SetLoadingScreen - both managers swap the
        // same TMP_Text child's text in and out (this one to loadingLabelText, InAppManager to its
        // own store-specific message) rather than either leaving its own text behind for the other
        // to inherit. Each restores the ORIGINAL text captured here (whatever the label says before
        // either manager ever touches it) when its own request finishes, so the overlay always goes
        // back to the same neutral baseline regardless of which one used it last.
        public void SetLoadingScreen(GameObject root)
        {
            loadingRoot = root;
            loadingLabel = root != null ? root.GetComponentInChildren<TMP_Text>(true) : null;
            originalLoadingLabelText = loadingLabel != null ? loadingLabel.text : null;
        }

        // Same story again - lives in the GamePlay scene's UI, assigned at runtime.
        private NoAdsPopupController noAdsPopup;

        public void SetNoAdsPopup(NoAdsPopupController popup)
        {
            noAdsPopup = popup;
        }

        private void ShowNoAdsPopup()
        {
            if (noAdsPopup != null)
            {
                noAdsPopup.Show();
            }
        }

        private bool advertisingInitialized;

#if !DEV_MODE
        private IBannerController bannerController;
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

            SetLoadingVisible(false);
        }

        // Core.GetService<IAdvertisementsService>() triggers Coredian's own Core.Initialize() the
        // first time anything touches it - deliberately NOT done in Awake, since AdsManager's
        // GameObject lives in BootScene and Awake for every object there runs before BootLoader's
        // own Start() coroutine (PlayerData/GlobalConfig load, splash timing) even begins. Call
        // this once BootScene's own work is actually done (BootLoader does, after its scene load
        // completes) - ShowRewarded/ShowInterstitial also call it lazily as a fallback so a missed
        // call site never leaves interstitialController/rewardedController unset.
        public void InitializeAdvertising()
        {
#if DEV_MODE
            // DEV_MODE never touches Coredian at all - some dev machines don't have the ad SDKs
            // set up correctly and Core.GetService<IAdvertisementsService>() throws there. Latch
            // initialized so ShowRewarded/ShowInterstitial's own DEV_MODE short-circuit is the only
            // path that ever runs.
            advertisingInitialized = true;
            return;
#else
            if (advertisingInitialized || useFakeAds)
            {
                return;
            }

            var advertisements = Core.GetService<IAdvertisementsService>();
            if (advertisements == null)
            {
                // Don't latch advertisingInitialized on a failed attempt - Core may simply not have
                // finished registering IAdvertisementsService yet the first time this runs. Leaving
                // it false lets ShowRewarded/ShowInterstitial's lazy fallback call retry later,
                // instead of permanently leaving interstitialController/rewardedController null.
                Debug.LogWarning($"{nameof(AdsManager)}: {nameof(IAdvertisementsService)} not available yet - will retry on next ad request.");
                return;
            }

            advertisingInitialized = true;
            interstitialController = advertisements.GetInterstitialController();
            rewardedController = advertisements.GetRewardedController();

            // This game never shows banner ads - some ad SDKs/mediation configs still auto-prepare
            // one as soon as the service initializes. That prepare is async, so Hide() here can run
            // before the banner has actually loaded (a no-op); Ready fires once it does load, so
            // re-Hide() there catches it whenever it actually appears.
            bannerController = advertisements.GetBannerController();
            if (bannerController != null)
            {
                bannerController.Hide();
                bannerController.Ready += HideBanner;
            }
#endif
        }

        public void HideBanner()
        {
#if !DEV_MODE
            bannerController?.Hide();
#endif
        }

        private void ShowLoadingScreen()
        {
            if (loadingLabel != null)
            {
                loadingLabel.text = loadingLabelText;
            }

            SetLoadingVisible(true);
        }

        private void HideLoadingScreen()
        {
            SetLoadingVisible(false);

            if (loadingLabel != null && originalLoadingLabelText != null)
            {
                loadingLabel.text = originalLoadingLabelText;
            }
        }

        private void SetLoadingVisible(bool visible)
        {
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(visible);
            }
        }

        #region Rewarded

#if !DEV_MODE
        private IRewardedController rewardedController;
#endif
        private string pendingRewardedPlacement;
        private Action<bool> pendingRewardedCallback;

        public AdState RewardedState { get; private set; } = AdState.Idle;

        public event Action<AdState> RewardedStateChanged;

        public event Action RewardedAdCompleted;

        public event Action<string> RewardedAdCompletedForPlacement;

        public void ShowRewarded(string placement, Action<bool> onComplete = null)
        {
            pendingRewardedPlacement = placement;
            pendingRewardedCallback = onComplete;

            SetRewardedState(AdState.Loading);
            ShowLoadingScreen();

#if DEV_MODE
            // Resolve immediately as a successful watch - no Coredian/SDK touch at all.
            CompleteRewarded(true);
            return;
#else
            if (useFakeAds)
            {
                if (fakeAdViewer == null)
                {
                    Debug.LogWarning($"{nameof(AdsManager)}: useFakeAds is on but fakeAdViewer is not assigned - resolving rewarded '{placement}' as failed.");
                    CompleteRewarded(false);
                    return;
                }

                // The fake dialog is its own visible UI - don't leave the loading overlay up
                // behind/over it while it's waiting on the player to tap Watch/Skip. No timeout
                // here either - the player, not a network call, decides how long this takes.
                HideLoadingScreen();
                fakeAdViewer.Show(
                    onWatched: () => CompleteRewarded(true),
                    onSkipped: () => CompleteRewarded(false));
                return;
            }

            InitializeAdvertising();

            // rewardedController can still be null here - e.g. Core.GetService<IAdvertisementsService>()
            // wasn't ready yet the first time InitializeAdvertising ran (advertisingInitialized is
            // now permanently true, so it never retries), or this rewarded placement simply isn't
            // configured/mediated for this build. Fail gracefully instead of crashing on .Show().
            if (rewardedController == null)
            {
                Debug.LogWarning($"{nameof(AdsManager)}: rewardedController unavailable - resolving rewarded '{placement}' as failed.");
                CompleteRewarded(false);
                return;
            }

            rewardedController.Show(HandleRewardedResult, placement);
#endif
        }

        public void ShowDoubleCoinNormalLevelRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.DoubleCoinNormalLevel, onComplete);

        public void ShowDoubleCoinDcLevelRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.DoubleCoinDcLevel, onComplete);

        public void ShowReviveLevelRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.ReviveLevel, onComplete);

        public void ShowReviveDcLevelRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.ReviveDcLevel, onComplete);

        public void ShowBuyHintBoosterRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.BuyHintBooster, onComplete);

        public void ShowDailyChallengeRestartRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.DailyChallengeRestart, onComplete);

        public void ShowDailyChallengePlayRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.DailyChallengePlay, onComplete);

        public void ShowDailyRewardRewarded(Action<bool> onComplete = null) => ShowRewarded(RewardedPlacement.DailyReward, onComplete);

#if !DEV_MODE
        private void HandleRewardedResult(IRewardedAdResult result)
        {
            if (!result.WasSuccess)
            {
                Log.InfoFormat("Rewarded ad failed: {0}", result.ErrorDescription);
                ShowNoAdsPopup();
                CompleteRewarded(false);
                return;
            }

            // Ad request itself succeeded here - result.WasRewarded being false just means the
            // player skipped out before earning the reward, not an error, so no popup for that.
            CompleteRewarded(result.WasRewarded);
        }
#endif

        private void CompleteRewarded(bool success)
        {
            HideLoadingScreen();

            string placement = pendingRewardedPlacement;
            Action<bool> callback = pendingRewardedCallback;
            pendingRewardedPlacement = null;
            pendingRewardedCallback = null;

            AnalyticsManager.Instance.LogAdResult(AnalyticsEvents.AdTypeValues.Rewarded, placement, success);

            SetRewardedState(success ? AdState.Success : AdState.Failed);

            callback?.Invoke(success);

            if (success)
            {
                RewardedAdCompleted?.Invoke();
                RewardedAdCompletedForPlacement?.Invoke(placement);
            }
        }

        private void SetRewardedState(AdState state)
        {
            RewardedState = state;
            RewardedStateChanged?.Invoke(state);
        }

        #endregion

        #region Interstitial

#if !DEV_MODE
        private IInterstitialController interstitialController;
#endif
        private string pendingInterstitialPlacement;
        private Action<bool> pendingInterstitialCallback;

        public AdState InterstitialState { get; private set; } = AdState.Idle;

        public event Action<AdState> InterstitialStateChanged;

        public void ShowInterstitial(string placement, Action<bool> onComplete = null)
        {
            // Remove Ads is a permanent purchase that suppresses interstitials/banners (not
            // rewarded ads - those stay opt-in/player-requested even after buying it, same
            // convention StoreManager/StorePopupController/ShopPageController already follow for
            // gating the purchase button itself). Resolves as an instant "success" with no ad
            // shown, same contract as the DEV_MODE/useFakeAds short-circuits below.
            if (Shared.PlayerData.PlayerData.Instance.NoAdsPurchased)
            {
                onComplete?.Invoke(true);
                return;
            }

            pendingInterstitialPlacement = placement;
            pendingInterstitialCallback = onComplete;

            // Unlike rewarded, interstitials don't show the shared loading overlay - they're
            // expected to already be preloaded/cached by the mediation SDK, so there's no
            // player-visible wait to cover; CompleteInterstitial's HideLoadingScreen() call is a
            // harmless no-op here (it's only ever needed if a rewarded request is still up).
            SetInterstitialState(AdState.Loading);

#if DEV_MODE
            // Resolve immediately as success - no Coredian/SDK touch at all.
            CompleteInterstitial(true);
            return;
#else
            if (useFakeAds)
            {
                // FakeAdViewerController only simulates the watch/skip choice a rewarded ad
                // offers - an interstitial has no such choice, so faking it just means resolving
                // it as an immediate success instead of routing through that dialog. No timeout
                // here either - nothing is waiting on a network call.
                CompleteInterstitial(true);
                return;
            }

            InitializeAdvertising();

            // Same reasoning as ShowRewarded's rewardedController check above.
            if (interstitialController == null)
            {
                Debug.LogWarning($"{nameof(AdsManager)}: interstitialController unavailable - resolving interstitial '{placement}' as failed.");
                CompleteInterstitial(false);
                return;
            }

            interstitialController.Show(HandleInterstitialResult, placement);
#endif
        }

        public void ShowLevelCompleteNormalInterstitial(Action<bool> onComplete = null) => ShowInterstitial(InterstitialPlacement.LevelCompleteNormal, onComplete);

        public void ShowLevelCompleteDcInterstitial(Action<bool> onComplete = null) => ShowInterstitial(InterstitialPlacement.LevelCompleteDc, onComplete);

        public void ShowOutOfLivesNormalInterstitial(Action<bool> onComplete = null) => ShowInterstitial(InterstitialPlacement.OutOfLivesNormal, onComplete);

        public void ShowOutOfLivesDcInterstitial(Action<bool> onComplete = null) => ShowInterstitial(InterstitialPlacement.OutOfLivesDc, onComplete);

#if !DEV_MODE
        private void HandleInterstitialResult(IInterstitialAdResult result)
        {
            if (!result.WasSuccess)
            {
                // Unlike rewarded, an unavailable/failed interstitial never shows the "ads not
                // available" popup - it's not something the player explicitly asked for, so it
                // just silently resolves as failed and the caller proceeds without an ad (see
                // TryShowLevelCompleteInterstitial's onComplete).
                Log.InfoFormat("Interstitial failed: {0}", result.ErrorDescription);
            }

            CompleteInterstitial(result.WasSuccess);
        }
#endif

        private void CompleteInterstitial(bool success)
        {
            HideLoadingScreen();

            string placement = pendingInterstitialPlacement;
            Action<bool> callback = pendingInterstitialCallback;
            pendingInterstitialPlacement = null;
            pendingInterstitialCallback = null;

            AnalyticsManager.Instance.LogAdResult(AnalyticsEvents.AdTypeValues.Interstitial, placement, success);

            SetInterstitialState(success ? AdState.Success : AdState.Failed);

            callback?.Invoke(success);
        }

        private void SetInterstitialState(AdState state)
        {
            InterstitialState = state;
            InterstitialStateChanged?.Invoke(state);
        }

        #endregion
    }
}
