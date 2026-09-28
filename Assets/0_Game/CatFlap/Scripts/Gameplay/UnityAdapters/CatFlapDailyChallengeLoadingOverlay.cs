using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// The "level start" cover shown for a few seconds right after a Daily Challenge sub-level
    /// loads, then fades out to reveal the already-loaded gameplay behind it - a fake-loading
    /// beat, not a real load (the level is local JSON and loads instantly).
    /// </summary>
    public sealed class CatFlapDailyChallengeLoadingOverlay : MonoBehaviour
    {
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text subLevelLabel;
        [SerializeField] private float fadeInSeconds = 0.2f;
        [SerializeField] private float holdSeconds = 3f;
        [SerializeField] private float fadeSeconds = 0.4f;
        [SerializeField] private string[] subLevelNames = { "Easy", "Medium", "Hard" };

        private Coroutine routine;

        public void Show(int levelIndex, Action onComplete)
        {
            if (overlayRoot == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            if (subLevelLabel != null)
            {
                subLevelLabel.text = levelIndex >= 0 && subLevelNames != null && levelIndex < subLevelNames.Length
                    ? subLevelNames[levelIndex]
                    : $"Level {levelIndex + 1}";
            }

            overlayRoot.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            routine = StartCoroutine(FadeInHoldThenFadeOut(onComplete));
        }

        private IEnumerator FadeInHoldThenFadeOut(Action onComplete)
        {
            float inDuration = Mathf.Max(0.01f, fadeInSeconds);
            float inElapsed = 0f;
            while (inElapsed < inDuration)
            {
                inElapsed += Time.unscaledDeltaTime;
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Clamp01(inElapsed / inDuration);
                }

                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds));

            float duration = Mathf.Max(0.01f, fadeSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                }

                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            overlayRoot.SetActive(false);
            routine = null;
            onComplete?.Invoke();
        }
    }
}
