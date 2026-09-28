using System.Collections;
using Shared.GameFlow;
using TMPro;
using UnityEngine;

namespace Shared.Ftue
{
    // The FTUE text box: just a description, opened/closed with the same shared
    // PopupScaleAnimator every other popup in this game already uses. Purely a dumb text
    // renderer - it has no opinion on when to show/hide or what the text should say.
    public sealed class FtueBannerController : MonoBehaviour
    {
        [SerializeField] private GameObject bannerRoot;
        [SerializeField] private PopupScaleAnimator animator;
        [SerializeField] private TextMeshProUGUI descriptionLabel;
        // Optional - a TMP_Text authored below descriptionLabel in the banner prefab (e.g. the
        // Level Attempt "Beware!" tip). Left unassigned, showDismissHint is simply ignored, same
        // as every other field here.
        [SerializeField] private TextMeshProUGUI dismissHintLabel;
        [SerializeField] private string dismissHintText = "Tap to continue";

        // Show() gets called on every tap/refresh (see CatFlapFtueDriver.Refresh, called far more
        // often than the visible text actually changes) - only replay the open/pop animation when
        // the banner is newly appearing or the text is genuinely different, so re-showing the SAME
        // step doesn't flash.
        private string currentDescription;
        private bool isShowing;

        // showDismissHint is only ever true for FtueController.ShowDismissOnTap (the tap-anywhere
        // "Beware!" tip) - the board-cell tutorial (FtueController.Show) never passes it, so the
        // hint never shows there.
        public void Show(string description, bool showDismissHint = false)
        {
            if (bannerRoot == null)
            {
                return;
            }

            bool isUnchanged = isShowing && currentDescription == description;

            if (descriptionLabel != null)
            {
                descriptionLabel.text = description;
            }

            if (dismissHintLabel != null)
            {
                dismissHintLabel.text = dismissHintText;
                dismissHintLabel.gameObject.SetActive(showDismissHint);
            }

            currentDescription = description;
            bannerRoot.SetActive(true);

            // Forces full visibility even if a previous MoveFadeAndFitTo left this faded out
            // (e.g. Cat Flap's Level 4 rules walkthrough, which fades the banner to nothing right
            // as it lands on a rule chip) - every fresh Show() should start fully opaque.
            if (bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = 1f;
            }

            if (!isUnchanged)
            {
                animator?.PlayOpen();
            }

            isShowing = true;
        }

        // Toggles just the dismiss-hint label in place, without replaying Show()'s open animation
        // or touching the description text - used by FtueController.ShowDismissOnTap to reveal the
        // hint a couple seconds after the banner itself first appears.
        public void SetDismissHintVisible(bool visible)
        {
            if (dismissHintLabel != null)
            {
                dismissHintLabel.gameObject.SetActive(visible);
            }
        }

        // Auto-resolved (and added if missing) from bannerRoot in Awake - no prefab wiring needed
        // for MoveFadeAndFitTo's fade-out to work.
        [SerializeField] private CanvasGroup bannerCanvasGroup;
        // The move animation starts fading out once it's this fraction of the way through -
        // e.g. 0.5 fades only over the second half of the move, finishing fully transparent
        // exactly as it arrives.
        [SerializeField] private float fadeOutStartFraction = 0.5f;

        private RectTransform bannerRect;
        private Vector3 originalPosition;
        private Vector3 originalLocalScale;
        private bool originalCaptured;
        private Coroutine moveScaleRoutine;

        private void Awake()
        {
            if (bannerRoot == null)
            {
                return;
            }

            if (bannerCanvasGroup == null)
            {
                bannerCanvasGroup = bannerRoot.GetComponent<CanvasGroup>();
            }

            if (bannerCanvasGroup == null)
            {
                bannerCanvasGroup = bannerRoot.AddComponent<CanvasGroup>();
            }
        }

