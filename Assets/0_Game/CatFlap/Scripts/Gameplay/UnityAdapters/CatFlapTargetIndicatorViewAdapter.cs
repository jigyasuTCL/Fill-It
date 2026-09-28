using DG.Tweening;
using Shared.GameConfig;
using TMPro;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapTargetIndicatorViewAdapter : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private SpriteRenderer stateOverlay;
        [SerializeField] private TMP_Text catText;
        [SerializeField] private TMP_Text fishText;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private GameObject tickRoot;
        // Wrong Solution icon (assign your own sprite/prefab child here) - shown/hidden the same
        // way tickRoot is, just driven by SetWarningState instead of SetIndicatorState's isCompleted.
        [SerializeField] private GameObject warningIconRoot;
        [SerializeField] private float warningIconScaleUpDuration = 0.3f;
        [SerializeField] private Color neutralOverlayColor = new Color(1f, 1f, 1f, 0f);
        [SerializeField] private Color completedColor = new Color(0.82f, 0.96f, 0.86f, 1f);
        [SerializeField] private float targetCellTextVerticalOffset = 0.2f;
        [SerializeField] private float tickScaleUpDuration = 0.3f;
        // How much of the root's non-uniform X squish (rows are half-width - see SetRootScale) to
        // undo when spacing catText/fishText apart: 0 = no compensation (numbers jam together on
        // rows, the original bug), 1 = full compensation (numbers match the column indicator's gap
        // exactly, which overflows a row indicator's narrower cell). Tune between the two.
        [SerializeField, Range(0f, 1f)] private float positionGapCompensation = 0.55f;

        private Color baseColor;
        private Vector3 catTextLocalPosition;
        private Vector3 fishTextLocalPosition;
        private Vector3 tickBaseLocalScale;
        private Vector3 tickCounterScale = Vector3.one;
        private Vector3 warningIconBaseLocalScale;
        private Vector3 warningIconCounterScale = Vector3.one;
        private Vector3 positionCounterScale = Vector3.one;
        private bool isTitleActive;
        private bool wasCompleted;
        private bool isWrongSequence;
        private Tween tickTween;
        private Tween warningIconTween;

        private void Awake()
        {
            baseColor = neutralOverlayColor;
            if (stateOverlay != null)
            {
                stateOverlay.color = baseColor;
            }

            if (titleText != null)
            {
                titleText.gameObject.SetActive(false);
            }

            if (catText != null)
            {
                catTextLocalPosition = catText.transform.localPosition;
                catText.verticalAlignment = VerticalAlignmentOptions.Geometry;
            }

            if (fishText != null)
            {
                fishTextLocalPosition = fishText.transform.localPosition;
                fishText.verticalAlignment = VerticalAlignmentOptions.Geometry;
            }

            if (tickRoot != null)
            {
                tickBaseLocalScale = tickRoot.transform.localScale;
                tickRoot.SetActive(false);
            }

            if (warningIconRoot != null)
            {
                warningIconBaseLocalScale = warningIconRoot.transform.localScale;
                warningIconRoot.SetActive(false);
            }
        }

        public void ApplyRulesConfig(GameRulesConfig rulesConfig)
        {
            if (rulesConfig == null)
            {
                return;
            }

            completedColor = rulesConfig.CloneNormalized().CellCompletedColor;
        }

        public void SetIndicatorState(int catCount, int fishCount, bool isCompleted)
        {
            baseColor = isCompleted ? completedColor : neutralOverlayColor;
            if (stateOverlay != null)
            {
                stateOverlay.color = baseColor;
            }

            if (catText != null)
            {
                catText.text = catCount.ToString();
                if (!isTitleActive)
                {
                    catText.transform.localPosition = new Vector3(GapScaledX(catTextLocalPosition.x), 0f, catTextLocalPosition.z);
                }
            }

            if (fishText != null)
            {
                fishText.text = fishCount.ToString();
                if (!isTitleActive)
                {
                    fishText.transform.localPosition = new Vector3(GapScaledX(fishTextLocalPosition.x), 0f, fishTextLocalPosition.z);
                }
            }

            if (isCompleted != wasCompleted)
            {
                wasCompleted = isCompleted;
                ApplyCompletionVisual(isCompleted);
            }
        }

        // Shared by ApplyCompletionVisual and SetWarningState - the cat/fish count numbers hide
        // whenever EITHER the tick or the warning icon is showing (the two are mutually exclusive
        // in practice - a line is either completed-correct or wrong-sequence, never both - but
        // this stays correct either way rather than each setter blindly overwriting the other's
        // SetActive call).
        private void UpdateNumberVisibility()
        {
            bool showNumbers = !wasCompleted && !isWrongSequence;

            if (catText != null)
            {
                catText.gameObject.SetActive(showNumbers);
            }

            if (fishText != null)
            {
                fishText.gameObject.SetActive(showNumbers);
            }
        }

        private void ApplyCompletionVisual(bool isCompleted)
        {
            tickTween?.Kill();
            UpdateNumberVisibility();

            if (tickRoot == null)
            {
                return;
            }

            if (isCompleted)
            {
                Vector3 targetScale = Vector3.Scale(tickBaseLocalScale, tickCounterScale);
                tickRoot.SetActive(true);
                tickRoot.transform.localScale = Vector3.zero;
                tickTween = tickRoot.transform
                    .DOScale(targetScale, tickScaleUpDuration)
                    .SetEase(Ease.OutBack);
            }
            else
            {
                tickRoot.SetActive(false);
            }
        }

        /// <summary>
        /// Shows/hides this row or column's Wrong Solution icon (see warningIconRoot) - same
        /// scale-up-from-zero pattern as the completed-line tick, just driven independently since a
        /// line can be wrong-sequence without being "completed" in SetIndicatorState's sense (it's
        /// filled but doesn't match the solution, never both at once).
        /// </summary>
        public void SetWarningState(bool isWrong)
        {
            if (isWrong == isWrongSequence)
            {
                return;
            }

            isWrongSequence = isWrong;
            warningIconTween?.Kill();
            UpdateNumberVisibility();

            if (warningIconRoot == null)
            {
                return;
            }

            if (isWrong)
            {
                Vector3 targetScale = Vector3.Scale(warningIconBaseLocalScale, warningIconCounterScale);
                warningIconRoot.SetActive(true);
                warningIconRoot.transform.localScale = Vector3.zero;
                warningIconTween = warningIconRoot.transform
                    .DOScale(targetScale, warningIconScaleUpDuration)
                    .SetEase(Ease.OutBack);
            }
            else
            {
                warningIconRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            tickTween?.Kill();
            warningIconTween?.Kill();
        }

        public void SetTitleState()
        {
            if (titleText == null)
            {
                return;
            }

            titleText.gameObject.SetActive(true);
            titleText.text = "Target";
            isTitleActive = true;

            if (catText != null)
            {
                catText.transform.localPosition = new Vector3(GapScaledX(catTextLocalPosition.x), catTextLocalPosition.y - targetCellTextVerticalOffset, catTextLocalPosition.z);
            }

            if (fishText != null)
            {
                fishText.transform.localPosition = new Vector3(GapScaledX(fishTextLocalPosition.x), fishTextLocalPosition.y - targetCellTextVerticalOffset, fishTextLocalPosition.z);
            }
        }

        // Blends between the original (squished, jammed-together) gap and the fully
        // counter-scaled (column-matching, but too wide for a row indicator's narrower cell) gap
        // - see positionGapCompensation.
        private float GapScaledX(float baseX)
        {
            return baseX * Mathf.Lerp(1f, positionCounterScale.x, positionGapCompensation);
        }

        public void SetRootScale(Vector3 scale)
        {
            transform.localScale = scale;

            Vector3 counterScale = new Vector3(
                scale.x != 0f ? 1f / scale.x : 1f,
                scale.y != 0f ? 1f / scale.y : 1f,
                1f);

            if (catText != null)
            {
                catText.transform.localScale = counterScale;
            }

            if (fishText != null)
            {
                fishText.transform.localScale = counterScale;
            }

            positionCounterScale = counterScale;
            if (!isTitleActive)
            {
                if (catText != null)
                {
                    catText.transform.localPosition = new Vector3(GapScaledX(catTextLocalPosition.x), catText.transform.localPosition.y, catTextLocalPosition.z);
                }

                if (fishText != null)
                {
                    fishText.transform.localPosition = new Vector3(GapScaledX(fishTextLocalPosition.x), fishText.transform.localPosition.y, fishTextLocalPosition.z);
                }
            }

            if (titleText != null)
            {
                // Unlike catText/fishText (which counter-scale to stay a fixed readable size),
                // titleText should track the parent's scale directly, so it shrinks/grows with
                // the corner indicator instead of staying a fixed absolute size.
                titleText.transform.localScale = Vector3.one;
            }

            // Counter-scale the tick too, so a non-uniform root scale (rows/columns of
            // different sizes) doesn't stretch it into an ellipse - it should stay a
            // uniformly-proportioned checkmark like the text labels do.
            tickCounterScale = counterScale;
            if (tickRoot != null && tickRoot.activeSelf && (tickTween == null || !tickTween.IsActive()))
            {
                tickRoot.transform.localScale = Vector3.Scale(tickBaseLocalScale, tickCounterScale);
            }

            // Same counter-scale reasoning as the tick above, applied to the warning icon.
            warningIconCounterScale = counterScale;
            if (warningIconRoot != null && warningIconRoot.activeSelf && (warningIconTween == null || !warningIconTween.IsActive()))
            {
                warningIconRoot.transform.localScale = Vector3.Scale(warningIconBaseLocalScale, warningIconCounterScale);
            }
        }
    }
}
