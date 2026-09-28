using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    /// <summary>
    /// "Hard Level" announcement popup - purely presentational, plays its own sound then:
    ///   Open:  panelCanvasGroup fades 0->1, THEN container scales up.
    ///   Close: depends on fadeAllTogetherOnClose - either panelCanvasGroup fades to 0 and then
    ///          moveToTargetImage flies to moveToTargetDestination, or everything simply fades
    ///          out at once. No player input needed for either.
    /// Knows nothing about GlobalConfig.HardLevels or whether it's already been shown before -
    /// CatFlapGameplayManager owns that decision entirely and only calls Show() when it has
    /// already decided this level's intro should actually play.
    ///
    /// Lives on HardLevelPanel.prefab (root inactive by default, with a centered "container"
    /// child holding the badge art).
    /// </summary>
    public sealed class HardLevelPopupController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private CanvasGroup panelCanvasGroup;

        [Header("Entry Animation - panelCanvasGroup fades 0->1, THEN container scales up")]
        [SerializeField] private RectTransform container;
        [SerializeField] private float fadeInDuration = 0.2f;
        [SerializeField] private float containerScaleUpDuration = 0.35f;
        [SerializeField] private Ease containerScaleUpEase = Ease.OutBack;

        [Header("Close Animation")]
        [Tooltip("OFF: everything fades out, THEN moveToTargetImage flies to moveToTargetDestination. ON: everything (moveToTargetImage included) just fades out at the same time, with no flight - the move/scale settings below are ignored.")]
        [SerializeField] private bool fadeAllTogetherOnClose = false;
        [Tooltip("The single image (inside container) that flies to moveToTargetDestination on close. Every other child just fades out with panelCanvasGroup. Ignored while fadeAllTogetherOnClose is ON.")]
        [SerializeField] private RectTransform moveToTargetImage;
        [Tooltip("World-space destination moveToTargetImage flies to on close (e.g. a HUD icon/marker elsewhere on screen).")]
        [SerializeField] private Transform moveToTargetDestination;
        [SerializeField] private float fadeOutDuration = 0.2f;
        [SerializeField] private float moveToTargetDuration = 0.4f;
        [SerializeField] private Ease moveToTargetEase = Ease.InOutQuad;

        [Tooltip("Optional. When enabled, moveToTargetImage also scales to moveToTargetScale while it flies to moveToTargetDestination.")]
        [SerializeField] private bool scaleWhileMovingToTarget = false;
        [SerializeField] private Vector3 moveToTargetScale = Vector3.one;

        [Tooltip("Fired the instant moveToTargetImage actually reaches moveToTargetDestination - e.g. wire this to CatFlapStatusViewAdapter to enable its hardLevelUI HUD element right as the badge \"arrives\".")]
        public UnityEvent OnMoveToTargetArrived = new UnityEvent();

        [Tooltip("Every image here also fades its own alpha to 0, alongside panelCanvasGroup and moveToTargetImage, the moment PlayCloseAnimation runs.")]
        [SerializeField] private List<Image> imagesToFadeOnClose = new List<Image>();

        [Header("Timing")]
        [Tooltip("How long the popup stays open before auto-closing, once the entry animation finishes.")]
        [SerializeField] private float autoCloseDelaySeconds = 1.5f;

        // Set this from outside (e.g. CatFlapGameplayManager) before calling ShowIfHardLevel -
        // invoked exactly once every time ShowIfHardLevel/Show runs, whichever path it takes
        // (skipped immediately, or played through the full open->wait->close animation).
        public System.Action pendingOnComplete;

        private Coroutine autoCloseRoutine;
        private Sequence activeSequence;
        private Vector2 moveToTargetImageHomeAnchoredPosition;
        private Vector3 moveToTargetImageHomeScale;
        private bool moveToTargetImageHomeCaptured;

        // Right-click this component's header (or the "..." menu) in Play Mode and pick "Test
        // Show Popup" to play the animation/sound on demand - same testing convention as
        // CollectScreenController/SpinWheelUIController.
        [ContextMenu("Test Show Popup")]
        private void TestShowPopup()
        {
            Show();
        }

        private void OnDisable()
        {
            if (autoCloseRoutine != null)
            {
                StopCoroutine(autoCloseRoutine);
                autoCloseRoutine = null;
            }

            activeSequence?.Kill();
            activeSequence = null;

            // Guards against stranding a caller waiting on pendingOnComplete forever if this
            // GameObject is disabled mid-animation (interrupted) instead of finishing normally
            // through PlayCloseAnimation's own seq.OnComplete below.
            InvokePendingComplete();
        }

        // Fires pendingOnComplete exactly once then clears it, regardless of which of the three
        // call sites (this interrupted-mid-animation guard, or PlayCloseAnimation's own
        // seq.OnComplete) actually triggers it.
        private void InvokePendingComplete()
        {
            System.Action callback = pendingOnComplete;
            pendingOnComplete = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Plays the full open->wait->close animation unconditionally - whether this is a hard
        /// level, whether it's already been shown before, etc. is entirely CatFlapGameplayManager's
        /// call (see LoadAssignedLevel/FinishLevelStartEntrance); this component knows nothing
        /// about GlobalConfig.HardLevels or PlayerPrefs. Set pendingOnComplete before calling this
        /// if you need to know when the whole sequence finishes.
        /// </summary>
        public void Show()
        {

            AudioManager.Instance.PlaySoundAction?.Invoke(SoundName.HardLevelPopupOpen);

            if (autoCloseRoutine != null)
            {
                StopCoroutine(autoCloseRoutine);
                autoCloseRoutine = null;
            }

            activeSequence?.Kill();

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            ResetVisualState();

            Sequence seq = DOTween.Sequence();

            if (panelCanvasGroup != null)
            {
                seq.Append(panelCanvasGroup.DOFade(1f, fadeInDuration));
            }

            if (container != null)
            {
                seq.Append(container.DOScale(Vector3.one, containerScaleUpDuration).SetEase(containerScaleUpEase));
            }

            seq.OnComplete(() => autoCloseRoutine = StartCoroutine(AutoCloseRoutine()));

            activeSequence = seq;
        }

        // Snaps every animated element back to its starting state - guards against a popup
        // reopened right after a previous close was interrupted mid-flight/fade.
        private void ResetVisualState()
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
            }

            if (container != null)
            {
                container.localScale = Vector3.zero;
            }

            if (moveToTargetImage != null)
            {
                if (!moveToTargetImageHomeCaptured)
                {
                    moveToTargetImageHomeAnchoredPosition = moveToTargetImage.anchoredPosition;
                    moveToTargetImageHomeScale = moveToTargetImage.localScale;
                    moveToTargetImageHomeCaptured = true;
                }

                moveToTargetImage.anchoredPosition = moveToTargetImageHomeAnchoredPosition;
                moveToTargetImage.localScale = moveToTargetImageHomeScale;
            }

            for (int i = 0; i < imagesToFadeOnClose.Count; i++)
            {
                Image image = imagesToFadeOnClose[i];
                if (image == null)
                {
                    continue;
                }

                Color color = image.color;
                color.a = 1f;
                image.color = color;
            }
        }

        private IEnumerator AutoCloseRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, autoCloseDelaySeconds));

            autoCloseRoutine = null;
            PlayCloseAnimation();
        }

        private void PlayCloseAnimation()
        {
            activeSequence?.Kill();

            Sequence seq = DOTween.Sequence();

            // Phase 1: panelCanvasGroup + imagesToFadeOnClose fade out together.
            if (panelCanvasGroup != null)
            {
                seq.Append(panelCanvasGroup.DOFade(0f, fadeOutDuration));
            }

            for (int i = 0; i < imagesToFadeOnClose.Count; i++)
            {
                Image image = imagesToFadeOnClose[i];
                if (image != null)
                {
                    seq.Join(image.DOFade(0f, fadeOutDuration));
                }
            }

            // Phase 2 (fadeAllTogetherOnClose OFF): only once the fade above has fully finished
            // does moveToTargetImage fly (optionally scaling at the same time) to
            // moveToTargetDestination. Fading all together skips this entirely - moveToTargetImage
            // sits under panelCanvasGroup, so it already faded out with everything else above.
            if (!fadeAllTogetherOnClose &&
                moveToTargetImage != null &&
                moveToTargetDestination != null)
            {
                Sequence moveStep = DOTween.Sequence();
                moveStep.Join(moveToTargetImage.DOMove(moveToTargetDestination.position, moveToTargetDuration).SetEase(moveToTargetEase));

                if (scaleWhileMovingToTarget)
                {
                    moveStep.Join(moveToTargetImage.DOScale(moveToTargetScale, moveToTargetDuration).SetEase(moveToTargetEase));
                }

                moveStep.OnComplete(() => OnMoveToTargetArrived.Invoke());
                seq.Append(moveStep);
            }

            seq.OnComplete(() =>
            {
                // Fading all together has no "arrival" beat of its own - fire the event as the
                // popup finishes instead, so listeners behave the same in both modes rather than
                // silently never being called in this one.
                if (fadeAllTogetherOnClose)
                {
                    OnMoveToTargetArrived.Invoke();
                }

                if (panelRoot != null)
                {
                    panelRoot.SetActive(false);
                }

                InvokePendingComplete();
            });

            activeSequence = seq;
        }
    }
}
