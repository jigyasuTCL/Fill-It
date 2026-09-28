using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Shared.GameFlow
{
    public sealed class PopupScaleAnimator : MonoBehaviour
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private float durationSeconds = 0.18f;

        private Coroutine routine;

        private void Awake()
        {
            if (target == null)
            {
                target = transform as RectTransform;
            }
        }

        public void SetTarget(RectTransform rectTransform)
        {
            target = rectTransform;
        }

        public void PlayOpen(UnityAction onComplete = null)
        {
            Play(Vector3.zero, Vector3.one, EaseBackOut, onComplete);
        }

        public void PlayClose(UnityAction onComplete = null)
        {
            Play(Vector3.one, Vector3.zero, EaseBackIn, onComplete);
        }

        private void Play(Vector3 from, Vector3 to, System.Func<float, float> ease, UnityAction onComplete)
        {
            if (target == null)
            {
                target = transform as RectTransform;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(ScaleRoutine(from, to, ease, onComplete));
        }

        private IEnumerator ScaleRoutine(Vector3 from, Vector3 to, System.Func<float, float> ease, UnityAction onComplete)
        {
            if (target == null)
            {
                onComplete?.Invoke();
                yield break;
            }

            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                target.localScale = Vector3.LerpUnclamped(from, to, ease(t));
                yield return null;
            }

            target.localScale = to;
            routine = null;
            onComplete?.Invoke();
        }

        private static float EaseBackOut(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }

        private static float EaseBackIn(float t)
        {
            const float c1 = 1.70158f;
            return (c1 + 1f) * t * t * t - c1 * t * t;
        }
    }
}
