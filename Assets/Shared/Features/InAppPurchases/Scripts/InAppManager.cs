namespace MangraGames
{
    using System;
    using System.Collections;

#if !DEV_MODE
    using Coredian;
    using Coredian.InAppPurchases;
    using EventArgs = Coredian.InAppPurchases.EventArgs;
#endif

    using Shared.Purchase;

    using TMPro;

    using UnityEngine;

    /// <summary>
    /// Single shared entry point for real-money in-app purchases. Every gameplay/store call site
    /// goes through here instead of talking to Coredian's IInAppPurchasesService directly, so:
    /// - the Editor-vs-device split (Editor never has a real store connection) is made once, not
    ///   per call site
    /// - a purchase's various validation/finalization events collapse into one Action&lt;bool&gt;
    ///   callback per request, matching AdsManager.ShowRewarded/ShowInterstitial's shape
    /// - the loading overlay is managed consistently, reusing the exact same GameObject
    ///   AdsManager already shows during an ad request (see SetLoadingScreen)
    /// </summary>
    public sealed class InAppManager : MonoBehaviour
    {
        [Tooltip("How long a simulated Editor purchase/restore takes before resolving as success - there is no real store connection in the Editor.")]
        [SerializeField] private float editorSimulatedDelaySeconds = 1f;

        private static InAppManager instance;

        public static InAppManager Instance
        {
            get
            {
                if (instance)
                {
                    return instance;
                }

                instance = FindFirstObjectByType<InAppManager>();
                if (!instance)
                {
                    instance = Instantiate(Resources.Load<InAppManager>(nameof(InAppManager)));
                }

                return instance;
            }
        }

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

        // Same story as AdsManager's loadingRoot/fakeAdViewer - InAppManager lives on a
        // DontDestroyOnLoad object created from BootScene, so it can't hold an Inspector reference
        // into the GamePlay scene's UI. GameFlowViewAdapter registers the SAME adLoadingRoot it
        // already gives AdsManager (see SetLoadingScreen doc) at runtime instead.
        private GameObject loadingRoot;
        private TMP_Text loadingLabel;
        private string originalLoadingLabelText;

        /// <summary>
        /// Registers the loading overlay to show during a purchase/restore request - pass the same
        /// GameObject already given to AdsManager.SetLoadingScreen so ads and purchases share one
        /// overlay instead of two competing ones. If that overlay has a TMP_Text child, its text is
        /// temporarily swapped to a store-specific message (see ResolveStoreLoadingText) while a
        /// request is in flight, then restored to whatever it originally said once the request
        /// finishes - AdsManager does the exact same swap-and-restore with its own distinct
        /// loadingLabelText ("Ads loading..." by default), so the two never leak into each other.
        /// </summary>
        public void SetLoadingScreen(GameObject root)
        {
            loadingRoot = root;
            loadingLabel = root != null ? root.GetComponentInChildren<TMP_Text>(true) : null;
            originalLoadingLabelText = loadingLabel != null ? loadingLabel.text : null;
        }

        private void ShowLoadingScreen()
        {
            if (loadingLabel != null)
            {
                loadingLabel.text = ResolveStoreLoadingText();
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

        // Same story as loadingRoot above - lives in the GamePlay scene's UI, assigned at runtime
        // by GameFlowViewAdapter (see AdsManager.SetNoAdsPopup for the same pattern).
        private PurchaseSuccessPopupController purchaseSuccessPopup;
        private PurchaseFailedPopupController purchaseFailedPopup;

        public void SetPurchaseSuccessPopup(PurchaseSuccessPopupController popup)
        {
            purchaseSuccessPopup = popup;
        }

        public void SetPurchaseFailedPopup(PurchaseFailedPopupController popup)
        {
            purchaseFailedPopup = popup;
        }

        private void ShowPurchaseSuccessPopup(int purchasedAmount, bool isCoinPurchase, string iconKey)
        {
            if (purchaseSuccessPopup != null)
            {
                purchaseSuccessPopup.Show(purchasedAmount, isCoinPurchase, iconKey);
            }
        }

        private void ShowPurchaseFailedPopup()
        {
            if (purchaseFailedPopup != null)
            {
                purchaseFailedPopup.Show();
            }
        }

        // DEV_MODE always simulates, exactly like the Editor path below - some dev machines don't
        // have the store SDK set up correctly and Core.GetService<IInAppPurchasesService>() throws
        // there. Unlike Application.isEditor, DEV_MODE can also be defined on a device dev build,
        // so every Application.isEditor check this class makes for "no real store connection"
        // checks this instead.
        private static bool ShouldSimulateStore =>
#if DEV_MODE
            true;
#else
            Application.isEditor;
#endif

        private static string ResolveStoreLoadingText()
        {
#if UNITY_IOS
            return "Loading App Store...";
#elif UNITY_ANDROID
            return "Loading Google Play Store...";
#else
            return "Loading Store...";
#endif
        }

        // ---------------------------------------------------------------
        // INITIALIZATION
        // ---------------------------------------------------------------

#if !DEV_MODE
        private IInAppPurchasesService iapService;
#endif
        private bool inAppInitialized;

        // Same reasoning as AdsManager.InitializeAdvertising - Core.GetService<IInAppPurchasesService>()
        // is subject to the same Core.Initialize() timing concern, so this is deliberately NOT
        // called from Awake. Purchase/RestorePurchases/GetLocalizedPrice all call this lazily as a
        // fallback so a missed call site never leaves iapService unset. Never touched at all in the
        // Editor - there is no real store connection to initialize there. Under DEV_MODE, this
        // whole body is compiled out - not just skipped at runtime via ShouldSimulateStore - since
        // some dev machines have a broken/missing store SDK setup that fails to even compile, not
        // just throw at runtime.
        public void InitializeInApp()
        {
            if (inAppInitialized || ShouldSimulateStore)
            {
                return;
            }

#if !DEV_MODE
            IInAppPurchasesService service = Core.GetService<IInAppPurchasesService>();
            if (service == null)
            {
                Debug.LogWarning($"{nameof(InAppManager)}: {nameof(IInAppPurchasesService)} not available yet - will retry on next request.");
                return;
            }

            inAppInitialized = true;
            iapService = service;

            iapService.PurchaseUpdated += HandlePurchaseUpdated;
            iapService.PurchaseValidationSucceeded += HandlePurchaseValidationSucceeded;
            iapService.PurchaseValidationFailed += HandlePurchaseValidationFailed;
            iapService.RestorePurchasesSucceeded += HandleRestorePurchasesSucceeded;
            iapService.RestorePurchasesFailed += HandleRestorePurchasesFailed;

            iapService.InitializeProducts();
#endif
        }

        // ---------------------------------------------------------------
        // PURCHASE
        // ---------------------------------------------------------------

        private string pendingProductId;
        private Action<bool> pendingPurchaseCallback;
        private int pendingPurchaseAmount = 1;
        private bool pendingIsCoinPurchase;
        private string pendingIconKey;

        /// <summary>
        /// Purchases a product by its Coredian product id (see StoreItemDefinition.ProductId).
        /// onComplete fires exactly once, with the final success/failure outcome. In the Editor,
        /// there is no real store to talk to - this always shows the loading screen for
        /// editorSimulatedDelaySeconds, then resolves as a success, so store UI/flow can be tested
        /// without a device build.
        /// purchasedAmount is shown on the success popup (e.g. a coin pack's StoreItemDefinition.CoinAmount)
        /// as "x{amount}" - leave at the default 1 for non-stacking purchases (e.g. Remove Ads),
        /// where the amount label stays blank. isCoinPurchase additionally plays the coin-fly-to-HUD
        /// animation on the success popup (set true for StoreItemType.CoinPack). iconKey is
        /// StoreItemDefinition.IconKey, resolved into the success popup's icon image.
        /// </summary>
        public void Purchase(string productId, Action<bool> onComplete = null, int purchasedAmount = 1, bool isCoinPurchase = false, string iconKey = null)
        {
            pendingProductId = productId;
            pendingPurchaseCallback = onComplete;
            pendingPurchaseAmount = Mathf.Max(1, purchasedAmount);
            pendingIsCoinPurchase = isCoinPurchase;
            pendingIconKey = iconKey;

            ShowLoadingScreen();

            if (ShouldSimulateStore)
            {
                StartCoroutine(SimulateEditorRequest(() => CompletePurchase(true)));
                return;
            }

#if !DEV_MODE
            InitializeInApp();

            if (iapService == null)
            {
                Debug.LogWarning($"{nameof(InAppManager)}: {nameof(IInAppPurchasesService)} unavailable - resolving purchase '{productId}' as failed.");
                CompletePurchase(false);
                return;
            }

            iapService.Purchase(productId);
#endif
        }

        /// <summary>
        /// Fires when a purchase is validated and finalized/consumed OUTSIDE of any currently
        /// pending <see cref="Purchase"/> request - i.e. one recovered via
        /// <see cref="RestorePurchases"/> (Settings' "Restore Purchases" action) whose original
        /// success callback was missed the first time around. Carries the raw store product id (see
        /// StoreItemDefinition.ProductId) so a listener can grant the matching reward exactly
        /// once. Never fires for a purchase already being handled by an in-flight Purchase() call
        /// - that one still resolves through its own onComplete callback as usual. Declared
        /// unconditionally (not inside the DEV_MODE-gated block below) so callers can always
        /// subscribe regardless of build symbol - it simply never fires under DEV_MODE/Editor,
        /// where there is no real store connection to recover a stuck purchase from.
        /// </summary>
        public event Action<string> UnattendedPurchaseGranted;

#if !DEV_MODE
        // Immediate store-level failures (e.g. "item unavailable"/product not configured) surface
        // ONLY through this event, never through PurchaseValidationFailed - that one is reserved
        // for server-side validation failures AFTER the store has already accepted the purchase
        // attempt. Without this handler, a store-level failure like item-not-available never
        // reached CompletePurchase at all: the loading overlay stayed up forever, the failed
        // popup never showed, and the caller's own callback never fired.
        private void HandlePurchaseUpdated(IPurchase purchase)
        {
            if (purchase == null || purchase.State != PurchaseState.Failed)
            {
                return;
            }

            // Same "still clear the loading screen even for a stale/mismatched event" reasoning as
            // HandlePurchaseValidationFailed below.
            if (!IsPendingPurchase(purchase))
            {
                HideLoadingScreen();
                return;
            }

            CompletePurchase(false);
        }

        private void HandlePurchaseValidationSucceeded(IPurchase purchase)
        {
            // Consumes/acknowledges the purchase now that it's validated - callers only ever see
            // the single success/failure result below, not this SDK-specific lifecycle step. This
            // must run for EVERY validated purchase, not just the one currently pending in memory
            // (see the IsPendingPurchase branch below) - a consumable that's validated but never
            // finalized/consumed stays stuck "purchased but not consumed" on Google Play's side,
            // which is exactly what makes the NEXT purchase attempt for that same product fail
            // with "You already own this item". This is reachable for a purchase that was never
            // "pending" in THIS session's memory at all - e.g. RestorePurchases (Settings'
            // "Restore Purchases" action) surfacing an old purchase whose original in-game success
            // callback was missed (app backgrounded/killed during checkout, a previous session
            // that crashed mid-purchase, or the server-side verification call itself timing out on
            // the first attempt).
            iapService.Finalize(purchase);

            if (IsPendingPurchase(purchase))
            {
                CompletePurchase(true);
                return;
            }

            // Not something this session's own Purchase() call is waiting on - most likely a
            // stuck/previously-unconsumed purchase just recovered via a RestorePurchases call.
            // The reward for it still needs to reach the player exactly once;
            // UnattendedPurchaseGranted lets a higher layer (which knows how
            // to map a raw store product id to its in-game reward - see
            // CatFlapGameplayManager/StoreManager) grant it without going through the normal
            // per-request popup/callback plumbing above.
            if (purchase?.Product != null)
            {
                UnattendedPurchaseGranted?.Invoke(purchase.Product.Id);
            }
        }

        private void HandlePurchaseValidationFailed(IPurchase purchase)
        {
            // Unlike HandlePurchaseValidationSucceeded, this must hide the loading screen even
            // when the failed purchase doesn't match pendingProductId (e.g. a stale/mismatched
            // event from the store SDK) - otherwise the loading overlay shown at purchase start
            // (see Purchase()) is left stuck on screen forever with no success event ever coming
            // to clear it. Only the pending-purchase's own success/failed popup and callback are
            // still gated on the match, via CompletePurchase's own pendingPurchaseCallback/
            // pendingProductId reads.
            if (!IsPendingPurchase(purchase))
            {
                HideLoadingScreen();
                return;
            }

            CompletePurchase(false);
        }

        private bool IsPendingPurchase(IPurchase purchase)
        {
            return !string.IsNullOrEmpty(pendingProductId) &&
                purchase?.Product != null &&
                string.Equals(purchase.Product.Id, pendingProductId, StringComparison.Ordinal);
        }
#endif

        private void CompletePurchase(bool success)
        {
            HideLoadingScreen();

            Action<bool> callback = pendingPurchaseCallback;
            int purchasedAmount = pendingPurchaseAmount;
            bool isCoinPurchase = pendingIsCoinPurchase;
            string iconKey = pendingIconKey;
            pendingProductId = null;
            pendingPurchaseCallback = null;
            pendingPurchaseAmount = 1;
            pendingIsCoinPurchase = false;
            pendingIconKey = null;

            if (success)
            {
                ShowPurchaseSuccessPopup(purchasedAmount, isCoinPurchase, iconKey);
            }
            else
            {
                ShowPurchaseFailedPopup();
            }

            callback?.Invoke(success);
        }

        // ---------------------------------------------------------------
        // RESTORE
        // ---------------------------------------------------------------

        private Action<bool> pendingRestoreCallback;

        /// <summary>
        /// Re-queries the store for previously-purchased non-consumable/subscription products AND
        /// any consumable still sitting unconsumed from a prior purchase whose success was never
        /// actually processed in-game (see HandlePurchaseValidationSucceeded's own comment on why
        /// that can happen) - each one found is re-run through the normal validate/finalize
        /// pipeline, which grants a stuck consumable's reward via UnattendedPurchaseGranted. Called
        /// from Settings' "Restore Purchases" action so the player can trigger it on demand.
        /// onComplete only reports whether the restore/query call itself succeeded, not whether
        /// anything was actually found to recover - same Editor-simulates-success behavior as
        /// Purchase above.
        /// </summary>
        public void RestorePurchases(Action<bool> onComplete = null)
        {
            pendingRestoreCallback = onComplete;

            ShowLoadingScreen();

            if (ShouldSimulateStore)
            {
                StartCoroutine(SimulateEditorRequest(() => CompleteRestore(true)));
                return;
            }

#if !DEV_MODE
            InitializeInApp();

            if (iapService == null)
            {
                Debug.LogWarning($"{nameof(InAppManager)}: {nameof(IInAppPurchasesService)} unavailable - resolving restore as failed.");
                CompleteRestore(false);
                return;
            }

            iapService.RestorePurchases();
#endif
        }

#if !DEV_MODE
        private void HandleRestorePurchasesSucceeded(EventArgs args)
        {
            CompleteRestore(true);
        }

        private void HandleRestorePurchasesFailed(EventArgs args)
        {
            CompleteRestore(false);
        }
#endif

        private void CompleteRestore(bool success)
        {
            HideLoadingScreen();

            Action<bool> callback = pendingRestoreCallback;
            pendingRestoreCallback = null;
            callback?.Invoke(success);
        }

        // ---------------------------------------------------------------
        // EDITOR SIMULATION
        // ---------------------------------------------------------------

        private IEnumerator SimulateEditorRequest(Action resolve)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, editorSimulatedDelaySeconds));
            resolve();
        }

        // ---------------------------------------------------------------
        // LOCALIZED PRICE
        // ---------------------------------------------------------------

        /// <summary>
        /// Returns the store's own localized/formatted price string for a product (e.g. "$4.99"),
        /// or null if unavailable (Editor - no real store connection, product not yet initialized,
        /// or unknown id). Callers should fall back to a predefined label when this is null - see
        /// ResolveDisplayPrice.
        /// </summary>
        public string GetLocalizedPrice(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId) || ShouldSimulateStore)
            {
                return null;
            }

#if !DEV_MODE
            InitializeInApp();

            IProduct product = iapService?.GetProduct(productId);
            return product != null && product.Initialized ? product.Price : null;
#else
            return null;
#endif
        }

        /// <summary>
        /// GetLocalizedPrice with a fallback - what store button views should call directly. Used
        /// to fill a purchase button's price text from StoreItemDefinition.ProductId, falling back
        /// to that same item's own predefined PriceLabel when a live price isn't available.
        /// </summary>
        public string ResolveDisplayPrice(string productId, string fallbackLabel)
        {
            string localizedPrice = GetLocalizedPrice(productId);
            return string.IsNullOrWhiteSpace(localizedPrice) ? fallbackLabel : localizedPrice;
        }
    }
}
