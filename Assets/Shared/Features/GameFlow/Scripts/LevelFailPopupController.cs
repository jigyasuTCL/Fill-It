using System.Collections;
using TMPro;
using UnityEngine;

namespace Shared.GameFlow
{
    public sealed class LevelFailPopupController : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private RectTransform popupPanel;
        [SerializeField] private TMP_Text beatLevelText;

        [Header("Fail Sequence")]
        [SerializeField] private CanvasGroup backgroundGroup;
        [SerializeField] private RectTransform titleImage;
        [SerializeField] private RectTransform catTransform;
        [SerializeField] private RectTransform buttonContainer;
        [SerializeField] private RectTransform homeButton;
        [SerializeField] private RectTransform coinContainer;

        [Header("Timing")]
        // Entry animation tuned 2x faster than original (durations halved, typewriter speed
        // doubled) - see LevelWinPopupController for the matching change.
        [SerializeField] private float backgroundFadeDuration = 0.125f;
        [SerializeField] private float catScaleDuration = 0.175f;
        [SerializeField] private float buttonMoveDuration = 0.2f;
        [SerializeField] private float coinMoveDuration = 0.175f;
        [SerializeField] private float typewriterCharsPerSecond = 40f;
        [SerializeField] private float offscreenPadding = 200f;
        [SerializeField] private Color beatLevelHighlightColor = new Color(1f, 0.54f, 0.4f);

        private bool hasPlayedOpen;
        private Coroutine openRoutine;

        private string pendingBeatLevelValue = string.Empty;

        private Vector2 buttonContainerHomePos;
        private Vector2 homeButtonHomePos;
        private Vector2 coinContainerHomePos;
        private bool cachedHomePositions;

        private void Awake()
        {
            CacheHomePositions();
            ResetVisualState();
        }

        private void OnEnable()
        {
            // Content set by CatFlapGameplayManager.HandleLevelFailed (see SetLevelResultText)
            // before this popup is shown - nothing to populate here.
            PlayOpen();
        }

