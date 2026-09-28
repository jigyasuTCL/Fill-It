using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    /// <summary>
    /// Shows the Daily Challenge's reward track: a fill-animated progress bar plus one slot per
    /// configured DailyChallengeRewardTierConfigData (GlobalConfig.DailyChallenge.RewardTiers),
    /// each positioned along the bar at its own DaysRequired/span fraction - so a slot visually
    /// sits exactly where the fill reaches when that tier becomes claimable. Reads purely from
    /// DailyChallengeController's reward-tier API (see DailyChallengeController.Rewards.cs) - owns
    /// no progress state of its own, and both the bar and the tiers reset automatically when the
    /// real month rolls over.
    ///
    /// progressSpansRewardTiers picks what the bar's full length (and therefore every slot's
    /// position) is measured against:
    ///  - false (default): the whole calendar month - days won this month / days in this month.
    ///  - true: the reward span - days won this month / the highest configured tier's
    ///    DaysRequired (e.g. a last tier needing 25 days makes the bar's full length 25 days).
    ///
    /// No OnEnable/OnDisable here. DailyChallengeManager subscribes controller.RewardTierClaimed
    /// to Refresh once in its own Awake/OnDestroy, and explicitly calls Show()/Hide() from
    /// ShowPopup()/ClosePopup() - the auto-claim lock coroutine below is the only per-instance
    /// lifecycle concern this component has, and Show()/Hide() cover that without needing Unity's
    /// enable/disable messages.
    /// </summary>
    public sealed class DailyChallengeRewardTrackController : MonoBehaviour
    {
        [SerializeField] private DailyChallengeController controller;

        [Header("Progress Bar")]
        [Tooltip("Image.Type must be Filled (Horizontal) - fillAmount is animated toward the current progress fraction.")]
        [SerializeField] private Image progressBarImage;
        [SerializeField] private TMP_Text progressText;
        [Tooltip("Rides the fill bar's current leading edge (same anchor-fraction technique as " +
                 "the reward slots) - hidden once the bar reaches its end (fully completed).")]
        [SerializeField] private GameObject fillEndMarker;
        [Tooltip("How long the fill bar takes to animate to its new value, in seconds - the speed " +
                 "knob for the progress bar tween. Show() waits this long before checking/auto-" +
                 "claiming a reward tier, so the player watches the bar actually reach it first.")]
        [SerializeField] private float fillAnimationDuration = 0.6f;
        [Tooltip("false: bar spans the whole calendar month (original behavior). true: bar spans " +
                 "just the reward tiers - its full length is the highest tier's DaysRequired.")]
        [SerializeField] private bool progressSpansRewardTiers = false;

        [Header("Reward Slots (ascending DaysRequired)")]
        [Tooltip("Each slot's RectTransform anchor.x is set to DaysRequired/span, so it lands at " +
                 "the matching point along the bar - parent every slot under the same RectTransform " +
                 "the fill Image spans (or another one the same width) for this to line up visually.")]
        [SerializeField] private List<DailyChallengeRewardTierSlotView> rewardSlots = new List<DailyChallengeRewardTierSlotView>();

        [Tooltip("Extra local X offset applied only to the last reward slot, only when " +
                 "progressSpansRewardTiers is true - that mode puts the last tier exactly at " +
                 "fraction 1 (the bar's very end), so its icon needs pulling back in to avoid " +
                 "clipping past the bar's right edge.")]
        [SerializeField] private float lastSlotEdgeOffsetX = -18f;

        [Header("Auto Claim")]
        [Tooltip("Reward flies to the coin container via this the moment the track becomes " +
                 "visible, if a tier is already claimable - no Claim tap required.")]
        [SerializeField] private CollectScreenController collectScreenController;
        [Tooltip("Fallback icon key (resolved against CollectScreenController's IconDatabaseConfig) " +
                 "used only when the claimed tier's own IconId is blank.")]
        [SerializeField] private string coinIconKey = "coin";
        [SerializeField] private float collectWaitTime = 1f;
        [Tooltip("Disabled for as long as the collect animation is on screen, so the player can't " +
                 "jump into gameplay while a reward is still flying in.")]
        [SerializeField] private Button playButton;

        private Coroutine autoClaimLockRoutine;

        /// <summary>Called by DailyChallengeManager when the popup becomes visible - refreshes
        /// the bar/slots, then waits for the bar's fill animation to finish (see
        /// fillAnimationDuration) before checking/claiming whatever tier is already available, so
        /// the player watches the bar actually reach it instead of the reward claiming instantly.</summary>
        public void Show()
        {
            Refresh();
// Debug.LogError(1);
            if (autoClaimLockRoutine != null)
            {
                StopCoroutine(autoClaimLockRoutine);
            }

            autoClaimLockRoutine = StartCoroutine(CheckRewardAfterProgressBarAnimates());
        }

        // See Show() - defers the reward-tier check until the fill tween started by Refresh()
        // has had time to finish, using the same fillAnimationDuration as the tween itself.
        private IEnumerator CheckRewardAfterProgressBarAnimates()
        {
            yield return new WaitForSeconds(fillAnimationDuration);
            autoClaimLockRoutine = null;
            TryAutoClaimAvailableTier();
        }

        /// <summary>Called by DailyChallengeManager when the popup is closed - stops any
        /// in-flight auto-claim lock so a half-finished collect animation doesn't leave Play
        /// stuck disabled next time the popup opens.</summary>
        public void Hide()
        {
            if (autoClaimLockRoutine != null)
            {
                StopCoroutine(autoClaimLockRoutine);
                autoClaimLockRoutine = null;
            }

            SetPlayButtonInteractable(true);
        }

        // -----------------------------------------------------------------
        // AUTO CLAIM
        // -----------------------------------------------------------------

        // Claims whatever tier is already available, once the bar's fill animation has had time
        // to finish (see Show()/CheckRewardAfterProgressBarAnimates) - the slots (see
        // RefreshSlots) are display-only, there's no Claim button to tap.
        private void TryAutoClaimAvailableTier()
        {
            if (controller == null)
            {
                return;
            }

            Shared.GameConfig.DailyChallengeRewardTierConfigData tier = controller.GetNextUnclaimedRewardTier();
            if (tier == null || !controller.CanClaimRewardTier(tier))
            {
                return;
            }

            if (!controller.TryClaimRewardTier(tier))
            {
                return;
            }

            SetPlayButtonInteractable(false);

            PlayRewardWinAnimation(tier, () => StartCollectScreenAnimation(tier));
        }

        /// <summary>
        /// Called right after a reward tier is claimed, once the progress bar's fill animation
        /// has finished (see CheckRewardAfterProgressBarAnimates/TryAutoClaimAvailableTier) -
        /// plays the claimed slot's particle burst, claimed-badge pop-in, and reward-icon punch
        /// (see DailyChallengeRewardTierSlotView.PlayClaimAnimation), then calls onComplete once
        /// that's done. onComplete is wired to the collect screen animation.
        /// </summary>
        private void PlayRewardWinAnimation(Shared.GameConfig.DailyChallengeRewardTierConfigData unlockedReward, Action onComplete)
        {
            DailyChallengeRewardTierSlotView slot = FindSlotForTier(unlockedReward);
            if (slot == null)
            {
                onComplete?.Invoke();
                return;
            }

            slot.PlayClaimAnimation(onComplete);
        }

        // Finds the reward slot showing `tier`, if any - rewardSlots is positioned 1:1 against
        // the same RewardTiers config list RefreshSlots reads (ascending DaysRequired), so
        // matching on DaysRequired is enough to find which slot a claimed tier is displayed in.
        private DailyChallengeRewardTierSlotView FindSlotForTier(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            if (tier == null || rewardSlots == null)
            {
                return null;
            }

            List<Shared.GameConfig.DailyChallengeRewardTierConfigData> tiers =
                Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                    ? Shared.Boot.GlobalConfigRuntimeContext.Config.DailyChallenge.RewardTiers
                    : null;

            if (tiers == null)
            {
                return null;
            }

            for (int i = 0; i < tiers.Count && i < rewardSlots.Count; i++)
            {
                if (tiers[i] != null && tiers[i].DaysRequired == tier.DaysRequired)
                {
                    return rewardSlots[i];
                }
            }

            return null;
        }

        // Plays the reward-fly-to-coin-container animation for a just-claimed tier - see
        // PlayRewardWinAnimation's onComplete. Jumps from the claimed slot's own icon (see
        // CollectScreenController's jumpFromSource) when the slot is found, so the reward
        // visibly flies from the reward track into the collect screen instead of just appearing.
        private void StartCollectScreenAnimation(Shared.GameConfig.DailyChallengeRewardTierConfigData tier)
        {
            if (collectScreenController != null)
            {
                string iconKey = string.IsNullOrEmpty(tier.IconId) ? coinIconKey : tier.IconId;
                DailyChallengeRewardTierSlotView slot = FindSlotForTier(tier);
                RectTransform jumpFromSource = slot?.IconRectTransform;

                collectScreenController.Show(
                    iconKey,
                    true,
                    collectWaitTime,
                    tier.RewardCoins,
                    jumpFromSource: jumpFromSource);

                if (autoClaimLockRoutine != null)
                {
                    StopCoroutine(autoClaimLockRoutine);
                }

                autoClaimLockRoutine = StartCoroutine(ReenablePlayButtonWhenCollectScreenCloses());
            }
            else
            {
                // No collect screen wired up - nothing to wait on, so don't leave Play stuck
                // disabled.
                SetPlayButtonInteractable(true);
            }
        }

        // CollectScreenController exposes no "done" callback - its popup deactivates its own
        // GameObject on close (see CollectScreenController.OnDisable), so that's the signal
        // waited on here instead.
        private IEnumerator ReenablePlayButtonWhenCollectScreenCloses()
        {
            yield return null; // let Show()'s PopupAnimator.Open() activate it this frame first.

            if (collectScreenController != null)
            {
                yield return new WaitUntil(() => !collectScreenController.gameObject.activeInHierarchy);
            }

            SetPlayButtonInteractable(true);
            autoClaimLockRoutine = null;
        }

        private void SetPlayButtonInteractable(bool interactable)
        {
            if (playButton != null)
            {
                playButton.interactable = interactable;
            }
        }

        public void Refresh()
        {
            if (controller == null)
            {
                return;
            }

            var (tiers, completedDaysThisMonth, spanDays) = GatherProgressState();

            RefreshProgressBar(completedDaysThisMonth, spanDays);
            RefreshSlots(tiers, completedDaysThisMonth, spanDays);
        }

        /// <summary>
        /// Refreshes only the reward slots' Locked/Claimable/Claimed state, not the progress bar.
        /// DailyChallengeManager calls this (instead of Refresh()) right after a tier is claimed -
        /// claiming doesn't change the completed-day count the bar reflects, so a full Refresh()
        /// there would just replay the same fill tween a second time for no visual change.
        /// </summary>
        public void RefreshSlotsOnly()
        {
            if (controller == null)
            {
                return;
            }

            var (tiers, completedDaysThisMonth, spanDays) = GatherProgressState();

            RefreshSlots(tiers, completedDaysThisMonth, spanDays);
        }

        private (List<Shared.GameConfig.DailyChallengeRewardTierConfigData> tiers, int completedDaysThisMonth, int spanDays) GatherProgressState()
        {
            List<Shared.GameConfig.DailyChallengeRewardTierConfigData> tiers =
                Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                    ? Shared.Boot.GlobalConfigRuntimeContext.Config.DailyChallenge.RewardTiers
                    : null;

            int completedDaysThisMonth = controller.GetCompletedDaysThisMonth();
            int spanDays = GetProgressSpanDays(tiers);

            return (tiers, completedDaysThisMonth, spanDays);
        }

        // -----------------------------------------------------------------
        // PROGRESS BAR
        // -----------------------------------------------------------------

        // The bar's full length, in days - see progressSpansRewardTiers. Shared by both the fill
        // amount and every slot's position so a slot always lands exactly where the fill reaches
        // it, regardless of which mode is active.
        private int GetProgressSpanDays(List<Shared.GameConfig.DailyChallengeRewardTierConfigData> tiers)
        {
            if (!progressSpansRewardTiers)
            {
                DateTime now = DailyChallengeClock.Now;
                return DateTime.DaysInMonth(now.Year, now.Month);
            }

            int maxDaysRequired = 0;
            if (tiers != null)
            {
                foreach (var tier in tiers)
                {
                    if (tier != null && tier.DaysRequired > maxDaysRequired)
                    {
                        maxDaysRequired = tier.DaysRequired;
                    }
                }
            }

            // At least 1 - a misconfigured/empty tier list must never produce a div-by-zero span.
            return Mathf.Max(1, maxDaysRequired);
        }

        private void RefreshProgressBar(int completedDaysThisMonth, int spanDays)
        {
            float target = spanDays > 0
                ? Mathf.Clamp01((float)completedDaysThisMonth / spanDays)
                : 0f;

            if (progressBarImage != null)
            {
                progressBarImage.fillAmount=0;
                progressBarImage.DOKill();
                progressBarImage
                    .DOFillAmount(target, fillAnimationDuration)
                    .SetEase(Ease.OutQuad);
            }

            if (progressText != null)
            {
                progressText.text = /* BuildProgressText(completedDaysThisMonth, spanDays) */completedDaysThisMonth.ToString();
            }

            if (fillEndMarker != null)
            {
                PositionAtFraction(fillEndMarker.transform as RectTransform, target, fillAnimationDuration);

                // Fully completed (bar filled all the way) - nothing left for the marker to
                // point at, so it hides instead of sitting stranded at the bar's far end.
                fillEndMarker.SetActive(target < 1f);
            }
        }

        // Just the day count - see GetProgressSpanDays for what spanDays represents.
        private string BuildProgressText(int completedDaysThisMonth, int spanDays)
        {
            return progressSpansRewardTiers
                ? $"{completedDaysThisMonth}/{spanDays} days"
                : $"{completedDaysThisMonth}/{spanDays} days this month";
        }

        // -----------------------------------------------------------------
        // REWARD SLOTS
        // -----------------------------------------------------------------

        private void RefreshSlots(
            List<Shared.GameConfig.DailyChallengeRewardTierConfigData> tiers,
            int completedDays,
            int spanDays)
        {
            if (rewardSlots == null)
            {
                return;
            }

            for (int i = 0; i < rewardSlots.Count; i++)
            {
                DailyChallengeRewardTierSlotView slot = rewardSlots[i];
                if (slot == null)
                {
                    continue;
                }

                if (tiers == null || i >= tiers.Count || tiers[i] == null)
                {
                    slot.gameObject.SetActive(false);
                    continue;
                }

                slot.gameObject.SetActive(true);

                Shared.GameConfig.DailyChallengeRewardTierConfigData tier = tiers[i];

                bool claimed = controller.IsRewardTierClaimed(tier);
                bool claimable = !claimed && completedDays >= tier.DaysRequired;

                DailyChallengeRewardTierSlotState state =
                    claimed ? DailyChallengeRewardTierSlotState.Claimed
                    : claimable ? DailyChallengeRewardTierSlotState.Claimable
                    : DailyChallengeRewardTierSlotState.Locked;

                float fraction = spanDays > 0
                    ? Mathf.Clamp01((float)tier.DaysRequired / spanDays)
                    : 0f;

                // progressSpansRewardTiers puts the last tier exactly at fraction 1 (the bar's
                // very end) - nudge only that slot back in by lastSlotEdgeOffsetX so its icon
                // doesn't clip past the bar's right edge.
                bool isLastTier = i == tiers.Count - 1;
                float offsetX = progressSpansRewardTiers && isLastTier ? lastSlotEdgeOffsetX : 0f;

                PositionSlotAtFraction(slot, fraction, offsetX);

                slot.Setup(tier, state);
            }
        }

        // Snaps the slot's anchor.x to `fraction` along its own parent's width instantly - no
        // fill animation for the slots, only progressBarImage's fill and fillEndMarker animate
        // (see the animated PositionAtFraction overload below). Only lines up visually if the
        // slot's parent RectTransform spans the same width as progressBarImage (e.g. the slot is
        // a child of the bar, or of a same-width track). offsetX is an extra local X nudge on top
        // of the anchor fraction - see lastSlotEdgeOffsetX.
        private static void PositionSlotAtFraction(DailyChallengeRewardTierSlotView slot, float fraction, float offsetX = 0f)
        {
            if (slot == null)
            {
                return;
            }

            var rect = slot.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            fraction = Mathf.Clamp01(fraction);

            rect.anchorMin = new Vector2(fraction, rect.anchorMin.y);
            rect.anchorMax = new Vector2(fraction, rect.anchorMax.y);
            rect.anchoredPosition = new Vector2(offsetX, rect.anchoredPosition.y);
        }

        // Animates rect's anchor.x to `fraction` along its own parent's width, leaving its
        // vertical anchor/offset untouched - the standard "marker riding a fill bar" technique,
        // used only by fillEndMarker (see PositionSlotAtFraction above for the reward slots'
        // instant, non-animated positioning). Snaps back to 0 first and tweens from there, same
        // as progressBarImage's fillAmount reset in RefreshProgressBar, so the marker always
        // animates in from the start of the bar instead of from wherever it was left sitting.
        private static void PositionAtFraction(RectTransform rect, float fraction, float duration)
        {
            if (rect == null)
            {
                return;
            }

            fraction = Mathf.Clamp01(fraction);

            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(0f, rect.anchorMax.y);
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);

            rect.DOKill();
            rect.DOAnchorMin(new Vector2(fraction, rect.anchorMin.y), duration).SetEase(Ease.OutQuad);
            rect.DOAnchorMax(new Vector2(fraction, rect.anchorMax.y), duration).SetEase(Ease.OutQuad);
        }
    }
}