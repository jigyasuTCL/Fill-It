using System;
using DG.Tweening;
using Shared.Boosters;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// Idle-attention pulse on the Hint booster button - same DOTween Yoyo-scale technique as
    /// MainMenuHomeAnimator's Play button pulse, but gated by a configurable no-input idle timer
    /// (GameRulesConfig.HintHighlightIdleSeconds) instead of always-on.
    ///
    /// Starts pulsing once that many seconds pass with no board tap and no hint use while the Hint
    /// booster is actually usable (see CatFlapGameplayManager wiring: HandleCellSelected and
    /// BoosterManager.BoosterImplementationStarted both call ResetTimer). Stops immediately -
    /// snapping straight back to the button's own captured base scale, never left mid-pulse - the
    /// instant either of those happens, or the hint stops being available (used up, locked, etc.).
    /// </summary>
    public sealed class CatFlapHintHighlightAnimator : MonoBehaviour
    {
        [SerializeField] private float pulseScaleMultiplier = 1.08f;
        [SerializeField] private float pulseHalfDurationSeconds = 0.4f;

        private RectTransform target;
        private Func<bool> isAvailable;
        private float idleSeconds = 10f;
        private float idleTimer;
        private Vector3 baseScale = Vector3.one;
        private bool baseScaleCaptured;
        private Tween pulseTween;
        private bool isPulsing;

        /// <summary>
        /// hintButtonTransform: the Hint BoosterButtonUI's own RectTransform (whatever
        /// BoosterManager.GetBoosterButton(BoosterType.Hint) resolves to) - the whole button pulses,
        /// same as the Play button. availabilityProvider: a live "can the player actually use Hint
        /// right now" check (see BoosterManager.IsBoosterAvailable) - polled every frame, so it
        /// naturally reacts to the hint count/unlock state changing mid-level without any extra
        /// wiring. Safe to call repeatedly (e.g. every level load) with a new target/provider.
        /// </summary>
        public void Configure(RectTransform hintButtonTransform, float hintHighlightIdleSeconds, Func<bool> availabilityProvider)
        {
            StopPulse();

            target = hintButtonTransform;
            idleSeconds = Mathf.Max(0f, hintHighlightIdleSeconds);
            isAvailable = availabilityProvider;
            baseScaleCaptured = false;
            idleTimer = 0f;
        }

        /// <summary>
        /// Called whenever the player does anything that should postpone the highlight - a board
        /// tap (CatFlapGameplayManager.HandleCellSelected) or actually using the Hint booster
        /// (BoosterManager.BoosterImplementationStarted for BoosterType.Hint). Restarts the idle
        /// countdown from zero and, if a pulse was already playing, stops it and snaps the button
        /// back to its base scale - so a tap that lands WHILE the highlight is mid-pulse can never
        /// leave the button showing at the wrong scale.
        /// </summary>
        public void ResetTimer()
        {
            idleTimer = 0f;
            StopPulse();
        }

        private void Update()
        {
            if (target == null)
            {
                return;
            }

            CaptureBaseScale();

            if (isAvailable == null || !isAvailable.Invoke())
            {
                // Not eligible right now (locked, out of count, hint not unlocked yet) - never
                // pulse, and never let time silently accumulate toward a highlight that would fire
                // the instant it later becomes available again.
                idleTimer = 0f;
                StopPulse();
                return;
            }

            if (isPulsing)
            {
                return;
            }

            idleTimer += Time.unscaledDeltaTime;
            if (idleTimer >= idleSeconds)
            {
                StartPulse();
            }
        }

        private void CaptureBaseScale()
        {
            if (baseScaleCaptured || target == null)
            {
                return;
            }

            Vector3 currentScale = target.localScale;
            if (currentScale.sqrMagnitude < 0.0001f)
            {
                // Caught mid some other entrance/scale-in animation still at (or near) zero - not
                // this button's real resting size. Retried every frame via Update until it settles.
                return;
            }

            baseScale = currentScale;
            baseScaleCaptured = true;
        }

        private void StartPulse()
        {
            if (isPulsing || target == null || !baseScaleCaptured)
            {
                return;
            }

            isPulsing = true;
            pulseTween = target
                .DOScale(baseScale * Mathf.Max(1f, pulseScaleMultiplier), Mathf.Max(0.01f, pulseHalfDurationSeconds))
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true); // unscaled time - keeps pulsing even if gameplay time-scale changes
        }

        private void StopPulse()
        {
            if (pulseTween != null)
            {
                pulseTween.Kill();
                pulseTween = null;
            }

            if (!isPulsing)
            {
                return;
            }

            isPulsing = false;
            if (target != null && baseScaleCaptured)
            {
                target.localScale = baseScale;
            }
        }

        private void OnDisable()
        {
            StopPulse();
        }
    }
}
