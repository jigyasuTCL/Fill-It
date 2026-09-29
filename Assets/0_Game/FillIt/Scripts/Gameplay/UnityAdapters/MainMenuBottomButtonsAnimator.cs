using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace CatFlap.UnityAdapters
{
    public sealed class MainMenuBottomButtonsAnimator : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private float slideDuration = 0.3f;
        [SerializeField] private float slideDistance = 250f;

        private Vector2 restingPosition;
        private bool positionCaptured;
        private Coroutine routine;

        private void Awake()
        {
            if (panel == null)
            {
                panel = transform as RectTransform;
            }

            CaptureRestingPosition();
        }

        public void PlaySlideIn(bool immediate, UnityAction onComplete = null)
        {
            CaptureRestingPosition();
            gameObject.SetActive(true);

            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (panel == null || immediate)
            {
                if (panel != null)
                {
                    panel.anchoredPosition = restingPosition;
                }

                onComplete?.Invoke();
                return;
            }

            panel.anchoredPosition = restingPosition - new Vector2(0f, Mathf.Abs(slideDistance));
            routine = StartCoroutine(SlideRoutine(panel.anchoredPosition, restingPosition, onComplete));
        }

        public void PlaySlideOut(bool immediate, UnityAction onComplete = null)
        {
            CaptureRestingPosition();

            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (panel == null)
            {
                gameObject.SetActive(false);
                onComplete?.Invoke();
                return;
            }

            Vector2 to = restingPosition - new Vector2(0f, Mathf.Abs(slideDistance));
            if (immediate)
            {
                panel.anchoredPosition = to;
                gameObject.SetActive(false);
                onComplete?.Invoke();
                return;
            }

            routine = StartCoroutine(SlideRoutine(panel.anchoredPosition, to, () =>
            {
                gameObject.SetActive(false);
                onComplete?.Invoke();
            }));
        }

        private void CaptureRestingPosition()
        {
            if (positionCaptured || panel == null)
            {
                return;
            }

            restingPosition = panel.anchoredPosition;
            positionCaptured = true;
        }

        private IEnumerator SlideRoutine(Vector2 from, Vector2 to, UnityAction onComplete)
        {
            float seconds = Mathf.Max(0.01f, slideDuration);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutCubic(Mathf.Clamp01(elapsed / seconds));
                panel.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
                yield return null;
            }

            panel.anchoredPosition = to;
            routine = null;
            onComplete?.Invoke();
        }

        private static float EaseOutCubic(float t)
        {
            float value = 1f - t;
            return 1f - value * value * value;
        }
    }
}
