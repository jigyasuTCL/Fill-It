using System;
using Shared.Analytics;
using Shared.Notifications;
using Shared.PlayerData;
using UnityEngine;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        private const string NextDayNotificationId = "daily_challenge_next_day";
        private const int NextDayNotificationHourLocal = 10;

        #region Level progression

        private int GetNextLevelIndexToPlay(DailyChallengeData data)
        {
            if (data == null)
            {
                return -1;
            }

            for (var i = 0; i < data.LevelCount; i++)
            {
                if (data.GetLevelState(i) != DailyChallengePlayState.Win)
                {
                    return i;
                }
            }

            return -1;
        }

        // The Daily Challenge session currently in gameplay - set by EnterGameplay, independent of
        // selectedButton so direct-play from the Main Menu PLAY CTA (no calendar button tapped)
        // reports results correctly too.
        private DailyChallengeData activeChallengeData;

        /// <summary>
        /// Fired right as gameplay is about to start for a resolved (data, levelIndex) pair - the
        /// Cat Flap-side bridge (kept outside this Shared feature) subscribes to this to actually
        /// load the level and to report the eventual result back via ReportGameplayResult.
        /// </summary>
        public event Action<DailyChallengeData, int> DailyChallengeGameplayRequested;

        void SetPlayButtonState(DailyChallengeData data, DailyChallengeDateButton button)
        {
            ResetAdWatchStateForNewDay();

            if (ShouldRequireAdForSelectedDate(data))
            {
                if (dailyChallengeManager != null)
                {
                    dailyChallengeManager.WatchAdForDailyChallenge(() =>
                    {
                        MarkAdWatchedForDate(data);
                    });
                    return;
                }

                MarkAdWatchedForDate(data);
                return;
            }
        }

        /// <summary>
        /// What a caller must do before playing this day's current sub-level: Free (no gate),
        /// RewardedAd (must watch one first), Completed (nothing left to play), or Unavailable
        /// (locked/not yet reachable). Callers (Main Menu widget, calendar popup) branch on this;
        /// none of them decide the rule themselves.
        /// </summary>
        public DailyChallengePlayRequirement GetPlayRequirement(DailyChallengeData data)
        {
            if (data == null || !data.IsUnlocked)
            {
                return DailyChallengePlayRequirement.Unavailable;
            }

            if (data.HasWin)
            {
                return DailyChallengePlayRequirement.Completed;
            }

            ResetAdWatchStateForNewDay();
            return ShouldRequireAdForSelectedDate(data)
                ? DailyChallengePlayRequirement.RewardedAd
                : DailyChallengePlayRequirement.Free;
        }

        /// <summary>
        /// Today's DailyChallengeData, if the currently displayed month/year includes today.
        /// </summary>
        public DailyChallengeData GetTodaysChallenge()
        {
            if (dailyChallengeData == null)
            {
                return null;
            }

            var today = DailyChallengeClock.Now.Date;
            for (var i = 0; i < dailyChallengeData.Count; i++)
            {
                var data = dailyChallengeData[i];
                if (data != null && data.Year == today.Year && data.Month == today.Month && data.Day == today.Day)
                {
                    return data;
                }
            }

            return null;
        }

        /// <summary>
        /// What the Main Menu PLAY CTA is allowed to jump straight into: today's challenge, as
        /// long as it isn't fully won yet - never completed, never a missed previous day. A Hard
        /// fail (HasLost) still leaves easier sub-levels replayable, so the widget keeps showing
        /// Play rather than routing to the popup; only a full win (HasWin) or a missed previous
        /// day forces the popup instead - the popup's own default selection walks the calendar
        /// itself (see SelectClosestAvailableDayToToday).
        /// </summary>
        public DailyChallengeData GetTodaysDirectPlayChallenge()
        {
            var today = GetTodaysChallenge();
            if (today == null || !today.IsUnlocked || today.HasWin)
            {
                return null;
            }

            return today;
        }

        /// <summary>
        /// True if there's a Daily Challenge attempt in progress for this exact day (started,
        /// neither won nor lost nor abandoned) - used to show "Continue" instead of "Play"/"Watch
        /// Ad to Play" in the popup.
        /// </summary>
        public bool HasInProgressAttempt(DailyChallengeData data)
        {
            if (data == null || jsonConfig == null || !jsonConfig.attemptInProgress)
            {
                return false;
            }

            return jsonConfig.attemptYear == data.Year && jsonConfig.attemptMonth == data.Month && jsonConfig.attemptDay == data.Day;
        }

        public void PlayButtonClick()
        {
            if (selectedButton == null || selectedButton.Data == null)
            {
                Debug.LogWarning("DailyChallengeController: no day selected to play.");
                return;
            }

            TryPlayDailyChallenge(selectedButton.Data);
        }

        /// <summary>
        /// Resolves and plays whatever Daily Challenge is currently playable without requiring a
        /// calendar day to have been tapped first - the Main Menu PLAY CTA uses this to jump
        /// straight into gameplay instead of opening the calendar popup (see GDD section 3/8).
        /// </summary>
        public void PlayCurrentChallengeDirectly()
        {
            var data = GetTodaysDirectPlayChallenge();
            if (data == null)
            {
                return;
            }

            TryPlayDailyChallenge(data);
        }

        /// <summary>
        /// Continues the same day's challenge - either resuming an in-progress attempt (Continue)
        /// or moving on to the next sub-level after a win (Continue from the result popup). Both
        /// just re-enter the normal ad-gate/EnterGameplay flow for the same DailyChallengeData;
        /// GetNextLevelIndexToPlay naturally resolves to whichever sub-level is next.
        /// </summary>
        public void ContinueChallenge(DailyChallengeData data)
        {
            if (data == null)
            {
                Debug.LogWarning("DailyChallengeController: cannot continue a null Daily Challenge day.");
                return;
            }

            TryPlayDailyChallenge(data);
        }

        private void TryPlayDailyChallenge(DailyChallengeData data)
        {
            ResetAdWatchStateForNewDay();

            if (ShouldRequireAdForSelectedDate(data))
            {
                if (dailyChallengeManager != null)
                {
                    dailyChallengeManager.WatchAdForDailyChallenge(() =>
                    {
                        MarkAdWatchedForDate(data);
                        EnterGameplay(data);
                    });
                    return;
                }

                // No ad manager wired up - don't hard-block the player, just mark it watched.
                MarkAdWatchedForDate(data);
                EnterGameplay(data);
                return;
            }

            EnterGameplay(data);
        }

        private void EnterGameplay(DailyChallengeData data)
        {
            if (!data.IsPuzzleAssigned)
            {
                AssignPuzzleIndexToDay(data);
                selectedButton?.Refresh();
            }

            activeChallengeData = data;
            var levelIndex = GetNextLevelIndexToPlay(data);

            // Mark this sub-level as "opened, mid-attempt" the moment gameplay actually starts,
            // so the calendar shows in-progress (UnlockedWithProgress) immediately rather than
            // only after a loss - cleared by ReportGameplayResult's SetDayLevelState(Win/Lost)
            // once it resolves (see DailyChallengePlayState.Played).
            if (levelIndex >= 0)
            {
                SetDayLevelState(data.Day, levelIndex, DailyChallengePlayState.Played);
            }

            BeginAttempt(data, levelIndex);

            if (dailyChallengeManager != null)
            {
                dailyChallengeManager.ShowGamePlayPanel();
            }

            // preLevelPlayed/totalLevelPlayed are lifetime counters (across every day's Daily
            // Challenge, not just today) - see PlayerAnalyticsData.DailyChallengeTotalLevelsPlayed.
            PlayerAnalyticsData analytics = PlayerData.Instance.Analytics;
            int preLevelPlayed = analytics.DailyChallengeTotalLevelsPlayed;
            analytics.DailyChallengeTotalLevelsPlayed++;
            AnalyticsManager.Instance.LogDailyChallenge(PlayerData.Instance.CurrentLevelIndex + 1, analytics.DailyChallengeOpenCount, currentLevelPlay: true, preLevelPlayed, analytics.DailyChallengeTotalLevelsPlayed);

            DailyChallengeGameplayRequested?.Invoke(data, levelIndex);
        }

        public void OnGameplayLevelWinButtonClicked()
        {
            ReportGameplayResult(DailyChallengePlayState.Win);
        }

        public void OnGameplayLevelLoseButtonClicked()
        {
            ReportGameplayResult(DailyChallengePlayState.Lost);
        }

        /// <summary>
        /// Fired right after ReportGameplayResult records a Win/Lost outcome - a result popup
        /// (kept outside this Shared feature, see CatFlapDailyChallengeBridge) subscribes to this
        /// to show "{x}/3 completed" plus Back/Continue/Retry, without owning any of that count
        /// logic itself.
        /// </summary>
        public event Action<DailyChallengeResultInfo> DailyChallengeResultResolved;

        /// <summary>
        /// The single entry point gameplay (or its Cat Flap-side bridge) uses to report a generic
        /// outcome back - Daily Challenge is the one that interprets it (which sub-level advances,
        /// whether the day is now complete), not the caller.
        /// </summary>
        public void ReportGameplayResult(DailyChallengePlayState result)
        {
            var data = activeChallengeData ?? selectedButton?.Data;
            if (data == null)
            {
                Debug.LogWarning("DailyChallengeController: no active Daily Challenge session, cannot resolve a result.");
                return;
            }

            var levelIndex = GetNextLevelIndexToPlay(data);
            if (levelIndex < 0)
            {
                Debug.LogWarning($"DailyChallengeController: Day {data.Day:00} has no unplayed sublevel to resolve.");
                return;
            }

            SetDayLevelState(data.Day, levelIndex, result);
            EndAttempt();
            activeChallengeData = null;

            if (dailyChallengeManager != null)
            {
                dailyChallengeManager.HideGamePlayPanel();
            }

            int completedCount = 0;
            for (var i = 0; i < data.LevelCount; i++)
            {
                if (data.GetLevelState(i) == DailyChallengePlayState.Win)
                {
                    completedCount++;
                }
            }

            if (data.HasWin)
            {
                // Advances reward-tier progress (see DailyChallengeController.Rewards.cs) - only
                // the day itself matters here, not which/how many sub-levels it took.
                MarkDayCompleted(data);
            }

            ScheduleNextDayNotification();

            DailyChallengeResultResolved?.Invoke(new DailyChallengeResultInfo(data, result, levelIndex, completedCount, data.LevelCount));
        }

        // Reminds the player that a new Daily Challenge is waiting - scheduled for
        // NextDayNotificationHourLocal (10am) on the day after any level result, win or lose.
        // Uses DailyChallengeClock.Now rather than the real clock directly, consistent with the
        // rest of this feature's day-boundary logic (see DailyChallengeClock's own doc comment).
        // fireAtLocalOverride lets EnsureUnlockNotificationScheduled below use a different target
        // (there's no "next day" to count from before the player has ever played a single Daily
        // Challenge level) while every other caller keeps the normal tomorrow-10am schedule.
        private void ScheduleNextDayNotification(DateTime? fireAtLocalOverride = null)
        {
            DateTime fireAtLocal = fireAtLocalOverride ?? DailyChallengeClock.Now.Date.AddDays(1).AddHours(NextDayNotificationHourLocal);

            LocalNotificationController.Instance.ScheduleNotification(
                NextDayNotificationId,
                fireAtLocal,
                "A new Daily Challenge is here!",
                "Come back and play today's puzzle.");
        }

        // How long after Daily Challenge first unlocks (with no prior play at all, so there's no
        // "next day" timer to count from yet) its reminder fires by default.
        private const float UnlockNotificationDefaultHours = 6f;

        // Reminds the player how many levels are left to reach Daily Challenge's own unlock
        // level, while it's still locked - a separate id from NextDayNotificationId so it never
        // collides with (or gets silently overwritten by) the "come back and play" reminder.
        private const string UnlockProgressNotificationId = "daily_challenge_unlock_progress";
        private const float UnlockProgressNotificationHours = 6f;

        // Schedules the SAME "come back and play" reminder immediately once Daily Challenge
        // unlocks, rather than leaving the player with no notification at all until they've
        // played (and won/lost) their first level - ScheduleNextDayNotification was previously
        // only ever called from ReportGameplayResult. Called once per session (see
        // Initialize/InitializeCurrentMonth), so if the resulting notification later gets wiped
        // by LocalNotificationAppStateWatcher.SuppressNotificationsOnResume (every app foreground
        // cancels ALL scheduled notifications, not just due ones) while the player still hasn't
        // played a first level, this simply re-arms it again next session - exactly like
        // DailyRewardManager/SpinWheelManager's own equivalent re-arm behavior. Once the player
        // actually plays a level, DailyChallengeTotalLevelsPlayed becomes &gt; 0 and
        // ReportGameplayResult's own real "tomorrow 10am" schedule takes over permanently, so this
        // early-return here for good.
        //
        // While Daily Challenge is still LOCKED (below config.DailyChallenge.UnlockLevel), this
        // instead schedules UnlockProgressNotificationId telling the player how many levels are
        // left to unlock it, re-armed every session (same wipe-by-foreground concern as above) so
        // the "levels remaining" count stays accurate as they progress. UnlockLevel == -1
        // ("coming soon", feature hidden entirely) gets no notification at all.
        private void EnsureUnlockNotificationScheduled()
        {
            Shared.GameConfig.GlobalConfig config = Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config
                : null;

            int unlockLevel = config?.DailyChallenge.UnlockLevel ?? 1;
            int currentLevelNumber = PlayerData.Instance.CurrentLevelIndex + 1;
            bool comingSoon = unlockLevel == -1;
            bool unlocked = !comingSoon && (unlockLevel <= 1 || currentLevelNumber >= unlockLevel);

            if (comingSoon)
            {
                LocalNotificationController.Instance.CancelNotification(UnlockProgressNotificationId);
                return;
            }

            if (!unlocked)
            {
                int levelsRemaining = Mathf.Max(1, unlockLevel - currentLevelNumber);
                LocalNotificationController.Instance.ScheduleNotification(
                    UnlockProgressNotificationId,
                    DateTime.Now.AddHours(UnlockProgressNotificationHours),
                    "Daily Challenge is almost open!",
                    $"Just {levelsRemaining} more level{(levelsRemaining == 1 ? "" : "s")} to unlock Daily Challenge.");
                return;
            }

            // Now unlocked - clear any still-locked reminder so it can never fire after the fact.
            LocalNotificationController.Instance.CancelNotification(UnlockProgressNotificationId);

            if (PlayerData.Instance.Analytics.DailyChallengeTotalLevelsPlayed > 0)
            {
                return;
            }

            ScheduleNextDayNotification(DateTime.Now.AddHours(UnlockNotificationDefaultHours));
        }

        /// <summary>
        /// Debug-only: simulates fully winning a day's Daily Challenge without playing any
        /// gameplay - marks every sub-level Win, advances reward-tier progress (MarkDayCompleted),
        /// and fires DailyChallengeResultResolved exactly like a real full-day win would. If
        /// CatFlapDailyChallengeBridge is active in the scene, that drives the real chain: the
        /// win result popup, then back to this calendar popup, then the reward track's
        /// auto-claim/collect animation - the same sequence a real win produces. Defaults to the
        /// currently selected day, falling back to today's challenge if none is selected.
        /// </summary>
        public void DebugSimulateFullDayWin(DailyChallengeData data = null)
        {
            data ??= SelectedData ?? GetTodaysChallenge();
            if (data == null)
            {
                Debug.LogWarning("DailyChallengeController: no day selected/available to debug-win.");
                return;
            }

            if (!data.IsPuzzleAssigned)
            {
                AssignPuzzleIndexToDay(data);
            }

            for (var i = 0; i < data.LevelCount; i++)
            {
                data.SetLevelState(i, DailyChallengePlayState.Win);
            }

            data.SetPlayState(DailyChallengePlayState.Win);
            SaveLevelDataToJson(data.Day, data);

            EndAttempt();
            activeChallengeData = null;
            dailyChallengeManager?.HideGamePlayPanel();

            if (data.HasWin)
            {
                MarkDayCompleted(data);
            }

            SelectClosestAvailableDayToToday();
            UpdateViews();

            DailyChallengeResultResolved?.Invoke(
                new DailyChallengeResultInfo(data, DailyChallengePlayState.Win, data.LevelCount - 1, data.LevelCount, data.LevelCount));
        }

        /// <summary>
        /// The player intentionally left gameplay (quit/Home) before reporting a win or lose for
        /// the active attempt - consume it: the next attempt at this same sub-level requires a
        /// fresh rewarded-ad watch, same as a loss would require (see GDD section 12).
        /// </summary>
        public void ReportGameplayInterrupted()
        {
            if (activeChallengeData == null && (jsonConfig == null || !jsonConfig.attemptInProgress))
            {
                return;
            }

            var data = activeChallengeData;
            if (data != null)
            {
                ClearAdWatchFlagForDate(data);
            }
            else if (jsonConfig != null)
            {
                ClearAdWatchFlagForDate(jsonConfig.attemptYear, jsonConfig.attemptMonth, jsonConfig.attemptDay);
            }

            activeChallengeData = null;
            EndAttempt();
        }

        private void BeginAttempt(DailyChallengeData data, int levelIndex)
        {
            if (jsonConfig == null)
            {
                return;
            }

            jsonConfig.attemptInProgress = true;
            jsonConfig.attemptYear = data.Year;
            jsonConfig.attemptMonth = data.Month;
            jsonConfig.attemptDay = data.Day;
            jsonConfig.attemptLevelIndex = levelIndex;
            SaveJsonConfig();
        }

        private void EndAttempt()
        {
            if (jsonConfig == null)
            {
                return;
            }

            jsonConfig.attemptInProgress = false;
            SaveJsonConfig();
        }

        /// <summary>
        /// Called once from Initialize. If the previous session left an attempt marked
        /// in-progress, the app never got a chance to report win/lose/quit for it - a crash or
        /// force-quit. Treat it exactly like ReportGameplayInterrupted (see GDD section 13).
        /// </summary>
        private void RecoverInterruptedAttempt()
        {
            if (jsonConfig == null || !jsonConfig.attemptInProgress)
            {
                return;
            }

            ClearAdWatchFlagForDate(jsonConfig.attemptYear, jsonConfig.attemptMonth, jsonConfig.attemptDay);
            jsonConfig.attemptInProgress = false;
            SaveJsonConfig();
        }


        // private void PlayDailyChallenge(DailyChallengeData data)
        // {
        //     if (!data.IsPuzzleAssigned)
        //     {
        //         AssignPuzzleIndexToDay(data);
        //         selectedButton?.Refresh();
        //         Debug.Log($"DailyChallengeController: Day {data.Day:00} puzzle assigned on play: {data.GetCurrentPuzzleName()}");
        //     }

        //     var levelIndex = GetNextLevelIndexToPlay(data);
        //     if (levelIndex < 0)
        //     {
        //         Debug.Log($"DailyChallengeController: Day {data.Day:00} has no unplayed sublevel left.");
        //         return;
        //     }

        //     var winState = SubLevelWin;
        //     var loseState = SubLevelLoss;

        //     SubLevelWin = false;
        //     SubLevelLoss = false;

        //     if (winState)
        //     {
        //         SetDayLevelState(data.Day, levelIndex, DailyChallengePlayState.Win);
        //         Debug.Log($"DailyChallengeController: Day {data.Day:00} level {levelIndex} set to Win.");
        //         return;
        //     }

        //     if (loseState)
        //     {
        //         SetDayLevelState(data.Day, levelIndex, DailyChallengePlayState.Lost);
        //         Debug.Log($"DailyChallengeController: Day {data.Day:00} level {levelIndex} set to Lost.");
        //         return;
        //     }

        //     SetDayLevelState(data.Day, levelIndex, DailyChallengePlayState.NotPlayed);
        //     Debug.Log($"DailyChallengeController: Day {data.Day:00} level {levelIndex} set to NotPlayed.");
        // }


        void AssignDailyChallengeLevel(DailyChallengeData data, DailyChallengeDateButton button)
        {
            SetSelectedButton(button);
            // Ad gating now happens on Play, not on selection - see TryPlayDailyChallenge.
        }

        private void AssignPuzzleIndexToDay(DailyChallengeData data)
        {
            if (data == null || jsonConfig == null)
            {
                return;
            }

            if (data.IsPuzzleAssigned)
            {
                return;
            }

            var puzzleIndex = jsonConfig.nextPuzzleIndex % TotalLevels;
            data.SetPuzzleIndex(puzzleIndex);
            data.SetLevelCount(TotalSubLevels);

            jsonConfig.nextPuzzleIndex++;
            SaveLevelDataToJson(data.Day, data);
        }

        public int GetNextPuzzleIndex()
        {
            return jsonConfig == null ? 1 : jsonConfig.nextPuzzleIndex;
        }

        public int GetDayPuzzleIndex(int day)
        {
            if (dailyChallengeData == null || day < 1 || day > dailyChallengeData.Count)
            {
                return 0;
            }

            return dailyChallengeData[day - 1].PuzzleIndex;
        }

        #endregion
    }
}
