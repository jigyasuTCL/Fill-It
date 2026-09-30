using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    public sealed class MainMenuHomeAnimator : MonoBehaviour
    {
        [SerializeField] private RectTransform titleLabel;
        [SerializeField] private RectTransform[] scaleInButtons;
        [SerializeField] private float titleEntryDuration = 0.35f;
        [SerializeField] private float titleEntryDistance = 300f;
        [SerializeField] private float buttonScaleDuration = 0.25f;
        [SerializeField] private float buttonStaggerDelay = 0.08f;

        [Header("Play Button Idle Pulse - loops continuously once started")]
        [SerializeField] private RectTransform playButtonTransform;
        [SerializeField] private float playButtonPulseStartDelay = 1.5f;
        [SerializeField] private float playButtonPulseDuration = 0.6f;
        [SerializeField] private float playButtonPulseMinScale = 0.95f;
        [SerializeField] private float playButtonPulseMaxScale = 1.05f;

        [Header("Normal / Hard Level Play Buttons - exactly one shown, based on whether the " +
                 "current level number is listed in GlobalConfig.HardLevels")]
        [SerializeField] private GameObject normalLevelButtonRoot;
        [SerializeField] private GameObject hardLevelButtonRoot;

        private Vector2 titleRestingPosition;
        private Coroutine titleRoutine;
        private Tween playButtonPulseTween;
        private readonly List<Coroutine> buttonRoutines = new List<Coroutine>();

        public TMP_Text CoinText;

        public Action<int> CallCoinUpdate;
        private void Awake()
        {
            if (titleLabel != null)
            {
                titleRestingPosition = titleLabel.anchoredPosition;
            }
            CallCoinUpdate+=UpdateCoinText;
        }

        public void UpdateCoinText(int coin){
            CoinText.text =  coin.ToString();
        }

        public void PlayEntry(bool immediate)
        {
            PlayTitleEntry(immediate);
            PlayButtonScales(immediate);
            PlayButtonPulse();
        }

        /// <summary>
        /// Shows exactly one of normalLevelButtonRoot/hardLevelButtonRoot, based on whether
        /// currentLevelNumber is listed in GlobalConfig.HardLevels. Called by
        /// MainMenuHandler.ShowHome (the only place that already knows the current level number)
        /// alongside PlayEntry.
        /// </summary>
        public void RefreshLevelTypeButtons(int currentLevelNumber)
        {
            bool isHardLevel = IsHardLevel(currentLevelNumber);

            if (normalLevelButtonRoot != null)
            {
                normalLevelButtonRoot.SetActive(!isHardLevel);
            }

            if (hardLevelButtonRoot != null)
            {
                hardLevelButtonRoot.SetActive(isHardLevel);
            }
        }

        private static bool IsHardLevel(int levelNumber)
        {
            Shared.GameConfig.GlobalConfig config = Shared.Boot.GlobalConfigRuntimeContext.Config;
            if (config == null || config.HardLevels == null)
            {
                return false;
            }

            return Array.IndexOf(config.HardLevels, levelNumber) >= 0;
        }

        private void PlayTitleEntry(bool immediate)
        {
            if (titleLabel == null)
            {
                return;
            }

            if (titleRoutine != null)
            {
                StopCoroutine(titleRoutine);
                titleRoutine = null;
            }

            Vector2 from = titleRestingPosition + new Vector2(0f, Mathf.Abs(titleEntryDistance));
            if (immediate || !gameObject.activeInHierarchy)
            {
                titleLabel.anchoredPosition = titleRestingPosition;
                return;
            }

            titleLabel.anchoredPosition = from;
            titleRoutine = StartCoroutine(AnchoredPositionRoutine(titleLabel, from, titleRestingPosition, titleEntryDuration));
        }

        private void PlayButtonScales(bool immediate)
        {
            for (int index = 0; index < buttonRoutines.Count; index++)
            {
                if (buttonRoutines[index] != null)
                {
                    StopCoroutine(buttonRoutines[index]);
                }
            }

            buttonRoutines.Clear();

            if (scaleInButtons == null)
            {
                return;
            }

            for (int index = 0; index < scaleInButtons.Length; index++)
            {
                RectTransform target = scaleInButtons[index];
                if (target == null)
                {
                    continue;
                }

                if (immediate || !gameObject.activeInHierarchy)
                {
                    target.localScale = Vector3.one;
                    continue;
                }

                target.localScale = Vector3.zero;
                buttonRoutines.Add(StartCoroutine(ScaleRoutine(target, Vector3.zero, Vector3.one, buttonScaleDuration, index * Mathf.Max(0f, buttonStaggerDelay))));
            }
        }

        // Restarts the Play button's idle pulse - always killed/restarted from scratch on every
        // PlayEntry (home tab re-activated), so a re-entry never leaves two pulses running.
        //
        // Deliberately does NOT set playButtonTransform.localScale here: playButtonTransform is
        // also one of scaleInButtons, which PlayButtonScales() just reset to Vector3.zero and
        // queued a 0->1 entry-scale coroutine for (see ScaleRoutine). Stomping localScale to
        // playButtonPulseMinScale synchronously, before that coroutine has run even one frame,
        // corrupted its starting value and produced a visible jerk (0 -> 0.95 -> back near 0 ->
        // eases up to 1). The pulse tween's own SetDelay(playButtonPulseStartDelay) already keeps
        // it from starting until well after the entry scale-in has finished and settled on
        // Vector3.one, so it animates from whatever the live scale is at that point instead.
        private void PlayButtonPulse()
        {
            if (playButtonPulseTween != null)
            {
                playButtonPulseTween.Kill();
                playButtonPulseTween = null;
            }

            if (playButtonTransform == null || !gameObject.activeInHierarchy)
            {
                return;
            }

            playButtonPulseTween = playButtonTransform
                .DOScale(playButtonPulseMaxScale, Mathf.Max(0.01f, playButtonPulseDuration * 0.5f))
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetDelay(Mathf.Max(0f, playButtonPulseStartDelay))
                .SetUpdate(true); // unscaled time, so the pulse keeps going even if gameplay time-scale changes
        }

        private void OnDisable()
        {
            if (playButtonPulseTween != null)
            {
                playButtonPulseTween.Kill();
                playButtonPulseTween = null;
            }
        }

        private IEnumerator AnchoredPositionRoutine(RectTransform target, Vector2 from, Vector2 to, float duration)
        {
            float seconds = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseOutCubic(Mathf.Clamp01(elapsed / seconds));
                target.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.anchoredPosition = to;
            titleRoutine = null;
        }

        private IEnumerator ScaleRoutine(RectTransform target, Vector3 from, Vector3 to, float duration, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            float seconds = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = EaseBackOut(Mathf.Clamp01(elapsed / seconds));
                target.localScale = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.localScale = to;
        }

        private static float EaseOutCubic(float t)
        {
            float value = 1f - t;
            return 1f - value * value * value;
        }

        private static float EaseBackOut(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float value = t - 1f;
            return 1f + c3 * value * value * value + c1 * value * value;
        }

    }
}
