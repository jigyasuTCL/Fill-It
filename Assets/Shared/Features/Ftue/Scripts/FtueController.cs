using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Ftue
{
    // Standalone entry point for a single FTUE tutorial step: highlight an area (world-space or
    // a UI element) and show an explanatory text box, or close both. This is the entire public
    // surface - it has no concept of steps advancing, levels, boards, cells, or tap counts; a
    // game's own driver code (e.g. Cat Flap's CatFlapFtueDriver) owns that sequencing and calls
    // Show/ShowDismissOnTap/Hide here.
    public sealed class FtueController : MonoBehaviour
    {
        [SerializeField] private FtueSpotlightController spotlight;
        [SerializeField] private FtueBannerController banner;
        // Full-screen, otherwise-invisible raycast target used only by ShowDismissOnTap - stays
        // inactive/unused for the regular Show(Bounds, ...) board-highlight case, where taps are
        // restricted to a single cell by the game's own input-handling code instead.
        [SerializeField] private Button dismissCatcher;
        // How long ShowDismissOnTap waits, after first appearing, before the "tap to close" hint
        // shows and the tap-anywhere-to-dismiss catcher activates. Keeps the player from
        // dismissing (or the board from receiving taps behind) the tip before they've had a
        // chance to actually read it.
        [SerializeField] private float dismissEnableDelaySeconds = 2f;

        public bool IsShowing { get; private set; }

        private System.Action onDismissed;
        private Coroutine dismissEnableRoutine;

        // Confines the dim overlay for every subsequent Show(Bounds, ...) board-highlight call to
        // just the board's own on-screen area instead of the whole screen - see
        // FtueSpotlightController.SetBoardClipBounds. Called once per FTUE-active level by
        // CatFlapFtueDriver.Configure. Never affects ShowDismissOnTap's full-screen case.
        public void SetBoardClipBounds(Bounds worldBounds, Camera camera)
        {
            spotlight?.SetBoardClipBounds(worldBounds, camera);
        }

        // See FtueSpotlightController.SetOverlayDimAlpha/ResetOverlayDimAlpha - overrides (or
        // restores) how dark the dim overlay reads, on top of whatever's authored in the prefab.
        // Persists across Show/ShowCross/Hide until reset - callers own resetting it once
        // whatever needed the darker look ends.
        public void SetOverlayDimAlpha(float alpha)
        {
            spotlight?.SetOverlayDimAlpha(alpha);
        }

        public void ResetOverlayDimAlpha()
        {
            spotlight?.ResetOverlayDimAlpha();
        }

        // isDoubleTap picks which hand-pointer animation clip plays at this target (single-tap
        // vs double-tap) - purely cosmetic, has no bearing on how many taps are actually needed.
        // showHandPointer lets the caller keep the spotlight/banner up while hiding just the hand
        // (e.g. right after a tap is registered, while waiting for the next step to trigger).
        public void Show(Bounds worldBounds, Camera camera, FtueStepData step, bool isDoubleTap = false, bool showHandPointer = true, Bounds? handWorldBounds = null)
        {
            if (step == null)
            {
                Hide();
                return;
            }

            spotlight?.Show(worldBounds, camera, isDoubleTap, showHandPointer, handWorldBounds);
            banner?.Show(step.Description);
            IsShowing = true;
        }

        // Same as Show(Bounds, ...), but highlights a full row AND a full column at once (a "+"
        // shape) instead of one rectangular area - see FtueSpotlightController.ShowCross for why
        // that needs its own dedicated call rather than just merging both into one Bounds.
        // showBanner = false leaves the text box hidden entirely (spotlight + hand pointer still
        // show as normal) - e.g. Cat Flap's Level 4 rules walkthrough shows the last cell's cross
        // highlight the instant the player is free to tap, but withholds the banner until they
        // actually break the rule, via the separate ShowBanner call below.
        public void ShowCross(Bounds rowWorldBounds, Bounds columnWorldBounds, Camera camera, FtueStepData step, bool isDoubleTap = false, bool showHandPointer = true, Bounds? handWorldBounds = null, bool showBanner = true, bool showSpotlight = true)
        {
            if (step == null)
            {
                Hide();
                return;
            }

            spotlight?.ShowCross(rowWorldBounds, columnWorldBounds, camera, isDoubleTap, showHandPointer, handWorldBounds, showSpotlight);
            if (showBanner)
            {
                banner?.Show(step.Description);
            }

            IsShowing = true;
        }

        // Reveals the banner on its own, for a spotlight/hand already showing from an earlier
        // ShowCross(..., showBanner: false) call - see there for why this exists.
        public void ShowBanner(FtueStepData step)
        {
            if (step == null)
            {
                return;
            }

            banner?.Show(step.Description);
        }

        // Hides just the dim overlay + hand pointer, leaving the banner up and IsShowing true -
        // used mid-transition (e.g. Cat Flap's Level 4 rules walkthrough) when the banner needs to
        // stay visible and animate to a new position instead of closing and reopening.
        public void HideSpotlightOnly()
        {
            spotlight?.Hide();
        }

        // Animates the banner's own RectTransform to fly onto a target UI element (e.g. a Rules
        // panel row), shrinking to fit that element's own size and fading out as it arrives - see
        // FtueBannerController.MoveFadeAndFitTo. Call ResetBannerPositionAndScale before the next
        // normal Show() so later steps aren't left offset/shrunk/faded from this move.
        public void MoveBannerToTarget(RectTransform target, float durationSeconds, System.Action onComplete = null, float? overrideScale = null, float fadeToAlpha = 0f)
        {
            banner?.MoveFadeAndFitTo(target, durationSeconds, onComplete, overrideScale, fadeToAlpha);
        }

        public void ResetBannerPositionAndScale()
        {
            banner?.ResetPositionAndScale();
        }

        // Updates just the banner's text in place, leaving the spotlight and the banner's current
        // position/scale untouched - used when a step's guidance text needs to change without
        // touching whatever else is currently on screen (e.g. Cat Flap's Level 4 rules walkthrough
        // switches to a rule's own explanation right as the banner arrives at that rule's chip).
        public void UpdateBannerText(FtueStepData step)
        {
            if (step == null)
            {
                return;
            }

            banner?.Show(step.Description);
        }

        // Highlights a UI element (e.g. a HUD panel) instead of a world-space area, and lets a
        // tap ANYWHERE on screen dismiss it - the tap is fully consumed (no pass-through to
        // whatever is underneath). onDismissed (optional) fires once, when this closes for any
        // reason - the driving game code uses it to know when to release whatever input lock it
        // applied while this was showing.
        public void ShowDismissOnTap(RectTransform targetUIRect, FtueStepData step, System.Action onDismissedCallback = null)
        {
            if (step == null)
            {
                Hide();
                return;
            }

            onDismissed = onDismissedCallback;
            spotlight?.ShowUI(targetUIRect);
            banner?.Show(step.Description, showDismissHint: false);
            if (dismissCatcher != null)
            {
                dismissCatcher.gameObject.SetActive(false);
                dismissCatcher.onClick.RemoveListener(Hide);
                dismissCatcher.onClick.AddListener(Hide);
            }

            IsShowing = true;

            StopDismissEnableRoutine();
            dismissEnableRoutine = StartCoroutine(EnableDismissAfterDelay());
        }

        private IEnumerator EnableDismissAfterDelay()
        {
            yield return new WaitForSeconds(dismissEnableDelaySeconds);
            dismissEnableRoutine = null;
            banner?.SetDismissHintVisible(true);
            if (dismissCatcher != null)
            {
                dismissCatcher.gameObject.SetActive(true);
            }
        }

        private void StopDismissEnableRoutine()
        {
            if (dismissEnableRoutine != null)
            {
                StopCoroutine(dismissEnableRoutine);
                dismissEnableRoutine = null;
            }
        }

        public void Hide()
        {
            StopDismissEnableRoutine();
            spotlight?.Hide();
            banner?.Hide();
            if (dismissCatcher != null)
            {
                dismissCatcher.gameObject.SetActive(false);
            }

            IsShowing = false;
            System.Action callback = onDismissed;
            onDismissed = null;
            callback?.Invoke();
        }

        // Instant variant of Hide() - skips the spotlight's fade-out and the banner's close pop
        // entirely. See FtueSpotlightController.HideImmediate for why this matters.
        public void HideImmediate()
        {
            StopDismissEnableRoutine();
            spotlight?.HideImmediate();
            banner?.HideImmediate();
            if (dismissCatcher != null)
            {
                dismissCatcher.gameObject.SetActive(false);
            }

            IsShowing = false;
            onDismissed = null;
        }
    }
}
