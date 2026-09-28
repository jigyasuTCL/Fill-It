using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// One prize in the results popup - its icon, what it is, and how many.
    ///
    /// Purely presentational. It never grants anything and never asks what kind of reward it is
    /// holding; the popup resolves the sprite and the wording and hands them over finished.
    ///
    /// It does own its own pop-in, though, because the popup only knows WHEN each row should
    /// appear - the how belongs next to the art it is animating.
    /// </summary>
    public sealed class LeaderboardRewardRowView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text labelText;
        /// <summary>Optional separate amount chip, for art that shows "x500" apart from the name.</summary>
        [SerializeField] private TMP_Text amountText;

        [Header("Appear")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float appearScaleFrom = 0.6f;
        [SerializeField]
        private AnimationCurve appearCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            // Overshoots past 1 and settles - the same ease-back the popups use, so a prize lands
            // with a little weight instead of fading in.
            new Keyframe(0.7f, 1.08f),
            new Keyframe(1f, 1f));

        private Coroutine appearRoutine;
        private RectTransform cachedRect;

        // Resolved lazily rather than in Awake: a pooled row can be bound and prepared while its
        // object is still inactive, and Awake would not have run yet.
        private RectTransform Rect => cachedRect != null ? cachedRect : cachedRect = transform as RectTransform;

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
        }

        public void Bind(Sprite icon, string label, int amount)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                // Disabled rather than left blank: an enabled Image with no sprite draws a white
                // box the size of its rect.
                iconImage.enabled = icon != null;
            }

            if (labelText != null)
            {
                labelText.text = label;
            }

            if (amountText != null)
            {
                // Hidden for a reward that is one item rather than a quantity - "x1" on a frame
                // reads as a mistake.
                bool showsAmount = amount > 1;
                amountText.text = showsAmount ? "x" + amount : string.Empty;
                amountText.gameObject.SetActive(showsAmount);
            }
        }

        /// <summary>
        /// Hides the row ready for its entrance. Separate from PlayAppear so every row can be
        /// hidden in one pass before the first one starts - otherwise the later rows sit fully
        /// visible while they wait their turn.
        /// </summary>
        public void PrepareAppear()
        {
            StopAppear();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (Rect != null)
            {
                Rect.localScale = Vector3.one * Mathf.Max(0.01f, appearScaleFrom);
            }
        }

        public void PlayAppear(float delaySeconds, float durationSeconds)
        {
            StopAppear();

            if (!isActiveAndEnabled)
            {
                // Nothing can run a coroutine on a disabled object; show it outright rather than
                // leaving it stuck at zero alpha.
                Show();
                return;
            }

            appearRoutine = StartCoroutine(AppearRoutine(delaySeconds, durationSeconds));
        }

        private IEnumerator AppearRoutine(float delaySeconds, float durationSeconds)
        {
            if (delaySeconds > 0f)
            {
                // Unscaled throughout: results are shown over a paused menu.
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            float duration = Mathf.Max(0.01f, durationSeconds);
            Vector3 from = Vector3.one * Mathf.Max(0.01f, appearScaleFrom);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = appearCurve != null && appearCurve.length > 0 ? appearCurve.Evaluate(t) : t;

                if (Rect != null)
                {
                    Rect.localScale = Vector3.LerpUnclamped(from, Vector3.one, eased);
                }

                if (canvasGroup != null)
                {
                    // Alpha tracks t, not the eased value - an overshooting curve would drive it
                    // past 1 and clip.
                    canvasGroup.alpha = t;
                }

                yield return null;
            }

            Show();
            appearRoutine = null;
        }

        private void Show()
        {
            if (Rect != null)
            {
                Rect.localScale = Vector3.one;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
        }

        private void StopAppear()
        {
            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
                appearRoutine = null;
            }
        }

        private void OnDisable()
        {
            StopAppear();
        }
    }
}
