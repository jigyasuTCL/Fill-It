using System;
using System.Collections;
using FillIt.Models;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>Tunable beats between gameplay moments (e.g. a short pause before the win celebration).</summary>
    public sealed class FillItAnimationViewAdapter : MonoBehaviour
    {
        [SerializeField] private float pageStateDelaySeconds;

        private Coroutine activeRoutine;

        public float PageStateDelaySeconds => Mathf.Max(0f, pageStateDelaySeconds);

        public void PlayPageState(FillItGameState state, Action onComplete)
        {
            if (!isActiveAndEnabled || pageStateDelaySeconds <= 0f)
            {
                onComplete?.Invoke();
                return;
            }

            CancelAll();
            activeRoutine = StartCoroutine(CompleteAfterDelay(pageStateDelaySeconds, onComplete));
        }

        public void CancelAll()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }
        }

        private void OnDisable()
        {
            CancelAll();
        }

        private IEnumerator CompleteAfterDelay(float delaySeconds, Action onComplete)
        {
            yield return new WaitForSeconds(delaySeconds);
            activeRoutine = null;
            onComplete?.Invoke();
        }
    }
}
