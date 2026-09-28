#if UNITY_EDITOR
using Shared.GameConfig;
using Shared.Leaderboard.Bots;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// Editor-only cheats for driving the board by hand.
    ///
    /// Exists because the real thing is slow to observe: a point only arrives by winning a level
    /// with hearts to spare, and a whole event takes as long as the configured event length -
    /// normally a day. These push score and time directly.
    ///
    /// Editor-only by compilation, not just by convention: none of this reaches a build. Same
    /// split as LeaderboardPanelController.Testing.cs and CatFlapGameplayManager.Cheat.cs.
    /// </summary>
    public sealed partial class LeaderboardManager
    {
        private Coroutine simulationRoutine;

        [ContextMenu("Cheat/Score +10")]
        public void CheatAddTenScore()
        {
            CheatAddScore(10);
        }

        [ContextMenu("Cheat/Score -10")]
        public void CheatRemoveTenScore()
        {
            CheatAddScore(-10);
        }

        /// <summary>
        /// Moves the player's score by delta and saves it, flooring at zero.
        ///
        /// Goes through the same save path a real win does rather than poking the field, so the
        /// change survives a domain reload and the next GetBoard re-ranks against it.
        /// </summary>
        public void CheatAddScore(int delta)
        {
            EnsureInitialized();

            int updated = Mathf.Max(0, playerScore + delta);
            if (updated == playerScore)
            {
                return;
            }

            playerScore = updated;
            saveManager.SavePlayerScore(playerScore);
        }

        /// <summary>
        /// Puts the player one point clear of the best bot's FINAL score, so the event they are in
        /// ends with them in first.
        ///
        /// Exists because the reward table only pays the top few places: on a fresh board the
        /// player sits on nothing while fifty bots play a full day, finishes somewhere near last,
        /// and settlement correctly pays out nothing - which looks exactly like a broken results
        /// screen. This makes the winning case reachable in one click.
        ///
        /// Measured at the event END rather than now, because that is the board settlement will
        /// actually rank against.
        /// </summary>
        [ContextMenu("Cheat/Take first place")]
        public void CheatTakeFirstPlace()
        {
            EnsureInitialized();
            EnsureBotsPlanned();

            LeaderboardConfigData config = ResolveConfig();
            double eventSeconds = EventSeconds(config);
            double onlineTailSeconds = config.Bots.OnlineTailMinutes * 60d;

            int best = 0;
            for (int index = 0; index < botPlans.Count; index++)
            {
                BotSnapshot snapshot = BotEvaluator.Evaluate(botPlans[index], eventSeconds, eventSeconds, absenceWindows, onlineTailSeconds);
                if (snapshot.IsVisible && snapshot.Score > best)
                {
                    best = snapshot.Score;
                }
            }

            playerScore = best + 1;
            saveManager.SavePlayerScore(playerScore);
            Debug.Log($"[Leaderboard] Player score set to {playerScore} - one clear of the best bot's finish ({best}).", this);
        }

        /// <summary>
        /// Ends the current event immediately with the player finishing at <paramref name="rank"/>,
        /// then hands over to the normal cooldown.
        ///
        /// This is a TIME SKIP, not a special case: it sets the score that produces that placing and
        /// then drags the cycle's start back by a whole event length, so the ordinary
        /// SettleExpiredCycles runs the real payout, the real podium snapshot and the real cooldown.
        /// Nothing downstream can tell this apart from an event that ended on its own.
        ///
        /// The board is left mid-cooldown afterwards, which is the state worth testing: final
        /// standings frozen on screen, Collect button showing, results popup deliberately NOT open
        /// until the cooldown runs out.
        /// </summary>
        public void CheatFinishEventAt(int rank)
        {
            EnsureInitialized();
            EnsureBotsPlanned();

            rank = Mathf.Max(1, rank);
            playerScore = ResolveScoreForRank(rank);
            saveManager.SavePlayerScore(playerScore);

            SkipBackHours(ResolveConfig().CycleHours);

            Debug.Log(
                $"[Leaderboard] Event ended by cheat. Asked for rank {rank}, scored {playerScore}, "
                + $"finished {pendingRewards.Rank} with {pendingRewards.Rewards.Count} reward(s). "
                + $"Next event in {TimeUntilNextEvent:hh\\:mm\\:ss}.",
                this);
        }

        /// <summary>
        /// Jumps to the end of the cooldown, which starts the next event - and, if a reward is
        /// still sitting uncollected, is the moment the results popup is supposed to open itself.
        /// </summary>
        [ContextMenu("Cheat/Skip cooldown")]
        public void CheatSkipCooldown()
        {
            EnsureInitialized();

            LeaderboardConfigData config = ResolveConfig();
            SkipBackHours(config.CycleHours + config.CooldownHours);

            Debug.Log($"[Leaderboard] Cooldown skipped - event {state.EventId} is now running.", this);
        }

        /// <summary>Pushes the clock forward by an arbitrary amount, settling whatever that crosses.</summary>
        public void CheatSkipHours(float hours)
        {
            EnsureInitialized();
            SkipBackHours(hours);

            Debug.Log($"[Leaderboard] Skipped {hours:0.##}h. Event {state.EventId}, cooldown: {IsInCooldown}.", this);
        }

        /// <summary>
        /// Moves the cycle's ANCHOR backwards, which is the same thing as moving the clock forwards
        /// without touching the clock.
        ///
        /// Done this way on purpose. Every phase in this feature is derived from
        /// (CycleStartUtcTicks, now), so shifting the anchor reaches the exact state a real wait
        /// would have produced - and leaves the system clock alone, unlike the simulation cheat,
        /// which has to swap the clock out because it needs to watch the event play through.
        /// </summary>
        private void SkipBackHours(double hours)
        {
            var start = new DateTime(state.CycleStartUtcTicks, DateTimeKind.Utc);
            state.CycleStartUtcTicks = start.AddHours(-Math.Max(0d, hours)).Ticks;
            stateDirty = true;

            SettleExpiredCycles();

            if (stateDirty)
            {
                saveManager.SaveState(state);
                stateDirty = false;
            }
        }

        /// <summary>
        /// The score that lands the player on <paramref name="rank"/> against the bots' FINAL
        /// scores - the same numbers settlement will rank them against.
        ///
        /// Ties go to the player everywhere else in this feature, so a score merely equal to the
        /// bot in that slot is enough to take it. Where bots are tied with each other that is not
        /// achievable, so the candidate is walked down until the count of bots strictly above it is
        /// right, and the caller is told what placing actually came out.
        /// </summary>
        private int ResolveScoreForRank(int rank)
        {
            LeaderboardConfigData config = ResolveConfig();
            double eventSeconds = EventSeconds(config);
            double onlineTailSeconds = config.Bots.OnlineTailMinutes * 60d;

            var finals = new List<int>(botPlans.Count);
            for (int index = 0; index < botPlans.Count; index++)
            {
                BotSnapshot snapshot = BotEvaluator.Evaluate(botPlans[index], eventSeconds, eventSeconds, absenceWindows, onlineTailSeconds);
                if (snapshot.IsVisible)
                {
                    finals.Add(snapshot.Score);
                }
            }

            finals.Sort((left, right) => right.CompareTo(left));

            if (finals.Count == 0)
            {
                return 0;
            }

            // Beyond the bottom of the board: nothing scored at all is as low as it goes.
            if (rank > finals.Count)
            {
                return 0;
            }

            int candidate = finals[rank - 1];
            while (candidate > 0 && CountStrictlyAbove(finals, candidate) < rank - 1)
            {
                candidate--;
            }

            return candidate;
        }

        private static int CountStrictlyAbove(List<int> sortedDescending, int score)
        {
            int count = 0;
            for (int index = 0; index < sortedDescending.Count; index++)
            {
                if (sortedDescending[index] <= score)
                {
                    // Sorted, so nothing after this can be above it either.
                    break;
                }

                count++;
            }

            return count;
        }

        /// <summary>Takes first place, then replays the event into it - the whole results flow in one button.</summary>
        public void CheatSimulateEventAsWinner(float realSeconds)
        {
            CheatTakeFirstPlace();
            CheatSimulateEvent(realSeconds);
        }

        /// <summary>
        /// Fabricates a finish at <paramref name="rank"/> and announces it, so the results screen
        /// can be opened and dressed without waiting out an event at all.
        ///
        /// Goes through BuildRewardsForRank and the same save-then-announce order as real
        /// settlement, so what appears is exactly what a genuine finish at that place would pay -
        /// including nothing, if the reward table does not cover it.
        /// </summary>
        public void CheatGrantPlacement(int rank)
        {
            EnsureInitialized();

            List<LeaderboardRewardData> earned = BuildRewardsForRank(rank);
            if (earned.Count == 0)
            {
                Debug.LogWarning($"[Leaderboard] No reward row covers rank {rank} - nothing to show. Add one to GlobalConfig > Leaderboard > Rewards.", this);
                return;
            }

            pendingRewards = new LeaderboardPendingRewardsData
            {
                Rank = rank,
                Score = playerScore,
                RankWinCount = state.RegisterPlacement(rank)
            };

            pendingRewards.Rewards.AddRange(earned);

            saveManager.SaveState(state);
            stateDirty = false;
            saveManager.SavePendingRewards(pendingRewards);
            // Announced after the save, exactly as settlement does it.
            PendingRewardsChanged?.Invoke();
        }

        /// <summary>Clears anything pending, so the results screen can be triggered again from scratch.</summary>
        [ContextMenu("Cheat/Clear pending rewards")]
        public void CheatClearPendingRewards()
        {
            EnsureInitialized();

            pendingRewards = new LeaderboardPendingRewardsData();
            saveManager.SavePendingRewards(pendingRewards);
            PendingRewardsChanged?.Invoke();
        }

        /// <summary>True while a compressed event is playing out, so the UI can say so.</summary>
        public bool IsSimulating => simulationRoutine != null;

        [ContextMenu("Cheat/Simulate whole event in 1 minute")]
        public void CheatSimulateEventInOneMinute()
        {
            CheatSimulateEvent(60f);
        }

        /// <summary>
        /// Replays the entire event - bots joining, playing bursts, going quiet, the timer running
        /// out and settlement - compressed into <paramref name="realSeconds"/> of wall clock.
        ///
        /// Works by swapping the manager's clock rather than by fast-forwarding any state. Every
        /// bot score is already a pure function of the time it is asked about, so a clock that runs
        /// 1440x faster produces exactly the board that would have existed at that moment - no
        /// special simulation path, and nothing that can behave differently from the real thing.
        ///
        /// Time is rewound to the event's own start first, so the run shows the whole event from
        /// the open rather than from wherever the real clock happens to be. Rewinding is safe here
        /// and only here: scores are derived, not accumulated, so an earlier time simply shows an
        /// earlier board.
        /// </summary>
        public void CheatSimulateEvent(float realSeconds)
        {
            EnsureInitialized();

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Leaderboard] Simulation needs Play mode - it drives the board from a coroutine.", this);
                return;
            }

            realSeconds = Mathf.Max(1f, realSeconds);
            double eventSeconds = ResolveConfig().CycleHours * 3600d;

            // Anchored to the event's own start, so the compressed run covers open -> end exactly.
            var eventStart = new DateTime(state.CycleStartUtcTicks, DateTimeKind.Utc);
            // Slightly past the end, so the run finishes with the event actually over and
            // settlement having fired, rather than one tick short of it.
            double scale = (eventSeconds * 1.02d) / realSeconds;

            SetTimeProvider(new ScaledTimeProvider(eventStart, scale));

            if (simulationRoutine != null)
            {
                StopCoroutine(simulationRoutine);
            }

            simulationRoutine = StartCoroutine(SimulationRoutine(realSeconds));
            Debug.Log($"[Leaderboard] Simulating {ResolveConfig().CycleHours:0.##}h of event over {realSeconds:0}s ({scale:0}x).", this);
        }

        [ContextMenu("Cheat/Stop simulation")]
        public void CheatStopSimulation()
        {
            EndSimulation("stopped");
        }

        /// <summary>
        /// The single exit from a simulation, however it ends: coroutine stopped, real clock back,
        /// and - the part that matters - the cycle anchor repaired.
        ///
        /// A simulated rollover sets the next cycle to start at the fast clock's cycle end, which
        /// in real time is a whole cycle from now. Left alone, the next real session finds a board
        /// with no bots on it and a countdown of up to two cycles, for hours. Re-anchoring to the
        /// real now puts the player at the start of a fresh event instead, which is what the roll
        /// actually meant. Anything already won stays pending - this touches the clock, not the
        /// payout.
        /// </summary>
        private void EndSimulation(string reason)
        {
            if (simulationRoutine != null)
            {
                StopCoroutine(simulationRoutine);
                simulationRoutine = null;
            }

            SetTimeProvider(new SystemTimeProvider());

            long realNow = timeProvider.UtcNow.Ticks;
            if (state.CycleStartUtcTicks > realNow)
            {
                state.CycleStartUtcTicks = realNow;
                state.LastSimUtcTicks = realNow;
                saveManager.SaveState(state);
                stateDirty = false;
            }

            LeaderboardPanelController panel = FindFirstObjectByType<LeaderboardPanelController>();
            if (panel != null && panel.IsOpen)
            {
                panel.Refresh(animate: false);
            }

            Debug.Log($"[Leaderboard] Simulation {reason} - back on the system clock, cycle anchored to now.", this);
        }

        /// <summary>
        /// Drives the open board while the compressed event runs.
        ///
        /// Refreshed on a fixed interval rather than every frame: a refresh evaluates every bot's
        /// whole plan, and at 60fps that is a lot of work for motion nobody can read anyway.
        /// </summary>
        private IEnumerator SimulationRoutine(float realSeconds)
        {
            LeaderboardPanelController panel = FindFirstObjectByType<LeaderboardPanelController>();
            const float refreshInterval = 0.1f;

            // Settlement, not the roll, is what marks the event COMPLETE - and with a cooldown
            // between them the roll is now a whole CooldownHours later. Watching SettledEventId
            // stops the run the moment the payout lands, instead of fast-forwarding through the
            // cooldown as well.
            int startingSettledId = state.SettledEventId;

            float elapsed = 0f;
            float sinceRefresh = 0f;

            while (elapsed < realSeconds)
            {
                float delta = Time.unscaledDeltaTime;
                elapsed += delta;
                sinceRefresh += delta;

                if (sinceRefresh >= refreshInterval)
                {
                    sinceRefresh = 0f;
                    // animate: false - at this speed the slide animation never finishes before the
                    // next refresh, and the rows would jitter instead of moving.
                    if (panel != null && panel.IsOpen)
                    {
                        panel.Refresh(animate: false);
                    }
                    else
                    {
                        // Still drive the manager even with the panel closed, so the event ends and
                        // settles on schedule.
                        GetBoard();
                    }

                    if (state.SettledEventId != startingSettledId)
                    {
                        // Settled. Stop here so the board is left on its final standings, in
                        // cooldown, exactly as a real event would leave it.
                        EndSimulation("finished - event complete");
                        yield break;
                    }
                }

                yield return null;
            }

            EndSimulation("finished");
        }

        /// <summary>
        /// A clock that starts at a chosen instant and then runs at a multiple of real time.
        ///
        /// Uses unscaled realtime rather than DateTime.UtcNow deltas so it keeps working while the
        /// game is paused, which is exactly when somebody is likely to be watching the board.
        /// </summary>
        private sealed class ScaledTimeProvider : ITimeProvider
        {
            private readonly DateTime anchorUtc;
            private readonly float anchorRealtime;
            private readonly double scale;

            public ScaledTimeProvider(DateTime anchorUtc, double scale)
            {
                this.anchorUtc = anchorUtc;
                this.scale = scale;
                anchorRealtime = Time.realtimeSinceStartup;
            }

            public DateTime UtcNow
            {
                get
                {
                    double elapsed = (Time.realtimeSinceStartup - anchorRealtime) * scale;
                    return anchorUtc.AddSeconds(elapsed);
                }
            }
        }
    }
}
#endif
