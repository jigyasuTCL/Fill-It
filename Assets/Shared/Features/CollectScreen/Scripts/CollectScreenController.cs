using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    public enum CollectScreenMode
    {
        // Reward flies to its destination automatically after WaitTime, no input needed.
        Automatic,

        // Popup waits for the player to tap Collect or Double.
        Manual
    }

    public class CollectScreenController : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField]
        private RectTransform middleCollectingImage; // the icon that animates

        [SerializeField]
        private Image middleCollectingImageSprite; // Image component on middleCollectingImage to set the sprite

        [SerializeField]
        private RectTransform coinContainer; // target when isCoin == true

        [SerializeField]
        private GameObject playButton; // target/fallback when isCoin == false

        [Header("Animation Destinations")]
        [Tooltip("Landing point for the coin animation. Falls back to coinContainer if unassigned.")]
        [SerializeField]
        private Transform coinFinalPosition;

        [Tooltip("Landing point for the booster animation. Falls back to playButton if unassigned.")]
        [SerializeField]
        private Transform boosterFinalPosition;

        [Header("Icon Lookup")]
        [Tooltip("Maps a reward's icon key -> Sprite. Assign the shared IconDatabase asset here.")]
        [SerializeField]
        private IconDatabaseConfig iconDatabase;

        [Header("Manual Collect Buttons")]
        [SerializeField]
        private GameObject manualButtonsRoot; // parent containing collectButton + doubleButton, shown only in Manual mode

        [SerializeField]
        private Button collectButton;

        [SerializeField]
        private Button doubleButton;

        // Fired when the player taps Collect in Manual mode.
        public UnityEvent CollectRequested;

        // Fired when the player taps Double in Manual mode - caller decides what "double" means
        // (grant extra reward, gate it behind a rewarded ad, etc).
        public UnityEvent DoubleRewardRequested;

        [Header("Popup Animation")]
        [SerializeField]
        private PopupAnimator popupAnimator; // handles the open/close punch-scale animation

        [Header("Settings")]
        [SerializeField]
        private float scaleUpDuration = 0.25f;

        [SerializeField]
        private float moveDuration = 0.5f;

        [SerializeField]
        private float holdBeforeClose = 0.5f;

        [Header("Reward Multiply")]
        [Tooltip("Reward amounts above this spawn no more than this many icons.")]
        [SerializeField]
        private int maxRewardIcons = 15;

        [Tooltip("Coin rewards always animate at least this many flying icons, even if the actual reward amount is lower. Does not change the amount granted to the player.")]
        [SerializeField]
        private int minCoinReward = 1;

        [Tooltip("Delay between each spawned icon starting its flight to the destination.")]
        [SerializeField]
        private float iconSpawnStagger = 0.05f;

        [Header("Coin Animation")]
        [Tooltip("Prefab instantiated and flown to the destination for each reward icon. " +
                 "Falls back to cloning middleCollectingImage if left unassigned.")]
        [SerializeField]
        private RectTransform coinAnimationPrefab;

        [Tooltip("Particle effect instantiated where a flying coin icon lands and pops.")]
        [SerializeField]
        private ParticleSystem coinParticleEffect;

        [Tooltip("How long a coin icon takes to shrink to nothing once it reaches the destination.")]
        [SerializeField]
        private float coinScaleDownDuration = 0.15f;

        [Header("Coin Scatter")]
        [Tooltip("Each icon pops out to a random point within this radius of the main image before flying to the destination.")]
        [SerializeField, Range(0f, 400f)]
        private float scatterRadius = 80f;

        [Tooltip("How long the scatter pop-out (spawn -> random point around the main image) takes.")]
        [SerializeField]
        private float scatterDuration = 0.2f;

        [Tooltip("Each icon waits a random amount of time (picked from this range) at its scattered point before leaving for the destination, so they don't all fly off in lockstep.")]
        [SerializeField]
        private Vector2 randomDepartureDelayRange = new Vector2(0f, 0.3f);

        [Header("Tween Easing")]
        [SerializeField]
        private Ease scaleUpEase = Ease.OutBack;

        [SerializeField]
        private Ease moveEase = Ease.InOutQuad;

        [SerializeField]
        private Ease scaleDownEase = Ease.InQuad;

        [Header("Jump-In Animation")]
        [Tooltip("Duration of the reward icon's flight from its source (e.g. the winning wheel " +
                 "segment, or the claimed reward slot) into this popup's own icon spot. Only " +
                 "used when Show() is given a jumpFromSource.")]
        [SerializeField]
        private float jumpInDuration = 0.45f;

        [SerializeField]
        private Ease jumpInEase = Ease.InOutQuad;

        [Tooltip("Parent for the flying icon clone while this popup is still closed (and thus " +
                 "inactive) - must already be active in the hierarchy, e.g. the root Canvas. " +
                 "Falls back to this transform's root if left unassigned.")]
        [SerializeField]
        private RectTransform jumpInFlightParent;

        [Header("Player Coin Display")]
        [Tooltip("Shows the player's running coin total after collecting a coin reward.")]
        [SerializeField]
        private TMP_Text playerCoinText;

        [Tooltip("Shows how much of this reward the player just got (e.g. \"+50\").")]
        [SerializeField]
        private TMP_Text rewardAmountText;

        private Coroutine sequenceRoutine;
        private bool isCoinReward;
        private CollectScreenMode collectMode;

        private Sprite pendingIconSprite;
        private int pendingRewardCount = 1;
        private int pendingRewardAmount = 1;
        private Vector2 middleCollectingImageInitialAnchoredPos;
        private readonly List<RectTransform> activeRewardIconClones = new List<RectTransform>();
        private RectTransform activeJumpInClone;
        private Image activeJumpInSourceImage;

        // Fires once the whole sequence (icon flight + hold + popup close) has finished -
        // callers use this to defer follow-up state changes (e.g. unlocking the next reward
        // slot) until the collect animation is actually done, not just started.
        private Action pendingOnSequenceComplete;

        // True from the moment PlayCollectSequence asks PopupAnimator to close until
        // HandleCloseComplete runs. rootToToggle on the close popup is this GameObject, so
        // PopupAnimator.Close() deactivates it (firing OnDisable) BEFORE calling its own
        // onComplete (HandleCloseComplete) - this flag tells OnDisable "a normal close is
        // already in flight, HandleCloseComplete will fire the completion callback itself,
        // don't fire it early from here."
        private bool closingViaPopup;

        // Single delegate for "push the player's current coin total onto every coin-total
        // display this screen owns". Invoked both when the panel is shown (OnEnable) and right
        // after a coin reward is granted (HandleCoinReward) so those two spots share one place
        // to update text instead of each duplicating the same line - add any further coin
        // container references to UpdatePlayerCoinDisplay, not to the call sites.
        private Action UpdatePlayerCoinDisplay;

        void Awake()
        {
            collectButton?.onClick.AddListener(HandleCollectClicked);
            doubleButton?.onClick.AddListener(HandleDoubleClicked);

            if (middleCollectingImage != null)
                middleCollectingImageInitialAnchoredPos = middleCollectingImage.anchoredPosition;

            UpdatePlayerCoinDisplay = RefreshPlayerCoinText;
        }

        private void RefreshPlayerCoinText()
        {
            if (playerCoinText != null)
                playerCoinText.text = Shared.PlayerData.PlayerData.Instance.TotalCoins.ToString();
        }

        void OnEnable()
        {
            UpdatePlayerCoinDisplay?.Invoke();
        }

        void OnDisable()
        {
            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            // Kill any in-flight icon tweens before destroying their targets so DOTween
            // doesn't try to animate objects that no longer exist.
            ClearRewardIconClones();
            ClearJumpInClone();

            if (middleCollectingImageSprite != null)
                middleCollectingImageSprite.enabled = true;

            if (popupAnimator != null)
                popupAnimator.SnapTo(false);

            if (playButton != null)
                playButton.SetActive(false);

            if (manualButtonsRoot != null)
                manualButtonsRoot.SetActive(false);

            // Only fire here for a disable that did NOT go through PlayCollectSequence's own
            // popupAnimator.Close() (e.g. this GameObject force-disabled from outside mid-flight)
            // - the normal close path fires from HandleCloseComplete instead, once PopupAnimator
            // reports the close animation actually finished.
            if (!closingViaPopup)
                InvokeSequenceComplete();
        }

        /// <summary>
        /// Call this to feed reward data in and kick off the collect sequence.
        /// </summary>
        /// <param name="iconKey">Key resolved against the assigned IconDatabaseConfig</param>
        /// <param name="isCoin">True if this reward is a coin (goes to coinContainer), false otherwise (goes to playButton)</param>
        /// <param name="WaitTime">Time to wait before closing the popup (Automatic mode only)</param>
        /// <param name="rewardAmount">How many reward icons to spawn, clamped to maxRewardIcons</param>
        /// <param name="mode">Automatic collects on its own after WaitTime; Manual waits for the Collect/Double buttons</param>
        /// <param name="onSequenceComplete">Optional. Invoked once the icon flight, hold, and popup close have all finished.</param>
        /// <param name="jumpFromSource">Optional. A world-space RectTransform (e.g. the winning wheel segment, or the
        /// claimed reward slot's icon) the reward icon flies FROM before this popup opens - the popup's own open
        /// animation starts once that flight is halfway done, so the two blend into one continuous motion instead of
        /// popping in separately. Leave null to open immediately, as before.</param>
        float waitTimes;
        public void Show(string iconKey, bool isCoin, float WaitTime, int rewardAmount = 1, CollectScreenMode mode = CollectScreenMode.Automatic, Action onSequenceComplete = null, RectTransform jumpFromSource = null)
        {
            isCoinReward = isCoin;
            waitTimes = WaitTime;
            collectMode = mode;
            pendingOnSequenceComplete = onSequenceComplete;
            closingViaPopup = false;

            // The main image always keeps showing the reward's own icon and stays put -
            // it's the separately-animated coin icons (coinIcon) that fly to the target.
            pendingIconSprite = iconDatabase != null ? iconDatabase.GetIconOrFallback(iconKey) : null;

            pendingRewardAmount = Mathf.Max(0, rewardAmount);
            pendingRewardCount = Mathf.Clamp(rewardAmount, 1, maxRewardIcons);
            if (isCoin)
                pendingRewardCount = Mathf.Max(pendingRewardCount, minCoinReward);

            if (rewardAmountText != null)
                rewardAmountText.text = $"x{pendingRewardAmount}";

            ClearRewardIconClones();
            ClearJumpInClone();

            if (middleCollectingImageSprite != null)
                middleCollectingImageSprite.enabled = true;

            if (middleCollectingImage != null)
                middleCollectingImage.anchoredPosition = middleCollectingImageInitialAnchoredPos;

            if (middleCollectingImageSprite != null && pendingIconSprite != null)
                middleCollectingImageSprite.sprite = pendingIconSprite;

            if (coinContainer != null)
                coinContainer.gameObject.SetActive(isCoin);

            if (playButton != null)
                playButton.SetActive(!isCoin);

            if (manualButtonsRoot != null)
                manualButtonsRoot.SetActive(false);

            if (sequenceRoutine != null)
            {
                StopCoroutine(sequenceRoutine);
                sequenceRoutine = null;
            }

            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.RewardScreen);

            if (jumpFromSource != null)
            {
                PlayJumpIn(jumpFromSource);
            }
            else
            {
                // Open() activates rootToToggle synchronously as its first step, so it must be
                // called directly (not from inside a coroutine) - StartCoroutine throws immediately
                // if this GameObject is still inactive at this point.
                popupAnimator.Open(OnOpenComplete);
            }
        }

        // Flies a clone of the reward icon from jumpFromSource into this popup's own icon spot,
        // opening the popup itself once that flight is halfway done so the popup visibly appears
        // just as the icon arrives instead of the two playing back-to-back. Built on a DOTween
        // sequence rather than a coroutine because this GameObject is still inactive at this
        // point (Open() is what activates it), and StartCoroutine requires an active GameObject.
        private void PlayJumpIn(RectTransform jumpFromSource)
        {
            if (middleCollectingImage == null)
            {
                popupAnimator.Open(OnOpenComplete);
                return;
            }

            RectTransform flightParent =
                jumpInFlightParent != null ? jumpInFlightParent : transform.root as RectTransform;

            RectTransform clone = Instantiate(middleCollectingImage, flightParent);
            clone.gameObject.SetActive(true);
            clone.position = jumpFromSource.position;
            clone.sizeDelta = jumpFromSource.rect.size;

            Image cloneImage = clone.GetComponent<Image>();
            if (cloneImage != null)
            {
                cloneImage.enabled = true;
                if (pendingIconSprite != null)
                    cloneImage.sprite = pendingIconSprite;
            }

            // Hidden while the clone is in flight so the two icons never show at once - swapped
            // back on once the clone lands (see OnComplete below).
            if (middleCollectingImageSprite != null)
                middleCollectingImageSprite.enabled = false;

            // Also hide the source's own static image for the same reason - otherwise the icon
            // stays visible sitting at the source (wheel segment / reward slot) at the same time
            // its clone is flying away from that exact spot. Restored in ClearJumpInClone once
            // the clone lands (or immediately if this jump-in gets interrupted).
            activeJumpInSourceImage = jumpFromSource.GetComponent<Image>();
            if (activeJumpInSourceImage != null)
                activeJumpInSourceImage.enabled = false;

            activeJumpInClone = clone;

            Vector3 targetPosition = middleCollectingImage.position;
            Vector2 targetSize = middleCollectingImage.rect.size;

            Sequence seq = DOTween.Sequence();
            seq.SetTarget(clone);
            seq.Join(clone.DOMove(targetPosition, jumpInDuration).SetEase(jumpInEase));
            seq.Join(clone.DOSizeDelta(targetSize, jumpInDuration).SetEase(jumpInEase));
            seq.InsertCallback(jumpInDuration * 0.5f, () => popupAnimator.Open(OnOpenComplete));
            seq.OnComplete(() =>
            {
                if (middleCollectingImageSprite != null)
                    middleCollectingImageSprite.enabled = true;

                ClearJumpInClone();
            });
        }

        private void ClearJumpInClone()
        {
            if (activeJumpInSourceImage != null)
            {
                activeJumpInSourceImage.enabled = true;
                activeJumpInSourceImage = null;
            }

            if (activeJumpInClone == null)
                return;

            activeJumpInClone.DOKill();
            Destroy(activeJumpInClone.gameObject);
            activeJumpInClone = null;
        }

        [Header("Editor Testing")]
        [SerializeField] private string testIconKey = "coin";
        [SerializeField] private int testRewardAmount = 5;
        [SerializeField] private float testWaitTime = 2f;

        [ContextMenu("Test Coin Animation")]
        private void TestCoinAnimation()
        {
            Show(testIconKey, true, testWaitTime, testRewardAmount);
        }

        [ContextMenu("Test Booster Animation")]
        private void TestBoosterAnimation()
        {
            Show(testIconKey, false, testWaitTime, testRewardAmount);
        }

        private void OnOpenComplete()
        {
            if (collectMode == CollectScreenMode.Manual)
            {
                if (manualButtonsRoot != null)
                    manualButtonsRoot.SetActive(true);
                return;
            }

            sequenceRoutine = StartCoroutine(PlayCollectSequence(waitTimes));
        }

        private void HandleCollectClicked()
        {
            // SoundName.CollectReward already fires immediately as PlayCollectSequence's first
            // line below - only the haptic was missing here. Same ImpactFeedbackStyle used
            // elsewhere in this project for a positive/confirm tap (e.g. the Play button).
            Shared.Vibration.VibrationManager.Instance.PlayVibration(global::ImpactFeedbackStyle.Soft);
            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.ClickSound);

            if (manualButtonsRoot != null)
                manualButtonsRoot.SetActive(false);

            CollectRequested?.Invoke();
            sequenceRoutine = StartCoroutine(PlayCollectSequence(0f));
        }

        private void HandleDoubleClicked()
        {
            if (manualButtonsRoot != null)
                manualButtonsRoot.SetActive(false);

            DoubleRewardRequested?.Invoke();
            sequenceRoutine = StartCoroutine(PlayCollectSequence(0f));
        }

        private IEnumerator PlayCollectSequence(float initialWait)
        {
            // Single entry point for both Automatic mode (via OnOpenComplete) and Manual mode
            // (via HandleCollectClicked/HandleDoubleClicked), so this is where "collecting"
            // starts regardless of which path got here.
            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.CollectReward);

            // GameObject is guaranteed active here (PopupAnimator.Open already activated it).
            if (initialWait > 0f)
                yield return new WaitForSeconds(initialWait);

            // 1. Decide destination based on data passed into Show()
            Transform destination = isCoinReward
                ? (coinFinalPosition != null ? coinFinalPosition : (Transform)coinContainer)
                : (boosterFinalPosition != null ? boosterFinalPosition : (playButton != null ? playButton.transform : null));

            // 2. Animate toward destination (scale up at source, then fly to destination)
            if (destination != null)
                yield return StartCoroutine(MoveRewardIconsToTarget(destination));

            // 3. Small hold so the player can see it land
            yield return new WaitForSeconds(holdBeforeClose);

            // 4. Auto close (PopupAnimator handles scale + optional fade, and deactivates root if
            // assigned). PopupAnimator's own onComplete callback is what actually fires
            // InvokeSequenceComplete (see HandleCloseComplete) - it's driven by DOTween directly,
            // not by this coroutine resuming, so it still fires even though rootToToggle ==
            // this GameObject and SetActive(false) kills this coroutine as part of closing.
            closingViaPopup = true;
            popupAnimator.Close(HandleCloseComplete);
        }

        private void HandleCloseComplete()
        {
            // Debug.LogError($"[CollectScreen] Closed at t={Time.time:F3} frame={Time.frameCount}");

            closingViaPopup = false;
            sequenceRoutine = null;
            InvokeSequenceComplete();
        }

        private void InvokeSequenceComplete()
        {
            Action callback = pendingOnSequenceComplete;
            pendingOnSequenceComplete = null;
            callback?.Invoke();
        }

        private IEnumerator MoveRewardIconsToTarget(Transform target)
        {
            if (middleCollectingImage == null || target == null)
                yield break;

            Vector3 startPos = middleCollectingImage.position;
            Vector3 endPos = target.position;

            // The main image (middleCollectingImage) stays put showing the reward's own
            // icon. Coin rewards fly the dedicated coin prefab; non-coin (booster) rewards
            // fly clones of the main image instead, carrying the same reward icon.
            RectTransform iconTemplate = isCoinReward && coinAnimationPrefab != null
                ? coinAnimationPrefab
                : middleCollectingImage;
            var icons = new List<RectTransform>();

            // Parented under the popup's own animated transform (not just middleCollectingImage's
            // parent) so every instantiated icon scales/moves along with the popup rather than
            // sitting in whatever unrelated hierarchy happened to contain middleCollectingImage.
            Transform iconParent =
                popupAnimator != null && popupAnimator.TargetTransform != null
                    ? popupAnimator.TargetTransform
                    : middleCollectingImage.parent;

            for (int i = 0; i < pendingRewardCount; i++)
            {
                RectTransform clone = Instantiate(iconTemplate, iconParent);
                clone.position = startPos;
                clone.localScale = Vector3.zero; // starts invisible so the scale-up pop reads clearly

                icons.Add(clone);
                activeRewardIconClones.Add(clone);
            }

            int arrivals = 0;

            foreach (RectTransform icon in icons)
            {
                PlayIconTween(icon, startPos, endPos, () => arrivals++);
                yield return new WaitForSeconds(iconSpawnStagger);
            }

            yield return new WaitUntil(() => arrivals >= icons.Count);

            ClearRewardIconClones();

            GrantPendingReward();
        }

        /// <summary>
        /// Plays one reward icon's full journey with DOTween: scale up while popping out to a
        /// random scatter point around the main image, wait a random beat, then fly directly to
        /// the destination, then burst + shrink on arrival.
        /// </summary>
        private void PlayIconTween(RectTransform icon, Vector3 startPos, Vector3 endPos, Action onArrived)
        {
            icon.DOKill();
            icon.position = startPos;
            icon.localScale = Vector3.zero;

            Vector3 scatterPos = startPos + (Vector3)(UnityEngine.Random.insideUnitCircle * scatterRadius);

            Sequence seq = DOTween.Sequence();
            seq.SetTarget(icon);

            // 1. Pop out to a random point around the main image, scaling up while it moves
            // there rather than pausing in place first.
            seq.Append(icon.DOScale(Vector3.one, scaleUpDuration).SetEase(scaleUpEase));
            seq.Join(icon.DOMove(scatterPos, scatterDuration).SetEase(scaleUpEase));

            // 2. Hold at the scatter point for a random beat so icons don't all leave for the
            // destination in lockstep, then fly straight there.
            seq.AppendInterval(UnityEngine.Random.Range(randomDepartureDelayRange.x, randomDepartureDelayRange.y));
            seq.Append(icon.DOMove(endPos, moveDuration).SetEase(moveEase));

            // 3. Burst + shrink on arrival
             seq.AppendCallback(() => SpawnCoinParticleBurst());
            seq.Append(icon.DOScale(Vector3.zero, coinScaleDownDuration).SetEase(scaleDownEase));

            seq.OnComplete(() => onArrived?.Invoke());
        }

        private void SpawnCoinParticleBurst()
        {
            // Fires once per reward icon as it lands - this is "a coin gets added to the HUD"
            // for a coin reward. Booster rewards land here too (same tween path) but shouldn't
            // play a coin sound.
            if (isCoinReward)
                AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.CoinCollect);

            if (coinParticleEffect == null)
                return;
            coinParticleEffect.Play();
        }

        /// <summary>
        /// Grants the reward once the coin animation finishes landing. Coin rewards update
        /// the player's persisted coin total and its display text; booster rewards are
        /// handled elsewhere (shared BoosterManager) - this hook is left for any
        /// Collect-Screen-specific booster follow-up.
        /// </summary>
        private void GrantPendingReward()
        {
            if (isCoinReward)
                HandleCoinReward(pendingRewardAmount);
            else
                HandleBoosterReward(pendingRewardAmount);
        }

        // Fired whenever a coin reward is actually granted here (TotalCoins increased) - screens
        // that show their own running coin total but sit behind this popup (e.g. the Home tab
        // behind a Daily Reward / Spin Wheel popup) never get re-enabled while this collect
        // sequence plays, so they can't rely on their own OnEnable to notice the change.
        // Subscribe to this instead of adding another one-off call site per feature.
        public static event Action OnCoinRewardGranted;

        private void HandleCoinReward(int amount)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.TotalCoins = Mathf.Max(0, data.TotalCoins + amount);

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;

            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);

            UpdatePlayerCoinDisplay?.Invoke();

            OnCoinRewardGranted?.Invoke();
        }

        private void HandleBoosterReward(int amount)
        {
        }

        private void ClearRewardIconClones()
        {
            foreach (RectTransform clone in activeRewardIconClones)
            {
                if (clone != null)
                {
                    clone.DOKill();
                    Destroy(clone.gameObject);
                }
            }

            activeRewardIconClones.Clear();
        }
    }
}