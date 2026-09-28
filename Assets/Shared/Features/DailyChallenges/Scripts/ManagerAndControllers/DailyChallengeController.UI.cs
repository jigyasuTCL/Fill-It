using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        #region UI and button state

        /// <summary>
        /// Resolves the presentation-facing display state for one calendar day. Calendar cell UI
        /// should call this (and map the result to a color/icon) rather than deriving Locked vs
        /// Current vs Available vs Upcoming from raw data itself.
        /// </summary>
        public DailyChallengeDayDisplayState GetDayDisplayState(DailyChallengeData data)
        {
            if (data == null)
            {
                return DailyChallengeDayDisplayState.Upcoming;
            }

            if (data.HasWin)
            {
                return DailyChallengeDayDisplayState.Completed;
            }

            if (!data.IsUnlocked)
            {
                return DailyChallengeDayDisplayState.Upcoming;
            }

            if (data.HasLost)
            {
                return DailyChallengeDayDisplayState.Failed;
            }

            var entryDate = new DateTime(data.Year, data.Month, data.Day).Date;
            if (entryDate == DailyChallengeClock.Now.Date)
            {
                return DailyChallengeDayDisplayState.Current;
            }

            return GetPlayRequirement(data) == DailyChallengePlayRequirement.RewardedAd
                ? DailyChallengeDayDisplayState.Locked
                : DailyChallengeDayDisplayState.Available;
        }

        public void SetDayPlayState(int day, DailyChallengePlayState state)
        {
            if (dailyChallengeData == null || jsonConfig == null)
            {
                return;
            }

            if (day < 1 || day > dailyChallengeData.Count)
            {
                Debug.LogWarning($"DailyChallengeController: invalid day index {day}.");
                return;
            }

            var data = dailyChallengeData[day - 1];
            data.SetPlayState(state);
            SyncAdWatchStateForDate(data);
            SaveStateToJson(day, state);
            UpdateViews();
        }

        public void MarkDayWin(int day)
        {
            SetDayPlayState(day, DailyChallengePlayState.Win);
        }

        public void MarkDayLost(int day)
        {
            SetDayPlayState(day, DailyChallengePlayState.Lost);
        }

        public void MarkDayNotPlayed(int day)
        {
            SetDayPlayState(day, DailyChallengePlayState.NotPlayed);
        }

        public void OnWinButtonPressed()
        {
            ApplyStateToSelected(DailyChallengePlayState.Win);
        }

        public void OnLoseButtonPressed()
        {
            ApplyStateToSelected(DailyChallengePlayState.Lost);
        }

        public void OnNotPlayedButtonPressed()
        {
            ApplyStateToSelected(DailyChallengePlayState.NotPlayed);
        }

        private void ApplyStateToSelected(DailyChallengePlayState state)
        {
            if (selectedButton == null || selectedButton.Data == null || jsonConfig == null)
            {
                Debug.LogWarning("DailyChallengeController: no day selected.");
                return;
            }

            var day = selectedButton.Data.Day;
            selectedButton.Data.SetPlayState(state);
            SaveStateToJson(day, state);
            selectedButton.Refresh();
            UpdateButtonForDay(day);
        }

        public void ResetAllStates()
        {
            if (dailyChallengeData == null || jsonConfig == null)
            {
                return;
            }

            for (var index = 0; index < dailyChallengeData.Count; index++)
            {
                var data = dailyChallengeData[index];
                data.SetPlayState(DailyChallengePlayState.NotPlayed);
                SaveStateToJson(data.Day, DailyChallengePlayState.NotPlayed);
            }

            UpdateViews();
        }

        public void SetDayLevelCount(int day, int levelCount)
        {
            if (dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                Debug.LogWarning($"DailyChallengeController: invalid day index {day} or levelCount {levelCount}.");
                return;
            }

            if (levelCount < 1)
            {
                Debug.LogWarning("DailyChallengeController: levelCount must be at least 1.");
                return;
            }

            var data = dailyChallengeData[day - 1];
            data.SetLevelCount(levelCount);
            SaveLevelDataToJson(day, data);
            UpdateViews();
        }

        public void SetDayLevelState(int day, int levelIndex, DailyChallengePlayState levelState)
        {
            if (dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                Debug.LogWarning($"DailyChallengeController: invalid day index {day}.");
                return;
            }

            var data = dailyChallengeData[day - 1];
            if (levelIndex < 0 || levelIndex >= data.LevelCount)
            {
                Debug.LogWarning($"DailyChallengeController: invalid level index {levelIndex} for day {day}.");
                return;
            }

            data.SetLevelState(levelIndex, levelState);
            SyncAdWatchStateForDate(data);
            SaveLevelDataToJson(day, data);

            if (data.HasWin)
            {
                SelectClosestAvailableDayToToday();
            }

            UpdateViews();
        }

        /// <summary>
        /// Re-derives each day's IsUnlocked flag against the real current date. IsUnlocked is
        /// computed once when the month's data is generated (see CreateDailyChallengeData) and
        /// never revisited - if the app stays resident across a midnight rollover (paused/resumed
        /// rather than fully killed, as most mobile OSes do), tomorrow's entry stays stuck as
        /// "not yet unlocked" and the Main Menu widget falls back to showing "Completed" even
        /// though nothing was won or lost. Callers should invoke this whenever the app regains
        /// focus and on every Daily Challenge widget refresh so the day boundary self-heals
        /// without requiring a full process restart.
        /// </summary>
        public void RefreshForPossibleDayChange()
        {
            var today = DailyChallengeClock.Now.Date;

            if (today.Year != year || today.Month != month)
            {
                Initialize(today.Year, today.Month);
                return;
            }

            if (dailyChallengeData == null)
            {
                return;
            }

            var changed = false;
            foreach (var data in dailyChallengeData)
            {
                if (data == null)
                {
                    continue;
                }

                var entryDate = new DateTime(data.Year, data.Month, data.Day).Date;
                var shouldBeUnlocked = entryDate <= today;
                if (data.IsUnlocked != shouldBeUnlocked)
                {
                    data.IsUnlocked = shouldBeUnlocked;
                    changed = true;
                }
            }

            if (changed)
            {
                UpdateViews();
            }
        }

        public DailyChallengePlayState GetDayLevelState(int day, int levelIndex)
        {
            if (dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                Debug.LogWarning($"DailyChallengeController: invalid day index {day}.");
                return DailyChallengePlayState.NotPlayed;
            }

            var data = dailyChallengeData[day - 1];
            return data.GetLevelState(levelIndex);
        }

        public void MarkDayAllLevelsWon(int day)
        {
            if (dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                Debug.LogWarning($"DailyChallengeController: invalid day index {day}.");
                return;
            }

            var data = dailyChallengeData[day - 1];
            for (var i = 0; i < data.LevelCount; i++)
            {
                data.SetLevelState(i, DailyChallengePlayState.Win);
            }

            data.SetPlayState(DailyChallengePlayState.Win);
            SaveLevelDataToJson(day, data);
            SelectClosestAvailableDayToToday();
            UpdateViews();
        }

        public void MarkDayLevelWon(int day, int levelIndex)
        {
            SetDayLevelState(day, levelIndex, DailyChallengePlayState.Win);
        }

        public void MarkDayLevelLost(int day, int levelIndex)
        {
            SetDayLevelState(day, levelIndex, DailyChallengePlayState.Lost);
        }

        public void MarkDayLevelNotPlayed(int day, int levelIndex)
        {
            SetDayLevelState(day, levelIndex, DailyChallengePlayState.NotPlayed);
        }

        private void UpdateOverallStateFromLevels(DailyChallengeData data, int day)
        {
            if (data == null || data.LevelStates == null || data.LevelStates.Length == 0)
            {
                return;
            }

            var allWon = true;
            var anyLost = false;

            foreach (var levelState in data.LevelStates)
            {
                if (levelState != DailyChallengePlayState.Win)
                {
                    allWon = false;
                }

                if (levelState == DailyChallengePlayState.Lost)
                {
                    anyLost = true;
                }
            }

            if (allWon)
            {
                data.SetPlayState(DailyChallengePlayState.Win);
            }
            else if (anyLost)
            {
                data.SetPlayState(DailyChallengePlayState.Lost);
            }
            else if (data.LevelStates[0] != DailyChallengePlayState.NotPlayed)
            {
                data.SetPlayState(data.LevelStates[0]);
            }
        }

        public void UpdateButtonForDay(int day)
        {
            if (dayButtons == null || dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                return;
            }

            var button = dayButtons[day - 1];
            if (button == null)
            {
                return;
            }

            button.Initialize(dailyChallengeData[day - 1], this);
        }

        private void SelectDefaultDay()
        {
            if (dailyChallengeData == null || dayButtons == null)
            {
                return;
            }

            var today = DailyChallengeClock.Now;
            if (today.Year != year || today.Month != month)
            {
                SelectFirstAvailableDay();
                return;
            }

            if (!SelectClosestAvailableDayToToday())
            {
                // Nothing selectable at all (shouldn't normally happen - today is always
                // selectable unless it's already won, which SelectClosestAvailableDayToToday
                // already handles by landing on today directly) - fall back to first available
                // day overall as a last resort.
                SelectFirstAvailableDay();
            }
        }

        private bool SelectClosestAvailableDayToToday()
        {
            var today = DailyChallengeClock.Now;
            if (today.Year != year || today.Month != month)
            {
                return false;
            }

            var todayIndex = today.Day - 1;
            if (todayIndex < 0 || todayIndex >= dailyChallengeData.Count)
            {
                return false;
            }

            // Today's challenge is already completed - always land on today rather than
            // resurfacing an older missed day, so a completed today keeps showing today (its
            // result/reward state) instead of jumping back to some earlier incomplete date.
            if (dailyChallengeData[todayIndex]?.HasWin == true)
            {
                if (TrySelectDayAtIndex(todayIndex, ignoreCanSelect: true))
                {
                    return true;
                }

                return false;
            }

            // Today isn't won yet - walk backward from today: today, today-1, today-2, ... day 1.
            // This lands on today immediately (today is always unlocked and not yet won here),
            // unless something unexpected blocks it, in which case the most recent still-
            // incomplete earlier day is used instead (a missed previous day).
            for (var index = todayIndex; index >= 0; index--)
            {
                if (TrySelectDayAtIndex(index))
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectFirstAvailableDay()
        {
            for (var index = 0; index < dailyChallengeData.Count && index < dayButtons.Count; index++)
            {
                if (TrySelectDayAtIndex(index))
                {
                    return;
                }
            }
        }
        /// <summary>
        /// Fired whenever the selected calendar day changes - the popup's Play/Watch Ad/Continue
        /// CTA (kept as a separate component) subscribes to this to refresh its label/action
        /// instead of re-deriving "what's selected" itself.
        /// </summary>
        public event Action<DailyChallengeData> SelectedDayChanged;

        private void SetSelectedButton(DailyChallengeDateButton button)
        {
            if (button == null)
            {
                return;
            }

            if (selectedButton != null)
            {
                selectedButton.SelectState(false);
            }

            selectedButton = button;
            selectedButton.SelectState(true);
            SelectedDayChanged?.Invoke(selectedButton.Data);
        }
        // ignoreCanSelect lets a caller land on a day that wouldn't normally be selectable for
        // gameplay (e.g. already won) purely to show it as the default selection - see the
        // "today already completed" case in SelectClosestAvailableDayToToday.
        private bool TrySelectDayAtIndex(int index, bool ignoreCanSelect = false)
        {
            if (index < 0 || index >= dailyChallengeData.Count || index >= dayButtons.Count)
            {
                return false;
            }

            var button = dayButtons[index];
            if (button == null)
            {
                return false;
            }

            if (!ignoreCanSelect && !dailyChallengeData[index].CanSelect)
            {
                return false;
            }

            SetSelectedButton(button);
            return true;
        }


        private void SetSelectedDayLevelWin(int levelIndex)
        {
            if (selectedButton == null || selectedButton.Data == null)
            {
                Debug.LogWarning("[DailyChallenges] No day selected. Please select a date first.");
                return;
            }

            var data = selectedButton.Data;

            if (levelIndex < 0 || levelIndex >= data.LevelCount)
            {
                Debug.LogWarning($"[DailyChallenges] Invalid level index {levelIndex} for day {data.Day:00}. LevelCount={data.LevelCount}");
                return;
            }

            if (data.GetLevelState(levelIndex) == DailyChallengePlayState.Win)
            {
                return;
            }

            data.SetLevelState(levelIndex, DailyChallengePlayState.Win);

            int wonCount = 0;
            for (int i = 0; i < data.LevelCount; i++)
            {
                if (data.GetLevelState(i) == DailyChallengePlayState.Win)
                {
                    wonCount++;
                }
            }

            if (wonCount >= data.LevelCount)
            {
                data.SetPlayState(DailyChallengePlayState.Win);

                SaveLevelDataToJson(data.Day, data);
                SelectClosestAvailableDayToToday();
                UpdateViews();
                return;
            }

            UpdateOverallStateFromLevels(data, data.Day);
            SaveLevelDataToJson(data.Day, data);
            UpdateViews();

            if (selectedButton != null)
            {
                selectedButton.Refresh();
            }
        }

        private void UpdateMonthLabel(int year, int month)
        {
            if (monthLabel == null)
            {
                return;
            }

            var firstOfMonth = new DateTime(year, month, 1);
            monthLabel.text = firstOfMonth.ToString("MMMM");
        }

        private void OnValidate()
        {
            if (dayButtons == null)
            {
                return;
            }

            dayButtons = dayButtons.FindAll(x => x != null);
        }

        private void UpdateViews()
        {
            if (dayButtons == null || dailyChallengeData == null)
            {
                return;
            }

            for (var index = 0; index < dayButtons.Count; index++)
            {
                var dayButton = dayButtons[index];
                if (dayButton == null || dayButton.gameObject == null)
                {
                    continue;
                }

                if (index >= dailyChallengeData.Count)
                {
                    dayButton.gameObject.SetActive(false);
                    continue;
                }

                dayButton.gameObject.SetActive(true);
                dayButton.Initialize(dailyChallengeData[index], this);
            }

            // Initialize() resets each button's color from its play state, which wipes out
            // the selection highlight. Reapply it to whichever button is still selected.
            selectedButton?.SelectState(true);
        }

        public void DebugPrintData()
        {
            if (dailyChallengeData == null)
            {
                return;
            }

            foreach (var data in dailyChallengeData)
            {
                // Debug.Log(data);
            }
        }

        [ContextMenu("Initialize Test Data")]
        public void InitializeTestData()
        {
            if (dailyChallengeData == null)
            {
                Debug.LogWarning("DailyChallengeController: Initialize controller first with InitializeCurrentMonth() or Initialize()");
                return;
            }

            for (var day = 1; day <= 5 && day <= dailyChallengeData.Count; day++)
            {
                var data = dailyChallengeData[day - 1];

                if (!data.IsUnlocked)
                {
                    continue;
                }

                if (!data.IsPuzzleAssigned)
                {
                    var puzzleIndex = jsonConfig != null ? jsonConfig.nextPuzzleIndex % TotalLevels : day;
                    data.SetPuzzleIndex(puzzleIndex);
                    data.SetLevelCount(TotalSubLevels);

                    if (jsonConfig != null)
                    {
                        jsonConfig.nextPuzzleIndex++;
                    }
                }

                switch (day)
                {
                    case 1:
                        data.SetLevelState(0, DailyChallengePlayState.Win);
                        data.SetLevelState(1, DailyChallengePlayState.NotPlayed);
                        data.SetLevelState(2, DailyChallengePlayState.NotPlayed);
                        break;
                    case 2:
                        data.SetLevelState(0, DailyChallengePlayState.Win);
                        data.SetLevelState(1, DailyChallengePlayState.Win);
                        data.SetLevelState(2, DailyChallengePlayState.Win);
                        data.SetPlayState(DailyChallengePlayState.Win);
                        break;
                    case 3:
                        data.SetLevelState(0, DailyChallengePlayState.Lost);
                        data.SetLevelState(1, DailyChallengePlayState.NotPlayed);
                        data.SetLevelState(2, DailyChallengePlayState.NotPlayed);
                        data.SetPlayState(DailyChallengePlayState.Lost);
                        break;
                    case 4:
                        break;
                    case 5:
                        data.SetLevelState(0, DailyChallengePlayState.Win);
                        data.SetLevelState(1, DailyChallengePlayState.Win);
                        data.SetLevelState(2, DailyChallengePlayState.NotPlayed);
                        break;
                }

                SaveLevelDataToJson(day, data);
            }

            UpdateViews();
        }

        [ContextMenu("Clear All Test Data")]
        public void ClearAllTestData()
        {
            if (dailyChallengeData == null || jsonConfig == null)
            {
                return;
            }

            jsonConfig.nextPuzzleIndex = 1;

            for (var day = 1; day <= dailyChallengeData.Count; day++)
            {
                var data = dailyChallengeData[day - 1];
                data.SetPuzzleIndex(-1);

                for (var i = 0; i < data.LevelCount; i++)
                {
                    data.SetLevelState(i, DailyChallengePlayState.NotPlayed);
                }

                data.SetPlayState(DailyChallengePlayState.NotPlayed);
                SaveLevelDataToJson(day, data);
            }

            UpdateViews();
        }

        public DailyChallengeJsonConfig CreateJsonConfig()
        {
            if (jsonConfig != null)
            {
                return jsonConfig;
            }

            if (dailyChallengeData == null)
            {
                return null;
            }

            var config = CreateDefaultJsonConfig(year);
            var monthState = config.monthlyStates?[month - 1];
            if (monthState == null)
            {
                return config;
            }

            config.monthlyStates[month - 1] = monthState;
            return config;
        }

        public void ExportJsonConfig(string fileName = null)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = GetJsonResourceName(year) + ".json";
            }

            var config = CreateJsonConfig();
            if (config == null)
            {
                Debug.LogWarning("DailyChallengeController: no data available to export.");
                return;
            }

            var json = JsonUtility.ToJson(config, true);
            var folderPath = Path.Combine(Application.dataPath, "Resources");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var filePath = Path.Combine(folderPath, fileName);
            File.WriteAllText(filePath, json);
        }

        #endregion
    }
}
