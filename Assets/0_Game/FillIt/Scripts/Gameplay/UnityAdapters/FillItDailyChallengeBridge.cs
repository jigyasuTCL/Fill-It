using CatFlap.Runtime;
using DailyChallenges;
using MangraGames;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    /// <summary>
    /// The only place Cat Flap gameplay and the Shared Daily Challenge feature talk to each
    /// other. It does not own any calendar/date/queue/unlock/progression logic itself - that all
    /// stays inside DailyChallengeController. This bridge:
    /// 1) reacts to DailyChallengeController.DailyChallengeGameplayRequested by loading the
    ///    resolved level through the normal Cat Flap runtime, behind a level-start cover overlay,
    /// 2) reports the already-generic CatFlapRuntimeController.LevelWon/LevelFailed/OutOfMistakes
    ///    events (and a Home/quit while a session is active) back to
    ///    DailyChallengeController.ReportGameplayResult/ReportGameplayInterrupted - gameplay
    ///    itself never branches on "is this a Daily Challenge" internally, and
    /// 3) presents the win/fail result popup once DailyChallengeController resolves a result,
    ///    wiring its Back/Continue/Retry actions back into DailyChallengeController/menu
    ///    navigation - the popup itself only presents whatever labels/actions it's given.
    /// </summary>
    public sealed class CatFlapDailyChallengeBridge : MonoBehaviour
    {
        [SerializeField] private CatFlapGameplayManager gameplayManager;
        [SerializeField] private DailyChallengeManager dailyChallengeManager;
        [SerializeField] private MainMenuHandler mainMenuHandler;
        [SerializeField] private CatFlapDailyChallengeWinPopupController winResultPopup;
        [SerializeField] private CatFlapDailyChallengeFailPopupController failResultPopup;

        [SerializeField] private string winTitle = "Level Complete!";
        [SerializeField] private string failTitle = "Level Failed";
        [SerializeField] private string continueLabel = "Continue";
        // Same default as LevelWinPopupController.beatPlayersHighlightColor, for visual
        // consistency between the two "You beat X% of players" lines.
        [SerializeField] private Color winTextHighlightColor = new Color(0.99215686f, 0.6313726f, 0.5176471f);

        private DailyChallengeController controller;
        private bool isDailyChallengeSessionActive;
        private bool reopenDailyChallengePopupOnMainMenuReturn;

        private void OnEnable()
        {
            controller = dailyChallengeManager != null ? dailyChallengeManager.Controller : null;
            if (controller != null)
            {
                controller.DailyChallengeGameplayRequested += HandleGameplayRequested;
                controller.DailyChallengeResultResolved += HandleResultResolved;
            }

            if (gameplayManager != null)
            {
                // Deliberately NOT keying off ReturnedToMainMenu for the interrupted-report itself
                // (HandleAttemptAbandoned/ReportInterruptedIfActive) - that also fires for a plain
                // Home pause, which must stay resumable (Continue), not be treated as an
                // interrupted attempt. ReturnedToMainMenu IS used below (HandleReturnedToMainMenu)
                // for two unrelated, independently-gated things - see that method's own comment.
                gameplayManager.DailyChallengeAttemptAbandoned.AddListener(HandleAttemptAbandoned);
                gameplayManager.DailyChallengeSessionRestarted.AddListener(HandleSessionRestarted);
                gameplayManager.ReturnedToMainMenu.AddListener(HandleReturnedToMainMenu);
            }

            // The only place a calendar cell (DailyChallengeDateButton, Shared) reaches CatFlap's
            // saved-board progress data - a static delegate rather than a direct reference, so
            // that Shared feature never needs to know CatFlapGameplayManager exists.
            DailyChallengeDateButton.ProgressPercentProvider = TryGetProgressPercent;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.DailyChallengeGameplayRequested -= HandleGameplayRequested;
                controller.DailyChallengeResultResolved -= HandleResultResolved;
            }

            UnbindRuntimeEvents();

            if (gameplayManager != null)
            {
                gameplayManager.DailyChallengeAttemptAbandoned.RemoveListener(HandleAttemptAbandoned);
                gameplayManager.DailyChallengeSessionRestarted.RemoveListener(HandleSessionRestarted);
                gameplayManager.ReturnedToMainMenu.RemoveListener(HandleReturnedToMainMenu);
            }

            DailyChallengeDateButton.ProgressPercentProvider = null;
        }

        private float? TryGetProgressPercent(int puzzleIndex, int subLevelIndex)
        {
            if (gameplayManager != null && gameplayManager.TryGetDailyChallengeProgressPercent(puzzleIndex, subLevelIndex, out float percent))
            {
                return percent;
            }

            return null;
        }

        private void OnApplicationQuit()
        {
            ReportInterruptedIfActive();
        }

        private void HandleGameplayRequested(DailyChallengeData data, int levelIndex)
        {
            if (gameplayManager == null || data == null || levelIndex < 0)
            {
                Debug.LogWarning("CatFlapDailyChallengeBridge: ignoring request - gameplayManager not assigned, or invalid data/levelIndex.");
                return;
            }

            UnbindRuntimeEvents();
            var challengeDate = new System.DateTime(data.Year, data.Month, data.Day);

            // Fade in first, THEN get whichever CTA started this (Main Menu widget or the popup's
            // own Play/Watch Ad/Continue button) out of the way once fully covered, THEN actually
            // load the level - its board-enter animation plays underneath while the overlay fades
            // back out, instead of the level loading/swapping in uncovered before this even ran.
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => mainMenuHandler?.HideForDailyChallengeGameplay(),
                revealNewPage: () =>
                {
                    bool loaded = gameplayManager.LoadDailyChallengeLevel(data.PuzzleIndex, levelIndex, challengeDate);
                    if (!loaded)
                    {
                        return;
                    }

                    isDailyChallengeSessionActive = true;
                    BindRuntimeEvents();
                });
        }

        private void BindRuntimeEvents()
        {
            CatFlapRuntimeController runtimeController = gameplayManager != null ? gameplayManager.Controller : null;
            if (runtimeController == null)
            {
                return;
            }

            // OutOfMistakes only means "show the Out-of-Lives popup and let the player choose" -
            // it is NOT a final result. Reacting to it here would show the Daily Challenge fail
            // popup at the same moment as Out-of-Lives. The real result only arrives via
            // LevelFailed, which fires once the player actually taps Give Up
            // (CatFlapGameplayManager.HandleOutOfLivesGiveUp -> controller.ForceFail()) - the
            // intended flow is Out-of-Lives -> (Watch Ad/Refill continues, or Give Up) -> Challenge
            // Fail, never both popups at once.
            runtimeController.LevelWon += HandleLevelWon;
            runtimeController.LevelFailed += HandleLevelFailed;
        }

        private void UnbindRuntimeEvents()
        {
            CatFlapRuntimeController runtimeController = gameplayManager != null ? gameplayManager.Controller : null;
            if (runtimeController == null)
            {
                return;
            }

            runtimeController.LevelWon -= HandleLevelWon;
            runtimeController.LevelFailed -= HandleLevelFailed;

            if (gameplayManager != null)
            {
                gameplayManager.LevelWinCelebrationFinished -= HandleLevelWinCelebrationFinished;
            }
        }

        private void HandleLevelWon(object sender, System.EventArgs eventArgs)
        {
            // Waits for the same celebration beat a normal level's win popup waits for (see
            // CatFlapGameplayManager.PlayLevelWinCelebrationThenShowPopup/LevelWinCelebrationFinished)
            // before reporting the Win - previously this reported (and so showed the DC win popup)
            // immediately on LevelWon, popping it up while the board was still mid-celebration.
            if (gameplayManager != null)
            {
                gameplayManager.LevelWinCelebrationFinished -= HandleLevelWinCelebrationFinished;
                gameplayManager.LevelWinCelebrationFinished += HandleLevelWinCelebrationFinished;
            }
            else
            {
                ReportResult(DailyChallengePlayState.Win);
            }
        }

        private void HandleLevelWinCelebrationFinished()
        {
            if (gameplayManager != null)
            {
                gameplayManager.LevelWinCelebrationFinished -= HandleLevelWinCelebrationFinished;
            }

            ReportResult(DailyChallengePlayState.Win);
        }

        private void HandleLevelFailed(object sender, System.EventArgs eventArgs)
        {
            ReportResult(DailyChallengePlayState.Lost);
        }

        private void ReportResult(DailyChallengePlayState result)
        {
            if (!isDailyChallengeSessionActive || controller == null)
            {
                return;
            }

            isDailyChallengeSessionActive = false;
            UnbindRuntimeEvents();

            // A lost attempt is over - clear its saved board so the next attempt (Retry, or
            // coming back to it later) starts fresh instead of resuming the failed board.
            if (result == DailyChallengePlayState.Lost)
            {
                gameplayManager?.ClearDailyChallengeBoard();
            }

            controller.ReportGameplayResult(result);
        }

        /// <summary>
        /// DailyChallengeController has just recorded a Win/Lost outcome - present the matching
        /// result popup. Continue (win, more sub-levels left) and Retry (loss) both just re-enter
        /// ContinueChallenge for the same day; DailyChallengeController's own ad-gate
        /// (ShouldRequireAdForSelectedDate) already knows a just-recorded loss needs a fresh ad
        /// watch, so this bridge does not need to trigger the ad itself.
        /// </summary>
        private void HandleResultResolved(DailyChallengeResultInfo info)
        {
            if (controller == null)
            {
                return;
            }

            string progressText = $"{info.CompletedCount}/{info.TotalCount} completed";
            DailyChallengeData data = info.Data;

            if (info.Result == DailyChallengePlayState.Win)
            {
                if (winResultPopup == null)
                {
                    return;
                }

                bool dayComplete = info.IsDayComplete;

                int coinsBeforeWin = 0;
                int coinsWon = 0;
                gameplayManager?.GetDailyChallengeLevelWinCoinInfo(out coinsBeforeWin, out coinsWon);
                winResultPopup.SetCurrentCoin(coinsBeforeWin, coinsWon);
                winResultPopup.SetDate(FormatChallengeDate(data));

                winResultPopup.WatchAdRequested.RemoveListener(HandleWinWatchAd);
                winResultPopup.WatchAdRequested.AddListener(HandleWinWatchAd);

                // Same "You beat X% of players" line (and SO data source) as
                // LevelWinPopupController.SetLevelResultText, not the "{completed}/{total}
                // completed" progress counter - int.MaxValue forces the SO's out-of-range/random
                // branch every time, since a Daily Challenge sub-level has no meaningful index
                // into that per-main-game-level text list.
                string winProgressText = Shared.GameFlow.CatFlapLevelResultTextConfig.ApplyHighlight(
                    Shared.GameFlow.CatFlapLevelResultTextConfig.Load()?.GetWinText(int.MaxValue),
                    winTextHighlightColor);

                System.Action proceedAfterWin = dayComplete ? BackToDailyChallengePopup : (System.Action)(() => controller.ContinueChallenge(data));

                winResultPopup.Show(
                    winTitle,
                    winProgressText,
                    continueLabel,
                    showBack: !dayComplete,
                    // DC's own level-complete interstitial gate - shares the same normal-level-
                    // progression unlock/cooldown as LoadNextLevel (see
                    // CatFlapGameplayManager.TryShowLevelCompleteInterstitial), just with the DC
                    // ad placement. Falls back to proceeding with no ad if gameplayManager is
                    // somehow unassigned, so Continue never silently stops working.
                    primaryAction: () =>
                    {
                        if (gameplayManager != null)
                        {
                            gameplayManager.TryShowLevelCompleteInterstitial(isDailyChallenge: true, proceedAfterWin);
                        }
                        else
                        {
                            proceedAfterWin?.Invoke();
                        }
                    },
                    backAction: BackToDailyChallengePopup);
            }
            else
            {
                if (failResultPopup == null)
                {
                    return;
                }

                failResultPopup.SetDate(FormatChallengeDate(data));

                failResultPopup.Show(
                    failTitle,
                    progressText,
                    showBack: true,
                    primaryAction: () => controller.ContinueChallenge(data),
                    backAction: BackToDailyChallengePopup);

                // This popup doubles as CatFlapGameplayManager's "out of lives" (still-recoverable,
                // mid-level) popup, which DOES offer a revive option - but here it's presenting the
                // FINAL lost result (after Give Up), where reviving is no longer a real option, so
                // force those hidden regardless of whatever affordability/eligibility state is
                // still cached from an earlier out-of-lives showing this session.
                failResultPopup.HideReviveOptions();
            }
        }

        // "September 3" style label shown at the top of both result popups.
        private static string FormatChallengeDate(DailyChallengeData data)
        {
            return data != null
                ? new System.DateTime(data.Year, data.Month, data.Day).ToString("MMMM d")
                : string.Empty;
        }

        // Mirrors CatFlapGameplayManager.HandleLevelWinWatchAd's AdsManager.ShowRewarded(...)
        // pattern - once actually rewarded, doubles the Daily Challenge win popup's coin reward
        // and continues, same as tapping Primary.
        private void HandleWinWatchAd()
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DoubleCoinDcLevel, success =>
            {
                if (success)
                {
                    // Grants the wallet the second half of the reward before the popup's own
                    // DoubleWinCoinsAndContinue doubles what it merely DISPLAYS - see
                    // CatFlapGameplayManager.GrantDailyChallengeWatchAdBonus.
                    gameplayManager?.GrantDailyChallengeWatchAdBonus();
                    winResultPopup?.DoubleWinCoinsAndContinue();
                }
                else
                {
                    winResultPopup?.OnWatchAdDeclined();
                }
            });
        }

        private void BackToDailyChallengePopup()
        {
            gameplayManager?.SaveAndShowMainMenu();

            // SaveAndShowMainMenu() fires CatFlapGameplayManager.ReturnedToMainMenu, which
            // MainMenuHandler answers by forcing its Home tab toggle on (ActivateHomeTab) - going
            // through mainMenuHandler here (instead of dailyChallengeManager.ShowPopup()
            // directly) re-selects the bottom-nav Daily Challenges icon afterward, so it doesn't
            // keep showing Home as active while this popup is what's actually on screen.
            if (mainMenuHandler != null)
            {
                mainMenuHandler.OpenDailyChallengePopup();
                return;
            }

            dailyChallengeManager?.ShowPopup();
        }

        private void HandleAttemptAbandoned()
        {
            ReportInterruptedIfActive();

            // Give Up from the Out of Lives popup during a Daily Challenge attempt should land
            // back on the Daily Challenge popup, not a bare Home screen - same destination as
            // BackToDailyChallengePopup. This fires from inside
            // CatFlapGameplayManager.AbandonLevelToMainMenu BEFORE it calls ReturnToMainMenu(), so
            // the menu isn't showing yet here; defer to HandleReturnedToMainMenu instead of calling
            // OpenDailyChallengePopup directly.
            reopenDailyChallengePopupOnMainMenuReturn = true;
        }

        /// <summary>
        /// Fires on every return to the Main Menu, including a plain Home pause
        /// (CatFlapGameplayManager.SettingsHome) that must stay resumable. Two independent things
        /// happen here, each gated by its own flag so neither masks the other:
        /// 1) If reopenDailyChallengePopupOnMainMenuReturn was set (Give Up during a Daily
        ///    Challenge attempt - see HandleAttemptAbandoned), reopen the Daily Challenge popup
        ///    now that the menu has actually finished showing.
        /// 2) If isDailyChallengeSessionActive is still true (e.g. a plain Home pause
        ///    mid-attempt), stop listening to this attempt's
        ///    CatFlapRuntimeController.LevelWon/LevelFailed - without this, going Home
        ///    mid-attempt then playing (and winning/losing) a normal level would still hit this
        ///    bridge's stale HandleLevelWon/HandleLevelFailed, reporting that unrelated result to
        ///    DailyChallengeController and popping its result popup alongside the normal one.
        /// Neither branch reports anything to DailyChallengeController itself - that would
        /// incorrectly clear/interrupt an attempt that is only paused, not abandoned. Resuming
        /// the Daily Challenge later re-enters through HandleGameplayRequested, which rebinds
        /// normally either way.
        /// </summary>
        private void HandleReturnedToMainMenu()
        {
            if (reopenDailyChallengePopupOnMainMenuReturn)
            {
                reopenDailyChallengePopupOnMainMenuReturn = false;
                if (mainMenuHandler != null)
                {
                    mainMenuHandler.OpenDailyChallengePopup();
                }
                else
                {
                    dailyChallengeManager?.ShowPopup();
                }
            }

            if (isDailyChallengeSessionActive)
            {
                isDailyChallengeSessionActive = false;
                UnbindRuntimeEvents();
            }
        }

        /// <summary>
        /// CatFlapGameplayManager.RestartLevel() reloads the same Daily Challenge sub-level in
        /// place after Settings Quit already reported the discarded attempt as interrupted (see
        /// HandleAttemptAbandoned above), which left this bridge unbound/inactive. Re-bind so the
        /// new attempt's eventual LevelWon/LevelFailed reaches DailyChallengeController again.
        /// </summary>
        private void HandleSessionRestarted()
        {
            if (controller == null)
            {
                return;
            }

            UnbindRuntimeEvents();
            isDailyChallengeSessionActive = true;
            BindRuntimeEvents();
        }

        private void ReportInterruptedIfActive()
        {
            if (!isDailyChallengeSessionActive || controller == null)
            {
                return;
            }

            isDailyChallengeSessionActive = false;
            UnbindRuntimeEvents();
            controller.ReportGameplayInterrupted();
        }
    }
}
