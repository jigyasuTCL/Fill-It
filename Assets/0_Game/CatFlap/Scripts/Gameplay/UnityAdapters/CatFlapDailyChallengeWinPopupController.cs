using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Game.SpinWheel;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Daily Challenge sub-level WIN result popup. Visual/animation clone of
    /// Shared.GameFlow.LevelWinPopupController (background fade, title/cat pop, coin header,
    /// feature-nudge banner) EXCEPT:
    ///  - Four buttons: 2x (ads, doubles the reward then behaves like Primary), Primary, Back,
    ///    and Close (X, behaves exactly like Primary - not a separate destination). The bridge
    ///    (CatFlapDailyChallengeBridge) decides Primary/Back's labels/actions/whether Back shows;
    ///    this component only presents them.
    ///  - Same CoinCollectAnimator bloom/hover/stream coin-collect VFX as LevelWinPopupController,
    ///    played on Primary/Back click (see AnimateCoins) - the real reward amount comes from
    ///    GlobalConfig.DailyChallenge.LevelWinCoins via CatFlapGameplayManager.
    ///    GetDailyChallengeLevelWinCoinInfo / CatFlapDailyChallengeBridge.SetCurrentCoin.
    ///  - Moves/timer values are still placeholders - a Daily Challenge sub-level has no
    ///    moves/time data of its own to report; day-completion rewards are presented separately
    ///    by DailyChallengeController's reward track.
    ///  - Primary/Back invoke the bridge-supplied Action callbacks (same as before) instead of
    ///    calling CatFlapGameplayManager directly, since this popup doesn't own navigation - the
    ///    callback only fires once the coin-collect VFX finishes, same as
    ///    LevelWinPopupController's OnNextLevelClicked/OnHomeClicked.
    /// </summary>
    public sealed class CatFlapDailyChallengeWinPopupController : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private GameObject root;
        [Tooltip("The challenge's date (e.g. \"September 3\"), shown at the top of the popup.")]
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text movesText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private RectTransform timerContainer;
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private TMP_Text CoinText;

        [Header("Win Sequence")]
        [SerializeField] private ParticleSystem winParticles;
        [SerializeField] private CanvasGroup backgroundGroup;
        [Tooltip("Predefined title art/text baked into the prefab (e.g. \"Level Complete!\") - " +
                 "this only scales it up, it does not set any text at runtime.")]
        [SerializeField] private RectTransform titleImage;
        [SerializeField] private RectTransform characterTransform;
        [SerializeField] private float characterTargetScale = 1f;
        [SerializeField] private RectTransform coinContainer;
        [SerializeField] private RectTransform winCoinContainer;
        [SerializeField] private TMP_Text winCoinText;

        [Header("Cat Open Animation (VideoPlayer, plays once when this popup appears)")]
        [SerializeField] private SpriteRenderer catVideoRenderer;
        [SerializeField] private VideoClip catOpenVideoClip;

        private VideoPlayer catVideoPlayer;
        private RawImage catVideoImage;
        private RenderTexture catVideoRenderTexture;

        [Header("Buttons - scale up, ease back out, primary then back")]
        [SerializeField] private Button primaryButton;
        [SerializeField] private RectTransform primaryButtonTransform;
        [SerializeField] private TMP_Text primaryButtonLabel;
        [SerializeField] private Button backButton;
        [SerializeField] private RectTransform backButtonTransform;

        [Header("Primary (Continue) Button Idle Bob - draws the eye to it if the player just sits on this popup")]
        [Tooltip("Optional. Wraps Primary/Ads/Back/Close as one row. Only used when bobButtonContainer is checked - otherwise the Primary/Continue button bobs alone.")]
        [SerializeField] private RectTransform buttonContainer;
        [Tooltip("Checked: bobs the whole buttonContainer row instead of just the Primary/Continue button. Unchecked (default): only Primary bobs.")]
        [SerializeField] private bool bobButtonContainer;
        [Tooltip("Seconds after the Primary/Continue button finishes popping in before it starts bobbing.")]
        [SerializeField] private float continueBobDelaySeconds = 1.5f;
        [SerializeField] private float continueBobAmplitude = 1.15f;
        [SerializeField] private float continueBobDuration = 0.55f;

        private Tween continueBobTween;
        private Vector3 continueBobRestingPos;
        private bool continueBobRestingCaptured;

        [Header("2x Reward Button - watches an ad, doubles the coin reward, then behaves exactly like Primary")]
        [SerializeField] private Button adsButton;
        [SerializeField] private RectTransform adsButtonTransform;
        private bool showAdsButton = true;

        [Header("Close (X) Button - behaves exactly like Primary, not a separate destination")]
        [SerializeField] private Button closeButton;

        [Tooltip("Hidden by default. Shown the instant the coin-collect animation starts (see AnimateCoins), hidden again once it finishes or is skipped. Tapping it calls SkipCoinAnimation directly.")]
        [SerializeField] private Button skipButton;

        [Header("Timing")]
        // Entry animation tuned 2x faster than original (durations halved, typewriter speed
        // doubled) - see LevelWinPopupController for the matching change.
        [SerializeField] private float particleDelay = 0f;
        [SerializeField] private float backgroundFadeDuration = 0.125f;
        [SerializeField] private float catScaleDuration = 0.175f;
        [SerializeField] private float buttonScaleDuration = 0.2f;
        [SerializeField] private float coinMoveDuration = 0.125f;
        [SerializeField] private float coinWinScaleDuration = 0.175f;
        [SerializeField] private float timeTextScaleDuration = 0.15f;
        [SerializeField] private float typewriterCharsPerSecond = 80f;
        [SerializeField] private float offscreenPadding = 200f;

        [Header("Coin Collect VFX - same CoinCollectAnimator as LevelWinPopupController")]
        [SerializeField] private List<GameObject> vfxToHideObjs = new List<GameObject>();
        [SerializeField] private Image coinVFXPrefab;
        [Tooltip("Where the coin VFX spawns from. Falls back to coinImage's position if left unassigned.")]
        [SerializeField] private Transform coinVFXSpawnTransform;
        [SerializeField] private CoinCollectSettings coinCollectSettings;
        public Image coinImage;
        public Image levelCoinsImage;
        public GameObject vfxContainerObj;
        public GameObject levelCoinVfxObj;

        [Header("Feature Nudges")]
        [Tooltip("Enabled whenever a spin is available and/or the daily reward is ready to collect.")]
        [SerializeField] private SpinWheelManager spinWheelManager;
        [SerializeField] private DailyRewardManager dailyRewardManager;
        [SerializeField] private GameObject spinOrRewardAvailableBanner;

        private Action onBack;
        private Action onPrimary;
        private Coroutine openRoutine;
        private bool hasPlayedOpen;
        private bool actionRequested;
        private bool primaryButtonReady;
        private bool backButtonReady;
        private bool showBackButton = true;

        // Blocks Primary/Back/Ads/Close clicks purely at the function level (Button.interactable/
        // tint untouched, so they stay full color throughout) from the moment the open sequence
        // starts until the WHOLE sequence - not just each button's own pop-in - actually finishes.
        // See ResetVisualState and the tail of PlayOpenSequence.
        private bool openAnimationPlaying = true;

        // Guards the 2x button separately from actionRequested - watching an ad doesn't resolve
        // to Primary/Back until AdsManager's callback actually reports success (see
        // CatFlapDailyChallengeBridge.HandleWinWatchAd), so a declined/failed ad needs to hand
        // control back to the player via OnWatchAdDeclined instead of leaving every button
        // permanently disabled.
        private bool adWatchInProgress;

        private string pendingMovesValue = "0";
        private string pendingTimerValue = "00:00";

        private Vector2 coinContainerHomePos;
        private Vector2 primaryButtonHomePos;
        private Vector2 backButtonHomePos;
        private bool cachedHomePositions;

        private int currentCoin;
        private int winCoin;
        private int finalCoin;
        private bool coinDataReady;
        private static bool _hasResetCoinTextOnce;

        private void Awake()
        {
            CacheHomePositions();
            ResetVisualState();
            EnsureCatVideoPlayer();
        }

        // VideoPlayer is added/configured in code rather than authored on the prefab, so this
        // works whether or not the prefab already carries one - see PlayCatOpenAnimation.
        //
        // MaterialOverride mode (feeding the video straight into catVideoRenderer's own material)
        // was tried first and rejected: a SpriteRenderer's mesh/UVs follow the SOURCE SPRITE's own
        // packed rect/silhouette (its "Tight" mesh), not a full 0-1 quad, so the video frame came
        // out cropped into that tiny sub-region and looked like a plain gray blob. A RawImage
        // always renders a full quad with 0-1 UVs regardless of what texture it holds, so instead
        // this creates one at runtime, sized/positioned to exactly match catVideoRenderer's own
        // RectTransform and parented as its sibling (so it inherits the SAME characterTransform
        // scale-up animation), and points the VideoPlayer's RenderTexture output at it. The
        // RawImage stays hidden until PlayCatOpenAnimation actually plays, and catVideoRenderer
        // (showing the static poster sprite) is hidden only while the video is visible.
        //
        // The extra Canvas/overrideSorting step (unlike the simpler version in
        // CatFlapDailyChallengeFailPopupController) exists because winParticles can draw on top of
        // a plain nested-Canvas RawImage at an exactly equal Order in Layer - padding well past any
        // particle/decoration order in this popup makes the ordering unambiguous.
        private void EnsureCatVideoPlayer()
        {
            if (catVideoRenderer == null)
            {
                return;
            }

            if (catVideoPlayer == null)
            {
                catVideoPlayer = catVideoRenderer.GetComponent<VideoPlayer>();
                if (catVideoPlayer == null)
                {
                    catVideoPlayer = catVideoRenderer.gameObject.AddComponent<VideoPlayer>();
                }
            }

            if (catVideoImage == null)
            {
                var imageObject = new GameObject("CatOpenVideoImage", typeof(RectTransform), typeof(Canvas), typeof(RawImage));
                RectTransform sourceRect = catVideoRenderer.GetComponent<RectTransform>();
                RectTransform imageRect = (RectTransform)imageObject.transform;
                imageRect.SetParent(sourceRect != null ? sourceRect.parent : catVideoRenderer.transform.parent, false);

                const int SortingOrderPadding = 20;
                Canvas videoCanvas = imageObject.GetComponent<Canvas>();
                videoCanvas.overrideSorting = true;
                videoCanvas.sortingLayerID = catVideoRenderer.sortingLayerID;
                videoCanvas.sortingOrder = catVideoRenderer.sortingOrder + SortingOrderPadding;

                Vector2 renderedSize = sourceRect != null ? sourceRect.sizeDelta : new Vector2(800f, 800f);
                if (catVideoRenderer.sprite != null)
                {
                    Vector3 spriteSize = catVideoRenderer.sprite.bounds.size;
                    Vector3 localScale = catVideoRenderer.transform.localScale;
                    renderedSize = new Vector2(spriteSize.x * localScale.x, spriteSize.y * localScale.y);
                }

                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = renderedSize;
                imageRect.anchoredPosition = sourceRect != null ? sourceRect.anchoredPosition : Vector2.zero;
                if (sourceRect != null)
                {
                    imageRect.SetSiblingIndex(sourceRect.GetSiblingIndex() + 1);
                }

                catVideoImage = imageObject.GetComponent<RawImage>();
                catVideoImage.raycastTarget = false;
                imageObject.SetActive(false);
            }

            if (catVideoRenderTexture == null)
            {
                int width = catOpenVideoClip != null && catOpenVideoClip.width > 0 ? (int)catOpenVideoClip.width : 720;
                int height = catOpenVideoClip != null && catOpenVideoClip.height > 0 ? (int)catOpenVideoClip.height : 720;
                catVideoRenderTexture = new RenderTexture(width, height, 0);
                catVideoImage.texture = catVideoRenderTexture;
            }

            catVideoPlayer.playOnAwake = false;
            catVideoPlayer.isLooping = true;
            catVideoPlayer.waitForFirstFrame = true;
            catVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            catVideoPlayer.targetTexture = catVideoRenderTexture;
            catVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        // Plays catOpenVideoClip on a loop for as long as this popup stays open (isLooping is set
        // in EnsureCatVideoPlayer) - never stops itself or reverts to the static poster sprite on
        // its own; ResetVisualState does that when the popup is closed/reopened.
        private IEnumerator PlayCatOpenAnimation()
        {
            EnsureCatVideoPlayer();
            if (catVideoPlayer == null || catOpenVideoClip == null || catVideoImage == null)
            {
                yield break;
            }

            catVideoPlayer.Stop();
            catVideoPlayer.clip = catOpenVideoClip;

            // Swapping to the RawImage/hiding the poster sprite BEFORE the video actually has a
            // decoded frame ready shows a blank/stale RenderTexture for a frame or two. Prepare +
            // wait for the first real frame first, so the swap only happens once there's real
            // video content to show.
            catVideoPlayer.Prepare();
            while (!catVideoPlayer.isPrepared)
            {
                yield return null;
            }

            catVideoPlayer.Play();
            while (!catVideoPlayer.isPlaying)
            {
                yield return null;
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = false;
            }

            catVideoImage.gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            primaryButton?.onClick.AddListener(OnPrimaryClicked);
            backButton?.onClick.AddListener(OnBackClicked);
            adsButton?.onClick.AddListener(RequestWatchAd);
            // Close (X) is not a separate destination - it's wired straight to the same handler
            // as Primary, guard included, rather than its own action.
            closeButton?.onClick.AddListener(OnPrimaryClicked);
            skipButton?.onClick.AddListener(SkipCoinAnimation);
            skipButton?.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            primaryButton?.onClick.RemoveListener(OnPrimaryClicked);
            backButton?.onClick.RemoveListener(OnBackClicked);
            adsButton?.onClick.RemoveListener(RequestWatchAd);
            closeButton?.onClick.RemoveListener(OnPrimaryClicked);
            skipButton?.onClick.RemoveListener(SkipCoinAnimation);

            hasPlayedOpen = false;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }
            StopContinueBob();
        }

        // title is unused here - the Win popup's title is predefined art/text baked into
        // titleImage's prefab content, not set at runtime (kept in the signature to stay
        // symmetric with CatFlapDailyChallengeFailPopupController and the bridge's call site).
        public void Show(string title, string progressText, string primaryLabel, bool showBack, Action primaryAction, Action backAction)
        {
            if (root == null)
            {
                return;
            }

            SetText(primaryButtonLabel, primaryLabel);
            showBackButton = showBack;
            backButton?.gameObject.SetActive(showBack);

            onPrimary = primaryAction;
            onBack = backAction;

            // TODO: fake placeholder values until moves/timer data is actually wired up for
            // Daily Challenge sub-levels - mirrors LevelWinPopupController's own
            // SetBeatPercentage(90f) TODO for its "beat X% of players" stat.
            // winCoin/currentCoin are NOT reset here - CatFlapDailyChallengeBridge calls
            // SetCurrentCoin with the real pre-win balance/reward just before this runs.
            pendingMovesValue = "0";
            pendingTimerValue = "00:00";

            // Set the real pre-reward balance before the panel is activated or any opening
            // animation can begin.
            SetText(CoinText, currentCoin.ToString());

            root.SetActive(true);
            CacheHomePositions();
            ResetVisualState();
            RefreshFeatureAvailabilityBanner();

            hasPlayedOpen = true;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
            }

            openRoutine = StartCoroutine(WaitForCoinDataAndPlayOpen(progressText));
        }

        private IEnumerator WaitForCoinDataAndPlayOpen(string progressText)
        {
            // SetCurrentCoin is supplied by the bridge before/around Show(). Wait until the
            // real wallet value exists so CoinText is correct before any opening animation starts.
            while (!coinDataReady)
            {
                yield return null;
            }

            openRoutine = StartCoroutine(PlayOpenSequence(progressText));
        }

        public void Hide()
        {
            if (root == null || !root.activeSelf)
            {
                return;
            }

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            StopContinueBob();
            root.SetActive(false);
        }

        // Nudges the player toward Spin the Wheel / Daily Reward whenever either one actually has
        // something waiting for them right now.
        private void RefreshFeatureAvailabilityBanner()
        {
            if (spinOrRewardAvailableBanner == null)
            {
                return;
            }

            bool spinAvailable = spinWheelManager != null && spinWheelManager.IsSpinAvailable();
            bool rewardAvailable = dailyRewardManager != null && dailyRewardManager.CanClaimMain();

            spinOrRewardAvailableBanner.SetActive(spinAvailable || rewardAvailable);
        }

        // Wired to the Primary button - ignores clicks until the button itself has actually
        // popped in. A repeat click once an action has already been requested (the coin animation
        // is playing) SKIPS it instead of being ignored - see SkipCoinAnimation. Plays the same
        // coin-collect VFX as LevelWinPopupController.OnNextLevelClicked before handing off to the
        // bridge-supplied action, instead of hiding immediately.
        public void OnPrimaryClicked()
        {
            if (!hasPlayedOpen || !primaryButtonReady || openAnimationPlaying)
            {
                return;
            }
            StopContinueBob();
            SetButtonsInteractable(false);
            Action action = onPrimary;
            onPrimary = null;
            onBack = null;
            AnimateCoins(winCoin, () =>
            {
                Hide();
                action?.Invoke();
            });
        }

        // Fires WatchAdRequested - CatFlapDailyChallengeBridge shows the fake-ads dialog and, once
        // actually watched, calls DoubleWinCoinsAndContinue below (same pattern as
        // LevelWinPopupController's ads button / CatFlapGameplayManager.HandleLevelWinWatchAd).
        public UnityEvent WatchAdRequested = new UnityEvent();

        private void RequestWatchAd()
        {
            if (actionRequested || adWatchInProgress || openAnimationPlaying)
            {
                return;
            }
            adWatchInProgress = true;
            // Disable touch on all four buttons the instant the tap registers, same convention as
            // PurchaseSuccessPopupController.RequestClose - confirms the tap landed and blocks a
            // second tap while AdsManager is still showing/resolving the ad.
            SetButtonsInteractable(false);
            WatchAdRequested.Invoke();
        }

        /// <summary>
        /// Called by CatFlapDailyChallengeBridge.HandleWinWatchAd when the rewarded ad callback
        /// reports failure/decline, so the player isn't stranded with every button disabled and
        /// no way to continue.
        /// </summary>
        public void OnWatchAdDeclined()
        {
            if (actionRequested)
            {
                return;
            }
            adWatchInProgress = false;
            SetButtonsInteractable(true);
        }

        // Same disable-on-tap convention as PurchaseSuccessPopupController.RequestClose - applied
        // to all four win-popup buttons together since Primary/Back/Close/2x all lead to leaving
        // this popup and none of them should still register a tap once one has been actioned.
        private void SetButtonsInteractable(bool interactable)
        {
            if (primaryButton != null)
            {
                primaryButton.interactable = interactable;
            }
            if (backButton != null)
            {
                backButton.interactable = interactable;
            }
            if (adsButton != null)
            {
                adsButton.interactable = interactable;
            }
            if (closeButton != null)
            {
                closeButton.interactable = interactable;
            }
        }

        // bobButtonContainer decides once (Inspector-only, no runtime switching) whether the idle
        // bob below moves the whole button row or just the Primary/Continue button by itself.
        // Falls back to primaryButtonTransform if bobButtonContainer is checked but buttonContainer
        // was left unassigned.
        private RectTransform ResolveBobTarget()
        {
            return bobButtonContainer && buttonContainer != null ? buttonContainer : primaryButtonTransform;
        }

        // Draws the eye to Primary/Continue (or the whole button row - see ResolveBobTarget) if the
        // player just sits on this popup - starts continueBobDelaySeconds after the Primary button
        // finishes popping in (see PlayButtonsSequence), then bobs the resolved target up/down
        // forever until StopContinueBob cancels it (tapping Primary/Back/Close, or this popup
        // closing/reopening).
        private void StartContinueBob()
        {
            RectTransform target = ResolveBobTarget();
            if (target == null)
            {
                return;
            }

            continueBobRestingPos = target.localScale;
            continueBobRestingCaptured = true;

            continueBobTween?.Kill();
            continueBobTween = DOVirtual.DelayedCall(continueBobDelaySeconds, () =>
            {
                if (target == null || actionRequested)
                {
                    return;
                }

                continueBobTween = target
                    .DOScale(continueBobRestingPos * continueBobAmplitude, continueBobDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }).SetUpdate(true);
        }

        // Kills whichever tween is currently pending/running (the delay itself, or the loop once it
        // has started) and snaps back to rest - same interrupted-animation guard used throughout this
        // popup, so the bobbed target can never be caught mid-bob when this popup closes or a click
        // fires. Reads the SAME target ResolveBobTarget would return right now rather than
        // remembering which one StartContinueBob used, but bobButtonContainer is Inspector-only (not
        // meant to change at runtime), so the two always agree in practice.
        private void StopContinueBob()
        {
            continueBobTween?.Kill();
            continueBobTween = null;

            RectTransform target = ResolveBobTarget();
            if (continueBobRestingCaptured && target != null)
            {
                target.localScale = continueBobRestingPos;
            }
            continueBobRestingCaptured = false;
        }

        /// <summary>
        /// Called once the fake-ads dialog reports the ad was actually watched - doubles the
        /// pending coin reward, updates the "+winCoin" label to match, then proceeds exactly like
        /// tapping Primary (same as LevelWinPopupController.DoubleWinCoinsAndContinue).
        /// </summary>
        public void DoubleWinCoinsAndContinue()
        {
            // Keep currentCoin as the real pre-win wallet balance. Only the reward amount changes
            // when the player chooses 2x; the coin display will animate from currentCoin to finalCoin.
            winCoin *= 2;
            finalCoin = currentCoin + winCoin;
            SetText(winCoinText, $"+{winCoin}");
            OnPrimaryClicked();
        }

        // Wired to the Back button. Same click-guard semantics as OnPrimaryClicked.
        public void OnBackClicked()
        {
            if (actionRequested || !hasPlayedOpen || !backButtonReady || openAnimationPlaying)
            {
                return;
            }
            actionRequested = true;
            StopContinueBob();
            SetButtonsInteractable(false);
            Action action = onBack;
            onPrimary = null;
            onBack = null;
            AnimateCoins(winCoin, () =>
            {
                Hide();
                action?.Invoke();
            });
        }

        // Set for the CURRENTLY in-flight AnimateCoins call - CompleteCoinAnimation invokes
        // whichever of these was captured, guarded so it only ever fires once (see
        // SkipCoinAnimation: CoinCollectAnimator has no cancel API, so its own internal per-coin
        // timers keep running and would otherwise eventually call onAllCoinsComplete a second time
        // after a skip already fired it early).
        private Action pendingCoinCompleteCallback;
        private bool coinAnimationFinished = true;

        // Coin-collect VISUALS only (CoinCollectAnimator's bloom/hover/stream) - identical to
        // LevelWinPopupController.AnimateCoins, kept as its own copy here because this popup lives
        // in a different namespace/prefab and has no dependency on Shared.GameFlow otherwise.
        private void AnimateCoins(int coins, Action onComplete)
        {
            coinAnimationFinished = false;
            pendingCoinCompleteCallback = onComplete;
            skipButton?.gameObject.SetActive(true);
            primaryButtonTransform?.gameObject.SetActive(false);

            foreach (var vfxObj in vfxToHideObjs)
                vfxObj.gameObject.SetActive(false);

            var coinPrefab = coinVFXPrefab;
            var burstOrigin = coinVFXSpawnTransform != null
                ? coinVFXSpawnTransform.position
                : coinImage.rectTransform.position;
            var targetPosition = levelCoinsImage.rectTransform.position;
            var parent = vfxContainerObj.transform;

            int coinCount = CoinCollectAnimator.CoinCountFor(coins, coinCollectSettings);

            // CoinCollectAnimator's own doc comment on onCoinArrived is explicit about this: "drive
            // the wallet from this, not from a roll of your own, or the number will finish before
            // the coins do." currentCoin's final value is split evenly across every coin that's
            // about to land (remainder absorbed by the very last one, so the total always ends up
            // exactly currentCoin) - each arrival below nudges the displayed total up by its share
            // instead of one continuous tween kicked off by the first coin alone.
            // currentCoin is always the real wallet balance BEFORE this reward is collected.
            // Never derive this from CoinText because CoinText is only the visual counter.
            coinTextBaseValue = currentCoin;
            coinTextPerCoinValue = coinCount > 0 ? coins / coinCount : coins;
            coinTextPerCoinRemainder = coinCount > 0 ? coins % coinCount : 0;
            finalCoin = currentCoin + coins;

            // Start the visual counter from the real pre-reward balance.
            SetText(CoinText, coinTextBaseValue.ToString());

            CoinCollectAnimator.Play(coinCount, coinPrefab, parent,
                burstOrigin,
                targetPosition,
                levelCoinsImage.rectTransform.sizeDelta,
                coinCollectSettings,
                onCoinSpawned: null,
                onCoinArrived: (arrivedCount, totalCount) =>
                {
                    // Every coin, not just the first - plays right as each one actually lands and
                    // is destroyed (see CoinCollectAnimator.Stream, which fires onCoinArrived at
                    // that exact moment), not just once for the whole collection.
                    PlayLevelCoinVfx();

                    UpdateLevelCoins(arrivedCount, totalCount);
                },
                onAllCoinsComplete: delegate
                {
                    ForceUpdateLevelCoinText();

                    DOVirtual.DelayedCall(0.3f, CompleteCoinAnimation);
                });
        }

        // Fires pendingCoinCompleteCallback exactly once then clears it - called either from the
        // coin animation's own natural finish above, or immediately from SkipCoinAnimation.
        private void CompleteCoinAnimation()
        {
            if (coinAnimationFinished)
            {
                return;
            }

            coinAnimationFinished = true;
            skipButton?.gameObject.SetActive(false);
            primaryButtonTransform?.gameObject.SetActive(true);
            Action callback = pendingCoinCompleteCallback;
            pendingCoinCompleteCallback = null;
            callback?.Invoke();
        }

        // Tapping Primary again while its own coin-collect animation is still playing skips
        // straight to whichever action was requested instead of being silently ignored.
        // CoinCollectAnimator has no cancel API (see AnimateCoins) - its per-coin visuals are
        // destroyed here so nothing keeps flying on screen, and the coin total snaps to its final
        // value immediately, but CoinCollectAnimator's own untargeted internal timers keep running
        // regardless; that's harmless since CompleteCoinAnimation's guard means they can only ever
        // no-op afterward.
        public void SkipCoinAnimation()
        {
            if (coinAnimationFinished)
            {
                return;
            }

            if (vfxContainerObj != null)
            {
                for (int index = vfxContainerObj.transform.childCount - 1; index >= 0; index--)
                {
                    Destroy(vfxContainerObj.transform.GetChild(index).gameObject);
                }
            }

            ForceUpdateLevelCoinText();
            CompleteCoinAnimation();
        }

        private Sequence coinLevelTextSequence;
        private int coinTextBaseValue;
        private int coinTextPerCoinValue;
        private int coinTextPerCoinRemainder;

        private void ForceUpdateLevelCoinText()
        {
            coinLevelTextSequence?.Complete();
            SetText(CoinText, finalCoin.ToString());
        }

        // Re-triggered on every coin's arrival (not just once) - if it carries a ParticleSystem,
        // Clear()+Play() so it actually restarts each time instead of SetActive(true) silently
        // no-opping on an object that's already active from an earlier coin. Falls back to a
        // disable/re-enable toggle for anything else driven off OnEnable.
        private void PlayLevelCoinVfx()
        {
            if (levelCoinVfxObj == null)
            {
                return;
            }

            ParticleSystem[] particles = levelCoinVfxObj.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length > 0)
            {
                levelCoinVfxObj.SetActive(true);
                for (int index = 0; index < particles.Length; index++)
                {
                    particles[index].Clear();
                    particles[index].Play();
                }
                return;
            }

            levelCoinVfxObj.SetActive(false);
            levelCoinVfxObj.SetActive(true);
        }

        private void UpdateLevelCoins(int arrivedCount, int totalCount)
        {
            int targetValue = coinTextBaseValue + coinTextPerCoinValue * arrivedCount
                + (arrivedCount == totalCount ? coinTextPerCoinRemainder : 0);
            var textCoins = string.IsNullOrEmpty(CoinText.text) ? 0 : int.Parse(CoinText.text);
            coinLevelTextSequence?.Complete();
            coinLevelTextSequence = Shared.GameFlow.LevelWinPopupController.UpdateCountText(
                textCoins, targetValue, levelCoinsImage, CoinText, delegate { });
        }

        public void SetCurrentCoin(int coins, int winCoinAmount)
        {
            currentCoin = coins;
            winCoin = winCoinAmount;
            finalCoin = currentCoin + winCoin;
            coinDataReady = true;

            // Refresh the visual wallet immediately from the real value supplied by the bridge.
            SetText(CoinText, currentCoin.ToString());
            SetText(winCoinText, $"+{winCoin}");
        }

        /// <summary>Sets the challenge date shown at the top of the popup (e.g. "September 3").</summary>
        public void SetDate(string date)
        {
            SetText(dateText, date);
        }

        /// <summary>Shows/hides the 2x reward (watch-ad) button - defaults to visible.</summary>
        public void SetAdsButtonVisible(bool visible)
        {
            showAdsButton = visible;
        }

        public void SetStats(int moves, float elapsedSeconds)
        {
            pendingMovesValue = Mathf.Max(0, moves).ToString();
            pendingTimerValue = FormatTime(elapsedSeconds);

            if (hasPlayedOpen && openRoutine == null)
            {
                SetText(movesText, pendingMovesValue);
                SetText(timerText, pendingTimerValue);
            }
        }

        private IEnumerator PlayOpenSequence(string progressText)
        {
            if (particleDelay > 0f)
            {
                yield return new WaitForSeconds(particleDelay);
            }

            if (winParticles != null)
            {
                winParticles.Play();
                AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.LwinConfitti);
            }

            yield return StartCoroutine(FadeInBackground());

            yield return StartCoroutine(ScaleUp(titleImage, catScaleDuration));

            yield return StartCoroutine(MoveIn(coinContainer, Vector2.left * offscreenPadding, coinMoveDuration));

            // Video prep/first-frame-decode starts NOW, alongside the scale-up, rather than only
            // after it finishes - otherwise the poster sprite sits visible for the whole scale-up
            // and only swaps to video right as it completes, reading as a separate "then character
            // animation plays" beat instead of one continuous reveal.
            StartCoroutine(PlayCatOpenAnimation());
            yield return StartCoroutine(ScaleUp(characterTransform, catScaleDuration, Vector3.one * characterTargetScale));

            SetText(movesText, pendingMovesValue);
            SetText(timerText, pendingTimerValue);
            yield return StartCoroutine(ScaleUp(timerContainer, timeTextScaleDuration));

            SetText(winCoinText, $"+{winCoin}");
            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.LevelCoinReward);
            // currentCoin remains the real pre-win wallet balance. The final value is stored
            // separately so the UI can reveal it only through the coin-collect animation.
            yield return StartCoroutine(RunParallel(
                Typewriter(progressLabel, progressText),
                ScaleUp(winCoinContainer, coinWinScaleDuration),
                PlayButtonsSequence()
            ));

            // Only now (the whole open sequence, not just the buttons' own pop-in) has finished -
            // Primary/Ads/Back/Close can register a click.
            openAnimationPlaying = false;

            openRoutine = null;
        }

        // ---------- Individual effects ----------

        private IEnumerator PlayButtonsSequence()
        {
            float stagger = buttonScaleDuration * 0.85f;

            if (showAdsButton)
            {
                StartCoroutine(ScaleUp(adsButtonTransform, buttonScaleDuration));
                yield return new WaitForSeconds(stagger);
            }

            StartCoroutine(ScaleUpThenMarkReady(primaryButtonTransform, buttonScaleDuration, () =>
            {
                primaryButtonReady = true;
                StartContinueBob();
            }));
            yield return new WaitForSeconds(stagger);

            if (showBackButton)
            {
                yield return StartCoroutine(ScaleUpThenMarkReady(backButtonTransform, buttonScaleDuration, () => backButtonReady = true));
            }
            else
            {
                backButtonReady = true;
            }
        }

        private IEnumerator ScaleUpThenMarkReady(RectTransform target, float duration, Action onReady)
        {
            yield return StartCoroutine(ScaleUp(target, duration));
            onReady?.Invoke();
        }

        private IEnumerator FadeInBackground()
        {
            if (backgroundGroup == null)
            {
                yield break;
            }
            float elapsed = 0f;
            backgroundGroup.alpha = 0f;
            while (elapsed < backgroundFadeDuration)
            {
                elapsed += Time.deltaTime;
                backgroundGroup.alpha = Mathf.Clamp01(elapsed / backgroundFadeDuration);
                yield return null;
            }
            backgroundGroup.alpha = 1f;
        }

        private IEnumerator ScaleUp(RectTransform target, float duration, Vector3? targetScale = null)
        {
            if (target == null)
            {
                yield break;
            }
            Vector3 fullScale = targetScale ?? Vector3.one;
            target.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = fullScale * t;
                yield return null;
            }
            target.localScale = fullScale;
        }

        private IEnumerator MoveIn(RectTransform target, Vector2 offscreenOffset, float duration)
        {
            if (target == null)
            {
                yield break;
            }
            Vector2 homePos = GetHomePosition(target);
            Vector2 startPos = homePos + offscreenOffset;
            target.anchoredPosition = startPos;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
                target.anchoredPosition = Vector2.LerpUnclamped(startPos, homePos, t);
                yield return null;
            }
            target.anchoredPosition = homePos;
        }

        private IEnumerator Typewriter(TMP_Text label, string fullValue)
        {
            if (label == null)
            {
                yield break;
            }
            if (string.IsNullOrEmpty(fullValue))
            {
                label.text = string.Empty;
                yield break;
            }

            label.text = fullValue;
            label.ForceMeshUpdate();
            int totalVisibleCharacters = label.textInfo.characterCount;
            label.maxVisibleCharacters = 0;

            float secondsPerChar = 1f / Mathf.Max(1f, typewriterCharsPerSecond);
            for (int visible = 1; visible <= totalVisibleCharacters; visible++)
            {
                label.maxVisibleCharacters = visible;
                yield return new WaitForSeconds(secondsPerChar);
            }
        }

        private IEnumerator RunParallel(params IEnumerator[] routines)
        {
            var running = new Coroutine[routines.Length];
            for (int index = 0; index < routines.Length; index++)
            {
                running[index] = StartCoroutine(routines[index]);
            }
            for (int index = 0; index < running.Length; index++)
            {
                if (running[index] != null)
                {
                    yield return running[index];
                }
            }
        }

        // ---------- Setup helpers ----------

        private void ResetVisualState()
        {
            actionRequested = false;
            primaryButtonReady = false;
            backButtonReady = false;
            adWatchInProgress = false;
            openAnimationPlaying = true;
            StopContinueBob();
            SetButtonsInteractable(true);
            skipButton?.gameObject.SetActive(false);
            primaryButtonTransform?.gameObject.SetActive(true);

            // Guards against a popup closed mid-video (PlayCatOpenAnimation's own StartCoroutine
            // isn't tracked/cancelled by OnDisable) leaving the video image stuck visible and the
            // poster sprite stuck hidden the next time this popup opens.
            if (catVideoPlayer != null)
            {
                catVideoPlayer.Stop();
            }

            if (catVideoImage != null)
            {
                catVideoImage.gameObject.SetActive(false);
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = true;
            }

            if (backgroundGroup != null)
            {
                backgroundGroup.alpha = 0f;
            }
            if (titleImage != null)
            {
                titleImage.localScale = Vector3.zero;
            }
            if (characterTransform != null)
            {
                characterTransform.localScale = Vector3.zero;
            }
            if (winCoinContainer != null)
            {
                winCoinContainer.localScale = Vector3.zero;
            }
            if (timerContainer != null)
            {
                timerContainer.localScale = Vector3.zero;
            }
            if (primaryButtonTransform != null)
            {
                primaryButtonTransform.localScale = Vector3.zero;
            }
            if (backButtonTransform != null)
            {
                backButtonTransform.localScale = Vector3.zero;
            }
            if (adsButtonTransform != null)
            {
                adsButtonTransform.localScale = Vector3.zero;
                adsButtonTransform.gameObject.SetActive(showAdsButton);
            }
            SetText(movesText, string.Empty);
            SetText(timerText, string.Empty);
            SetText(progressLabel, string.Empty);

            // CoinText is reset only ONCE. Subsequent opens use the value supplied by SetCurrentCoin.
            if (!_hasResetCoinTextOnce)
            {
                SetText(CoinText, currentCoin.ToString());
                _hasResetCoinTextOnce = true;
            }

            SetText(winCoinText, string.Empty);

            if (cachedHomePositions && coinContainer != null)
            {
                coinContainer.anchoredPosition = coinContainerHomePos + Vector2.left * offscreenPadding;
            }
        }

        private void CacheHomePositions()
        {
            if (cachedHomePositions)
            {
                return;
            }
            if (coinContainer != null) coinContainerHomePos = coinContainer.anchoredPosition;
            if (primaryButtonTransform != null) primaryButtonHomePos = primaryButtonTransform.anchoredPosition;
            if (backButtonTransform != null) backButtonHomePos = backButtonTransform.anchoredPosition;
            cachedHomePositions = true;
        }

        private Vector2 GetHomePosition(RectTransform target)
        {
            if (target == coinContainer) return coinContainerHomePos;
            if (target == primaryButtonTransform) return primaryButtonHomePos;
            if (target == backButtonTransform) return backButtonHomePos;
            return target.anchoredPosition;
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
                label.maxVisibleCharacters = int.MaxValue;
            }
        }

        private static string FormatTime(float elapsedSeconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private static float EaseOutCubic(float t)
        {
            float f = t - 1f;
            return f * f * f + 1f;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }
    }
}
