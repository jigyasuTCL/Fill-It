using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    /// <summary>
    /// One calendar-day cell.
    ///
    /// States:
    ///   Win                            - HasWin (fully completed)
    ///   WrongAndSelected               - HasLost AND currently selected
    ///   UnlockedWithProgressSelected   - has progress between 0% and 100% AND currently selected
    ///   Selected                       - currently selected, not lost, no progress
    ///   Locked                         - entry date is in the future
    ///   Lost                           - HasLost and replayable
    ///   UnlockedWithProgress           - unlocked and has actual progress between 0% and 100%
    ///   Unlocked                       - unlocked with no progress
    /// </summary>
    public sealed class DailyChallengeDateButton : MonoBehaviour
    {
        #region Inspector - Core References

        [Header("Core References")]
        [SerializeField] private Button button = default;
        [SerializeField] private TMP_Text dayLabel = default;

        #endregion

        #region Inspector - State Roots (exactly one active at a time)

        [Header("State Roots - exactly one active at a time")]
        [SerializeField] private GameObject lockedStateRoot;
        [SerializeField] private GameObject unlockedStateRoot;
        [SerializeField] private GameObject unlockedWithProgressStateRoot;
        [SerializeField] private GameObject lostStateRoot;
        [SerializeField] private GameObject winStateRoot;
        [SerializeField] private GameObject selectedStateRoot;

        #endregion

        #region Inspector - Progress Images

        [Header("Progress Images (fillAmount driven by actual gameplay progress)")]
        [SerializeField] private Image unlockedStateRootImage;
        [SerializeField] private Image unlockedWithProgressStateRootImage;

        #endregion

        #region Inspector - Combo State Roots (selected + another condition)

        [Header("Combo State Roots - selected AND another condition")]
        [Tooltip("Shown when this day HasLost and is also the currently selected day.")]
        [SerializeField] private GameObject wrongAndSelectedStateRoot;

        [Tooltip("Shown when this day has actual progress (0-100%) and is also the currently selected day.")]
        [SerializeField] private GameObject unlockedWithProgressSelectedStateRoot;

        #endregion

        #region Inspector - Ad Gate

        [Header("Ad Gate")]
        [Tooltip(
            "Independent of the state roots above - can be active alongside any of them " +
            "whenever this day still needs a rewarded ad watched before it's playable."
        )]
        [SerializeField] private GameObject adsObject;

        #endregion

        #region Inspector - Debug

        [Header("Debug")]
        [Tooltip("Read-only - shows which state (see DateButtonState) this cell last resolved to, " +
                 "for inspecting in the Inspector. Set by RefreshAppearance, never edit by hand.")]
        [SerializeField] private DateButtonState currentState;

        #endregion

        #region External Hooks

        /// <summary>
        /// Set by the Cat Flap-side bridge.
        ///
        /// Args:
        ///     puzzleIndex   - 0-based puzzle index
        ///     subLevelIndex - 0-based sub-level index
        ///
        /// Returns:
        ///     null when the sub-level was never attempted,
        ///     otherwise a 0-100 progress percentage.
        /// </summary>
        public static Func<int, int, float?> ProgressPercentProvider;

        #endregion

        #region Runtime State

        private DailyChallengeController controller;
        private bool isSelected;

        public DailyChallengeData Data { get; private set; }

        private enum DateButtonState
        {
            Locked,
            Unlocked,
            UnlockedWithProgress,
            Lost,
            Win,
            Selected,
            WrongAndSelected,
            UnlockedWithProgressSelected
        }

        #endregion

        #region Lifecycle / Public API

        public void Initialize(
            DailyChallengeData data,
            DailyChallengeController controller)
        {
            Data = data;
            this.controller = controller;

            gameObject.name = $"Day {data.Day:00} Button";

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnButtonClicked);
            }

            Refresh();
        }

        /// <summary>
        /// Called by the controller when this cell becomes or stops being selected.
        /// </summary>
        public void SelectState(bool selected)
        {
            isSelected = selected;
            RefreshAppearance();
        }

        public void Refresh()
        {
            if (Data == null)
            {
                return;
            }

            if (dayLabel != null)
            {
                dayLabel.text = Data.Day.ToString();
            }

            RefreshAppearance();
        }

        private void RefreshAppearance()
        {
            if (Data == null)
            {
                return;
            }

            DateButtonState state = ResolveState(Data, isSelected);
            currentState = state;

            ApplyState(state);

            bool needsAd =
                controller != null &&
                controller.GetPlayRequirement(Data) ==
                DailyChallengePlayRequirement.RewardedAd;

            SetActive(adsObject, needsAd);

            if (button != null)
            {
                button.interactable =
                    state != DateButtonState.Locked &&
                    Data.CanSelect;
            }
        }

        #endregion

        #region State Resolution

        private static DateButtonState ResolveState(
            DailyChallengeData data,
            bool selected)
        {
            // Fully completed always takes highest priority.
            if (data.HasWin)
            {
                return DateButtonState.Win;
            }

            // Selected state takes priority over normal unlocked/progress states.
            if (selected)
            {
                if (data.HasLost)
                {
                    return DateButtonState.WrongAndSelected;
                }

                return HasProgress(data)
                    ? DateButtonState.UnlockedWithProgressSelected
                    : DateButtonState.Selected;
            }

            // Not unlocked yet.
            if (!data.IsUnlocked)
            {
                return DateButtonState.Locked;
            }

            // Lost days are replayable but have their own visual state.
            if (data.HasLost)
            {
                return DateButtonState.Lost;
            }

            // Check actual saved gameplay progress.
            return HasProgress(data)
                ? DateButtonState.UnlockedWithProgress
                : DateButtonState.Unlocked;
        }

        /// <summary>
        /// Returns true only when the actual gameplay progress is
        /// greater than 0% and less than 100%.
        ///
        /// 0%   -> Unlocked
        /// 1-99% -> UnlockedWithProgress
        /// 100% -> Win is handled before this method
        /// </summary>
        private static bool HasProgress(DailyChallengeData data)
        {
            if (data == null || !data.IsPuzzleAssigned)
            {
                return false;
            }

            float? percent =
                ProgressPercentProvider?.Invoke(
                    data.PuzzleIndex,
                    0
                );

            return percent.HasValue &&
                   percent.Value > 0f &&
                   percent.Value < 100f;
        }

        #endregion

        #region State Application

        private void ApplyState(DateButtonState state)
        {
            switch (state)
            {
                case DateButtonState.Locked:
                    ApplyLockedState();
                    break;

                case DateButtonState.Unlocked:
                    ApplyUnlockedState();
                    break;

                case DateButtonState.UnlockedWithProgress:
                    ApplyUnlockedWithProgressState();
                    break;

                case DateButtonState.Lost:
                    ApplyLostState();
                    break;

                case DateButtonState.Win:
                    ApplyWinState();
                    break;

                case DateButtonState.Selected:
                    ApplySelectedState();
                    break;

                case DateButtonState.WrongAndSelected:
                    ApplyWrongAndSelectedState();
                    break;

                case DateButtonState.UnlockedWithProgressSelected:
                    ApplyUnlockedWithProgressSelectedState();
                    break;
            }
        }

        /// <summary>
        /// Turns every state root off before enabling the required state.
        /// </summary>
        private void ResetStateRoots()
        {
            SetActive(lockedStateRoot, false);
            SetActive(unlockedStateRoot, false);
            SetActive(unlockedWithProgressStateRoot, false);
            SetActive(lostStateRoot, false);
            SetActive(winStateRoot, false);
            SetActive(selectedStateRoot, false);
            SetActive(wrongAndSelectedStateRoot, false);
            SetActive(unlockedWithProgressSelectedStateRoot, false);
            SetActive(unlockedWithProgressStateRootImage, false);
            SetActive(unlockedStateRootImage, false);
        }

        private void ApplyLockedState()
        {
            ResetStateRoots();

            SetActive(lockedStateRoot, true);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(134);
        }

        private void ApplyUnlockedState()
        {
            ResetStateRoots();

            SetActive(unlockedStateRoot, true);
            SetActive(dayLabel?.gameObject, true);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(255);
        }

        private void ApplyUnlockedWithProgressState()
        {
            ResetStateRoots();

            SetActive(unlockedStateRoot, true);
            SetActive(dayLabel?.gameObject, true);

            // Enable the progress-specific visual.
            SetActive(unlockedWithProgressStateRoot, true);
            SetActive(unlockedStateRootImage.gameObject, true);

            // Apply the real gameplay progress.
            SetProgressFillAmount(
                unlockedStateRootImage,
                GetLevelProgressFraction(Data)
            );

            SetDayLabelAlpha(255);
        }

        private void ApplyLostState()
        {
            ResetStateRoots();

            SetActive(lostStateRoot, true);
            SetActive(dayLabel?.gameObject, true);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(255);
        }

        private void ApplyWinState()
        {
            ResetStateRoots();

            SetActive(winStateRoot, true);
            SetActive(dayLabel?.gameObject, false);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(255);
        }

        private void ApplySelectedState()
        {
            ResetStateRoots();

            SetActive(dayLabel?.gameObject, true);
            SetActive(selectedStateRoot, true);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(255);
        }

        private void ApplyWrongAndSelectedState()
        {
            ResetStateRoots();

            SetActive(dayLabel?.gameObject, true);
            SetActive(wrongAndSelectedStateRoot, true);

            ResetProgressFillAmounts();
            SetDayLabelAlpha(255);
        }

        private void ApplyUnlockedWithProgressSelectedState()
        {
            ResetStateRoots();

            SetActive(dayLabel?.gameObject, true);

            // Same progress-fill visual as the non-selected progress state (see
            // ApplyUnlockedWithProgressState) - this day still has actual progress, it's just
            // also currently selected.
            // SetActive(unlockedStateRoot, true);
            // SetActive(unlockedWithProgressStateRoot, true);
            // SetActive(unlockedStateRootImage, true);
            SetActive(unlockedWithProgressStateRootImage.gameObject, true);
            SetProgressFillAmount(
                unlockedWithProgressStateRootImage,
                GetLevelProgressFraction(Data)
            );

            // Selection highlight on top of the progress visual.
            SetActive(unlockedWithProgressSelectedStateRoot, true);

            SetDayLabelAlpha(255);
        }

        #endregion

        #region Progress Helpers

        /// <summary>
        /// Gets the actual progress percentage from the gameplay-side provider
        /// and converts it from 0-100 into Unity's 0-1 fillAmount range.
        /// </summary>
        private static float GetLevelProgressFraction(
            DailyChallengeData data)
        {
            if (data == null || !data.IsPuzzleAssigned)
            {
                return 0f;
            }

            float? percent =
                ProgressPercentProvider?.Invoke(
                    data.PuzzleIndex,
                    0
                );

            return percent.HasValue
                ? Mathf.Clamp01(percent.Value / 100f)
                : 0f;
        }

        /// <summary>
        /// Updates the fill amount of the given progress image - there are two (unlockedStateRootImage,
        /// unlockedWithProgressStateRootImage), so the caller must say which one is actually
        /// visible for the state being applied.
        /// </summary>
        private static void SetProgressFillAmount(Image image, float fillAmount)
        {
            if (image != null)
            {
                image.fillAmount = Mathf.Clamp01(fillAmount);
            }
        }

        /// <summary>Zeroes both progress images - used by every state that doesn't show a
        /// progress fill at all, so neither is left showing a stale value from a previous day.</summary>
        private void ResetProgressFillAmounts()
        {
            SetProgressFillAmount(unlockedStateRootImage, 0f);
            SetProgressFillAmount(unlockedWithProgressStateRootImage, 0f);
        }

        #endregion

        #region Generic Helpers

        private static void SetActive(
            GameObject target,
            bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void SetActive(
            Image image,
            bool active)
        {
            if (image != null)
            {
                SetActive(image.gameObject, active);
            }
        }

        /// <summary>
        /// TMP alpha uses a 0-1 range, while this feature uses
        /// 0-255 values for readability.
        /// </summary>
        private void SetDayLabelAlpha(byte alpha0To255)
        {
            if (dayLabel == null)
            {
                return;
            }

            Color color = dayLabel.color;

            color.a = alpha0To255 / 255f;

            dayLabel.color = color;
        }

        #endregion

        #region Unity Messages / Input

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(OnButtonClicked);
            }
        }

        private void OnButtonClicked()
        {
            if (Data == null)
            {
                return;
            }

            if (controller != null)
            {
                controller.TrySelectDay(Data, this);
            }
        }

        #endregion
    }
}
