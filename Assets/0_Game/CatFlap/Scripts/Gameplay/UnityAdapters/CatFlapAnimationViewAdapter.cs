using System;
using System.Collections;
using System.Collections.Generic;
using CatFlap.Boosters;
using CatFlap.Models;
using CatFlap.Views;
using Shared.Boosters;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapAnimationViewAdapter : MonoBehaviour, ICatFlapAnimationView
    {
        [SerializeField] private float cellChangedDelaySeconds;
        [SerializeField] private float cellsLockedDelaySeconds;
        [SerializeField] private float mistakeDelaySeconds;
        [SerializeField] private float boosterDelaySeconds;
        [SerializeField] private float pageStateDelaySeconds;

        private Coroutine activeRoutine;

        public void PlayCellChanged(CatFlapCellPosition position, CatFlapCellValue value, bool isNote, Action onComplete)
        {
            PlayDelay(cellChangedDelaySeconds, onComplete);
        }

        public void PlayCellsLocked(IReadOnlyList<CatFlapCellPosition> positions, Action onComplete)
        {
            PlayDelay(cellsLockedDelaySeconds, onComplete);
        }

        public void PlayMistake(CatFlapCellPosition position, Action onComplete)
        {
            PlayDelay(mistakeDelaySeconds, onComplete);
        }

        public void PlayBooster(BoosterType boosterType, CatFlapCellRevealResult result, Action onComplete)
        {
            PlayDelay(boosterDelaySeconds, onComplete);
        }

        public void PlayPageState(CatFlapGameState state, Action onComplete)
        {
            PlayDelay(pageStateDelaySeconds, onComplete);
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

        private void PlayDelay(float delaySeconds, Action onComplete)
        {
            if (!isActiveAndEnabled)
            {
                onComplete?.Invoke();
                return;
            }

            CancelAll();
            activeRoutine = StartCoroutine(CompleteAfterDelay(delaySeconds, onComplete));
        }

        private IEnumerator CompleteAfterDelay(float delaySeconds, Action onComplete)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }

            activeRoutine = null;
            onComplete?.Invoke();
        }
    }
}