        private void OnDisable()
        {
            hasPlayedOpen = false;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }
        }

        /// <summary>
        /// Levels 1..N get that level's own CatFlapLevelResultTextConfig fail line, in order; any
        /// level past the last configured entry gets a random one from the same list. Called by
        /// CatFlapGameplayManager.HandleLevelFailed before this popup is shown.
        /// </summary>
        public void SetLevelResultText(int levelNumber)
        {
            string rawText = CatFlapLevelResultTextConfig.Load()?.GetFailText(levelNumber);
            pendingBeatLevelValue = CatFlapLevelResultTextConfig.ApplyHighlight(rawText, beatLevelHighlightColor);

            // If the value changes after the popup has already finished opening,
            // just refresh the label immediately instead of retyping it.
            if (hasPlayedOpen && openRoutine == null)
            {
                SetText(beatLevelText, pendingBeatLevelValue);
            }
        }

        public void PlayOpen()
        {
            if (hasPlayedOpen)
            {
                return;
            }
            hasPlayedOpen = true;
            CacheHomePositions();
            ResetVisualState();
            openRoutine = StartCoroutine(PlayOpenSequence());
        }

        private IEnumerator PlayOpenSequence()
        {
            // 1. Background color fades in, then the title and cat pop up together
            //    with the same scale-up animation.
            yield return StartCoroutine(FadeInBackground());
            yield return StartCoroutine(RunParallel(
                ScaleUp(titleImage, catScaleDuration),
                ScaleUp(catTransform, catScaleDuration)
            ));

            // 2. Button container (revive + restart) slides up from the bottom,
            //    home slides in from the right, coin container slides in from the left.
            yield return StartCoroutine(RunParallel(
                MoveIn(buttonContainer, Vector2.down * offscreenPadding, buttonMoveDuration),
                MoveIn(homeButton, Vector2.right * offscreenPadding, buttonMoveDuration),
                MoveIn(coinContainer, Vector2.left * offscreenPadding, coinMoveDuration)
            ));

            // 3. "X% of players beat this level" types out.
            yield return StartCoroutine(Typewriter(beatLevelText, pendingBeatLevelValue));

            openRoutine = null;
        }

        // ---------- Individual effects ----------

        private IEnumerator FadeInBackground()
        {
            if (backgroundGroup == null)
            {
                yield break;
            }
            float elapsed = 0f;
            backgroundGroup.alpha = 0f;
            while (elapsed < backgroundFadeDuration)
            {
                elapsed += Time.deltaTime;
                backgroundGroup.alpha = Mathf.Clamp01(elapsed / backgroundFadeDuration);
                yield return null;
            }
            backgroundGroup.alpha = 1f;
        }

        private IEnumerator ScaleUp(RectTransform target, float duration)
        {
            if (target == null)
            {
                yield break;
            }
            target.localScale = Vector3.zero;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutBack(Mathf.Clamp01(elapsed / duration));
                target.localScale = Vector3.one * t;
                yield return null;
            }
            target.localScale = Vector3.one;
        }

        private IEnumerator MoveIn(RectTransform target, Vector2 offscreenOffset, float duration)
        {
            if (target == null)
            {
                yield break;
            }
            Vector2 homePos = GetHomePosition(target);
            Vector2 startPos = homePos + offscreenOffset;
            target.anchoredPosition = startPos;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutCubic(Mathf.Clamp01(elapsed / duration));
                target.anchoredPosition = Vector2.LerpUnclamped(startPos, homePos, t);
                yield return null;
            }
            target.anchoredPosition = homePos;
        }

        private IEnumerator Typewriter(TMP_Text label, string fullValue)
        {
            if (label == null)
            {
                yield break;
            }
            if (string.IsNullOrEmpty(fullValue))
            {
                label.text = string.Empty;
                yield break;
            }

            // Set the full (possibly rich-text) string up front, then reveal it
            // character-by-character via maxVisibleCharacters so tags like
            // <color=...> aren't split mid-tag while typing.
            label.text = fullValue;
            label.ForceMeshUpdate();
            int totalVisibleCharacters = label.textInfo.characterCount;
            label.maxVisibleCharacters = 0;

            float secondsPerChar = 1f / Mathf.Max(1f, typewriterCharsPerSecond);
            for (int visible = 1; visible <= totalVisibleCharacters; visible++)
            {
                label.maxVisibleCharacters = visible;
                yield return new WaitForSeconds(secondsPerChar);
            }
        }

        private IEnumerator RunParallel(params IEnumerator[] routines)
        {
            var running = new Coroutine[routines.Length];
            for (int index = 0; index < routines.Length; index++)
            {
                running[index] = StartCoroutine(routines[index]);
            }
            for (int index = 0; index < running.Length; index++)
            {
                if (running[index] != null)
                {
                    yield return running[index];
                }
            }
        }

        // ---------- Setup helpers ----------

        private void ResetVisualState()
        {
            if (backgroundGroup != null)
            {
                backgroundGroup.alpha = 0f;
            }
            if (titleImage != null)
            {
                titleImage.localScale = Vector3.zero;
            }
            if (catTransform != null)
            {
                catTransform.localScale = Vector3.zero;
            }
            SetText(beatLevelText, string.Empty);

            if (cachedHomePositions)
            {
                if (buttonContainer != null) buttonContainer.anchoredPosition = buttonContainerHomePos + Vector2.down * offscreenPadding;
                if (homeButton != null) homeButton.anchoredPosition = homeButtonHomePos + Vector2.right * offscreenPadding;
                if (coinContainer != null) coinContainer.anchoredPosition = coinContainerHomePos + Vector2.left * offscreenPadding;
            }
        }

        private void CacheHomePositions()
        {
            if (cachedHomePositions)
            {
                return;
            }
            if (buttonContainer != null) buttonContainerHomePos = buttonContainer.anchoredPosition;
            if (homeButton != null) homeButtonHomePos = homeButton.anchoredPosition;
            if (coinContainer != null) coinContainerHomePos = coinContainer.anchoredPosition;
            cachedHomePositions = true;
        }

        private Vector2 GetHomePosition(RectTransform target)
        {
            if (target == buttonContainer) return buttonContainerHomePos;
            if (target == homeButton) return homeButtonHomePos;
            if (target == coinContainer) return coinContainerHomePos;
            return target.anchoredPosition;
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
                label.maxVisibleCharacters = int.MaxValue;
            }
        }

        private static float EaseOutCubic(float t)
        {
            float f = t - 1f;
            return f * f * f + 1f;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float f = t - 1f;
            return 1f + c3 * f * f * f + c1 * f * f;
        }
    }
}