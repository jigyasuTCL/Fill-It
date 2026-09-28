using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shared.GameFlow
{
    /// <summary>
    /// Generic press-feedback for any UI button: scales down while held, then eases back up to its
    /// original scale on release, ease-out-back the same way PopupScaleAnimator's open animation
    /// does. Add alongside a Button/RectTransform - no wiring beyond that is required.
    ///
    /// If the button's GameObject is disabled mid-press (e.g. the popup it lives on closes while
    /// the player is still holding it down), the pending release animation never gets to run.
    /// OnEnable force-resets the scale back to original instantly instead of leaving it stuck
    /// scaled down the next time the button becomes visible again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonPressScaleFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private RectTransform target;
        [Tooltip("Optional. If assigned, press/click feedback (scale + sound) is skipped while button.interactable is false. Auto-resolved from this GameObject if left unassigned.")]
        [SerializeField] private Button button;
        [SerializeField, Range(0.01f, 0.99f)] private float pressedScaleFactor = 0.85f;
        [SerializeField] private float pressDurationSeconds = 0.08f;
        [SerializeField] private float releaseDurationSeconds = 0.18f;

        private Vector3 originalScale = Vector3.one;
        private bool originalScaleCaptured;
        private Coroutine routine;
        private bool pressed;

        // Captured at OnPointerDown, BEFORE this click is handled - Button's own OnPointerClick
        // (invoking onClick, which often sets button.interactable = false to block a double-tap,
        // e.g. LevelWinUIRoot's Continue/2x buttons) fires first (both it and this component
        // implement IPointerClickHandler on the same GameObject, and Button's wins the ordering),
        // so by the time OUR OnPointerClick below runs, a fresh IsInteractable check would already
        // read false for a perfectly valid click and skip the sound. Using the state from press-
        // down instead means a genuine click always gets its sound, even if the click's own
        // handler immediately disabled the button.
        private bool wasInteractableOnPress;

        // True while the press-down or release-back scale tween is still playing - lets
        // other scripts (e.g. disabling this button after a click) wait for the feedback
        // to finish instead of cutting it off mid-animation.
        public bool IsAnimating => routine != null;

        private void Awake()
        {
            if (target == null)
            {
                target = transform as RectTransform;
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }

            CaptureOriginalScale();
        }

        // No button assigned/found means this feedback isn't tied to a Selectable at all (e.g. a
        // plain tappable icon) - always animate in that case, same as before this field existed.
        private bool IsInteractable => button == null || button.interactable;


        // True only when this component's OWN press/release animation was still running (or a
        // press was mid-hold) the moment the GameObject got disabled - e.g. its popup closed while
        // the player was still holding the button down. Unity kills the coroutine outright with no
        // cleanup in that case, so without tracking this the button could reopen visually stuck
        // mid-press-scale. Deliberately NOT set for an ordinary close/reopen with nothing mid-press
        // - forcing a reset then would fight an owning popup's own entrance animation, which starts
        // buttons at zero and scales them up on every open (see LevelWinPopupController.
        // ResetVisualState/PlayButtonsSequence) - see the "not playing after 1st time" regression
        // this caused when OnEnable forced a reset unconditionally.
        private bool needsForceResetOnEnable;

        private void OnDisable()
        {
            needsForceResetOnEnable = pressed || routine != null;
        }

        private void OnEnable()
        {
            pressed = false;
            routine = null;

            if (needsForceResetOnEnable && originalScaleCaptured && target != null)
            {
                target.localScale = originalScale;
            }

            needsForceResetOnEnable = false;
        }

        // Skips capturing a near-zero scale as "original" - that's not this button's real resting
        // size, just a popup's entrance animation caught mid-flight (starts buttons at
        // Vector3.zero and scales them up, see LevelWinPopupController.ResetVisualState/
        // PlayButtonsSequence). Locking that in here would make every future press/release
        // animate toward/from zero, i.e. the button visibly vanishes on tap. Re-tried on every
        // press so it always ends up capturing the button's actual settled scale.
        private void CaptureOriginalScale()
        {
            if (originalScaleCaptured || target == null)
            {
                return;
            }

            Vector3 currentScale = target.localScale;
            if (currentScale.sqrMagnitude < 0.0001f)
            {
                return;
            }

            originalScale = currentScale;
            originalScaleCaptured = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            wasInteractableOnPress = IsInteractable;

            if (!wasInteractableOnPress)
            {
                return;
            }

            // A real tap only ever lands once this button has visually settled - safe to trust
            // its current scale as "original" if a prior Awake/OnEnable couldn't (e.g. caught it
            // at zero mid an owning popup's entrance animation).
            CaptureOriginalScale();
            pressed = true;
            Play(originalScale * Mathf.Max(0.01f, pressedScaleFactor), pressDurationSeconds);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed)
            {
                return;
            }

            pressed = false;
            Play(originalScale, releaseDurationSeconds);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // A drag-off-then-release never fires OnPointerUp on this object - release the scale
            // as soon as the pointer leaves instead of leaving the button stuck scaled down.
            if (!pressed)
            {
                return;
            }

            pressed = false;
            Play(originalScale, releaseDurationSeconds);
        }

        // IPointerClickHandler only fires for a genuine click (pointer down, then up while
        // still over this object) - the single place the click sound plays, so every button
        // that has this component gets both the press feedback and the sound for free.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!wasInteractableOnPress)
            {
                return;
            }

            AudioManager.PlayClickSound();
        }

        private void Play(Vector3 to, float duration)
        {
            if (target == null)
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(ScaleRoutine(target.localScale, to, duration));
        }

        private IEnumerator ScaleRoutine(Vector3 from, Vector3 to, float durationSeconds)
        {
            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.localScale = to;
            routine = null;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }
    }
}
