using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace Shared.GameFlow
{
    /// <summary>
    /// Small transient message that pops up FROM a given anchor (e.g. a locked bottom-nav icon)
    /// instead of opening a full popup - slides in (local Y) while fading in, holds, then fades
    /// out IN PLACE (no reverse move). Prefab-agnostic: this only drives whatever background/text
    /// the Inspector wires in, so any screen can reuse the same component for its own
    /// "locked"/info toasts.
    /// </summary>
    public sealed class ToastMessageController : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text messageText;

        [Header("Timing")]
        [SerializeField] private float moveInDuration = 0.25f;
        [SerializeField] private float holdDuration = 0.5f;
        [SerializeField] private float fadeOutDuration = 0.2f;

        [Tooltip("Local Y offset (RectTransform.anchoredPosition), added on top of the anchor's " +
                 "own resting position, that the toast slides to while fading in - e.g. -250 to " +
                 "rise from a bottom-nav icon up into view. The fade-out plays at this same " +
                 "position, with no further movement.")]
        [SerializeField] private float moveInOffsetY = -250f;

        private Coroutine activeRoutine;
        private Vector2 restingBasePosition;

        private void Awake()
        {
            if (root == null)
            {
                root = transform as RectTransform;
            }

            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            HideImmediate();
        }

        private void OnDisable()
        {
            // Unity kills PlaySequence outright (no cleanup) whenever this GameObject - or an
            // ancestor, e.g. the whole GamePlayUIRoot switching to the main menu - gets disabled
            // mid-animation. Without this, activeRoutine is left stuck non-null forever (silently
            // blocking every future Show() call via its own early-return guard above) and the
            // toast reappears frozen mid fade/slide instead of hidden the next time this becomes
            // active again.
            HideImmediate();
        }

        /// <summary>
        /// Slides in from anchor while fading in, holds for holdDuration, then fades out in
        /// place. Spam-tap proof: while already showing, further calls are ignored rather than
        /// restarting/stacking the animation - the toast just keeps running its own timer.
        /// </summary>
        public void Show(string message, Transform anchor)
        {
            if(!gameObject.activeSelf)
            gameObject.SetActive(true);
            if (root == null || activeRoutine != null)
            {
                return;
            }

            if (messageText != null)
            {
                messageText.text = message;
            }

            PositionAt(anchor);

            // Activates THIS component's own GameObject, not root's - StartCoroutine below needs
            // to run on this, and root may be a separate (e.g. child) RectTransform than the one
            // the script is actually attached to.
            gameObject.SetActive(true);
            activeRoutine = StartCoroutine(PlaySequence());
        }

        public void Hide()
        {
            HideImmediate();
        }

        private void PositionAt(Transform anchor)
        {
            if (anchor != null)
            {
                root.position = anchor.position;
            }

            restingBasePosition = root.anchoredPosition;
        }

        private IEnumerator PlaySequence()
        {
            Vector2 hiddenPos = restingBasePosition;
            Vector2 shownPos = restingBasePosition + new Vector2(0f, moveInOffsetY);

            root.anchoredPosition = hiddenPos;
            SetAlpha(0f);

            yield return Animate(moveInDuration, t =>
            {
                float eased = EaseOutCubic(t);
                SetAlpha(t);
                root.anchoredPosition = Vector2.LerpUnclamped(hiddenPos, shownPos, eased);
            });

            yield return new WaitForSeconds(Mathf.Max(0f, holdDuration));

            yield return Animate(fadeOutDuration, t => SetAlpha(1f - t));

            // Reset back to the hidden/base position (invisible already, alpha 0) so the next
            // Show() always slides the same distance regardless of where this one ended.
            root.anchoredPosition = hiddenPos;
            gameObject.SetActive(false);
            activeRoutine = null;
        }

        private void HideImmediate()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            SetAlpha(0f);
            gameObject.SetActive(false);
        }

        private void SetAlpha(float alpha)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }

        private static IEnumerator Animate(float duration, Action<float> onStep)
        {
            float seconds = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                onStep(Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }
            onStep(1f);
        }

        private static float EaseOutCubic(float t)
        {
            float f = t - 1f;
            return f * f * f + 1f;
        }
    }
}
