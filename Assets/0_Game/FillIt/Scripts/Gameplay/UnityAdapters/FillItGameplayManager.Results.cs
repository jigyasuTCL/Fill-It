using System.Collections;
using FillIt.Levels;
using FillIt.Rules;
using Shared.Analytics;
using Shared.Boosters;
using Shared.GameFlow;
using Shared.RemoteConfig;
using MangraGames;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    // Level end flows: win celebration -> leaderboard -> win popup, fail, out of lives / revive,
    // interstitials, booster purchase and the store. Unchanged from the base project apart from
    // the board hooks - these are shared-feature flows, not gameplay.
    public partial class FillItGameplayManager
    {
        /// <summary>
        /// Fires once PlayLevelWinCelebrationThenShowPopup's celebration beat (tile-flip wait +
        /// happy mood/board pulse/confetti, waited out until every tile's own win-pulse has
        /// actually finished playing) has finished - for BOTH normal and Daily Challenge sessions,
        /// since that beat always plays regardless of session type. FillItDailyChallengeBridge
        /// waits for this before reporting a Win, so the DC win popup (like the normal one) only
        /// appears once the board's own scale-up celebration has visibly finished.
        /// </summary>
        public event System.Action LevelWinCelebrationFinished;

        public MainMenuHandler MainMenuHandler;
        /// <summary>
        /// The leaderboard to credit, falling back to a scene lookup when the Inspector reference
        /// is empty.
        ///
        /// Worth the fallback because the failure mode is invisible: an unassigned reference does
        /// not error, it just means every win scores nothing, and the board looks like it is broken
        /// rather than unwired. Resolved once and cached.
        /// </summary>
        private Shared.Leaderboard.LeaderboardManager ResolveLeaderboardManager()
        {
            if (leaderboardManager == null)
            {
                // Include inactive: the board lives on a UI root that is switched off whenever the
                // menu is not up, which is most of the time a level is being played.
                leaderboardManager = FindFirstObjectByType<Shared.Leaderboard.LeaderboardManager>(FindObjectsInactive.Include);

                if (leaderboardManager == null)
                {
                    WarnMissingLeaderboard();
                }
            }

            return leaderboardManager;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnMissingLeaderboard()
        {
            Debug.LogWarning(
                "[Leaderboard] No LeaderboardManager in the scene - level wins will score nothing. "
                + "Assign it on FillItGameplayManager.",
                this);
        }

        private void HandleLevelWon(object sender, System.EventArgs eventArgs)
        {
            // Freeze booster button state right here, before currentLevelIndex advances below -
            // CurrentLevelNumber (read by sharedBoosterManager's level-number provider) points at
            // the NEXT level from that point on, well before it actually loads, so nothing about
            // the boosters must change again until then (see BoosterManager.SuppressUntilNextConfigure).
            sharedBoosterManager?.SuppressUntilNextConfigure();

            // Disabled the instant a win is recorded, before the celebration/coin/popup animations
            // below even start - it must never be tappable through any of them.
            statusView?.DisableInfoButton();

            MainMenuHandler.OnLevelWinSpinTheWheel();
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LevelComplete);

            // Daily Challenge sub-levels are rewarded from their own config value
            // (GlobalConfig.DailyChallenge.LevelWinCoins), separate from the main game's
            // LevelWinCoins/HardLevelWinCoins - captured here (before AddCoins changes the wallet)
            // so FillItDailyChallengeBridge can feed the real amounts into
            // FillItDailyChallengeWinPopupController.SetCurrentCoin once its own result-resolved
            // handler runs (see GetDailyChallengeLevelWinCoinInfo).
            // Resolved while CurrentLevelNumber is still the level just won - currentLevelIndex
            // only advances further below - and cached in lastLevelWinCoinReward, since the win
            // popup and the 2x ad grant both need this exact amount after that point, when
            // CurrentLevelNumber already points at the NEXT level and so can no longer be asked
            // whether the WON level was hard.
            int levelWinCoinReward = ResolveLevelWinCoinReward(CurrentLevelNumber);
            lastLevelWinCoinReward = levelWinCoinReward;
            if (isDailyChallengeSession)
            {
                dailyChallengeCoinsBeforeWin = Mathf.Max(0, controller.Session.BoosterWallet.CurrentCoins);
                dailyChallengeCoinsWon = levelWinCoinReward;
            }

            controller?.Session.AddCoins(levelWinCoinReward);
            if (controller != null)
            {
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    levelWinCoinReward,
                    isDailyChallengeSession ? "daily_challenge_win" : "level_win",
                    controller.Session.BoosterWallet.CurrentCoins);
            }

            // Hearts left over become leaderboard points, 1:1. Read here, before currentLevelIndex
            // advances below, while Session.LevelMistakeLimit is still this level's - it is only
            // rebuilt by the next LoadAssignedLevel.
            //
            // Gated on LevelAttemptFeatureEnabled so wins below GlobalConfig.LevelAttemptUnlockLevel
            // score nothing: hearts ARE still spent on those levels, but the player is never shown
            // them, and charging for invisible mistakes isn't defensible. Daily Challenge is
            // excluded outright - it has its own reward track, doesn't touch normal progression,
            // and force-enables that same flag.
            lastLevelEarnedPoints = 0;
            if (!isDailyChallengeSession && controller != null &&
                (controller.LevelAttemptFeatureEnabled || controller.Session.TimerEnabled))
            {
                // Hearts left over ARE the points - one heart, one point, no conversion. Losing
                // a heart costs a point, so a flawless level is worth the full mistake limit.
                lastLevelEarnedPoints = Mathf.Max(0, controller.Session.LevelMistakeLimit.RemainingMistakes);
                if (controller.Session.TimerEnabled)
                {
                    // Timer mode spends no hearts, so the time left stands in for them: a fast
                    // finish is worth the full heart value, a last-second finish at least 1.
                    float timeLeft = Mathf.Clamp01(controller.Session.RemainingSeconds / Mathf.Max(1f, controller.Session.TimeLimitSeconds));
                    lastLevelEarnedPoints = Mathf.Max(1, Mathf.CeilToInt(timeLeft * controller.Session.LevelMistakeLimit.MaxMistakes));
                }

                // NOT credited here when the board is going to show it happening. The whole point
                // of that sequence is that the player sees their OLD standing first and watches
                // the points land on it - crediting now would open the board on the result and
                // animate a climb that had already happened.
                //
                // Exactly one of these two paths runs. Granting in both would double every win.
                if (leaderboardPanel == null)
                {
                    Shared.Leaderboard.LeaderboardManager board = ResolveLeaderboardManager();
                    if (board != null)
                    {
                        board.AddWinPoints(lastLevelEarnedPoints);
                    }
                }
            }

            // A different cell's own delayed-validation coroutine (see StartDelayedValidation -
            // each cell's wrong/correct feedback runs independently and isn't cancelled by another
            // cell completing a line) may still be counting down when the winning tap lands. Left
            // running, it can fire ResolveMistake/ValidateBoard after the board has already won and
            // locked, painting a stale red rule-violation highlight onto an otherwise-completed
            // board. Every other level-ending path (restart/undo/quit/load) already cancels these;
            // winning must too.
            StopPendingMistake();

            // Every cell's Happy mood plays first, board-wide, with input still blocked - only
            // once that celebration beat finishes does the LevelWinUIRoot popup itself appear.
            // SuppressWinFailPages holds off RefreshStatusOnly()'s own automatic ShowState(Win)
            // call (FillItRuntimeController.RenderStatus fires it unconditionally whenever
            // session.GameState.Current is Won) so the HUD can still be refreshed (coins, hearts,
            // etc.) immediately without the popup jumping in ahead of the celebration. A Daily
            // Challenge session already holds this true for its whole attempt (see
            // LoadDailyChallengeLevel) and shows its own result popup instead - never clear it
            // back to false for that case.
            if (uiFlowManager != null && !isDailyChallengeSession)
            {
                uiFlowManager.SuppressWinFailPages = true;
            }

            // Status/HUD only, not RefreshViews' full board repaint - the winning line's own
            // reveal cascade (kicked off moments earlier, inside ValidateBoard's Won branch) is
            // still mid-flip right now, and a full repaint here would immediately force-stop it.
            controller?.RefreshStatusOnly();
            boardSaveManager?.Clear(controller?.Session);

            // Captured before currentLevelIndex advances below - this is the level number the
            // player actually just won, which is what the ads-button gate (and any other
            // this-level-specific popup content) must be evaluated against.
            int wonLevelNumber = CurrentLevelNumber;

            AnalyticsManager.Instance.LogLevelComplete(BuildLevelResultEventData(wonLevelNumber));

            // Progression advances the instant the win is recorded, not only when the player
            // presses Continue (LoadNextLevel) - otherwise closing the win popup or quitting from
            // it saves the level as never-won, and the home screen keeps showing the old level
            // number. LoadNextLevel no longer advances currentLevelIndex itself; it just loads
            // whatever this already-advanced index now points to.
            currentLevelIndex++;
            SaveProgress();

            StartCoroutine(PlayLevelWinCelebrationThenShowPopup(wonLevelNumber));
        }

        private IEnumerator PlayLevelWinCelebrationThenShowPopup(int wonLevelNumber)
        {
            // Skip Tutorial goes straight on with no celebration beat.
            bool skipCelebration = skippingFtueTutorial;
            skippingFtueTutorial = false;

            if (!skipCelebration && boardView != null)
            {
                // Let the winning move's own pop / region-complete animation land first.
                while (boardView.IsAnyCellAnimating())
                {
                    yield return null;
                }

                if (animationView != null && animationView.PageStateDelaySeconds > 0f)
                {
                    yield return new WaitForSeconds(animationView.PageStateDelaySeconds);
                }

                // FTUE levels skip the confetti - they slide straight into the next board.
                bool playWinParticles = !IsFtueLevel(wonLevelNumber);
                boardView.PlayLevelWinBoardAnimation(playWinParticles);

                // Wait for the whole pulse wave to finish, not just start.
                while (boardView.IsAnyLevelWinPulsePlaying())
                {
                    yield return null;
                }
            }

            LevelWinCelebrationFinished?.Invoke();

            if (isDailyChallengeSession)
            {
                // Daily Challenge shows its own result popup (FillItDailyChallengeBridge).
                yield break;
            }

            // FTUE levels before the last one skip the win popup - the board slides out and the
            // next level slides in instead.
            if (IsFtueLevel(wonLevelNumber) && wonLevelNumber < activeConfig.FtueEnabledUntilLevel)
            {
                yield return new WaitForSeconds(Mathf.Max(0f, ftueLevelWinExtraDelaySeconds));
                yield return PlayFtueLevelTransition();
                yield break;
            }

            // Leaderboard first (old standing, then the climb), then the win popup.
            yield return PlayLeaderboardWinFlow();

            if (uiFlowManager != null)
            {
                uiFlowManager.SuppressWinFailPages = false;
            }

            UpdateLevelWinPopup(wonLevelNumber);
            uiFlowManager?.ShowWin();
        }

        /// <summary>
        /// Opens the leaderboard on the pre-win standing, adds this level's points, and waits for
        /// the player to dismiss it.
        ///
        /// Waits on a flag rather than a fixed duration because there is no single length to wait
        /// for: the player may sit on the board, or press Collect and go through the whole reward
        /// screen first. Either way the win popup must not appear underneath.
        /// </summary>
        private IEnumerator PlayLeaderboardWinFlow()
        {
            if (leaderboardPanel == null)
            {
                yield break;
            }

            // A board the player cannot open from the menu must not be forced on them here. The
            // points are still credited - they just accrue quietly until it unlocks.
            Shared.Leaderboard.LeaderboardManager board = ResolveLeaderboardManager();
            if (board == null || !board.IsFeatureUnlocked)
            {
                board?.AddWinPoints(lastLevelEarnedPoints);
                yield break;
            }

            // The reward screen is part of this sequence, not a separate thing that happens
            // afterwards: pressing Collect on the board CLOSES the board and opens the popup, so
            // watching the board alone would call the flow finished the moment it handed over.
            Shared.Leaderboard.LeaderboardRewardPopupController rewardPopup =
                FindFirstObjectByType<Shared.Leaderboard.LeaderboardRewardPopupController>(FindObjectsInactive.Include);

            bool finished = false;
            void OnFinished() => finished = true;

            leaderboardPanel.WinFlowContinued += OnFinished;
            if (rewardPopup != null)
            {
                rewardPopup.Completed += OnFinished;
            }

            try
            {
                leaderboardPanel.OpenWithScoreGain(lastLevelEarnedPoints);

                // Held while EITHER is up. The last exit condition also covers the board being
                // dismissed some other way - the back button - which would otherwise leave the win
                // popup waiting forever.
                while (!finished
                    && (leaderboardPanel.IsOpen || (rewardPopup != null && rewardPopup.IsOpen)))
                {
                    yield return null;
                }
            }
            finally
            {
                leaderboardPanel.WinFlowContinued -= OnFinished;
                if (rewardPopup != null)
                {
                    rewardPopup.Completed -= OnFinished;
                }
            }
        }

        [SerializeField] private float ftueBoardSlideDurationSeconds = 0.4f;
        // FTUE levels 1-3 only - see PlayLevelWinCelebrationThenShowPopup's own call site.
        [SerializeField] private float ftueLevelWinExtraDelaySeconds = 0.25f;

        // FTUE levels 1..3's own "no popup" completion: slides the whole board (with its base -
        // see FillItBoardView.SlideLocalPosition, which moves the board's root
        // transform, never touched by Build/individual cell positioning) fully off-screen to the
        // left, then teleports it off-screen to the right and loads the next level into it while
        // nothing is visible, then slides it back in to its normal centered position - reusing
        // the SAME board instance throughout rather than juggling two live boards at once.
        // Also used by the Skip Tutorial flow (see SkipFtueTutorial) after auto-solving the
        // current board, so both a real completion and a skip land on the exact same transition.
        // FTUE levels' "no popup" completion: slide the board off to the left, reload the next
        // level while it's off-screen, then slide it back in from the right - one board instance.
        private IEnumerator PlayFtueLevelTransition()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (skipTutorialButtonRoot != null)
            {
                skipTutorialButtonRoot.SetActive(false);
            }

            if (boardView == null)
            {
                LoadAssignedLevel(isContinueFromWin: true);
                Shared.GameFlow.InputManager.GamePlayInputEnabled = true;
                yield break;
            }

            Vector3 originalPosition = boardView.transform.localPosition;
            float offscreenDistance = boardView.GetOffscreenSlideDistance();
            yield return boardView.SlideLocalPosition(originalPosition + new Vector3(-offscreenDistance, 0f, 0f), ftueBoardSlideDurationSeconds);

            boardView.transform.localPosition = originalPosition + new Vector3(offscreenDistance, 0f, 0f);
            LoadAssignedLevel(isContinueFromWin: true);
            if (skipTutorialButtonRoot != null)
            {
                skipTutorialButtonRoot.SetActive(false);
            }

            yield return boardView.SlideLocalPosition(originalPosition, ftueBoardSlideDurationSeconds);

            if (skipTutorialButtonRoot != null && IsFtueLevel(CurrentLevelNumber))
            {
                skipTutorialButtonRoot.SetActive(true);
            }

            Shared.GameFlow.InputManager.GamePlayInputEnabled = true;
        }

        private void UpdateLevelWinPopup(int wonLevelNumber)
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            Shared.GameFlow.LevelWinPopupController popup = uiFlowManager?.GetOrCreateLevelWinPopup();
            // AddCoins(lastLevelWinCoinReward) already ran back in HandleLevelWon, so
            // BoosterWallet.CurrentCoins is already the post-win total here - but the popup's own
            // open animation (PlayOpenSequence) does currentCoin += winCoinAmount internally to
            // count up on screen. Feeding it the already-incremented total would double-count the
            // reward visually, so hand it the pre-win balance instead and let its own animation
            // add the reward once.
            int coinsBeforeWin = Mathf.Max(0, controller.Session.BoosterWallet.CurrentCoins - lastLevelWinCoinReward);
            popup?.SetCurrentCoin(coinsBeforeWin, lastLevelWinCoinReward);
            popup?.SetStats(
                controller.Session.Progress.Moves,
                controller.Session.Progress.ElapsedSeconds);
            popup?.SetAdsButtonVisible(wonLevelNumber > activeConfig.NoAdsBeforeLevel);
            // Hidden for the first 3 levels - a brand new player can only go forward (Next) out of
            // the win popup, not back out to the main menu, on levels 1-3.
            popup?.SetHomeButtonVisible(wonLevelNumber > 3);
            popup?.SetLevelResultText(wonLevelNumber);
            if (popup != null)
            {
                popup.WatchAdRequested.RemoveListener(HandleLevelWinWatchAd);
                popup.WatchAdRequested.AddListener(HandleLevelWinWatchAd);
            }
        }

        /// <summary>
        /// The pre-win wallet balance and coin reward for the Daily Challenge sub-level just won -
        /// captured in HandleLevelWon (see dailyChallengeCoinsBeforeWin/dailyChallengeCoinsWon).
        /// FillItDailyChallengeBridge calls this from its own DailyChallengeResultResolved handler
        /// to feed real values into FillItDailyChallengeWinPopupController.SetCurrentCoin instead
        /// of that popup's old placeholder amount.
        /// </summary>
        public void GetDailyChallengeLevelWinCoinInfo(out int coinsBeforeWin, out int coinsWon)
        {
            coinsBeforeWin = dailyChallengeCoinsBeforeWin;
            coinsWon = dailyChallengeCoinsWon;
        }

        /// <summary>
        /// Grants the Daily Challenge win reward a second time - called by
        /// FillItDailyChallengeBridge.HandleWinWatchAd once its rewarded ad actually succeeds.
        /// HandleLevelWon already credited the wallet with the single (non-doubled) reward before
        /// this popup even opened, and FillItDailyChallengeWinPopupController.
        /// DoubleWinCoinsAndContinue only doubles what the popup DISPLAYS - without this, the
        /// player would see "2x" on screen but their actual coin balance would only ever reflect
        /// the original single reward.
        /// </summary>
        public void GrantDailyChallengeWatchAdBonus()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            controller.Session.AddCoins(dailyChallengeCoinsWon);
            SaveProgress();
        }

        // Routes through the shared AdsManager - once actually rewarded, doubles the level-win
        // coin reward and continues to the next level, same as tapping Continue/Next.
        private void HandleLevelWinWatchAd()
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DoubleCoinNormalLevel, success =>
            {
                if (success)
                {
                    GrantLevelWinWatchAdReward();
                }
                else
                {
                    uiFlowManager?.GetOrCreateLevelWinPopup()?.OnWatchAdDeclined();
                }
            });
        }

        private void GrantLevelWinWatchAdReward()
        {
            // DoubleWinCoinsAndContinue below only doubles what the popup DISPLAYS
            // (winCoin/winCoinText) - the wallet was already credited with the single
            // (non-doubled) reward back in HandleLevelWon, before this popup even opened. Grant
            // the same amount again here so the player's actual coin balance matches what they now
            // see on screen instead of silently falling short of the "2x" they were promised.
            if (controller != null && controller.Session.HasLevel)
            {
                controller.Session.AddCoins(lastLevelWinCoinReward);
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    lastLevelWinCoinReward,
                    "watch_ad_double",
                    controller.Session.BoosterWallet.CurrentCoins);
                SaveProgress();
            }

            uiFlowManager?.GetOrCreateLevelWinPopup()?.DoubleWinCoinsAndContinue();
        }

        private void HandleLevelFailed(object sender, System.EventArgs eventArgs)
        {
            // Same "disabled the instant it's recorded" reasoning as HandleLevelWon.
            statusView?.DisableInfoButton();

            outOfLivesPopupOpen = false;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LevelFail);
            // LevelFail analytics no longer logs here (the game-over transition itself) - it now
            // fires specifically when the player clicks Home or Restart on the resulting popup
            // (see RestartLevel/SaveAndShowMainMenu/AbandonLevelToMainMenu), since simply reaching
            // this state isn't itself the event the requirement wants tracked.
            uiFlowManager?.GetOrCreateLevelFailPopup()?.SetLevelResultText(CurrentLevelNumber);
            SaveAll();
        }

        // Fires once per mistake that actually spends a life - see FillItRuntimeController.
        // MistakeSpent/ResolveMistake. Independent of HandleOutOfMistakes below, which only fires
        // once the LAST mistake is spent - this fires on every single one, including that last.
        private void HandleMistakeSpent(object sender, System.EventArgs eventArgs)
        {
            attemptLivesUsedTotal++;
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            // progression_lives_lost is BOARD progress (how much of the puzzle is already
            // correctly solved), not lives/mistake progress - i.e. "the player lost a life when
            // the board was X% done", not "X% of their mistake allowance is gone".
            AnalyticsManager.Instance.LogLivesLost(CurrentLevelNumber, ComputeBoardProgressPercent());
        }

        // Live board progress - the same percent the Daily Challenge calendar reads from saves.
        private float ComputeBoardProgressPercent()
        {
            return FillItLevelProgressCalculator.CalculatePercent(controller.Session.Level, controller.Session.Board.CopyValues());
        }


        // Feeds level_complete/level_fail's rule_1_break_count/rule_2_break_count.
        private void HandleRuleViolationCharged(FillItRule rule)
        {
            if (rule == FillItRule.RegionTooBig)
            {
                attemptRegionTooBigCount++;
            }
            else if (rule == FillItRule.RegionTooSmall)
            {
                attemptRegionTooSmallCount++;
            }
        }

        [SerializeField] private float outOfLivesHeartBreakDelaySeconds = 0.5f;
        [Header("Timer mode - Out of Lives popup reused as Time's Up")]
        [SerializeField] private string timeUpTitle = "Time's Up!";
        // {0} = seconds added (GlobalConfig.LevelExtraTime); the popup then fills its own {0} with the coin cost.
        [SerializeField] private string timeUpRefillTextFormat = "+{0}s - {{0}}";

        // Every revive (ad, coins, free) restores play: Hearts mode gives lives back (the ad gives
        // one, coins/free refill to full), Timer mode adds GlobalConfig.LevelExtraTime seconds.
        private void GrantRevive(bool refillFully)
        {
            if (controller.Session.TimerEnabled)
            {
                controller.Session.AddTime(activeConfig.LevelExtraTime);
            }
            else if (refillFully)
            {
                controller.Session.RefillLevelMistakes();
            }
            else
            {
                controller.Session.AddLevelMistakes(1);
            }
        }

        private void HandleOutOfMistakes(object sender, System.EventArgs eventArgs)
        {
            // The Level Attempt feature (hearts/mistake-limit) isn't unlocked yet for this level -
            // running out of mistakes should have no visible effect at all, so skip the Out of
            // Lives popup/input-block entirely rather than only hiding its heart-break FX.
            // Timer mode never runs out of hearts (mistakes aren't charged) - only out of time.
            if (controller == null || !controller.LevelAttemptFeatureEnabled || controller.Session.TimerEnabled)
            {
                return;
            }

            BeginOutOfLivesFlow();
        }

        // Timer mode: the countdown hit zero - same revive flow as running out of hearts, shown as
        // "Time's Up!" and every revive adds GlobalConfig.LevelExtraTime seconds.
        private void HandleTimeUp(object sender, System.EventArgs eventArgs)
        {
            if (controller == null || !controller.Session.IsPlaying || outOfLivesPopupOpen)
            {
                return;
            }

            controller.CancelDrag();
            BeginOutOfLivesFlow();
        }

        private void BeginOutOfLivesFlow()
        {
            // This fires synchronously from inside ResolveMistake, before the HUD heart-break FX
            // (RenderStatus(animateHeartLoss: true) / boardView.PlayHeartBreak, a few lines further
            // down the same call stack) has even started - so input must be blocked here right
            // away, but the popup itself has to wait for that FX to actually finish playing,
            // mirroring PlayLevelWinCelebrationThenShowPopup's "celebration beat, then popup"
            // sequencing for the win path.
            StopPendingMistake();
            outOfLivesPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            PlaySound(global::SoundName.LifeEmpty);
            // LevelFail itself is timed to the popup's own cat entry animation instead (see
            // OutOfLivesPopupController.PlayCatOpenAnimation) - this fires well before the popup
            // even shows (still mid heart-break FX delay), which was too early for it.
            AnalyticsManager.Instance.LogOutOfLives(CurrentLevelNumber, isDailyChallengeSession, levelReviveCount, levelRetryCount);
            StartCoroutine(PlayHeartBreakThenShowOutOfLivesPopup());
        }

        private IEnumerator PlayHeartBreakThenShowOutOfLivesPopup()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, outOfLivesHeartBreakDelaySeconds));

            int refillCost = EconomyConfigController.OutOfLivesRefillCoinCost;
            bool canAffordRefill = controller != null &&
                controller.Session.HasLevel &&
                controller.Session.BoosterWallet.CurrentCoins >= refillCost;
            bool freeReviveAvailable = !HasUsedFreeRevive();
            bool timeUp = IsTimerModeActive;
            string title = timeUp ? timeUpTitle : null;

            if (isDailyChallengeSession && dailyChallengeOutOfLivesPopup != null)
            {
                BindDailyChallengeOutOfLivesPopupEvents();
                dailyChallengeOutOfLivesPopup.SetRefillCoinCost(refillCost);
                dailyChallengeOutOfLivesPopup.SetAffordableRefill(canAffordRefill);
                dailyChallengeOutOfLivesPopup.SetFreeReviveAvailable(freeReviveAvailable);
                dailyChallengeOutOfLivesPopup.Show(
                    title: title,
                    progressText: string.Empty,
                    showBack: true,
                    primaryAction: HandleOutOfLivesRestart,
                    backAction: HandleOutOfLivesGiveUp);

                // GamePlayInputEnabled stays false here, same as before this delay existed - the
                // out-of-lives popup now owns input until it closes.
                yield break;
            }

            BindOutOfLivesPopupEvents();
            OutOfLivesPopupController popup = uiFlowManager != null ? uiFlowManager.OutOfLivesPopup : null;
            // Same popup either way - Timer mode just retitles it and relabels the coin revive.
            popup?.SetTexts(title, timeUp ? string.Format(timeUpRefillTextFormat, activeConfig.LevelExtraTime) : null);
            uiFlowManager?.SetCurrentCoinOutOfLife(controller.Session.BoosterWallet.CurrentCoins);
            uiFlowManager?.ShowOutOfLivesPopup(refillCost);

            popup?.SetAffordableRefill(canAffordRefill);
            popup?.SetFreeReviveAvailable(freeReviveAvailable);
            popup?.SetLevelResultText(CurrentLevelNumber);

            // GamePlayInputEnabled stays false here, same as before this delay existed - the
            // out-of-lives popup now owns input until it closes.
        }

        private void HandleOutOfLivesWatchAd()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            string placement = isDailyChallengeSession
                ? AdsManager.RewardedPlacement.ReviveDcLevel
                : AdsManager.RewardedPlacement.ReviveLevel;

            AdsManager.Instance.ShowRewarded(placement, success =>
            {
                if (success)
                {
                    GrantOutOfLivesWatchAdReward();
                }
            });
        }

        private void GrantOutOfLivesWatchAdReward()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            GrantRevive(refillFully: false);
            levelReviveCount++;
            ContinueAfterOutOfLivesPopup();
        }

        // Free Revive: same refill-to-full effect as the coin revive button, just with no coin
        // cost and no ad, and works exactly once per player lifetime (see FreeReviveUsedKey). The
        // popup only shows this option at all when HasUsedFreeRevive() was false at Show() time
        // (see PlayHeartBreakThenShowOutOfLivesPopup), but the flag is still re-checked here so a
        // stale/already-open popup can never grant it twice.
        private void HandleOutOfLivesFreeRevive()
        {
            if (controller == null || !controller.Session.HasLevel || HasUsedFreeRevive())
            {
                return;
            }

            Shared.PlayerData.PlayerData.Instance.SetInt(FreeReviveUsedKey, 1);
            SaveAll();

            GrantRevive(refillFully: true);
            levelReviveCount++;
            ContinueAfterOutOfLivesPopup();
        }

        private static bool HasUsedFreeRevive()
        {
            return Shared.PlayerData.PlayerData.Instance.TryGetInt(FreeReviveUsedKey, out int used) && used != 0;
        }

        private void HandleOutOfLivesRefill()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            int cost = Mathf.Max(0, EconomyConfigController.OutOfLivesRefillCoinCost);
            if (!controller.Session.TrySpendCoins(cost))
            {
                Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
                CloseActiveOutOfLivesPopup(() =>
                {
                    outOfLivesPopupOpen = false;
                    OpenStorePopup();
                });
                return;
            }

            AnalyticsManager.Instance.LogCurrencySink(
                AnalyticsEvents.CurrencyTypeValues.Coins,
                cost,
                "out_of_lives_refill",
                1,
                controller.Session.BoosterWallet.CurrentCoins);
            levelReviveCount++;
            GrantRevive(refillFully: true);
            ContinueAfterOutOfLivesPopup();
        }

        // Give Up (the Out of Lives popup's "Home" button) abandons this attempt straight to the
        // main menu. NOTE: this used to call SettingsQuit(), but SettingsQuit() was later
        // repurposed to restart the level in place for the Settings popup's own Quit button -
        // Give Up was never updated to match, so it silently inherited "restart" instead of
        // "go home". Use AbandonLevelToMainMenu() here instead, which clears the saved
        // board/progress like a restart would, but returns to the main menu rather than
        // reloading this level in place.
        private void HandleOutOfLivesGiveUp()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            outOfLivesPopupOpen = false;

            // Fade in first, THEN close the Out of Lives popup (once fully covered), THEN abandon
            // to the main menu - whose own entry animation plays underneath while the overlay
            // fades back out.
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => CloseActiveOutOfLivesPopup(null),
                revealNewPage: AbandonLevelToMainMenu);
        }

        // Abandons the current attempt back to the main menu: clears the saved board for this
        // level (so it starts fresh next time, same as RestartLevel()'s clearing) but goes home
        // instead of reloading the level in place.
        private void AbandonLevelToMainMenu()
        {
            // Out-of-Lives popup's "Give Up"/Home click - this method is only ever reached from an
            // actual out-of-mistakes state, but route through the same shared guard as
            // SaveAndShowMainMenu/RestartLevel for consistency.
            if (IsCurrentLevelFailedOrOutOfLives())
            {
                AnalyticsManager.Instance.LogLevelFail(BuildLevelResultEventData());
            }

            AnalyticsManager.Instance.LogHome(BuildLevelResultEventData(), setting: false, levelWin: false, placement: AnalyticsEvents.HomePlacementValues.OutOfLives);

            if (isDailyChallengeSession)
            {
                DailyChallengeAttemptAbandoned.Invoke();
            }

            StopPendingMistake();
            boardSaveManager?.Clear(controller?.Session);
            boosterManager?.ClearUndoHistory();
            SaveProgress();
            ReturnToMainMenu();
        }

        private void HandleOutOfLivesRestart()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            outOfLivesPopupOpen = false;

            // A Daily Challenge restart from Out of Lives requires watching an ad first - a
            // regular level's restart stays free/instant. Shown on top of the still-open popup,
            // same as the other AdsManager.ShowRewarded(...) call sites (HandleLevelWinWatchAd,
            // HandleOutOfLivesWatchAd) - only once rewarded does the popup actually close/restart.
            if (isDailyChallengeSession)
            {
                AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DailyChallengeRestart, success =>
                {
                    if (success)
                    {
                        PerformOutOfLivesRestart();
                    }
                });
                return;
            }

            PerformOutOfLivesRestart();
        }

        private void PerformOutOfLivesRestart()
        {
            Shared.GameFlow.GameTransitionOverlay.Instance.Play(
                hideOldPage: () => CloseActiveOutOfLivesPopup(null),
                revealNewPage: RestartLevel);
        }

        private void ContinueAfterOutOfLivesPopup()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            CloseActiveOutOfLivesPopup(ResumeAfterOutOfLivesPopupClosed);
        }

        // Closes whichever out-of-lives popup is actually open - the shared
        // Shared.GameFlow.OutOfLivesPopupController, or dailyChallengeOutOfLivesPopup during a
        // Daily Challenge session (see PlayHeartBreakThenShowOutOfLivesPopup) - so
        // HandleOutOfLivesRefill/GiveUp/Restart/ContinueAfterOutOfLivesPopup don't each need to
        // know which one is showing.
        private void CloseActiveOutOfLivesPopup(UnityEngine.Events.UnityAction onClosed)
        {
            if (isDailyChallengeSession && dailyChallengeOutOfLivesPopup != null)
            {
                dailyChallengeOutOfLivesPopup.Hide();
                onClosed?.Invoke();
                return;
            }

            if (uiFlowManager != null)
            {
                uiFlowManager.CloseOutOfLivesPopup(onClosed);
                return;
            }

            onClosed?.Invoke();
        }

        private void ResumeAfterOutOfLivesPopupClosed()
        {
            outOfLivesPopupOpen = false;

            // Every path that reaches here (watch-ad +1, coin refill, free revive) just restored
            // at least one attempt - the pips visibly fill back up to the real post-grant
            // remaining count (not always to max - the ad grant only restores 1) once the popup
            // is out of the way, instead of instantly snapping behind it. Played before
            // RefreshViews() below, not instead of it - RenderAttempts only ever touches each
            // pip's sprite/active-state/label, never fillAmount, so it can't stomp this animation
            // while it's still running.
            if (controller != null && controller.Session.HasLevel && !controller.Session.TimerEnabled)
            {
                statusView?.PlayReviveAnimation(controller.Session.LevelMistakeLimit.RemainingMistakes, controller.Session.LevelMistakeLimit.MaxMistakes);
            }

            controller?.RefreshViews();
            sharedBoosterManager?.RefreshAllBoosterUI();
            SaveAll();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.IsPlaying;
        }

        private void BindOutOfLivesPopupEvents()
        {
            OutOfLivesPopupController popup = uiFlowManager != null ? uiFlowManager.OutOfLivesPopup : null;
            if (popup == null)
            {
                return;
            }

            popup.WatchAdRequested.RemoveListener(HandleOutOfLivesWatchAd);
            popup.RefillRequested.RemoveListener(HandleOutOfLivesRefill);
            popup.GiveUpRequested.RemoveListener(HandleOutOfLivesGiveUp);
            popup.RestartRequested.RemoveListener(HandleOutOfLivesRestart);
            popup.FreeReviveRequested.RemoveListener(HandleOutOfLivesFreeRevive);
            popup.WatchAdRequested.AddListener(HandleOutOfLivesWatchAd);
            popup.RefillRequested.AddListener(HandleOutOfLivesRefill);
            popup.GiveUpRequested.AddListener(HandleOutOfLivesGiveUp);
            popup.RestartRequested.AddListener(HandleOutOfLivesRestart);
            popup.FreeReviveRequested.AddListener(HandleOutOfLivesFreeRevive);
        }

        // Only the three revive options are UnityEvents here - Restart/Give Up are wired directly
        // as dailyChallengeOutOfLivesPopup.Show(...)'s primaryAction/backAction parameters instead
        // (see PlayHeartBreakThenShowOutOfLivesPopup), since that popup's API takes them as plain
        // Actions rather than UnityEvents.
        private void BindDailyChallengeOutOfLivesPopupEvents()
        {
            if (dailyChallengeOutOfLivesPopup == null)
            {
                return;
            }

            dailyChallengeOutOfLivesPopup.WatchAdRequested.RemoveListener(HandleOutOfLivesWatchAd);
            dailyChallengeOutOfLivesPopup.RefillRequested.RemoveListener(HandleOutOfLivesRefill);
            dailyChallengeOutOfLivesPopup.FreeReviveRequested.RemoveListener(HandleOutOfLivesFreeRevive);
            dailyChallengeOutOfLivesPopup.WatchAdRequested.AddListener(HandleOutOfLivesWatchAd);
            dailyChallengeOutOfLivesPopup.RefillRequested.AddListener(HandleOutOfLivesRefill);
            dailyChallengeOutOfLivesPopup.FreeReviveRequested.AddListener(HandleOutOfLivesFreeRevive);
        }

        // Gates the level-complete interstitial (both normal AND Daily Challenge) on normal-level
        // progression - currentLevelIndex is never touched by a Daily Challenge session (see
        // LoadDailyChallengeLevel), so reading it here works unchanged for both call sites. Also
        // numerically equals "levels completed so far": HandleLevelWon increments it BEFORE
        // LoadNextLevel runs, so by the time LoadNextLevel calls this, currentLevelIndex already
        // equals the level number that was just won.
        private bool IsInterstitialLevelUnlocked()
        {
            if (!AdsConfigController.InterstitialAdsEnabled)
            {
                return false;
            }

            int unlockLevel = AdsConfigController.InterstitialStartLevel;
            return unlockLevel <= 0 || currentLevelIndex >= unlockLevel;
        }

        private bool IsInterstitialCooldownElapsed()
        {
            if (!Shared.PlayerData.PlayerData.Instance.TryGetString(LastInterstitialShownUtcKey, out string rawLastShown) ||
                !System.DateTime.TryParse(rawLastShown, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime lastShownUtc))
            {
                return true;
            }

            double secondsSinceLastShown = (System.DateTime.UtcNow - lastShownUtc).TotalSeconds;
            return secondsSinceLastShown >= Mathf.Max(0f, AdsConfigController.InterstitialDelayBetweenAds);
        }

        // Marks "now" as the last interstitial shown time - also called from
        // HandleRewardedAdCompletedForInterstitialCooldown to re-arm the same cooldown after a
        // rewarded ad, so a player who just watched one isn't shown an interstitial right after.
        private static void MarkInterstitialCooldownStartNow()
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.SetString(LastInterstitialShownUtcKey, System.DateTime.UtcNow.ToString("O"));
            data.Touch();

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }

        private void HandleRewardedAdCompletedForInterstitialCooldown()
        {
            MarkInterstitialCooldownStartNow();
        }

        /// <summary>
        /// Shows the level-complete interstitial (normal or Daily Challenge, per isDailyChallenge)
        /// and only then invokes proceed - unless it's not unlocked yet for this level, or still on
        /// cooldown from the last one, in which case proceed runs immediately with no ad. Callers:
        /// LoadNextLevel (normal) and FillItDailyChallengeBridge (DC's own Continue action).
        /// </summary>
        public void TryShowLevelCompleteInterstitial(bool isDailyChallenge, System.Action proceed)
        {
            if (!IsInterstitialLevelUnlocked() || !IsInterstitialCooldownElapsed())
            {
                proceed?.Invoke();
                return;
            }

            MarkInterstitialCooldownStartNow();

            System.Action<bool> onComplete = _ => proceed?.Invoke();
            if (isDailyChallenge)
            {
                AdsManager.Instance.ShowLevelCompleteDcInterstitial(onComplete);
            }
            else
            {
                AdsManager.Instance.ShowLevelCompleteNormalInterstitial(onComplete);
            }
        }

        private void HandleBoosterPurchaseRequested(BoosterType boosterType)
        {
            if (controller == null || !controller.Session.HasLevel || controller.Session.BoosterWallet == null)
            {
                return;
            }

            pendingBuyBoosterType = boosterType;
            buyBoosterPopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            BindBuyBoosterPopupEvents();

            BoosterWallet wallet = controller.Session.BoosterWallet;
            int buyCount = wallet.Config.GetBuyCount(boosterType);
            int adBuyCount = wallet.Config.GetAdBuyCount(boosterType);
            int coinCost = wallet.Config.GetCost(boosterType);
            uiFlowManager?.ShowBuyBoosterPopup(boosterType, buyCount, adBuyCount, coinCost);
        }

        private void BindBuyBoosterPopupEvents()
        {
            BuyBoosterPopupController popup = uiFlowManager != null ? uiFlowManager.BuyBoosterPopup : null;
            if (popup == null)
            {
                return;
            }

            popup.BuyRequested.RemoveListener(HandleBuyBoosterConfirm);
            popup.CancelRequested.RemoveListener(HandleBuyBoosterCancel);
            popup.WatchAdRequested.RemoveListener(HandleBuyBoosterWatchAd);
            popup.BuyRequested.AddListener(HandleBuyBoosterConfirm);
            popup.CancelRequested.AddListener(HandleBuyBoosterCancel);
            popup.WatchAdRequested.AddListener(HandleBuyBoosterWatchAd);
        }

        // Routes through the shared AdsManager - once actually rewarded, grants one free charge
        // of the pending booster type without spending coins.
        private void HandleBuyBoosterWatchAd()
        {
            AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.BuyHintBooster, success =>
            {
                if (success)
                {
                    GrantBuyBoosterWatchAdReward();
                }
            });
        }

        private void GrantBuyBoosterWatchAdReward()
        {
            if (controller != null && controller.Session.HasLevel && controller.Session.BoosterWallet != null)
            {
                controller.Session.BoosterWallet.RefundCharge(pendingBuyBoosterType);
                SaveAll();
            }

            if (pendingBuyBoosterType == BoosterType.Hint)
            {
                AnalyticsManager.Instance.LogHintPurchasedPopup(CurrentLevelNumber, hintPurchasedCoin: 0, hintPurchasedAd: 1);
            }

            ContinueAfterBuyBoosterPopup();
        }

        private void HandleBuyBoosterConfirm()
        {
            if (controller != null && controller.Session.HasLevel && controller.Session.BoosterWallet != null)
            {
                BoosterWallet wallet = controller.Session.BoosterWallet;
                int boosterCost = wallet.Config.GetCost(pendingBuyBoosterType);
                if (wallet.TryPurchaseCharge(pendingBuyBoosterType))
                {
                    AnalyticsManager.Instance.LogBoosterPurchased(pendingBuyBoosterType.ToString(), CurrentLevelNumber);
                    AnalyticsManager.Instance.LogCurrencySink(
                        AnalyticsEvents.CurrencyTypeValues.Coins,
                        boosterCost,
                        "booster_" + pendingBuyBoosterType.ToString().ToLowerInvariant(),
                        wallet.Config.GetBuyCount(pendingBuyBoosterType),
                        wallet.CurrentCoins);
                    if (pendingBuyBoosterType == BoosterType.Hint)
                    {
                        AnalyticsManager.Instance.LogHintPurchasedPopup(CurrentLevelNumber, hintPurchasedCoin: boosterCost, hintPurchasedAd: 0);
                    }

                    SaveAll();
                    ContinueAfterBuyBoosterPopup();
                    return;
                }
            }

            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseBuyBoosterPopup(() =>
                {
                    buyBoosterPopupOpen = false;
                    OpenStorePopup();
                });
                return;
            }

            buyBoosterPopupOpen = false;
            OpenStorePopup();
        }

        private void HandleBuyBoosterCancel()
        {
            ContinueAfterBuyBoosterPopup();
        }

        private void ContinueAfterBuyBoosterPopup()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseBuyBoosterPopup(ResumeAfterBuyBoosterPopupClosed);
                return;
            }

            ResumeAfterBuyBoosterPopupClosed();
        }

        private void ResumeAfterBuyBoosterPopupClosed()
        {
            buyBoosterPopupOpen = false;
            sharedBoosterManager?.RefreshAllBoosterUI();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.IsPlaying;
        }

        public void OpenStorePopup()
        {
            if (controller == null || !controller.Session.HasLevel)
            {
                return;
            }

            storePopupOpen = true;
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            BindStorePopupEvents();
            uiFlowManager?.ShowStorePopup();
            AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, string.Empty, Shared.PlayerData.PlayerData.Instance.Analytics.IapPackPurchaseCount, shopOpen: true);
        }

        private void BindStorePopupEvents()
        {
            Shared.Store.UnityAdapters.StorePopupController popup = uiFlowManager != null ? uiFlowManager.StorePopup : null;
            if (popup == null)
            {
                return;
            }

            popup.CloseRequested.RemoveListener(HandleStoreCloseRequested);
            popup.PurchaseRequested.RemoveListener(HandleStorePurchaseRequested);
            popup.CloseRequested.AddListener(HandleStoreCloseRequested);
            popup.PurchaseRequested.AddListener(HandleStorePurchaseRequested);
        }

        private void HandleStoreCloseRequested()
        {
            Shared.GameFlow.InputManager.GamePlayInputEnabled = false;
            if (uiFlowManager != null)
            {
                uiFlowManager.CloseStorePopup(ResumeAfterStorePopupClosed);
                return;
            }

            ResumeAfterStorePopupClosed();
        }

        private void ResumeAfterStorePopupClosed()
        {
            storePopupOpen = false;
            sharedBoosterManager?.RefreshAllBoosterUI();
            Shared.GameFlow.InputManager.GamePlayInputEnabled =
                controller != null &&
                controller.Session.HasLevel &&
                controller.Session.IsPlaying;
        }

        // Grants the reward for a purchase InAppManager just recovered OUTSIDE the normal
        // Purchase()-request flow - see InAppManager.UnattendedPurchaseGranted's own comment.
        // This is exactly what closes the "paid but never got the reward, then can't re-purchase
        // because the store still thinks it's owned" gap: RestorePurchases (run once at app
        // startup by BootLoader, and again on demand from Settings) re-validates any purchase
        // still sitting unconsumed and finalizes it, and this is where that finalized purchase's
        // coins/unlock actually reaches the player.
        private void HandleUnattendedPurchaseGranted(string productId)
        {
            Shared.Store.StoreItemDefinition item = storeManager?.FindItemByProductId(productId);
            if (item == null)
            {
                Debug.LogWarning($"{nameof(FillItGameplayManager)}: recovered a purchase for product '{productId}' but no StoreItemDefinition in storeConfigAsset has that ProductId - reward could not be granted.");
                return;
            }

            storeManager?.CompletePurchase(item.ItemId);
            Shared.PlayerData.PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
            analytics.IapPackPurchaseCount++;
            AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, item.ItemId, analytics.IapPackPurchaseCount, shopOpen: false);
            sharedBoosterManager?.RefreshAllBoosterUI();
        }

        private void HandleStorePurchaseRequested(string itemId)
        {
            Shared.Store.StoreItemDefinition item = storeManager?.FindItem(itemId);
            if (item == null)
            {
                // Surfaced rather than silently doing nothing - a UI button firing an itemId that
                // doesn't exist in storeConfigAsset means the button's binding and the config data
                // it was bound from have drifted apart.
                Debug.LogWarning($"{nameof(FillItGameplayManager)}: no StoreItemDefinition found for itemId '{itemId}' - check storeConfigAsset.");
                return;
            }

            if (string.IsNullOrWhiteSpace(item.ProductId))
            {
                Debug.LogWarning($"{nameof(FillItGameplayManager)}: StoreItemDefinition '{itemId}' has no ProductId set - purchase will still simulate in the Editor, but will always fail on a real device build.");
            }

            InAppManager.Instance.Purchase(item.ProductId, success =>
            {
                if (success)
                {
                    storeManager?.CompletePurchase(itemId);
                    Shared.PlayerData.PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
                    analytics.IapPackPurchaseCount++;
                    AnalyticsManager.Instance.LogIapShop(CurrentLevelNumber, itemId, analytics.IapPackPurchaseCount, shopOpen: false);
                }

                sharedBoosterManager?.RefreshAllBoosterUI();
            }, item.CoinAmount, item.Type == Shared.Store.StoreItemType.CoinPack, item.IconKey);
        }

        private void GrantStoreCoins(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (controller != null && controller.Session.HasLevel)
            {
                controller.Session.AddCoins(amount);
                AnalyticsManager.Instance.LogCurrencySource(
                    AnalyticsEvents.CurrencyTypeValues.Coins,
                    amount,
                    "iap_coin_pack",
                    controller.Session.BoosterWallet.CurrentCoins);
                SaveAll();
            }
            else
            {
                // No level loaded (e.g. purchased from the Main Menu/Home shop, not mid-gameplay) -
                // there's no active Session.BoosterWallet to credit and SaveAll() no-ops without a
                // level too (see SaveAll's own HasLevel guard), so this would otherwise silently
                // drop the purchase: the popup still shows and the coin-fly animation still plays,
                // but PlayerData.TotalCoins - and everything reading it (HUD, shop) - never actually
                // changes. Grant straight to the persisted total instead.
                GrantStoreCoinsWithoutSession(amount);
            }

            sharedBoosterManager?.RefreshAllBoosterUI();
        }

        private static void GrantStoreCoinsWithoutSession(int amount)
        {
            Shared.PlayerData.PlayerData data = Shared.PlayerData.PlayerData.Instance;
            data.TotalCoins = Mathf.Max(0, data.TotalCoins + amount);
            data.Touch();

            AnalyticsManager.Instance.LogCurrencySource(
                AnalyticsEvents.CurrencyTypeValues.Coins,
                amount,
                "iap_coin_pack",
                data.TotalCoins);

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }
    }
}
