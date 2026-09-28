using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.Leaderboard
{
    /// <summary>
    /// The victory stage of the results screen: three podiums rising, the player's own avatar
    /// dropping onto the one they finished on, and the headline counting how many times they have
    /// managed it.
    ///
    /// Owns only the MOTION. The popup decides when to play it and what it says; the confetti and
    /// sparkles are whatever the designer drops under confettiRoot/sparkleRoot, switched on at the
    /// moment the winner lands rather than recreated here - a particle system authored in the
    /// editor will always look better than one described in code.
    ///
    /// Everything is driven from authored resting positions captured once in Awake, so the stage
    /// can replay any number of times without drifting away from how it was laid out.
    /// </summary>
    public sealed class LeaderboardRewardCelebrationView : MonoBehaviour
    {
        [Header("Podium")]
        // Index 0 = 1st place. They rise from below into these authored positions.
        [SerializeField] private RectTransform[] podiums = new RectTransform[3];
        [SerializeField] private float podiumRiseDistance = 320f;
        [SerializeField] private float podiumRiseDuration = 0.34f;
        [SerializeField] private float podiumStagger = 0.08f;
        [SerializeField]
        private AnimationCurve podiumRiseCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.72f, 1.06f),
            new Keyframe(1f, 1f));

        [Header("Winner")]
        // Moved between the anchors below, so it MUST share their parent - anchoredPosition is
        // only comparable between siblings.
        [SerializeField] private RectTransform winnerRoot;
        // Where the avatar stands for each place; index 0 = 1st. Same order as podiums.
        [SerializeField] private RectTransform[] winnerAnchors = new RectTransform[3];
        [SerializeField] private Image winnerAvatarImage;
        [SerializeField] private Image winnerFrameImage;
        [SerializeField] private float winnerDropHeight = 520f;
        [SerializeField] private float winnerDropDuration = 0.36f;
        [SerializeField]
        private AnimationCurve winnerDropCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1f, 1f));

        [Header("Landing")]
        // Squash on impact, then back. Sells the weight of the drop far more cheaply than a
        // bounce-and-settle would.
        [SerializeField] private float landSquashY = 0.82f;
        [SerializeField] private float landSquashX = 1.12f;
        [SerializeField] private float landSquashDuration = 0.16f;

        [Header("Idle bob")]
        [SerializeField] private float bobAmplitude = 14f;
        [SerializeField] private float bobPeriodSeconds = 1.8f;

        [Header("Headline")]
        [SerializeField] private RectTransform headlineRoot;
        [SerializeField] private TMP_Text headlineLabel;
        [SerializeField] private CanvasGroup headlineGroup;
        [SerializeField] private float headlineDropDistance = 90f;
        [SerializeField] private float headlineDuration = 0.3f;

        [Header("Effects")]
        // Switched on as the winner lands, off when the stage resets. Fill with a particle system,
        // an Animator, or anything else that plays on enable.
        [SerializeField] private GameObject confettiRoot;
        [SerializeField] private GameObject sparkleRoot;

        private Vector2[] podiumRestingPositions;
        private Vector2 headlineRestingPosition;
        private Vector2 winnerRestingPosition;
        private Coroutine playRoutine;
        private Coroutine bobRoutine;
        private bool captured;

        private void Awake()
        {
            CaptureRestingPositions();
        }

        /// <summary>
        /// Remembers where everything was authored, once. Guarded rather than done blindly in
        /// Awake alone, because Play can be reached from OnEnable on the same frame the popup is
        /// activated and the order of the two is not worth relying on.
        /// </summary>
        private void CaptureRestingPositions()
        {
            if (captured)
            {
                return;
            }

            captured = true;

            podiumRestingPositions = new Vector2[podiums != null ? podiums.Length : 0];
            for (int index = 0; index < podiumRestingPositions.Length; index++)
            {
                podiumRestingPositions[index] = podiums[index] != null ? podiums[index].anchoredPosition : Vector2.zero;
            }

            if (headlineRoot != null)
            {
                headlineRestingPosition = headlineRoot.anchoredPosition;
            }
        }

        /// <summary>
        /// Plays the whole entrance for a finish at <paramref name="rank"/>.
        ///
        /// Safe to call again while one is running - the previous pass is stopped and everything
        /// is reset first, so a replay never starts from half-animated state.
        /// </summary>
        public void Play(int rank, Sprite avatar, Sprite frame, string headline)
        {
            CaptureRestingPositions();
            StopAndReset();

            if (headlineLabel != null)
            {
                headlineLabel.text = headline;
            }

            SetSprite(winnerAvatarImage, avatar);
            SetSprite(winnerFrameImage, frame);

            // Index 0 is 1st place. Anything outside the authored anchors stands on the top one -
            // the results screen only opens for a placement that paid, so this is a guard rather
            // than a real case.
            int slot = Mathf.Clamp(rank - 1, 0, Mathf.Max(0, (winnerAnchors != null ? winnerAnchors.Length : 1) - 1));
            winnerRestingPosition = ResolveAnchorPosition(slot);

            if (!isActiveAndEnabled)
            {
                // Nothing can run a coroutine here; land everything in its final state so a hidden
                // stage still reads correctly the instant it is shown.
                ApplyFinalState();
                return;
            }

            playRoutine = StartCoroutine(PlayRoutine());
        }

        /// <summary>Stops everything and puts the stage back to how it was authored, ready to replay.</summary>
        public void StopAndReset()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }

            if (bobRoutine != null)
            {
                StopCoroutine(bobRoutine);
                bobRoutine = null;
            }

            CaptureRestingPositions();

            for (int index = 0; index < podiumRestingPositions.Length; index++)
            {
                if (podiums[index] != null)
                {
                    podiums[index].anchoredPosition = podiumRestingPositions[index];
                }
            }

            if (headlineRoot != null)
            {
                headlineRoot.anchoredPosition = headlineRestingPosition;
            }

            if (headlineGroup != null)
            {
                headlineGroup.alpha = 1f;
            }

            if (winnerRoot != null)
            {
                winnerRoot.localScale = Vector3.one;
            }

            SetActive(confettiRoot, false);
            SetActive(sparkleRoot, false);
        }

        private IEnumerator PlayRoutine()
        {
            // Everything starts off-stage in one pass before the first thing moves, so nothing is
            // caught sitting in its final position for a frame.
            for (int index = 0; index < podiumRestingPositions.Length; index++)
            {
                if (podiums[index] != null)
                {
                    podiums[index].anchoredPosition = podiumRestingPositions[index] + Vector2.down * podiumRiseDistance;
                }
            }

            if (headlineRoot != null)
            {
                headlineRoot.anchoredPosition = headlineRestingPosition + Vector2.up * headlineDropDistance;
            }

            if (headlineGroup != null)
            {
                headlineGroup.alpha = 0f;
            }

            if (winnerRoot != null)
            {
                winnerRoot.anchoredPosition = winnerRestingPosition + Vector2.up * winnerDropHeight;
                winnerRoot.localScale = Vector3.one;
            }

            yield return null;

            // Sides first, then the winner's own podium last - it is the one the eye should end on.
            StartCoroutine(RisePodiums());
            StartCoroutine(DropHeadline());

            float podiumTotal = podiumRiseDuration + podiumStagger * Mathf.Max(0, podiumRestingPositions.Length - 1);
            yield return WaitUnscaled(podiumTotal * 0.6f);

            yield return DropWinner();

            SetActive(confettiRoot, true);
            SetActive(sparkleRoot, true);

            yield return Squash();

            bobRoutine = StartCoroutine(BobRoutine());
            playRoutine = null;
        }

        private IEnumerator RisePodiums()
        {
            for (int order = 0; order < podiumRestingPositions.Length; order++)
            {
                // 1st place is index 0 but should arrive LAST, so the stage builds up to it.
                int index = podiumRestingPositions.Length - 1 - order;
                if (podiums[index] != null)
                {
                    StartCoroutine(RiseOne(podiums[index], podiumRestingPositions[index]));
                }

                if (podiumStagger > 0f)
                {
                    yield return WaitUnscaled(podiumStagger);
                }
            }
        }

        private IEnumerator RiseOne(RectTransform rect, Vector2 resting)
        {
            Vector2 from = resting + Vector2.down * podiumRiseDistance;
            float duration = Mathf.Max(0.01f, podiumRiseDuration);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                rect.anchoredPosition = Vector2.LerpUnclamped(from, resting, Evaluate(podiumRiseCurve, t));
                yield return null;
            }

            rect.anchoredPosition = resting;
        }

        private IEnumerator DropHeadline()
        {
            if (headlineRoot == null && headlineGroup == null)
            {
                yield break;
            }

            Vector2 from = headlineRestingPosition + Vector2.up * headlineDropDistance;
            float duration = Mathf.Max(0.01f, headlineDuration);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);

                if (headlineRoot != null)
                {
                    headlineRoot.anchoredPosition = Vector2.LerpUnclamped(from, headlineRestingPosition, Evaluate(podiumRiseCurve, t));
                }

                if (headlineGroup != null)
                {
                    headlineGroup.alpha = t;
                }

                yield return null;
            }

            if (headlineRoot != null)
            {
                headlineRoot.anchoredPosition = headlineRestingPosition;
            }

            if (headlineGroup != null)
            {
                headlineGroup.alpha = 1f;
            }
        }

        private IEnumerator DropWinner()
        {
            if (winnerRoot == null)
            {
                yield break;
            }

            Vector2 from = winnerRestingPosition + Vector2.up * winnerDropHeight;
            float duration = Mathf.Max(0.01f, winnerDropDuration);

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                // Squared, so it falls the way something dropped falls - slow at the top, fast into
                // the podium - rather than sliding down at a constant rate.
                float eased = Evaluate(winnerDropCurve, t);
                winnerRoot.anchoredPosition = Vector2.LerpUnclamped(from, winnerRestingPosition, eased * eased);
                yield return null;
            }

            winnerRoot.anchoredPosition = winnerRestingPosition;
        }

        private IEnumerator Squash()
        {
            if (winnerRoot == null)
            {
                yield break;
            }

            var squashed = new Vector3(landSquashX, landSquashY, 1f);
            float half = Mathf.Max(0.01f, landSquashDuration) * 0.5f;

            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                winnerRoot.localScale = Vector3.LerpUnclamped(Vector3.one, squashed, elapsed / half);
                yield return null;
            }

            for (float elapsed = 0f; elapsed < half; elapsed += Time.unscaledDeltaTime)
            {
                winnerRoot.localScale = Vector3.LerpUnclamped(squashed, Vector3.one, elapsed / half);
                yield return null;
            }

            winnerRoot.localScale = Vector3.one;
        }

        /// <summary>
        /// The idle bob that keeps the stage alive while the player reads it. Runs until the popup
        /// resets the view, which is what StopAndReset is for.
        /// </summary>
        private IEnumerator BobRoutine()
        {
            if (winnerRoot == null || bobAmplitude <= 0f || bobPeriodSeconds <= 0f)
            {
                yield break;
            }

            float elapsed = 0f;
            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                float phase = Mathf.Sin(elapsed / bobPeriodSeconds * Mathf.PI * 2f);
                winnerRoot.anchoredPosition = winnerRestingPosition + Vector2.up * (phase * bobAmplitude);
                yield return null;
            }
        }

        private void ApplyFinalState()
        {
            for (int index = 0; index < podiumRestingPositions.Length; index++)
            {
                if (podiums[index] != null)
                {
                    podiums[index].anchoredPosition = podiumRestingPositions[index];
                }
            }

            if (headlineRoot != null)
            {
                headlineRoot.anchoredPosition = headlineRestingPosition;
            }

            if (headlineGroup != null)
            {
                headlineGroup.alpha = 1f;
            }

            if (winnerRoot != null)
            {
                winnerRoot.anchoredPosition = winnerRestingPosition;
                winnerRoot.localScale = Vector3.one;
            }
        }

        private Vector2 ResolveAnchorPosition(int slot)
        {
            if (winnerAnchors != null && slot >= 0 && slot < winnerAnchors.Length && winnerAnchors[slot] != null)
            {
                return winnerAnchors[slot].anchoredPosition;
            }

            // No anchor authored for that place: stay where the winner already is rather than
            // teleporting to the parent's origin.
            return winnerRoot != null ? winnerRoot.anchoredPosition : Vector2.zero;
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            // An enabled Image with no sprite draws a white box; same rule as the board rows.
            image.enabled = sprite != null;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static float Evaluate(AnimationCurve curve, float t)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            if (seconds > 0f)
            {
                // Unscaled throughout: the results screen runs over a paused menu.
                yield return new WaitForSecondsRealtime(seconds);
            }
        }

        private void OnDisable()
        {
            StopAndReset();
        }
    }
}
