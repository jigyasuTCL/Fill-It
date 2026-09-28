using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.Currency;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.Purchase
{
    /// <summary>
    /// Generic "purchase successful" dialog - shown whenever an IAP request (InAppManager.Purchase)
    /// resolves as a success. Single Close button; the caller decides what happens next.
    /// </summary>
    public sealed class PurchaseSuccessPopupController : MonoBehaviour
    {
        [SerializeField] private PopupAnimator popupAnimator;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private string message = "Purchase successful! Your purchase has been completed.";
        [SerializeField] private TMP_Text amountText;

        // Same convention as CollectScreenController.iconDatabase - maps a purchased item's
        // iconKey (e.g. StoreItemDefinition.ItemId) to the Sprite shown on iconImage.
        [Tooltip("Maps a purchased item's icon key -> Sprite. Assign the shared IconDatabaseConfig asset here.")]
        [SerializeField] private IconDatabaseConfig iconDatabase;

        [Tooltip("Shows the purchased item's icon, resolved from iconDatabase.")]
        [SerializeField] private Image iconImage;

        // Same visual as CollectScreenController's own coin reward (scale up out of the source
        // icon, scatter, hold, fly to the HUD counter, burst + shrink on arrival) - reused here
        // rather than reopening a whole CollectScreenController popup on top of this one, since
        // the coins purchased are already granted (see StoreManager.CompletePurchase) by the time
        // this popup shows; this only needs to play, never grant.
        [Header("Coin Fly Animation")]
        [Tooltip("Coin start point - the flying coin clones spawn from here (typically the coin/coin art on this popup).")]
        [SerializeField] private RectTransform coinStartPoint;

        [Tooltip("Prefab instantiated and flown to coinDestination for each coin. Falls back to cloning coinStartPoint if unassigned.")]
        [SerializeField] private RectTransform coinAnimationPrefab;

        [Tooltip("Landing point for the coin animation - typically the coin HUD counter.")]
        [SerializeField] private Transform coinDestination;

        [Tooltip("Particle effect played where a flying coin lands and pops.")]
        [SerializeField] private ParticleSystem coinParticleEffect;

        [Tooltip("Coin total display on this popup - refreshed once the flying coins land, same as CollectScreenController's playerCoinText and SpinWheelUIController's _coinContainer.")]
        [SerializeField] private CoinContainerView coinContainer;

        [Header("Auto Claim")]
        [Tooltip("The claim/close button is hidden (not just disabled) and this popup claims itself automatically after this many seconds, instead of waiting for a tap.")]
        [SerializeField] private float autoClaimDelaySeconds = 0.7f;

        private Coroutine autoClaimRoutine;

        [Tooltip("Purchased amounts above this spawn no more than this many coin icons.")]
        [SerializeField] private int maxCoinIcons = 15;

        [SerializeField] private float coinSpawnStagger = 0.05f;
        [SerializeField, Range(0f, 400f)] private float coinScatterRadius = 80f;
        [SerializeField] private float coinScatterDuration = 0.2f;
        [SerializeField] private Vector2 coinRandomDepartureDelayRange = new Vector2(0f, 0.3f);
        [SerializeField] private float coinScaleUpDuration = 0.25f;
        [SerializeField] private float coinMoveDuration = 0.5f;
        [SerializeField] private float coinScaleDownDuration = 0.15f;
        [SerializeField] private Ease coinScaleUpEase = Ease.OutBack;
        [SerializeField] private Ease coinMoveEase = Ease.InOutQuad;
        [SerializeField] private Ease coinScaleDownEase = Ease.InQuad;

        private Coroutine coinFlightRoutine;
        private readonly List<RectTransform> activeCoinClones = new List<RectTransform>();

        private UnityAction pendingOnClosed;
        private int pendingPurchasedAmount = 1;
        private bool pendingIsCoinPurchase;
        private int? pendingCoinContainerValue;

        public UnityEvent Closed = new UnityEvent();

        // Shows the current (pre-purchase) total the instant this popup activates - Open()
        // activates rootToToggle synchronously, so this fires before the real purchase flow has
        // granted its coins (see StoreManager.CompletePurchase), giving the coin-fly animation an
        // honest "before" value to visibly tick up from once ApplyCoinContainerValue runs at the end.
        private void OnEnable()
        {
            coinContainer?.Refresh();
        }

        private void OnDisable()
        {
            popupAnimator?.SnapTo(false);

            if (coinFlightRoutine != null)
            {
                StopCoroutine(coinFlightRoutine);
                coinFlightRoutine = null;
            }

            if (autoClaimRoutine != null)
            {
                StopCoroutine(autoClaimRoutine);
                autoClaimRoutine = null;
            }

            ClearActiveCoinClones();
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        // coinContainerValue is an explicit override for the coin total shown once the coins
        // land - leave null to just Refresh() from the real PlayerData.TotalCoins (the normal path
        // for a real purchase, which has already granted the coins by the time this popup shows -
        // see StoreManager.CompletePurchase). Pass an explicit value for previews/tests (see
        // ShowTestPreview) that don't touch PlayerData at all, so the container still visibly
        // reflects the amount being "purchased".
        public void Show(int purchasedAmount = 1, bool isCoinPurchase = false, string iconKey = null, int? coinContainerValue = null, UnityAction onClosed = null)
        {
            pendingOnClosed = onClosed;
            pendingPurchasedAmount = purchasedAmount;
            pendingIsCoinPurchase = isCoinPurchase;
            pendingCoinContainerValue = coinContainerValue;

            // RequestClose disables this while a coin-fly animation is in flight so a second tap
            // can't trigger it twice - reset it here so a reused/pooled popup instance isn't
            // permanently stuck disabled after its first coin purchase. Hidden outright (not just
            // disabled) since claiming is automatic now - see autoClaimDelaySeconds below.
            if (closeButton != null)
            {
                closeButton.interactable = true;
                closeButton.gameObject.SetActive(false);
            }

            if (autoClaimRoutine != null)
            {
                StopCoroutine(autoClaimRoutine);
                autoClaimRoutine = null;
            }

            BindButtons();
            // messageText reads for a coin purchase ("x500" sits right above it); non-coin
            // purchases (e.g. Remove Ads) use messageOtherThenText instead, since that copy
            // doesn't make sense paired with a coin amount/animation.
            SetText(messageText , message);
            SetAmountText(purchasedAmount);
            SetIcon(iconKey);
            PlayPopupOpenSound();

            // Open() activates rootToToggle synchronously as its first step, so it must be
            // called directly (not from inside a coroutine) - StartCoroutine throws immediately
            // if this GameObject is still inactive at this point. The auto-claim timer only
            // starts once the open animation has actually finished, so autoClaimDelaySeconds is
            // how long the player gets to see the popup fully on screen, not counted from Show().
            popupAnimator.Open(StartAutoClaimTimer);
        }

        private void StartAutoClaimTimer()
        {
            autoClaimRoutine = StartCoroutine(AutoClaimAfterDelay());
        }

        private IEnumerator AutoClaimAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, autoClaimDelaySeconds));
            autoClaimRoutine = null;

            // Same path a manual tap would take (coin-fly animation if this is a coin purchase,
            // then close) - claiming is automatic now, but everything past the trigger is identical.
            RequestClose();
        }

        [Header("Editor/Cheat Testing")]
        [SerializeField] private int testPurchasedAmount = 500;
        [SerializeField] private bool testIsCoinPurchase = true;
        [SerializeField] private string testIconKey;

        // Parameterless so it can be wired straight into a UnityEvent (e.g. CheatPanelController's
        // TestPurchaseSuccessRequested) from the Inspector, the same way WinLevelRequested/
        // FailLevelRequested target CatFlapGameplayManager.CheatWinLevel/CheatFailLevel.
        [ContextMenu("Test Show")]
        public void ShowTestPreview()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(PurchaseSuccessPopupController)}.{nameof(ShowTestPreview)} requires Play Mode.");
                return;
            }

            // The test flow never grants real coins (unlike a real purchase - see
            // StoreManager.CompletePurchase), so Refresh() alone would show no change here.
            // Preview the total PlayerData.TotalCoins would reach instead, so the coin container
            // still visibly ticks up by testPurchasedAmount.
            int? previewCoinContainerValue = testIsCoinPurchase
                ? Shared.PlayerData.PlayerData.Instance.TotalCoins + testPurchasedAmount
                : null;

            Show(testPurchasedAmount, testIsCoinPurchase, testIconKey, previewCoinContainerValue);
        }

        // A single purchased item (amount == 1) reads fine from messageText/art alone - the
        // amount label only earns its place for stacked/bulk rewards (e.g. coin packs), shown as
        // "x{amount}".
        private void SetAmountText(int purchasedAmount)
        {
            if (amountText == null)
            {
                return;
            }

            amountText.text = purchasedAmount > 1 ? $"x{purchasedAmount}" : string.Empty;
        }

        // Leaves whatever sprite is already on iconImage untouched when there's no key or no
        // match/fallback in iconDatabase, rather than clearing it to blank - the prefab's own
        // authored icon is a safer default than an empty Image.
        private void SetIcon(string iconKey)
        {
            if (iconImage == null || iconDatabase == null || string.IsNullOrEmpty(iconKey))
            {
                return;
            }

            Sprite icon = iconDatabase.GetIconOrFallback(iconKey);
            if (icon != null)
            {
                iconImage.sprite = icon;
            }
        }

        // Mirrors CollectScreenController.MoveRewardIconsToTarget/PlayIconTween: clone icons pop
        // out of coinStartPoint, scatter around it, hold a random beat, then fly to coinDestination
        // (the coin HUD counter), bursting + shrinking on arrival. Purely visual - the coins this
        // purchase granted are already added to the player's total (see StoreManager.CompletePurchase),
        // so nothing here touches PlayerData. Played from RequestClose, so onAllCoinsComplete is
        // what actually closes the popup - it fires immediately when nothing can be animated, so a
        // missing/misconfigured reference can never strand the popup open.
        private void PlayCoinFlyAnimation(int purchasedAmount, Action onAllCoinsComplete)
        {
            if (coinStartPoint == null || coinDestination == null)
            {
                onAllCoinsComplete?.Invoke();
                return;
            }

            if (coinFlightRoutine != null)
            {
                StopCoroutine(coinFlightRoutine);
            }

            coinFlightRoutine = StartCoroutine(SpawnCoinIcons(purchasedAmount, pendingCoinContainerValue, onAllCoinsComplete));
        }

        private IEnumerator SpawnCoinIcons(int purchasedAmount, int? coinContainerValue, Action onAllCoinsComplete)
        {
            RectTransform iconTemplate = coinAnimationPrefab != null ? coinAnimationPrefab : coinStartPoint;
            int count = Mathf.Clamp(purchasedAmount, 1, maxCoinIcons);

            Vector3 startPos = coinStartPoint.position;
            Vector3 endPos = coinDestination.position;

            Transform iconParent = popupAnimator != null && popupAnimator.TargetTransform != null
                ? popupAnimator.TargetTransform
                : coinStartPoint.parent;

            // Ticks the coin container up by a slice of purchasedAmount on every landing instead
            // of snapping it to the final total once at the end - same idea as
            // CollectScreenController's onCoinArrived driving the wallet number. runningTotal
            // starts at finalTotal - purchasedAmount (the "before" value OnEnable's Refresh()
            // already showed) and purchasedAmount is split as evenly as possible across count
            // arrivals, remainder going to the last few coins, so the running sum always lands
            // exactly on finalTotal rather than drifting from integer-division truncation (e.g.
            // 500 coins over 3 coins: 166, 166, 168 - never short by rounding).
            bool animatingTotal = coinContainer != null;
            int runningTotal = 0;
            int baseIncrement = 0;
            int remainder = 0;

            if (animatingTotal)
            {
                int finalTotal = coinContainerValue ?? Shared.PlayerData.PlayerData.Instance.TotalCoins;
                runningTotal = finalTotal - purchasedAmount;
                baseIncrement = purchasedAmount / count;
                remainder = purchasedAmount % count;
            }

            int arrived = 0;

            for (int i = 0; i < count; i++)
            {
                RectTransform clone = Instantiate(iconTemplate, iconParent);
                clone.position = startPos;
                clone.localScale = Vector3.zero;

                int increment = animatingTotal ? baseIncrement + (i >= count - remainder ? 1 : 0) : 0;

                activeCoinClones.Add(clone);
                PlayCoinIconTween(clone, startPos, endPos, () =>
                {
                    arrived++;

                    if (animatingTotal)
                    {
                        runningTotal += increment;
                        coinContainer.SetAmount(runningTotal);
                    }

                    if (arrived >= count)
                    {
                        onAllCoinsComplete?.Invoke();
                    }
                });

                yield return new WaitForSeconds(coinSpawnStagger);
            }

            coinFlightRoutine = null;
        }

        private void PlayCoinIconTween(RectTransform icon, Vector3 startPos, Vector3 endPos, UnityAction onArrived)
        {
            Vector3 scatterPos = startPos + (Vector3)(UnityEngine.Random.insideUnitCircle * coinScatterRadius);

            Sequence seq = DOTween.Sequence();
            seq.SetTarget(icon);

            // 1. Pop out to a random point around the source icon, scaling up while it moves there.
            seq.Append(icon.DOScale(Vector3.one, coinScaleUpDuration).SetEase(coinScaleUpEase));
            seq.Join(icon.DOMove(scatterPos, coinScatterDuration).SetEase(coinScaleUpEase));

            // 2. Hold at the scatter point for a random beat so icons don't all leave in lockstep.
            seq.AppendInterval(UnityEngine.Random.Range(coinRandomDepartureDelayRange.x, coinRandomDepartureDelayRange.y));
            seq.Append(icon.DOMove(endPos, coinMoveDuration).SetEase(coinMoveEase));

            // 3. Burst + shrink on arrival.
            seq.AppendCallback(PlayCoinLandedEffects);
            seq.Append(icon.DOScale(Vector3.zero, coinScaleDownDuration).SetEase(coinScaleDownEase));

            seq.OnComplete(() =>
            {
                activeCoinClones.Remove(icon);
                if (icon != null)
                {
                    Destroy(icon.gameObject);
                }

                onArrived?.Invoke();
            });
        }

        private void PlayCoinLandedEffects()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.CoinCollect);

            if (coinParticleEffect != null)
            {
                coinParticleEffect.Play();
            }
        }

        private void ClearActiveCoinClones()
        {
            foreach (RectTransform clone in activeCoinClones)
            {
                if (clone != null)
                {
                    clone.DOKill();
                    Destroy(clone.gameObject);
                }
            }

            activeCoinClones.Clear();
        }

        private void RequestClose()
        {
            PlayButtonSound();

            if (pendingIsCoinPurchase)
            {
                if (closeButton != null)
                {
                    closeButton.interactable = false;
                }

                PlayCoinFlyAnimation(pendingPurchasedAmount, ClosePopup);
                return;
            }

            ClosePopup();
        }

        private void ClosePopup()
        {
            UnityAction callback = pendingOnClosed;
            pendingOnClosed = null;

            popupAnimator.Close(() =>
            {
                Closed.Invoke();
                callback?.Invoke();
            });
        }

        private void BindButtons()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
            closeButton?.onClick.AddListener(RequestClose);
        }

        private void UnbindButtons()
        {
            closeButton?.onClick.RemoveListener(RequestClose);
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private static void PlayButtonSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.ButtonBack);
        }

        private static void PlayPopupOpenSound()
        {
            global::AudioManager.Instance.PlaySoundAction?.Invoke(global::SoundName.PopupOpen);
        }
    }
}
