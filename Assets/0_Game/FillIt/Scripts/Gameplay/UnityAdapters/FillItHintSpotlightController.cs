using System.Collections;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Dims the whole screen except a window over the active hint's cell, using four opaque
    /// strips sized from that cell's on-screen rect. Purely a UI reaction to
    /// FillItGameplayManager's hint state - it does not decide when a hint is active.
    /// </summary>
    public sealed class FillItHintSpotlightController : MonoBehaviour
    {
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private FillItBoardView boardView;
        [SerializeField] private RectTransform topStrip;
        [SerializeField] private RectTransform bottomStrip;
        [SerializeField] private RectTransform leftStrip;
        [SerializeField] private RectTransform rightStrip;
        [SerializeField] private float screenPaddingPixels = 14f;
        [SerializeField] private float overlayFadeDurationSeconds = 0.2f;

        // Auto-resolved (and added if missing) from overlayRoot in Awake - no prefab wiring
        // needed for the fade to work.
        private CanvasGroup overlayCanvasGroup;
        private Coroutine overlayFadeRoutine;

        private void Awake()
        {
            if (overlayRoot == null)
            {
                return;
            }

            overlayCanvasGroup = overlayRoot.GetComponent<CanvasGroup>();
            if (overlayCanvasGroup == null)
            {
                overlayCanvasGroup = overlayRoot.AddComponent<CanvasGroup>();
            }
        }

        public void Show(int row, int column)
        {
            if (overlayRoot == null || boardView == null || !boardView.TryGetCellWorldBounds(row, column, out Bounds bounds))
            {
                Hide();
                return;
            }

            Camera camera = boardView.InputCamera;
            if (camera == null)
            {
                Hide();
                return;
            }

            Vector3 screenMin = camera.WorldToScreenPoint(bounds.min);
            Vector3 screenMax = camera.WorldToScreenPoint(bounds.max);
            float minX = Mathf.Min(screenMin.x, screenMax.x) - screenPaddingPixels;
            float maxX = Mathf.Max(screenMin.x, screenMax.x) + screenPaddingPixels;
            float minY = Mathf.Min(screenMin.y, screenMax.y) - screenPaddingPixels;
            float maxY = Mathf.Max(screenMin.y, screenMax.y) + screenPaddingPixels;

            float left = Mathf.Clamp01(minX / Screen.width);
            float right = Mathf.Clamp01(maxX / Screen.width);
            float bottom = Mathf.Clamp01(minY / Screen.height);
            float top = Mathf.Clamp01(maxY / Screen.height);

            SetStripAnchors(leftStrip, new Vector2(0f, 0f), new Vector2(left, 1f));
            SetStripAnchors(rightStrip, new Vector2(right, 0f), new Vector2(1f, 1f));
            SetStripAnchors(bottomStrip, new Vector2(left, 0f), new Vector2(right, bottom));
            SetStripAnchors(topStrip, new Vector2(left, top), new Vector2(right, 1f));

            // Re-showing on an already-visible overlay (the hint target moved) just keeps it
            // fully opaque rather than replaying the fade-in from scratch.
            bool wasHidden = !overlayRoot.activeSelf;
            overlayRoot.SetActive(true);
            if (wasHidden)
            {
                FadeOverlay(0f, 1f, null);
            }
            else if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 1f;
            }
        }

        public void Hide()
        {
            if (overlayRoot == null || !overlayRoot.activeSelf)
            {
                return;
            }

            if (overlayCanvasGroup == null)
            {
                overlayRoot.SetActive(false);
                return;
            }

            FadeOverlay(overlayCanvasGroup.alpha, 0f, () => overlayRoot.SetActive(false));
        }

        private void FadeOverlay(float from, float to, System.Action onComplete)
        {
            if (overlayCanvasGroup == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (overlayFadeRoutine != null)
            {
                StopCoroutine(overlayFadeRoutine);
            }

            overlayFadeRoutine = StartCoroutine(FadeOverlayRoutine(from, to, onComplete));
        }

        private IEnumerator FadeOverlayRoutine(float from, float to, System.Action onComplete)
        {
            overlayCanvasGroup.alpha = from;
            float duration = Mathf.Max(0.01f, overlayFadeDurationSeconds);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                overlayCanvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            overlayCanvasGroup.alpha = to;
            overlayFadeRoutine = null;
            onComplete?.Invoke();
        }

        private static void SetStripAnchors(RectTransform strip, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (strip == null)
            {
                return;
            }

            strip.anchorMin = anchorMin;
            strip.anchorMax = anchorMax;
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = Vector2.zero;
        }
    }
}
