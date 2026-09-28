using System;
using System.Collections;
using CatFlap.Models;
using CatFlap.Views;
using Shared.GameConfig;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapCellViewAdapter : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color emptyColor = Color.white;
        [SerializeField] private Color fishColor = new Color(0.25f, 0.65f, 1f);
        [SerializeField] private Color catColor = new Color(1f, 0.72f, 0.28f);
        [SerializeField] private Color lockedColor = new Color(0.72f, 0.72f, 0.72f);
        [SerializeField] private Color noteColor = new Color(0.78f, 0.9f, 1f);
        [SerializeField] private Color completedColor = new Color(0.82f, 0.96f, 0.86f, 1f);
        [SerializeField] private Color errorColor = new Color(1f, 0.84f, 0.84f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.84f, 0.42f, 1f);
        [SerializeField] private Color mistakeBlinkColor = new Color(1f, 0.24f, 0.24f, 1f);
        [SerializeField] private float mistakeBlinkDurationSeconds = 0.45f;
        [SerializeField] private int mistakeBlinkCount = 3;

        private int row;
        private int column;
        private Action<int, int> selected;
        private Coroutine mistakeBlinkRoutine;
        private Color currentBackgroundColor = Color.white;

        private void Awake()
        {
            if (button != null)
            {
                button.onClick.AddListener(Select);
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(Select);
            }
        }

        public void Bind(int row, int column, Action<int, int> selected)
        {
            this.row = row;
            this.column = column;
            this.selected = selected;
        }

        public void ApplyRulesConfig(GameRulesConfig rulesConfig)
        {
            if (rulesConfig == null)
            {
                return;
            }

            GameRulesConfig config = rulesConfig.CloneNormalized();
            completedColor = config.CellCompletedColor;
            errorColor = config.CellErrorColor;
            warningColor = config.CellWarningColor;
            mistakeBlinkColor = config.MistakeBlinkColor;
            mistakeBlinkDurationSeconds = config.MistakeBlinkDurationSeconds;
            mistakeBlinkCount = config.MistakeBlinkCount;
        }

        public void SetState(CatFlapCellValue value, bool isLocked, bool isNote, bool useLockedColor, CatFlapCellVisualState visualState)
        {
            if (button != null)
            {
                button.interactable = !isLocked;
            }

            if (background != null)
            {
                currentBackgroundColor = ResolveColor(value, useLockedColor, isNote, visualState);
                if (mistakeBlinkRoutine == null)
                {
                    background.color = currentBackgroundColor;
                }
            }

            if (label != null)
            {
                label.text = ResolveLabel(value, isNote);
            }
        }

        private void Select()
        {
            selected?.Invoke(row, column);
        }

        public void PlayMistakeBlink()
        {
            if (background == null)
            {
                return;
            }

            if (mistakeBlinkRoutine != null)
            {
                StopCoroutine(mistakeBlinkRoutine);
            }

            mistakeBlinkRoutine = StartCoroutine(AnimateMistakeBlink());
        }

        private IEnumerator AnimateMistakeBlink()
        {
            int blinkCount = Mathf.Max(1, mistakeBlinkCount);
            float stepDuration = Mathf.Max(0.01f, mistakeBlinkDurationSeconds / (blinkCount * 2f));
            for (int index = 0; index < blinkCount; index++)
            {
                yield return LerpBackground(currentBackgroundColor, mistakeBlinkColor, stepDuration);
                yield return LerpBackground(mistakeBlinkColor, currentBackgroundColor, stepDuration);
            }

            background.color = currentBackgroundColor;
            mistakeBlinkRoutine = null;
        }

        private IEnumerator LerpBackground(Color from, Color to, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                background.color = Color.Lerp(from, to, t);
                yield return null;
            }

            background.color = to;
        }

        private Color ResolveColor(CatFlapCellValue value, bool isLocked, bool isNote, CatFlapCellVisualState visualState)
        {
            if (visualState == CatFlapCellVisualState.Error)
            {
                return errorColor;
            }

            if (visualState == CatFlapCellVisualState.Completed)
            {
                return completedColor;
            }

            if (visualState == CatFlapCellVisualState.Warning)
            {
                return warningColor;
            }

            if (isLocked)
            {
                return lockedColor;
            }

            if (isNote)
            {
                return noteColor;
            }

            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return fishColor;
                case CatFlapCellValue.Cat:
                    return catColor;
                default:
                    return emptyColor;
            }
        }

        private static string ResolveLabel(CatFlapCellValue value, bool isNote)
        {
            string prefix = isNote ? "?" : string.Empty;
            switch (value)
            {
                case CatFlapCellValue.Fish:
                    return prefix + "F";
                case CatFlapCellValue.Cat:
                    return prefix + "C";
                default:
                    return string.Empty;
            }
        }
    }
}
