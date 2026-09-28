using System.Collections;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// The "there is something waiting for you" marker - a badge that appears and pulses while a
    /// leaderboard reward is unclaimed, and hides the moment it is collected.
    ///
    /// One component for both places it is needed: drop it on the main menu's leaderboard button
    /// and on the leaderboard panel's Collect button. Neither has to know anything about rewards;
    /// this listens to the manager and drives its own art.
    ///
    /// Deliberately coroutine-driven rather than an Animator or a tween: the rest of this feature
    /// animates the same way, and a badge that needs a controller asset is one more thing to wire
    /// on every prefab that wants one.
    /// </summary>
    public sealed class LeaderboardRewardBadge : MonoBehaviour
    {
        [SerializeField] private LeaderboardManager leaderboard;
        // Shown only while something is unclaimed. Usually a dot, a glow or an exclamation mark.
        [SerializeField] private GameObject badgeRoot;
        // What actually pulses. Leave empty to pulse badgeRoot itself.
        [SerializeField] private RectTransform pulseTarget;

        [Header("Pulse")]
        [SerializeField] private float pulseScale = 1.12f;
        [SerializeField] private float pulseDurationSeconds = 0.6f;
        // Dead time between pulses, so the badge draws the eye without nagging continuously.
        [SerializeField] private float pulseIntervalSeconds = 1.2f;
        [SerializeField]
        private AnimationCurve pulseCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));

        private Coroutine pulseRoutine;
        private Vector3 restingScale = Vector3.one;

        private void Awake()
        {
            if (pulseTarget == null && badgeRoot != null)
            {
                pulseTarget = badgeRoot.transform as RectTransform;
            }

            // Captured before anything animates, so a repeated show/hide can never accumulate
            // scale drift away from what was authored.
            if (pulseTarget != null)
            {
                restingScale = pulseTarget.localScale;
            }
        }

        private void OnEnable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= Refresh;
                leaderboard.PendingRewardsChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (leaderboard != null)
            {
                leaderboard.PendingRewardsChanged -= Refresh;
            }

            StopPulse();
        }

        /// <summary>
        /// Shows or hides the badge from the manager's current state. Public so a screen that
        /// becomes visible can re-check without waiting for the next change.
        /// </summary>
        public void Refresh()
        {
            bool hasReward = leaderboard != null && leaderboard.PendingRewards.HasRewards;

            if (badgeRoot != null && badgeRoot.activeSelf != hasReward)
            {
                badgeRoot.SetActive(hasReward);
            }

            if (hasReward)
            {
                StartPulse();
            }
            else
            {
                StopPulse();
            }
        }

        private void StartPulse()
        {
            // Only when this object can actually run a coroutine - a badge on a menu that is
            // currently hidden simply starts pulsing when it comes back.
            if (pulseRoutine != null || pulseTarget == null || !isActiveAndEnabled)
            {
                return;
            }

            pulseRoutine = StartCoroutine(PulseRoutine());
        }

        private void StopPulse()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }

            if (pulseTarget != null)
            {
                pulseTarget.localScale = restingScale;
            }
        }

        private IEnumerator PulseRoutine()
        {
            float duration = Mathf.Max(0.01f, pulseDurationSeconds);
            Vector3 peak = restingScale * Mathf.Max(1f, pulseScale);

            while (true)
            {
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
                {
                    // Unscaled: this runs over menus and popups, which are often shown with the
                    // game paused.
                    float amount = pulseCurve != null && pulseCurve.length > 0
                        ? pulseCurve.Evaluate(Mathf.Clamp01(elapsed / duration))
                        : 0f;

                    pulseTarget.localScale = Vector3.LerpUnclamped(restingScale, peak, amount);
                    yield return null;
                }

                pulseTarget.localScale = restingScale;

                if (pulseIntervalSeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(pulseIntervalSeconds);
                }
            }
        }
    }
}
