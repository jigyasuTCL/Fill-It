using System;
using System.Collections;
using Shared.GameFlow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    /// <summary>
    /// Lives in the Calendar popup, next to DailyChallengePlayButtonController. Once today's
    /// Daily Challenge is fully won (DailyChallengeData.HasWin), this enables
    /// <see cref="nextDayTimerRoot"/> with a live "next challenge unlocks in HH:MM:SS" countdown,
    /// and fires <see cref="nextDayButton"/>'s own onClick programmatically - a real
    /// button.onClick.Invoke(), not just a visual press - so whatever that button is already
    /// wired to do in the Inspector runs exactly as if the player had tapped it themselves.
    ///
    /// No OnEnable/OnDisable here. DailyChallengeManager subscribes HandleSelectedDayChanged and
    /// HandleResultResolved to the matching controller events once in its own Awake/OnDestroy,
    /// and explicitly calls Hide() from ClosePopup() to stop the countdown coroutine - the only
    /// per-instance lifecycle concern this component has, and Manager already knows exactly when
    /// the popup closes.
    ///
    /// Two trigger paths, matching the rest of this feature's "never miss a state change" pattern
    /// (see DailyChallengeButtonUI/DailyChallengePlayButtonController):
    ///  1) SelectedDayChanged - covers the popup being (re)opened, or a different day re-selected,
    ///     while today is already won. In practice this also covers the moment a win lands during
    ///     this same session, since DailyChallengeController.SetDayLevelState already re-selects
    ///     today via SelectClosestAvailableDayToToday() the instant HasWin flips true.
    ///  2) DailyChallengeResultResolved - a direct, redundant hook on the exact instant
    ///     ReportGameplayResult records today's winning result, in case some future call path ever
    ///     changes state without going through SetDayLevelState's re-selection.
    /// Both paths converge on the same ShowPrompt/HidePrompt logic, and the fake tap itself is
    /// guarded to fire at most once per calendar day (see lastPromptedDateKey) even though both
    /// events may land for the same win.
    /// </summary>
    public sealed class DailyChallengeNextDayPromptController : MonoBehaviour
    {
        [Header("Next-day timer")]
        [Tooltip("Enabled once today's challenge is fully won; disabled otherwise (e.g. a " +
                 "different, not-yet-completed day gets selected).")]
        [SerializeField] private GameObject nextDayTimerRoot;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private float refreshIntervalSeconds = 1f;

        [Header("Fake tap")]
        [Tooltip("Clicked via button.onClick.Invoke() the moment the prompt first appears for a " +
                 "given completed day - a real invocation of whatever this button is wired to, " +
                 "simulating the player tapping it. Not fired again on every refresh tick or " +
                 "every time this popup reopens the same day.")]
        [SerializeField] private Button nextDayButton;

        private Coroutine countdownRoutine;

        // Guards the fake tap firing more than once for the same completed day - e.g. both
        // trigger paths landing on the same win, or the popup being closed and reopened later
        // the same day. Naturally resets once the date rolls over, since the key includes the
        // date itself.
        private string lastPromptedDateKey;

        /// <summary>Called by DailyChallengeManager when the popup closes - stops the countdown
        /// coroutine so it isn't still ticking (and doesn't wrongly appear "already running" on
        /// the next Show) while the popup is hidden.</summary>
        public void Hide()
        {
            StopCountdown();
        }

        /// <summary>Wired by DailyChallengeManager to controller.DailyChallengeResultResolved.
        /// Only today's own result should surface tomorrow's prompt - a missed earlier day being
        /// cleaned up elsewhere shouldn't trigger it.</summary>
        public void HandleResultResolved(DailyChallengeResultInfo info)
        {
            if (info.Data == null || !info.Data.HasWin || !IsToday(info.Data))
            {
                return;
            }

            ShowPrompt(info.Data);
        }

        /// <summary>Wired by DailyChallengeManager to controller.SelectedDayChanged, and also
        /// called directly once after the popup is shown/reopened to reflect the current
        /// selection immediately (SelectedDayChanged only fires on the NEXT selection).</summary>
        public void HandleSelectedDayChanged(DailyChallengeData data)
        {
            if (data != null && IsToday(data) && data.HasWin)
            {
                ShowPrompt(data);
                return;
            }

            HidePrompt();
        }

        private static bool IsToday(DailyChallengeData data)
        {
            DateTime today = DailyChallengeClock.Now.Date;
            return data.Year == today.Year && data.Month == today.Month && data.Day == today.Day;
        }

        private void ShowPrompt(DailyChallengeData data)
        {
            if (nextDayTimerRoot != null)
            {
                nextDayTimerRoot.SetActive(true);
            }

            StartCountdown();

            string dateKey = $"{data.Year}-{data.Month:00}-{data.Day:00}";
            if (string.Equals(lastPromptedDateKey, dateKey, StringComparison.Ordinal))
            {
                return;
            }

            lastPromptedDateKey = dateKey;
            FireFakeTap();
        }

        private void HidePrompt()
        {
            if (nextDayTimerRoot != null)
            {
                nextDayTimerRoot.SetActive(false);
            }

            StopCountdown();
        }

        private void FireFakeTap()
        {
            // A genuine onClick.Invoke() - runs every listener already wired to this button in
            // the Inspector, same as a real pointer click would, minus the pointer event itself.
            nextDayButton?.onClick.Invoke();
        }

        private void StartCountdown()
        {
            if (countdownRoutine != null)
            {
                return;
            }

            if (!gameObject.activeInHierarchy)
            {
                // StartCoroutine throws on an inactive object - the next explicit
                // HandleSelectedDayChanged call (Manager fires one right after ShowPopup) will
                // retry once this popup is active again.
                return;
            }

            countdownRoutine = StartCoroutine(CountdownRoutine());
        }

        private void StopCountdown()
        {
            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
                countdownRoutine = null;
            }
        }

        private IEnumerator CountdownRoutine()
        {
            while (true)
            {
                // Shared calculation with DailyChallengeButtonUI/DailyChallengeDateButton, so
                // every "time until tomorrow's challenge" display in the game reads from the same
                // clock and rolls over at exactly the same instant.
                SetCountdownText(DailyChallengeButtonUI.GetTimeUntilNextLocalMidnight());
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, refreshIntervalSeconds));
            }
        }

        private void SetCountdownText(TimeSpan remaining)
        {
            if (countdownText == null)
            {
                return;
            }

            countdownText.text = CountdownTimeFormatter.Format(remaining);
        }
    }
}