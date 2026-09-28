using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    public enum DailyChallengeRewardTierSlotState
    {
        Locked,
        Claimable,
        Claimed
    }

    /// <summary>
    /// Displays one Daily Challenge reward tier ("4 days -> 50 coins"). Mirrors DailyRewards'
    /// RewardSlotView - only job is to render whatever state it's told to show; knows nothing
    /// about progress calculation or claim persistence (see DailyChallengeRewardTrackController).
    /// Claiming itself is automatic (see DailyChallengeRewardTrackController.TryAutoClaimAvailableTier)
    /// - this view has no button, the Claimable state is purely visual (e.g. a glow/pulse).
    /// </summary>
    public sealed class DailyChallengeRewardTierSlotView : MonoBehaviour
    {
        [SerializeField] private TMP_Text daysRequiredText;
        [SerializeField] private TMP_Text rewardAmountText;
        [SerializeField] private Image icon;
        [SerializeField] private GameObject lockedOverlay;
        [SerializeField] private GameObject claimedOverlay;
        [SerializeField] private ParticleSystem particleEffect;

        [Header("Icon Lookup")]
        [Tooltip("Maps DailyChallengeRewardTierConfigData.IconId -> Sprite. Assign the shared IconDatabase asset here (same one used by DailyRewards/SpinWheel).")]
        [SerializeField] private IconDatabaseConfig iconDatabase;

        [Header("Claim Animation")]
        [Tooltip("How long claimedOverlay takes to pop in (scale 0 -> 1) when this tier is claimed.")]
        [SerializeField] private float claimedScaleInDuration = 0.35f;
        [SerializeField] private Ease claimedScaleInEase = Ease.OutBack;
        [Tooltip("DOPunchScale strength/duration applied to the reward icon the moment this tier is claimed.")]
        [SerializeField] private Vector3 rewardPunchStrength = new Vector3(0.3f, 0.3f, 0f);
        [SerializeField] private float rewardPunchDuration = 0.4f;
        [SerializeField] private int rewardPunchVibrato = 8;
        [SerializeField] private float rewardPunchElasticity = 0.8f;

        /// <summary>Icon RectTransform this slot shows its reward on - used as the jump-from
        /// source for the collect screen's fly-in animation (see
        /// DailyChallengeRewardTrackController.StartCollectScreenAnimation).</summary>
        public RectTransform IconRectTransform => icon != null ? icon.rectTransform : null;

        public void Setup(
            Shared.GameConfig.DailyChallengeRewardTierConfigData tier,
            DailyChallengeRewardTierSlotState state)
        {
            if (tier == null)
            {
                return;
            }

            if (daysRequiredText != null)
            {
                daysRequiredText.text = $"{tier.DaysRequired}";
            }

            if (rewardAmountText != null)
            {
                rewardAmountText.text = $"x{tier.RewardCoins}";
            }

            ApplyIcon(tier.IconId);

            if (lockedOverlay != null)
            {
                lockedOverlay.SetActive(state == DailyChallengeRewardTierSlotState.Locked);
            }

            if (claimedOverlay != null)
            {
                claimedOverlay.SetActive(state == DailyChallengeRewardTierSlotState.Claimed);
            }

        }

        /// <summary>
        /// Plays this slot's "just claimed" presentation - particle burst, the claimed badge
        /// popping in (scale 0 -> 1, claimedScaleInEase), and a punch on the reward icon - then
        /// calls onComplete once every tween in the sequence has finished. Called by
        /// DailyChallengeRewardTrackController.PlayRewardWinAnimation right after this tier is
        /// actually claimed.
        /// </summary>
        public void PlayClaimAnimation(Action onComplete)
        {
            if (particleEffect != null)
            {
                particleEffect.Play();
            }

            Tween overlayTween = null;
            if (claimedOverlay != null)
            {
                Transform overlayTransform = claimedOverlay.transform;
                overlayTransform.DOKill();
                overlayTransform.localScale = Vector3.zero;
                overlayTween = overlayTransform.DOScale(Vector3.one, claimedScaleInDuration).SetEase(claimedScaleInEase);
            }

            Tween punchTween = null;
            if (icon != null)
            {
                Transform iconTransform = icon.transform;
                iconTransform.DOKill();
                punchTween = iconTransform.DOPunchScale(rewardPunchStrength, rewardPunchDuration, rewardPunchVibrato, rewardPunchElasticity);
            }

            if (overlayTween == null && punchTween == null)
            {
                onComplete?.Invoke();
                return;
            }

            Sequence sequence = DOTween.Sequence();
            sequence.SetTarget(this);

            bool queuedFirst = false;
            if (overlayTween != null)
            {
                sequence.Append(overlayTween);
                queuedFirst = true;
            }

            if (punchTween != null)
            {
                if (queuedFirst)
                {
                    sequence.Join(punchTween);
                }
                else
                {
                    sequence.Append(punchTween);
                }
            }

            sequence.OnComplete(() => onComplete?.Invoke());
        }

        private void ApplyIcon(string iconId)
        {
            if (icon == null)
            {
                return;
            }

            if (iconDatabase == null)
            {
                Debug.LogWarning(
                    $"{nameof(DailyChallengeRewardTierSlotView)} on {name} has no {nameof(IconDatabaseConfig)} " +
                    "assigned - icons won't be shown.");
                return;
            }

            string key = string.IsNullOrEmpty(iconId) ? "coin" : iconId;
            Sprite sprite = iconDatabase.GetIconOrFallback(key);

            if (sprite != null)
            {
                icon.sprite = sprite;
            }

            // Hide the image entirely rather than showing Unity's broken-sprite square if
            // neither the key nor the fallback resolved.
            icon.enabled = sprite != null;
        }
    }
}