        // Captured lazily (not in Awake) so this only ever records the banner's true authored
        // resting position/scale - the first time it's needed, before anything has moved it.
        private void CaptureOriginalTransformIfNeeded()
        {
            if (originalCaptured || bannerRoot == null)
            {
                return;
            }

            bannerRect = bannerRoot.transform as RectTransform;
            if (bannerRect == null)
            {
                return;
            }

            originalPosition = bannerRect.position;
            originalLocalScale = bannerRect.localScale;
            originalCaptured = true;
        }

        /// <summary>
        /// Animates this banner's own RectTransform to fly onto another UI element (e.g. a Rules
        /// panel row), shrinking to FIT that element's own on-screen size (not a fixed scale) and
        /// fading out (see fadeOutStartFraction) so it visually "delivers" itself into the target
        /// rather than sitting on top of it at full opacity. Lerps real Transform world position
        /// (not anchoredPosition) - anchoredPosition is only equivalent to a screen-space
        /// conversion when the banner's own anchors happen to sit exactly on its parent's pivot,
        /// which silently produced almost no visible movement for any other anchor setup. World
        /// position has no such caveat as long as target lives under the same Canvas (or another
        /// Canvas with a matching scale), which every caller here does. Call ResetPositionAndScale
        /// afterwards, before the next normal Show(), so a later step doesn't stay offset/shrunk/
        /// faded from this move.
        /// </summary>
        /// overrideScale: a fixed uniform scale (e.g. Cat Flap's Level 4 rules walkthrough, which
        /// always shrinks to exactly 0.75x rather than fitting the target's own on-screen size -
        /// see CatFlapFtueDriver.rulesBannerTargetScale) - null (the default) keeps the original
        /// fit-to-target behavior via ComputeFitScale.
        /// fadeToAlpha: the alpha reached by the end of the move - 0 (the default) is a full
        /// fade-out; Cat Flap's Level 4 rules walkthrough instead only dims to 0.5 (see
        /// CatFlapFtueDriver.rulesBannerFadeToAlpha), so the banner stays partly visible sitting on
        /// the rule chip rather than disappearing entirely.
        public void MoveFadeAndFitTo(RectTransform target, float durationSeconds, System.Action onComplete = null, float? overrideScale = null, float fadeToAlpha = 0f)
        {
            CaptureOriginalTransformIfNeeded();
            if (bannerRect == null || target == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (moveScaleRoutine != null)
            {
                StopCoroutine(moveScaleRoutine);
            }

            Vector3 targetLocalScale = overrideScale.HasValue ? Vector3.one * overrideScale.Value : ComputeFitScale(target);
            moveScaleRoutine = StartCoroutine(AnimateMoveFadeAndScale(target.position, targetLocalScale, durationSeconds, fadeOut: true, onComplete, fadeToAlpha));
        }

        // Both RectTransform.rect sizes are already in their own local (unscaled-by-localScale)
        // units, and both live under the same Canvas (so world-units-per-local-unit matches) -
        // comparing them directly gives the uniform scale that makes the banner's on-screen
        // footprint match the target's, with no screen-space conversion needed. Assumes the
        // banner's own resting localScale is uniform (Vector3.one), true for every popup here.
        private Vector3 ComputeFitScale(RectTransform target)
        {
            Rect bannerLocalRect = bannerRect.rect;
            if (bannerLocalRect.width <= 0f || bannerLocalRect.height <= 0f)
            {
                return originalLocalScale;
            }

            Rect targetLocalRect = target.rect;
            float uniformScale = Mathf.Min(targetLocalRect.width / bannerLocalRect.width, targetLocalRect.height / bannerLocalRect.height);
            return Vector3.one * uniformScale;
        }

        /// <summary>
        /// Snaps (or, if durationSeconds > 0, animates) the banner's RectTransform back to its
        /// originally-authored position/scale. Deliberately leaves alpha exactly as it already is
        /// (whatever alpha a preceding MoveFadeAndFitTo left it at - 0 for a full fade-out, or
        /// its own fadeToAlpha for a partial one) rather than forcing it back to opaque - a
        /// caller that's about to Show() again gets full opacity from Show() itself
        /// (which always forces alpha to 1 on its own), and a caller that's about to Hide() this
        /// same banner instead (see CatFlapFtueDriver.RevealRuleThenAdvance, which always calls
        /// this right before Hide()) must NOT have this flash it back to fully visible first -
        /// that used to make the banner visibly reappear, scale and all, at its original position
        /// for one frame before Hide()'s own close animation played on top of it. A no-op if
        /// MoveFadeAndFitTo was never called.
        /// </summary>
        public void ResetPositionAndScale(float durationSeconds = 0f)
        {
            if (!originalCaptured || bannerRect == null)
            {
                return;
            }

            if (moveScaleRoutine != null)
            {
                StopCoroutine(moveScaleRoutine);
                moveScaleRoutine = null;
            }

            if (durationSeconds <= 0f)
            {
                bannerRect.position = originalPosition;
                bannerRect.localScale = originalLocalScale;
                return;
            }

            moveScaleRoutine = StartCoroutine(AnimateMoveFadeAndScale(originalPosition, originalLocalScale, durationSeconds, fadeOut: false, null, 0f));
        }

        // Scale only starts changing after this fraction of the move (position/fade still start
        // right away) - holding the banner at its starting scale for the first stretch of the
        // move reads as a clean "pick up and carry" instead of it visibly shrinking/growing from
        // the very first frame. Mirrors fadeOutStartFraction's same "start late" shape below.
        [SerializeField] private float scaleStartFraction = 0.25f;

        private IEnumerator AnimateMoveFadeAndScale(Vector3 targetPosition, Vector3 targetLocalScale, float durationSeconds, bool fadeOut, System.Action onComplete, float fadeToAlpha = 0f)
        {
            Vector3 startPosition = bannerRect.position;
            Vector3 startScale = bannerRect.localScale;
            float startAlpha = bannerCanvasGroup != null ? bannerCanvasGroup.alpha : 1f;
            float duration = Mathf.Max(0.01f, durationSeconds);
            float fadeStart = Mathf.Clamp01(fadeOutStartFraction);
            float scaleStart = Mathf.Clamp01(scaleStartFraction);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                bannerRect.position = Vector3.Lerp(startPosition, targetPosition, t);
                float scaleT = scaleStart >= 1f ? 0f : Mathf.Clamp01((t - scaleStart) / (1f - scaleStart));
                bannerRect.localScale = Vector3.Lerp(startScale, targetLocalScale, scaleT);
                if (fadeOut && bannerCanvasGroup != null)
                {
                    float fadeT = fadeStart >= 1f ? 0f : Mathf.Clamp01((t - fadeStart) / (1f - fadeStart));
                    bannerCanvasGroup.alpha = Mathf.Lerp(startAlpha, fadeToAlpha, fadeT);
                }

                yield return null;
            }

            bannerRect.position = targetPosition;
            bannerRect.localScale = targetLocalScale;
            if (fadeOut && bannerCanvasGroup != null)
            {
                bannerCanvasGroup.alpha = fadeToAlpha;
            }

            moveScaleRoutine = null;
            onComplete?.Invoke();
        }

        public void Hide()
        {
            isShowing = false;
            currentDescription = null;

            if (bannerRoot == null || !bannerRoot.activeSelf)
            {
                return;
            }

            if (animator == null)
            {
                bannerRoot.SetActive(false);
                return;
            }

            animator.PlayClose(() => bannerRoot.SetActive(false));
        }

        // Instant variant of Hide() - see FtueSpotlightController.HideImmediate for why this
        // matters (a lingering close animation visibly hanging in place while the thing it was
        // pointing at is already moving away).
        public void HideImmediate()
        {
            isShowing = false;
            currentDescription = null;

            if (moveScaleRoutine != null)
            {
                StopCoroutine(moveScaleRoutine);
                moveScaleRoutine = null;
            }

            if (bannerRoot != null)
            {
                bannerRoot.SetActive(false);
            }
        }
    }
}
