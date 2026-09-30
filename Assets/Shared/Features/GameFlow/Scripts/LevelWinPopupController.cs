using FillIt.UnityAdapters;
using DG.Tweening;
using Game.SpinWheel;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Shared.GameFlow
{
    public sealed class LevelWinPopupController : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private TMP_Text movesText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private RectTransform timerContainer;
        [SerializeField] private TMP_Text beatPlayersText;
        [SerializeField] private TMP_Text CoinText;

        [Header("Win Sequence")]
        [SerializeField] private ParticleSystem winParticles;
        [SerializeField] private CanvasGroup backgroundGroup;
        [SerializeField] private RectTransform titleImage;
        [SerializeField] private RectTransform catTransform;
        // catTransform's asset is authored tiny relative to its sibling UI, so its open-sequence
        // scale-up target isn't Vector3.one like everything else - see ScaleUp's targetScale param.
        [SerializeField] private float catTargetScale = 300f;
        [SerializeField] private RectTransform coinContainer;
        [SerializeField] private RectTransform winCoinContainer;
        [SerializeField] private TMP_Text winCoinText;

        [Header("Cat Open Animation (VideoPlayer, plays once when this popup appears)")]
        [SerializeField] private SpriteRenderer catVideoRenderer;
        [SerializeField] private VideoClip catOpenVideoClip;

        private VideoPlayer catVideoPlayer;
        private RawImage catVideoImage;

        [Header("Bottom Buttons - scale up, ease back out, in this order")]
        [SerializeField] private RectTransform adsButtonTransform;
        [SerializeField] private Button adsButton;
        [SerializeField] private RectTransform continueButtonTransform;
        [SerializeField, FormerlySerializedAs("homeButton")] private RectTransform closeButtonTransform;

        [Tooltip("Hidden by default. Shown the instant the coin-collect animation starts (see AnimateCoins), hidden again once it finishes or is skipped. Tapping it calls SkipCoinAnimation directly.")]
        [SerializeField] private Button skipButton;

        [Header("Continue Button Idle Bob (draws the eye to it if the player just sits on this popup)")]
        [Tooltip("Optional. Wraps Continue/Ads/Close as one row. Only used when bobButtonContainer is checked - otherwise the Continue button bobs alone.")]
        [SerializeField] private RectTransform buttonContainer;
        [Tooltip("Checked: bobs the whole buttonContainer row instead of just the Continue button. Unchecked (default): only the Continue button bobs.")]
        [SerializeField] private bool bobButtonContainer;
        [Tooltip("Seconds after the Continue button finishes popping in before it starts bobbing.")]
        [SerializeField] private float continueBobDelaySeconds = 1.5f;
        [SerializeField] private float continueBobAmplitude = 1.15f;
        [SerializeField] private float continueBobDuration = 0.55f;

        private Tween continueBobTween;
        private Vector3 continueBobRestingPos;
        private bool continueBobRestingCaptured;

        // Fires when the "2x" button is tapped - the caller is expected to show the fake-ads
        // dialog and, on watched, call DoubleWinCoinsAndContinue (see
        // FillItGameplayManager.HandleLevelWinWatchAd, mirroring the Out Of Lives watch-ad
        // pattern). Nothing happens here on its own if no listener is bound.
        public UnityEvent WatchAdRequested = new UnityEvent();

        [Header("Timing")]
        // Entry animation tuned 2x faster than original (durations halved, typewriter speed
        // doubled) - see LevelFailPopupController/OutOfLivesPopupController/
        // CatFlapDailyChallengeWinPopupController/CatFlapDailyChallengeFailPopupController for the
        // matching change applied to every level win/fail/out-of-lives popup, Daily Challenge
        // included.
        [SerializeField] private float particleDelay = 0f;
        [SerializeField] private float backgroundFadeDuration = 0.125f;
        [SerializeField] private float catScaleDuration = 0.175f;
        [SerializeField] private float buttonScaleDuration = 0.2f;
        [SerializeField] private float coinMoveDuration = 0.125f;
        [SerializeField] private float coinWinScaleDuration = 0.175f;
        [SerializeField] private float timeTextScaleDuration = 0.15f;
        [SerializeField] private float typewriterCharsPerSecond = 80f;
        [SerializeField] private float offscreenPadding = 200f;
        [SerializeField] private Color beatPlayersHighlightColor = new Color(1f, 0.54f, 0.4f);

        [Header("Coin Header")]
        [SerializeField] List<GameObject> vfxToHideObjs = new List<GameObject>();
        [FormerlySerializedAs("CatFlapGameplayManager")]
        [SerializeField] FillItGameplayManager gameplayManager;
        [SerializeField] Image coinVFXPrefab;
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


        private bool hasPlayedOpen;
        private Coroutine openRoutine;

        private string pendingMovesValue = "0";
        private string pendingTimerValue = "00:00";
        private string pendingBeatPlayersValue = string.Empty;

        private Vector2 coinContainerHomePos;
        private Vector2 adsButtonHomePos;
        private Vector2 continueButtonHomePos;
        private Vector2 closeButtonHomePos;
        private bool cachedHomePositions;

        // True unless SetAdsButtonVisible(false) is called before this popup opens (see
        // FillItGameplayManager.UpdateLevelWinPopup / GlobalConfig.NoAdsBeforeLevel).
        private bool showAdsButton = true;

        // True unless SetHomeButtonVisible(false) is called before this popup opens (see
        // FillItGameplayManager.UpdateLevelWinPopup) - hidden for the first 3 levels so a brand
        // new player can only go forward via Next, not back out to the main menu mid-tutorial.
        private bool showHomeButton = true;

        private int currentCoin;
        private int winCoin;
        private int finalCoin;
        private bool coinDataReady;
        private static bool _hasResetCoinTextOnce;
        private bool nextLevelRequested;
        private bool continueButtonReady;
        private bool homeButtonReady;

        // Blocks Continue/Home/Ads clicks purely at the function level (Button.interactable/tint
        // untouched, so they stay full color throughout) from the moment the open sequence starts
        // until the WHOLE sequence - not just each button's own pop-in - actually finishes. See
        // ResetVisualState and the tail of PlayOpenSequence.
        private bool openAnimationPlaying = true;

        // Guards the "2x" button separately from nextLevelRequested - watching an ad doesn't
        // resolve to Continue/Home until AdsManager's callback actually reports success (see
        // FillItGameplayManager.HandleLevelWinWatchAd), so a declined/failed ad needs to hand
        // control back to the player via OnWatchAdDeclined instead of leaving every button
        // permanently disabled.
        private bool adWatchInProgress;
        private Button continueButton;
        private Button closeButton;

        private void Awake()
        {
            CacheHomePositions();
            if (continueButtonTransform != null)
            {
                continueButton = continueButtonTransform.GetComponent<Button>();
            }
            if (closeButtonTransform != null)
            {
                closeButton = closeButtonTransform.GetComponent<Button>();
            }
            ResetVisualState();
            EnsureCatVideoPlayer();
            if (adsButton != null)
            {
                adsButton.onClick.RemoveListener(RequestWatchAd);
                adsButton.onClick.AddListener(RequestWatchAd);
            }

            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(SkipCoinAnimation);
                skipButton.onClick.AddListener(SkipCoinAnimation);
                skipButton.gameObject.SetActive(false);
            }
        }

        private void RequestWatchAd()
        {
            if (nextLevelRequested || adWatchInProgress || openAnimationPlaying)
            {
                return;
            }
            adWatchInProgress = true;
            // Disable touch on all three buttons the instant the tap registers, same convention
            // as PurchaseSuccessPopupController.RequestClose - visually confirms the tap landed
            // and blocks a second tap while AdsManager is still showing/resolving the ad.
            SetButtonsInteractable(false);
            WatchAdRequested.Invoke();
        }

        /// <summary>
        /// Called by FillItGameplayManager.HandleLevelWinWatchAd when the rewarded ad callback
        /// reports failure/decline, so the player isn't stranded with every button disabled and
        /// no way to continue.
        /// </summary>
        public void OnWatchAdDeclined()
        {
            if (nextLevelRequested)
            {
                return;
            }
            adWatchInProgress = false;
            SetButtonsInteractable(true);
        }

        // Same disable-on-tap convention as PurchaseSuccessPopupController.RequestClose - applied
        // to all three win-popup buttons together since Continue/Home/2x all lead to leaving this
        // popup and none of them should still register a tap once one has been actioned.
        private void SetButtonsInteractable(bool interactable)
        {
            if (adsButton != null)
            {
                adsButton.interactable = interactable;
            }
            if (continueButton != null)
            {
                continueButton.interactable = interactable;
            }
            if (closeButton != null)
            {
                closeButton.interactable = interactable;
            }
        }

        // bobButtonContainer decides once (Inspector-only, no runtime switching) whether the idle
        // bob below moves the whole bottom-button row or just the Continue button by itself.
        // Falls back to continueButtonTransform if bobButtonContainer is checked but buttonContainer
        // was left unassigned.
        private RectTransform ResolveBobTarget()
        {
            return bobButtonContainer && buttonContainer != null ? buttonContainer : continueButtonTransform;
        }

        // Draws the eye to Continue (or the whole button row - see ResolveBobTarget) if the player
        // just sits on this popup - starts continueBobDelaySeconds after the Continue button finishes
        // popping in (see PlayButtonsSequence), then bobs the resolved target up/down forever until
        // StopContinueBob cancels it (tapping Continue/Home, or this popup closing/reopening).
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
                if (target == null || nextLevelRequested)
                {
                    return;
                }

                continueBobTween = target
                    .DOScale(continueBobRestingPos* continueBobAmplitude, continueBobDuration)
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
        /// Called once the fake-ads dialog reports the ad was actually watched (see
        /// FillItGameplayManager.HandleLevelWinWatchAd) - doubles the pending coin reward, updates
        /// the "+winCoin" label to match, then proceeds exactly like tapping Continue/Next.
        /// </summary>
        public void DoubleWinCoinsAndContinue()
        {
            // Keep currentCoin as the real pre-win wallet balance. Only the reward amount changes
            // when the player chooses 2x; the coin display will animate from currentCoin to finalCoin.
            winCoin *= 2;
            finalCoin = currentCoin + winCoin;
            SetText(winCoinText, $"+{winCoin}");
            OnNextLevelClicked();
        }

        // GameObject name of the RawImage that "MangraGames > Bake Cat Video Image Into Prefabs"
        // (Assets/Shared/Features/GameFlow/Editor) permanently authors as catVideoRenderer's
        // sibling - this is only ever LOOKED UP by name at runtime, never built here.
        private const string CatVideoImageName = "CatOpenVideoImage";

        public VideoClip CatOpenVideoClip => catOpenVideoClip;

        // Pure runtime lookup - the RawImage, VideoPlayer, RenderTexture, and clip assignment are
        // all baked into the prefab ahead of time (see BakeCatVideoSetup below, run once via the
        // editor bake command); nothing is created or configured here.
        public void EnsureCatVideoPlayer()
        {
            if (catVideoRenderer == null)
            {
                return;
            }

            if (catVideoPlayer == null)
            {
                catVideoPlayer = catVideoRenderer.GetComponent<VideoPlayer>();
            }

            if (catVideoImage == null && catVideoRenderer.transform.parent != null)
            {
                Transform existing = catVideoRenderer.transform.parent.Find(CatVideoImageName);
                if (existing != null)
                {
                    catVideoImage = existing.GetComponent<RawImage>();
                }
            }
        }

        // Editor-only setup step - called once, at edit time, by CatVideoImagePrefabBaker (via
        // PrefabUtility.LoadPrefabContents) to permanently build the RawImage sibling and
        // configure the VideoPlayer against an already-saved RenderTexture project asset. Takes
        // the RenderTexture as a parameter (rather than creating one itself) so this method never
        // touches UnityEditor.AssetDatabase and stays safe to compile into player builds.
        //
        // MaterialOverride mode (feeding the video straight into catVideoRenderer's own material)
        // was tried first and rejected: a SpriteRenderer's mesh/UVs follow the SOURCE SPRITE's own
        // packed rect/silhouette (its "Tight" mesh), not a full 0-1 quad, so the video frame came
        // out cropped into that tiny sub-region and looked like a plain gray blob. A RawImage
        // always renders a full quad with 0-1 UVs regardless of what texture it holds, so instead
        // this builds one, sized/positioned to exactly match catVideoRenderer's own RectTransform
        // and parented as its sibling (so it inherits the SAME catTransform scale-up animation).
        public void BakeCatVideoSetup(RenderTexture renderTexture)
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

            if (catVideoImage == null && catVideoRenderer.transform.parent != null)
            {
                Transform existing = catVideoRenderer.transform.parent.Find(CatVideoImageName);
                if (existing != null)
                {
                    catVideoImage = existing.GetComponent<RawImage>();
                }
            }

            if (catVideoImage == null)
            {
                var imageObject = new GameObject(CatVideoImageName, typeof(RectTransform), typeof(Canvas), typeof(RawImage));
                RectTransform sourceRect = catVideoRenderer.GetComponent<RectTransform>();
                RectTransform imageRect = (RectTransform)imageObject.transform;
                imageRect.SetParent(sourceRect != null ? sourceRect.parent : catVideoRenderer.transform.parent, false);

                // catVideoRenderer's RectTransform.sizeDelta (800x800, authored purely because a
                // Canvas forces every descendant to carry a RectTransform) is NOT what actually
                // renders - a SpriteRenderer ignores RectTransform width/height entirely; its real
                // on-screen footprint is sprite.bounds x its own transform.localScale. Using
                // sizeDelta made the video balloon far past the sprite's real size once catTransform
                // (this GameObject's shared parent) scaled up to catTargetScale. Both the sprite and
                // this sibling RawImage live under the SAME parent, so deriving the size from the
                // sprite's own bounds (already excluding that shared parent scale) lines them up.
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

            // A RawImage under the shared parent Canvas draws in hierarchy/sibling order by
            // default, not by SpriteRenderer-style Sorting Layer/Order - it would ignore
            // catVideoRenderer's own sorting entirely. Adding a Canvas here with overrideSorting
            // puts it on the same Sorting Layer as the sprite it replaces, but a nested Canvas's
            // UI batch doesn't reliably interleave with a plain Renderer
            // (ParticleSystemRenderer/SpriteRenderer) at an EXACTLY equal Order in Layer - the
            // win-celebration confetti (Order in Layer 3) was still drawing on top even though
            // catVideoRenderer itself sits at 5. Padding well past every known particle/decoration
            // order in this popup, instead of matching it exactly, makes the ordering unambiguous.
            // Only takes effect once catVideoImage actually HAS a Canvas ancestor - true here since
            // it's already parented above.
            Canvas videoCanvas = catVideoImage.GetComponent<Canvas>();
            if (videoCanvas != null)
            {
                const int SortingOrderPadding = 20;
                videoCanvas.overrideSorting = true;
                videoCanvas.sortingLayerID = catVideoRenderer.sortingLayerID;
                videoCanvas.sortingOrder = catVideoRenderer.sortingOrder + SortingOrderPadding;
            }

            catVideoImage.texture = renderTexture;

            catVideoPlayer.clip = catOpenVideoClip;
            catVideoPlayer.playOnAwake = false;
            catVideoPlayer.isLooping = true;
            catVideoPlayer.waitForFirstFrame = true;
            catVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            catVideoPlayer.targetTexture = renderTexture;
            catVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        private void OnEnable()
        {
            // SetCurrentCoin can arrive after GameFlowViewAdapter activates this popup.
            // Do not start the opening animation until the real coin value has been supplied.
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }

            openRoutine = StartCoroutine(WaitForCoinDataAndPlayOpen());
            RefreshFeatureAvailabilityBanner();
        }

        private IEnumerator WaitForCoinDataAndPlayOpen()
        {
            // Give the caller a chance to supply SetCurrentCoin in the same frame before
            // ANY opening animation starts.
            while (!coinDataReady)
            {
                yield return null;
            }

            openRoutine = null;
            PlayOpen();
        }

        // Nudges the player toward Spin the Wheel / Daily Reward whenever either one actually has
        // something waiting for them right now.
        private void RefreshFeatureAvailabilityBanner()
        {
            if (spinOrRewardAvailableBanner == null)
                return;

            bool spinAvailable =
                spinWheelManager != null &&
                spinWheelManager.IsSpinAvailable();

            bool rewardAvailable =
                dailyRewardManager != null &&
                dailyRewardManager.CanClaimMain();

            spinOrRewardAvailableBanner.SetActive(spinAvailable || rewardAvailable);
        }

        private void OnDisable()
        {
            hasPlayedOpen = false;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }
            StopContinueBob();
        }
        private Sequence _coinLevelTextSequence;

        // Wired to the win popup's Next Level button (see WireResultPageButtons in
        // CatFlapSceneObjectBuilder.cs). Ignores clicks until the button itself has actually
        // popped in. A repeat click once the coin animation has already started SKIPS it instead
        // of being ignored - see SkipCoinAnimation.
        public void OnNextLevelClicked()
        {

            if (!hasPlayedOpen || !continueButtonReady || openAnimationPlaying)
            {
                return;
            }
            StopContinueBob();
            SetButtonsInteractable(false);
            AnimateCoins(winCoin, () => GameTransitionOverlay.Instance.Play(
                hideOldPage: () => gameObject.SetActive(false),
                revealNewPage: () => gameplayManager.LoadNextLevel()));
        }

        // Wired to the win popup's Home/close button (see WireResultPageButtons in
        // CatFlapSceneObjectBuilder.cs) - plays the exact same coin-collect animation as
        // Next/Continue before leaving, instead of skipping straight to the main menu.
        // Same click-guard semantics as OnNextLevelClicked.
        public void OnHomeClicked()
        {
            if (nextLevelRequested || !hasPlayedOpen || !homeButtonReady || openAnimationPlaying)
            {
                return;
            }
            nextLevelRequested = true;
            gameplayManager?.LogHomeFromWinPopup();
            StopContinueBob();
            SetButtonsInteractable(false);
            AnimateCoins(winCoin, () => GameTransitionOverlay.Instance.Play(
                hideOldPage: () => gameObject.SetActive(false),
                revealNewPage: () => gameplayManager.SaveAndShowMainMenu()));
        }

        // Set for the CURRENTLY in-flight AnimateCoins call - CompleteCoinAnimation invokes
        // whichever of these was captured, guarded so it only ever fires once (see
        // SkipCoinAnimation: CoinCollectAnimator has no cancel API, so its own internal per-coin
        // timers keep running and would otherwise eventually call onAllCoinsComplete a second
        // time after a skip already fired it early).
        private System.Action pendingCoinCompleteCallback;
        private bool coinAnimationFinished = true;

        // Coin-collect VISUALS only (CoinCollectAnimator's bloom/hover/stream, replacing the old
        // AnimateCoin/SpawnAndAnimateCoin fly-and-scale tween) - the flow around it (onComplete
        // callback rather than a scene-load call, same DelayedCall breathing-room beat) is
        // untouched.
        private void AnimateCoins(int coins, System.Action onComplete)
        {
            coinAnimationFinished = false;
            pendingCoinCompleteCallback = onComplete;

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(true);
            }

            if (continueButtonTransform != null)
            {
                continueButtonTransform.gameObject.SetActive(false);
            }

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
                levelCoinsImage.rectTransform.sizeDelta*0.75f,
                coinCollectSettings,
                onCoinSpawned: (spawnedCount, totalCount) =>
                {
                    // Fires once per coin, but the burst itself only reads as one event to the
                    // player - a single strong pulse right as it appears, not one per coin.
                    if (spawnedCount == 1)
                    {
                        Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Heavy);
                    }
                },
                onCoinArrived: (arrivedCount, totalCount) =>
                {
                    // Soft (the lowest-intensity ImpactFeedbackStyle) on every landing - a light
                    // tick per coin, distinct from the single heavy pulse the burst got above.
                    Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);

                    // Every coin, not just the first - plays right as each one actually lands and
                    // is destroyed (see CoinCollectAnimator.Stream, which fires onCoinArrived at
                    // that exact moment), not just once for the whole collection.
                    PlayLevelCoinVfx();

                    UpdateLevelCoins(arrivedCount, totalCount);
                },
                onAllCoinsComplete: delegate
                {
                    ForceUpdateLevelCoinText();

                    //Just wait for 1 second
                    DOVirtual.DelayedCall(1f, CompleteCoinAnimation);
                });
        }
        private void ForceUpdateLevelCoinText()
        {
            _coinLevelTextSequence?.Complete();
            SetText(CoinText, finalCoin.ToString());
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

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(false);
            }

            if (continueButtonTransform != null)
            {
                continueButtonTransform.gameObject.SetActive(true);
            }

            System.Action callback = pendingCoinCompleteCallback;
            pendingCoinCompleteCallback = null;
            callback?.Invoke();
        }

        // Tapping Continue again while its own coin-collect animation is still playing skips
        // straight to the level transition instead of being silently ignored. CoinCollectAnimator
        // has no cancel API (see AnimateCoins) - its per-coin visuals are destroyed here so
        // nothing keeps flying on screen, and the coin total snaps to its final value immediately,
        // but CoinCollectAnimator's own untargeted internal timers keep running regardless; that's
        // harmless since CompleteCoinAnimation's guard means they can only ever no-op afterward.
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

        private int coinTextBaseValue;
        private int coinTextPerCoinValue;
        private int coinTextPerCoinRemainder;
        private void UpdateLevelCoins(int arrivedCount, int totalCount)
        {
            int targetValue = coinTextBaseValue + coinTextPerCoinValue * arrivedCount
                + (arrivedCount == totalCount ? coinTextPerCoinRemainder : 0);
            var textCoins = string.IsNullOrEmpty(CoinText.text) ? 0 : int.Parse(CoinText.text);
            UpdateLevelCoinText(textCoins, targetValue, delegate { });
        }
        private void UpdateLevelCoinText(int currentValue, int targetValue, UnityAction onComplete)
        {
            _coinLevelTextSequence?.Complete();
            _coinLevelTextSequence =
                UpdateCountText(currentValue, targetValue, levelCoinsImage, CoinText, onComplete);
        }
        public static Sequence UpdateCountText(int currentValue, int targetValue,
    Image countImage, TMP_Text countText, UnityAction onComplete)
        {
            // Reset scale
            var countTextScale = countText.transform.localScale;
            var countImageScale = countImage ? countImage.transform.localScale : Vector3.one;

            // Value animation
            var displayValue = currentValue;
            Tween countTween = DOTween.To(() => displayValue, x =>
            {
                displayValue = x;
                countText.text = displayValue.ToString();
            }, targetValue, 0.5f).SetEase(Ease.OutCubic);

            // Scale up/down animation
            var seq = DOTween.Sequence();
            seq.Join(countTween);

            var scaleUp = 1.2f;
            var scaleTime = 0.2f;

            seq.Join(countText.transform.DOScale(countTextScale * scaleUp, scaleTime).SetEase(Ease.OutBack));

            seq.Append(countText.transform.DOScale(countTextScale, scaleTime).SetEase(Ease.InOutBack));

            if (countImage)
            {
                seq.Join(countImage.transform.DOScale(countImageScale * scaleUp, scaleTime).SetEase(Ease.OutBack));
                seq.Join(countImage.transform.DOScale(countImageScale, scaleTime).SetEase(Ease.InOutBack));
            }

            seq.OnComplete(() =>
            {
                countText.transform.localScale = countTextScale;
                countText.text = targetValue.ToString();

                if (countImage)
                    countImage.transform.localScale = countImageScale;

                onComplete?.Invoke();
            });

            return seq;
        }
        public void SetCurrentCoin(int coins, int winCoinAmount)
        {
            currentCoin = coins;
            winCoin = winCoinAmount;
            finalCoin = currentCoin + winCoin;
            coinDataReady = true;

            // Set the wallet value immediately. CoinText is only a visual value and must
            // always be refreshed from the real wallet supplied by the gameplay manager.
            SetText(CoinText, currentCoin.ToString());
            SetText(winCoinText, $"+{winCoin}");
        }

        public void SetStats(int moves, float elapsedSeconds)
        {
            pendingMovesValue = Mathf.Max(0, moves).ToString();
            pendingTimerValue = FormatTime(elapsedSeconds);

            // If stats change after the popup has already finished opening,
            // just refresh the labels immediately instead of retyping them.
            if (hasPlayedOpen && openRoutine == null)
            {
                SetText(movesText, pendingMovesValue);
                SetText(timerText, pendingTimerValue);
            }
        }

        /// <summary>
        /// Controls whether the ads (2x reward) button shows at all - hidden for e.g. the first N
        /// levels via GlobalConfig.NoAdsBeforeLevel. Must be called before this popup's GameObject
        /// is activated (OnEnable's PlayOpen reads it via ResetVisualState), i.e. before
        /// GameFlowViewAdapter.ShowWin() runs. When hidden, the Next button re-centers into the
        /// midpoint between where the ads and close buttons sit, so the remaining two buttons stay
        /// evenly spaced instead of leaving a gap.
        /// </summary>
        public void SetAdsButtonVisible(bool visible)
        {
            showAdsButton = visible;
        }

        /// <summary>
        /// Controls whether the Home button shows at all - hidden for the first 3 levels. Same
        /// "must be called before this popup's GameObject is activated" contract as
        /// SetAdsButtonVisible.
        /// </summary>
        public void SetHomeButtonVisible(bool visible)
        {
            showHomeButton = visible;
        }

        /// <summary>
        /// Levels 1..N get that level's own CatFlapLevelResultTextConfig win line, in order; any
        /// level past the last configured entry gets a random one from the same list. Called by
        /// FillItGameplayManager.UpdateLevelWinPopup before this popup is shown.
        /// </summary>
        public void SetLevelResultText(int levelNumber)
        {
            string rawText = CatFlapLevelResultTextConfig.Load()?.GetWinText(levelNumber);
            pendingBeatPlayersValue = CatFlapLevelResultTextConfig.ApplyHighlight(rawText, beatPlayersHighlightColor);

            // Same refresh-if-already-open behavior as SetStats.
            if (hasPlayedOpen && openRoutine == null)
            {
                SetText(beatPlayersText, pendingBeatPlayersValue);
            }
        }

        public void PlayOpen()
        {
            if (hasPlayedOpen)
            {
                return;
            }
            hasPlayedOpen = true;
            CacheHomePositions();
            ResetVisualState();
            openRoutine = StartCoroutine(PlayOpenSequence());
        }

        private IEnumerator PlayOpenSequence()
        {
            if (particleDelay > 0f)
            {
                yield return new WaitForSeconds(particleDelay);
            }

            // 1. Win particle burst plays first.
            if (winParticles != null)
            {
                winParticles.Play();
                AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.LwinConfitti);
            }

            yield return StartCoroutine(FadeInBackground());

            // 2. Title.
            yield return StartCoroutine(ScaleUp(titleImage, catScaleDuration));

            // 3. Coins.
            yield return StartCoroutine(MoveIn(coinContainer, Vector2.left * offscreenPadding, coinMoveDuration));

            // 4. Character. The video starts playing immediately (no wait), alongside the
            // catTransform scale-up.
            PlayCatOpenAnimation();
            yield return StartCoroutine(ScaleUp(catTransform, catScaleDuration, Vector3.one * catTargetScale));

            // 5. Time text - value is set instantly (no typewriter), then its parent (text +
            //    any icon/background around it) scales up together as one unit.
            SetText(movesText, pendingMovesValue);
            SetText(timerText, pendingTimerValue);
            yield return StartCoroutine(ScaleUp(timerContainer, timeTextScaleDuration));

            // 6. "You beat X% of players!" types out while the win-coin container
            //    pops in place (showing "+winCoin"). The coin total itself only
            //    counts up later, once Next Level is clicked (see AnimateCoins).
            SetText(winCoinText, $"+{winCoin}");
            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.LevelCoinReward);
            // currentCoin remains the real pre-win wallet balance. The final value is stored
            // separately so the UI can reveal it only through the coin-collect animation.
            // Staggered starts (not all three firing on the same frame) - same pattern as
            // OutOfLivesPopupController's title/cat fix.
            StartCoroutine(Typewriter(beatPlayersText, pendingBeatPlayersValue));
            yield return new WaitForSeconds(coinWinScaleDuration * 0.85f);
            StartCoroutine(ScaleUp(winCoinContainer, coinWinScaleDuration));
            yield return new WaitForSeconds(coinWinScaleDuration * 0.85f);
            yield return StartCoroutine(PlayButtonsSequence());

            // Only now (the whole open sequence, not just the buttons' own pop-in) has finished -
            // Continue/Ads/Home can register a click.
            openAnimationPlaying = false;

            openRoutine = null;
        }

        // ---------- Individual effects ----------

        // Plays the already-baked catOpenVideoClip on a loop for as long as this popup stays open
        // (isLooping/clip/targetTexture are all baked in - see BakeCatVideoSetup) - unlike the old
        // one-shot SpriteAnimator version, this never stops itself or reverts to the static poster
        // sprite on its own; ResetVisualState does that when the popup is closed/reopened.
        // No poster fallback/wait - the RawImage is active and the poster disabled immediately;
        // Play() auto-prepares internally if needed, and waitForFirstFrame keeps whatever's shown
        // held on the last/first decoded frame instead of flashing anything else.
        private void PlayCatOpenAnimation()
        {
            EnsureCatVideoPlayer();
            if (catVideoPlayer == null || catVideoImage == null)
            {
                return;
            }

            if (catVideoRenderer != null)
            {
                catVideoRenderer.enabled = false;
            }

            catVideoImage.gameObject.SetActive(true);
            catVideoPlayer.Play();
        }

        private IEnumerator PlayButtonsSequence()
        {
            float stagger = buttonScaleDuration * 0.85f;
            yield return new WaitForSeconds(stagger);
            if (showAdsButton)
            {
                StartCoroutine(ScaleUp(adsButtonTransform, buttonScaleDuration));
            }
            yield return new WaitForSeconds(stagger);

            // continueButtonReady/homeButtonReady flip as soon as EACH button's own pop-in
            // finishes, not when the whole PlayOpenSequence (including the "beat X% of players"
            // typewriter, started alongside it) finishes. Gating the click guard on
            // openRoutine (the WHOLE sequence) instead of this meant a player who tapped a button
            // the instant it visibly finished popping in - before the (often slower) typewriter
            // text caught up - had their click silently swallowed with zero animation and zero
            // feedback, since openRoutine was still non-null at that point.
            StartCoroutine(ScaleUpThenMarkReady(continueButtonTransform, buttonScaleDuration, () =>
            {
                continueButtonReady = true;
                StartContinueBob();
            }));
            yield return new WaitForSeconds(stagger);

            if (showHomeButton)
            {
                yield return StartCoroutine(ScaleUpThenMarkReady(closeButtonTransform, buttonScaleDuration, () => homeButtonReady = true));
            }
        }

        private IEnumerator ScaleUpThenMarkReady(RectTransform target, float duration, System.Action onReady)
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

            // Set the full (possibly rich-text) string up front, then reveal it
            // character-by-character via maxVisibleCharacters so tags like
            // <color=...> aren't split mid-tag while typing.
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

        // ---------- Setup helpers ----------

        private void ResetVisualState()
        {
            nextLevelRequested = false;
            continueButtonReady = false;
            homeButtonReady = false;
            adWatchInProgress = false;
            openAnimationPlaying = true;
            StopContinueBob();
            SetButtonsInteractable(true);

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(false);
            }

            if (continueButtonTransform != null)
            {
                continueButtonTransform.gameObject.SetActive(true);
            }

            // Resets both video image and poster before each open - PlayCatOpenAnimation only
            // hides the poster/shows the video when catVideoPlayer/catVideoImage are both present
            // (baked in ahead of time), so this is the fallback state if either is missing.
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
            if (catTransform != null)
            {
                catTransform.localScale = Vector3.zero;
            }
            if (winCoinContainer != null)
            {
                winCoinContainer.localScale = Vector3.zero;
            }
            if (timerContainer != null)
            {
                timerContainer.localScale = Vector3.zero;
            }
            if (adsButtonTransform != null)
            {
                adsButtonTransform.localScale = Vector3.zero;
                adsButtonTransform.gameObject.SetActive(showAdsButton);
            }
            if (continueButtonTransform != null)
            {
                continueButtonTransform.localScale = Vector3.zero;
                // Ads hidden: re-center Next into the midpoint of the ads/close slots so the two
                // remaining buttons stay evenly spaced instead of leaving Next in its original,
                // now off-center authored position with a gap where the ads button used to be.
                continueButtonTransform.anchoredPosition = showAdsButton
                    ? continueButtonHomePos
                    : Vector2.Lerp(adsButtonHomePos, closeButtonHomePos, 0.5f);
            }
            if (closeButtonTransform != null)
            {
                closeButtonTransform.localScale = Vector3.zero;
                closeButtonTransform.gameObject.SetActive(showHomeButton);
            }
            SetText(movesText, string.Empty);
            SetText(timerText, string.Empty);
            SetText(beatPlayersText, string.Empty);

            // CoinText is intentionally reset only ONCE for the lifetime of this popup class.
            // After that, SetCurrentCoin is the only place that establishes the wallet value.
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
            if (adsButtonTransform != null) adsButtonHomePos = adsButtonTransform.anchoredPosition;
            if (continueButtonTransform != null) continueButtonHomePos = continueButtonTransform.anchoredPosition;
            if (closeButtonTransform != null) closeButtonHomePos = closeButtonTransform.anchoredPosition;
            cachedHomePositions = true;
        }

        private Vector2 GetHomePosition(RectTransform target)
        {
            if (target == coinContainer) return coinContainerHomePos;
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