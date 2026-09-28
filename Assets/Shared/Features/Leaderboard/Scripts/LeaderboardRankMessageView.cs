using System.Collections;
using TMPro;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// The "you moved up/down the board" banner: slides in from off the left edge, holds long
    /// enough to read, then carries on out past the right edge. One continuous left-to-right pass
    /// rather than entering and leaving the same way, so it reads as a notification travelling
    /// through rather than a popup opening and closing.
    ///
    /// Knows nothing about the leaderboard itself - LeaderboardPanelController decides when a rank
    /// actually changed and whether that direction is enabled, and just calls Show.
    ///
    /// Put this component on an object that STAYS ACTIVE (the panel root is fine) and point
    /// containerRoot at the child that actually holds the banner art - the slide runs on a
    /// coroutine, which can't start on an inactive GameObject.
    /// </summary>
    public sealed class LeaderboardRankMessageView : MonoBehaviour
    {
        // The RectTransform that actually travels. Author it at its RESTING position (where it
        // should sit while being read) - the off-screen start/end points are derived from that,
        // so nothing needs hand-placing off-screen in the prefab.
        [SerializeField] private RectTransform container;
        // Toggled off between messages so the banner isn't sitting invisible-but-present over the
        // board, eating raycasts. Defaults to container's own GameObject when unassigned.
        [SerializeField] private GameObject containerRoot;
        [SerializeField] private TextMeshProUGUI messageLabel;
        // Optional direction art - an up arrow and a down arrow, shown one at a time.
        [SerializeField] private GameObject climbIconRoot;
        [SerializeField] private GameObject fallIconRoot;

        [Header("Text")]
        // {0} = the new rank, {1} = how many places moved (always positive).
        [SerializeField] private string climbMessageFormat = "You climbed to #{0}!";
        [SerializeField] private string fallMessageFormat = "You slipped to #{0}";

        [Header("Timing")]
        [SerializeField] private float slideInDurationSeconds = 0.35f;
        // How long it sits still at the resting position before leaving.
        [SerializeField] private float holdSeconds = 1.8f;
        [SerializeField] private float slideOutDurationSeconds = 0.3f;
        // Added on top of the computed off-screen distance. The computed value already clears the
        // parent's edge; this is headroom for banner art that overhangs its own RectTransform
        // (shadows, glow, a tail), which would otherwise still be poking in at rest.
        [SerializeField] private float extraOffscreenPaddingPixels = 40f;

        // Captured on first use rather than in Awake, so it always records the authored resting
        // position - before anything here has moved the container. Same lazy-capture reasoning as
        // FtueBannerController.CaptureOriginalTransformIfNeeded.
        private Vector2 restPosition;
        private bool restCaptured;
        private Coroutine routine;

        public bool IsShowing => routine != null;

        /// <summary>
        /// Plays one pass. climbed picks the wording and the direction icon; the caller owns
        /// deciding what counts as a climb (see LeaderboardPanelController - a climb is the rank
        /// NUMBER going down). A message already in flight is replaced rather than queued: the
        /// newer rank is the true one, and letting them stack would leave the banner running long
        /// after the change it describes.
        /// </summary>
        public void Show(int newRank, int placesMoved, bool climbed)
        {
            if (container == null)
            {
                return;
            }

            CaptureRestPositionIfNeeded();

            if (messageLabel != null)
            {
                string format = climbed ? climbMessageFormat : fallMessageFormat;
                messageLabel.text = string.IsNullOrEmpty(format)
                    ? newRank.ToString()
                    : string.Format(format, newRank, Mathf.Abs(placesMoved));
            }

            if (climbIconRoot != null)
            {
                climbIconRoot.SetActive(climbed);
            }

            if (fallIconRoot != null)
            {
                fallIconRoot.SetActive(!climbed);
            }

            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            GameObject root = ResolveContainerRoot();
            root.SetActive(true);

            if (!isActiveAndEnabled)
            {
                // Nothing can animate from here - land it at rest so the message is at least
                // readable rather than stranded off-screen.
                container.anchoredPosition = restPosition;
                return;
            }

            routine = StartCoroutine(PassRoutine());
        }

        /// <summary>Cuts any message short and parks the banner - e.g. when the panel closes mid-pass.</summary>
        public void HideImmediate()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (container != null && restCaptured)
            {
                container.anchoredPosition = restPosition;
            }

            ResolveContainerRoot()?.SetActive(false);
        }

        private IEnumerator PassRoutine()
        {
            float travel = ResolveTravelDistance();
            Vector2 entryPosition = restPosition + Vector2.left * travel;
            Vector2 exitPosition = restPosition + Vector2.right * travel;

            container.anchoredPosition = entryPosition;
            yield return SlideRoutine(entryPosition, restPosition, slideInDurationSeconds, EaseOutCubic);

            // Unscaled: the board is a popup and can be open while gameplay is paused.
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds));

            yield return SlideRoutine(restPosition, exitPosition, slideOutDurationSeconds, EaseInCubic);

            // Parked back at rest, not left sitting off to the right: the next Show starts by
            // placing it at the entry point anyway, and leaving a stray rect way outside the
            // canvas confuses layout debugging for no benefit.
            container.anchoredPosition = restPosition;
            ResolveContainerRoot()?.SetActive(false);
            routine = null;
        }

        private IEnumerator SlideRoutine(Vector2 from, Vector2 to, float durationSeconds, System.Func<float, float> ease)
        {
            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                container.anchoredPosition = Vector2.LerpUnclamped(from, to, ease(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            container.anchoredPosition = to;
        }

        /// <summary>
        /// Far enough that the banner is fully clear of the parent on either side, whatever the
        /// resolution - half the parent's width (to reach its edge) plus half the banner's own
        /// width (to clear it completely) plus however far off-centre it rests.
        /// </summary>
        private float ResolveTravelDistance()
        {
            float parentWidth = container.parent is RectTransform parentRect && parentRect.rect.width > 0f
                ? parentRect.rect.width
                : Screen.width;

            return parentWidth * 0.5f
                + container.rect.width * 0.5f
                + Mathf.Abs(restPosition.x)
                + Mathf.Max(0f, extraOffscreenPaddingPixels);
        }

        private void CaptureRestPositionIfNeeded()
        {
            if (restCaptured)
            {
                return;
            }

            restPosition = container.anchoredPosition;
            restCaptured = true;
        }

        private GameObject ResolveContainerRoot()
        {
            if (containerRoot != null)
            {
                return containerRoot;
            }

            return container != null ? container.gameObject : null;
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }
    }
}
