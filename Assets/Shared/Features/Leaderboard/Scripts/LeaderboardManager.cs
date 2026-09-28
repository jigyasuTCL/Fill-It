using Shared.Boot;
using Shared.GameConfig;
using Shared.Leaderboard.Bots;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.Leaderboard
{
    /// <summary>
    /// One row of the rendered board.
    /// </summary>
    public readonly struct LeaderboardEntry
    {
        public readonly int Rank;
        public readonly string Name;
        public readonly int Score;
        public readonly bool IsPlayer;

        // Resolved sprites rather than profile item ids: a row should not have to know what a
        // ProfileCatalogAsset is, and the player's and a rival's looks are resolved by completely
        // different routes (what they equipped vs. an index frozen in saved state). Either may be
        // null - no catalog assigned, or no art authored on that item - and the row renders
        // without it.
        public readonly Sprite Avatar;
        public readonly Sprite Frame;

        /// <summary>"Playing now", "Online now", or "12m ago" - see BotStatusFormatter.</summary>
        public readonly string StatusLabel;

        /// <summary>
        /// Whether this rival is offline, online, or actively playing right now.
        ///
        /// Carried alongside StatusLabel rather than parsed back out of it: the label is display
        /// text - it gets reworded and localised - and anything that drove a green dot by comparing
        /// it against "Online now" would break the first time somebody edited that string.
        /// </summary>
        public readonly Bots.BotActivityStatus Status;

        /// <summary>
        /// When this entry last reached its score. The first ranking tie-break: two rows on the
        /// same score are ordered by who got there first, not arbitrarily.
        /// </summary>
        public readonly double ReachedSeconds;

        public LeaderboardEntry(
            int rank,
            string name,
            int score,
            bool isPlayer,
            Sprite avatar = null,
            Sprite frame = null,
            string statusLabel = null,
            double reachedSeconds = 0d,
            Bots.BotActivityStatus status = Bots.BotActivityStatus.Offline)
        {
            Rank = rank;
            Name = name;
            Score = score;
            IsPlayer = isPlayer;
            Avatar = avatar;
            Frame = frame;
            StatusLabel = statusLabel;
            ReachedSeconds = reachedSeconds;
            Status = status;
        }

        /// <summary>Same entry at a different rank - what the ranking pass below produces.</summary>
        public LeaderboardEntry WithRank(int rank)
        {
            return new LeaderboardEntry(rank, Name, Score, IsPlayer, Avatar, Frame, StatusLabel, ReachedSeconds, Status);
        }
    }

    /// <summary>
    /// The local leaderboard. The player earns 1 point per heart left over on a level win (see
    /// CatFlapGameplayManager.HandleLevelWon) and is ranked against simulated rivals.
    ///
    /// There is no server: this project has no HTTP client and no authenticated identity, so the
    /// whole board is generated and advanced on-device.
    ///
    /// The one idea everything else follows from: a rival's score is NEVER incrementally mutated.
    /// It is BaseScore plus a pure function of how long the current epoch has been running, so
    /// opening the board twice with no time passing cannot change anything, and catching up after
    /// a month away costs the same as catching up after a minute.
    /// </summary>
    public sealed partial class LeaderboardManager : MonoBehaviour
    {
        [Tooltip("Display names for the simulated rivals. Falls back to Resources/Configs/LeaderboardRivalPool when unassigned.")]
        [SerializeField] private LeaderboardRivalPoolAsset rivalPool;

        [Tooltip("Supplies the player's name/avatar/frame, and dresses the rivals out of the same catalog. Leave unassigned and rows render name + score only.")]
        [SerializeField] private Shared.Profile.ProfileManager profile;

        private readonly LeaderboardSaveManager saveManager = new LeaderboardSaveManager();
        private ITimeProvider timeProvider = new SystemTimeProvider();

        private LeaderboardStateData state;
        private int playerScore;
        private bool initialized;
        private LeaderboardPendingRewardsData pendingRewards = new LeaderboardPendingRewardsData();

        // Once a second is plenty: this only decides which side of a timestamp the clock is on.
        private const float TickIntervalSeconds = 1f;
        private float tickCountdown;

        // The whole roster for the current event, derived from the event seed. Rebuilt when the
        // event changes, never saved - a plan is a pure function of seed + config + length, so
        // storing it would only be a way for it to go stale.
        private List<BotPlan> botPlans;
        private int plannedEventId = -1;

        // Set by anything that actually changes the saved state. GetBoard runs on every refresh -
        // and far faster than that under the simulation cheat - so saving unconditionally there
        // would rewrite the whole PlayerData file many times a second for no reason.
        private bool stateDirty;

        // Stretches the player was away for, in seconds from the event start. Bursts starting
        // inside one play the slower rubberband variant. Populated by the activity tracker in the
        // next phase; empty means every burst plays normally.
        private readonly List<BotAbsenceWindow> absenceWindows = new List<BotAbsenceWindow>();

        /// <summary>
        /// Raised by ClaimPendingRewards with what was won. Coins and boosters are granted by
        /// the GAME-side listener (see CatFlapLeaderboardRewardApplier) - this feature is
        /// Shared and deliberately knows nothing about either.
        /// </summary>
        public event Action<LeaderboardPendingRewardsData> RewardsClaimed;

        /// <summary>
        /// Raised whenever the pending reward changes - something was won at settlement, or the
        /// player collected it. Drives the results popup and the attention badges, so neither has
        /// to poll.
        /// </summary>
        public event Action PendingRewardsChanged;

        public int PlayerScore => playerScore;

        public bool Enabled => ResolveConfig().Enabled;

        /// <summary>
        /// The level the board opens at. Mirrors ProfileConfigData's convention exactly: -1 is
        /// coming soon, 0 is always available, positive is a level gate.
        /// </summary>
        public int UnlockLevel => ResolveConfig().UnlockLevel;

        /// <summary>Built but not live yet: shown locked with no level to chase.</summary>
        public bool IsComingSoon => UnlockLevel == -1;

        /// <summary>The one answer to "can the player open the leaderboard at all?".</summary>
        public bool IsFeatureUnlocked => Enabled && !IsComingSoon && CurrentLevelNumber >= UnlockLevel;

        /// <summary>
        /// 1-based level number, matching what UnlockLevel is authored against.
        ///
        /// Prefers the provider a menu handed over via SetLevelProvider - during a session the
        /// gameplay manager's live number is ahead of what PlayerData has been saved with - and
        /// falls back to the saved index, which is what matters on a cold main menu. PlayerData
        /// stores the 0-based index, so an UnlockLevel of 5 must not fire on index 5 (level 6).
        /// </summary>
        public int CurrentLevelNumber
        {
            get
            {
                if (levelNumberProvider != null)
                {
                    return levelNumberProvider.Invoke();
                }

                return Mathf.Max(0, Shared.PlayerData.PlayerData.Instance.CurrentLevelIndex) + 1;
            }
        }

        public void SetLevelProvider(Func<int> provider)
        {
            levelNumberProvider = provider;
        }

        private Func<int> levelNumberProvider;

        /// <summary>
        /// Loads (or first-time generates) the board. Safe to call more than once.
        ///
        /// Nothing has to call this: every public entry point runs EnsureInitialized first, so the
        /// board loads itself on first use. That matters because this component is authored on the
        /// leaderboard UI prefab, which ships inactive - neither Awake nor Start runs until the
        /// panel is first opened, while AddWinPoints can land long before that.
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            playerScore = saveManager.LoadPlayerScore();
            pendingRewards = saveManager.LoadPendingRewards();

            state = saveManager.LoadState();
            if (state == null)
            {
                state = CreateState();
                stateDirty = true;
            }

            EnsureBotsPlanned();
            // A cycle almost always ends while the app is shut. Settling it here, before anything
            // reads the board, is what makes the rollover invisible: the player opens the game to
            // a fresh cycle with the previous one's reward already waiting on the button.
            SettleExpiredCycles();
            saveManager.SaveState(state);
            stateDirty = false;
        }

        // =========================================================
        // CYCLE
        // =========================================================

        /// <summary>
        /// How long the current cycle has left. Zero once it has expired but before
        /// SettleExpiredCycles has run, so the UI shows 00:00:00 for that instant rather than
        /// counting into the negative.
        /// </summary>
        public TimeSpan TimeUntilCycleEnd
        {
            get
            {
                EnsureInitialized();
                DateTime end = CycleEndUtc();
                DateTime now = timeProvider.UtcNow;
                return now >= end ? TimeSpan.Zero : end - now;
            }
        }

        /// <summary>
        /// True between one event ending and the next starting.
        ///
        /// The board is frozen on its final standings throughout: bot scores are already clamped to
        /// the event length by GetBoard, and the player's score is not wiped until the roll. So the
        /// cooldown shows exactly who finished where, for as long as CooldownHours says.
        /// </summary>
        public bool IsInCooldown
        {
            get
            {
                EnsureInitialized();
                DateTime now = timeProvider.UtcNow;
                return now >= CycleEndUtc() && now < CooldownEndUtc();
            }
        }

        /// <summary>
        /// How long until the NEXT event opens - the cooldown countdown. Outside the cooldown this
        /// is the time to the end of the current event plus the cooldown after it, so a single
        /// label can count down to "next event" continuously if that is what the UI wants.
        /// </summary>
        public TimeSpan TimeUntilNextEvent
        {
            get
            {
                EnsureInitialized();
                DateTime end = CooldownEndUtc();
                DateTime now = timeProvider.UtcNow;
                return now >= end ? TimeSpan.Zero : end - now;
            }
        }

        /// <summary>
        /// Whether the results screen should put itself on screen.
        ///
        /// A win is deliberately NOT announced the moment it settles: during the cooldown the board
        /// stays up with its final standings and a Collect button, and the popup only takes over
        /// once the next event is about to begin. Opening it at settlement would cover the very
        /// standings the player just earned.
        /// </summary>
        public bool ShouldAutoOpenRewards
        {
            get
            {
                EnsureInitialized();
                return pendingRewards.HasRewards && !IsInCooldown;
            }
        }

        /// <summary>
        /// The player's equipped profile art, for UI that shows them outside a board row - the
        /// results screen's winner on the podium. Null when there is no ProfileManager assigned,
        /// which is also what the board rows fall back to.
        /// </summary>
        public Sprite PlayerAvatar => profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Avatar) : null;

        public Sprite PlayerFrame => profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Frame) : null;

        /// <summary>The placement and payout waiting to be collected. Never null; check HasRewards.</summary>
        public LeaderboardPendingRewardsData PendingRewards
        {
            get
            {
                EnsureInitialized();
                return pendingRewards;
            }
        }

        /// <summary>
        /// Hands the pending rewards to whoever is listening and clears them.
        ///
        /// This does NOT grant coins or boosters itself - the leaderboard is a Shared feature with
        /// no business knowing what a Cat Flap booster or coin wallet is. It says what was won and
        /// the game-side listener applies it. Profile items are the one exception, since
        /// ProfileManager is Shared too.
        ///
        /// Returns false when there was nothing to collect, so a double-tap cannot pay twice.
        /// </summary>
        public bool ClaimPendingRewards()
        {
            return ClaimPendingRewards(false);
        }

        /// <summary>
        /// Claims the pending result. When doubleRewards is true, stackable rewards (coins and
        /// boosters) are doubled before RewardsClaimed is raised. Profile avatar/frame rewards are
        /// still granted only once because those are ownership items, not quantities.
        /// </summary>
        public bool ClaimPendingRewards(bool doubleRewards)
        {
            EnsureInitialized();

            if (pendingRewards == null || !pendingRewards.HasRewards)
            {
                return false;
            }

            LeaderboardPendingRewardsData claimed = doubleRewards
                ? CreateClaimSnapshotWithMultiplier(pendingRewards, 2)
                : pendingRewards;

            // Cleared and SAVED before the event fires, never after: a listener that throws, or a
            // crash mid-grant, must not leave the same reward claimable again on the next launch.
            pendingRewards = new LeaderboardPendingRewardsData();
            saveManager.SavePendingRewards(pendingRewards);
            PendingRewardsChanged?.Invoke();

            GrantProfileRewards(claimed);
            WarnIfNobodyApplies(claimed);
            RewardsClaimed?.Invoke(claimed);
            return true;
        }

        private static LeaderboardPendingRewardsData CreateClaimSnapshotWithMultiplier(
            LeaderboardPendingRewardsData source,
            int multiplier)
        {
            var snapshot = new LeaderboardPendingRewardsData
            {
                Version = source.Version,
                Rank = source.Rank,
                Score = source.Score,
                RankWinCount = source.RankWinCount
            };

            if (source.Podium != null)
            {
                snapshot.Podium = new List<LeaderboardPodiumPlayerData>(source.Podium);
            }

            if (source.Rewards == null)
            {
                return snapshot;
            }

            for (int index = 0; index < source.Rewards.Count; index++)
            {
                LeaderboardRewardData sourceReward = source.Rewards[index];
                if (sourceReward == null)
                {
                    continue;
                }

                int amount = sourceReward.Amount;
                if (sourceReward.Kind == LeaderboardRewardKind.Coins
                    || sourceReward.Kind == LeaderboardRewardKind.Booster)
                {
                    amount *= Mathf.Max(1, multiplier);
                }

                snapshot.Rewards.Add(new LeaderboardRewardData
                {
                    Kind = sourceReward.Kind,
                    Amount = amount,
                    ItemId = sourceReward.ItemId,
                    IconKey = sourceReward.IconKey
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Says so when a payout is about to be announced into an empty room.
        ///
        /// Coins and boosters are deliberately granted by a game-side listener, so with no listener
        /// attached the reward is cleared, the popup closes, and nothing whatsoever happens to the
        /// player's balance - the collect button looks broken while every line of this class is
        /// behaving exactly as written. That is worth one loud line in the console.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnIfNobodyApplies(LeaderboardPendingRewardsData claimed)
        {
            if (RewardsClaimed != null)
            {
                return;
            }

            for (int index = 0; index < claimed.Rewards.Count; index++)
            {
                LeaderboardRewardKind kind = claimed.Rewards[index].Kind;
                if (kind != LeaderboardRewardKind.Coins && kind != LeaderboardRewardKind.Booster)
                {
                    continue;
                }

                Debug.LogWarning(
                    "[Leaderboard] Claimed a reward containing coins or boosters, but nothing is subscribed to "
                    + "RewardsClaimed - so none of it will be granted. Add a CatFlapLeaderboardRewardApplier to the "
                    + "scene and assign this manager to it.",
                    this);
                return;
            }
        }

        private DateTime CycleEndUtc()
        {
            LeaderboardConfigData config = ResolveConfig();
            var start = new DateTime(state.CycleStartUtcTicks, DateTimeKind.Utc);
            return start.AddHours(Math.Max(0.1f, config.CycleHours));
        }

        /// <summary>When the next event opens: the end of this one plus the cooldown after it.</summary>
        private DateTime CooldownEndUtc()
        {
            LeaderboardConfigData config = ResolveConfig();
            return CycleEndUtc().AddHours(Math.Max(0f, config.CooldownHours));
        }

        /// <summary>
        /// Closes out every cycle that has elapsed since the last launch.
        ///
        /// A loop rather than one check, because the player may have been away for days. Only the
        /// FIRST expired cycle pays out though - the ones after it were played by nobody, and
        /// paying a week of absence would make not playing the best strategy. The rest only
        /// advance the clock.
        /// </summary>
        private void SettleExpiredCycles()
        {
            if (state.CycleStartUtcTicks <= 0L)
            {
                state.CycleStartUtcTicks = timeProvider.UtcNow.Ticks;
                stateDirty = true;
                return;
            }

            // A cycle that starts in the FUTURE is always corrupt, and it fails silently in the
            // worst way: ElapsedEventSeconds floors at zero, so every bot reads as "not joined yet"
            // and the board shows nothing but the player, with a countdown of up to two cycles.
            //
            // It happens for real - a device clock moved forward and back - and it happens every
            // time the editor's event simulation rolls a cycle on its fast clock, because the new
            // cycle is anchored to an end that is a whole cycle ahead of real time.
            if (state.CycleStartUtcTicks > timeProvider.UtcNow.Ticks)
            {
                state.CycleStartUtcTicks = timeProvider.UtcNow.Ticks;
                stateDirty = true;
                LogSettlement("Cycle start was in the future - re-anchored to now. The board restarts from this moment.");
            }

            // PAY OUT AT THE END OF THE EVENT, not at the roll. The two used to be the same
            // instant; with a cooldown between them the player has to be paid for the standings
            // they actually finished on, while those standings are still on the board.
            //
            // Guarded by event id rather than by a flag, so it can only ever happen once per event
            // however many times this runs.
            if (timeProvider.UtcNow >= CycleEndUtc() && state.SettledEventId != state.EventId)
            {
                state.SettledEventId = state.EventId;
                stateDirty = true;
                CollectCycleRewards();
            }

            bool announceCooldownEnded = false;

            // Bounded so a corrupt far-past timestamp cannot spin here; past the bound the cycle
            // simply restarts from now, below.
            for (int guard = 0; guard < 400 && timeProvider.UtcNow >= CooldownEndUtc(); guard++)
            {
                // Only the first expired event paid out - the ones after it were played by nobody,
                // and paying a week of absence would make not playing the best strategy. The rest
                // just advance the clock, and the settle guard above already skipped them.
                RollCycle();
                announceCooldownEnded = true;
            }

            if (timeProvider.UtcNow >= CooldownEndUtc())
            {
                state.CycleStartUtcTicks = timeProvider.UtcNow.Ticks;
                stateDirty = true;
            }

            // Told after the roll, never during it: ShouldAutoOpenRewards reads IsInCooldown, and
            // a listener woken mid-rollover would be asking about a cooldown that is half gone.
            if (announceCooldownEnded && pendingRewards.HasRewards)
            {
                PendingRewardsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Drives the event clock with the board closed.
        ///
        /// Without this nothing advances unless something asks for the board, so a player sitting
        /// on the main menu would watch the cooldown timer expire and see no new event - and the
        /// results popup, which waits for the cooldown to end, would never open at all. Throttled
        /// because the only thing that can change between ticks is which side of a timestamp we are
        /// on.
        /// </summary>
        private void Update()
        {
            tickCountdown -= Time.unscaledDeltaTime;
            if (tickCountdown > 0f)
            {
                return;
            }

            tickCountdown = TickIntervalSeconds;

            // Initialised here rather than guarded against: if nothing else has woken the manager
            // yet, the clock still has to run.
            EnsureInitialized();
            SettleExpiredCycles();

            if (stateDirty)
            {
                saveManager.SaveState(state);
                stateDirty = false;
            }
        }

        /// <summary>
        /// Starts the next event: every score back to zero and a brand new roster.
        ///
        /// Paying out is NOT done here - that happened at the end of the event, a cooldown ago (see
        /// SettleExpiredCycles). By the time this runs the final standings have already been read,
        /// so wiping them costs nothing.
        /// </summary>
        private void RollCycle()
        {
            // Everything back to zero: the player's score, and the whole roster.
            playerScore = 0;
            saveManager.SavePlayerScore(playerScore);

            // A new event id is all it takes to get a completely fresh board - the seed derives
            // from it, and the plan derives from the seed. Nothing per-bot is stored to reset.
            state.EventId++;
            botPlans = null;
            // Absence only means anything inside one event.
            absenceWindows.Clear();

            state.LastSimUtcTicks = timeProvider.UtcNow.Ticks;
            // Anchored to the end of the cooldown, not the end of the event, or every cooldown
            // would be silently folded into the next event's running time.
            state.CycleStartUtcTicks = CooldownEndUtc().Ticks;
            stateDirty = true;
        }

        private void CollectCycleRewards()
        {
            int finalRank = ResolvePlayerRankUnsafe();
            if (finalRank <= 0)
            {
                return;
            }

            List<LeaderboardRewardData> earned = BuildRewardsForRank(finalRank);

            if (earned.Count == 0)
            {
                // By far the most common outcome, and the most confusing one to debug from
                // outside: the reward table only pays the top few places, so most events end
                // owing nothing at all and the results screen correctly never opens.
                LogSettlement($"Event settled at rank {finalRank} with {playerScore} points - no reward row covers that placing, so nothing is pending.");
                return;
            }

            LogSettlement($"Event settled at rank {finalRank} with {playerScore} points - {earned.Count} reward(s) pending.");

            // Merged rather than replaced: a player who misses two cycles without opening the
            // panel should still find the older payout waiting, not silently overwritten.
            // Rank/Score track the most recent finish.
            pendingRewards.Rank = finalRank;
            pendingRewards.Score = playerScore;
            // Counted here rather than on collection, so a reward the player never opens still
            // counts as the placement they achieved.
            pendingRewards.RankWinCount = state.RegisterPlacement(finalRank);
            pendingRewards.Podium = BuildFinalPodiumSnapshot();
            stateDirty = true;
            pendingRewards.Rewards.AddRange(earned);
            saveManager.SavePendingRewards(pendingRewards);
            // Announced AFTER the save, so anything that reacts by opening a popup can never show
            // a reward that was not durably recorded first.
            PendingRewardsChanged?.Invoke();
        }

        /// <summary>
        /// Captures the finished event's top three before RollCycle clears the score and advances
        /// the event id. This is persisted inside the pending reward receipt so the reward popup
        /// can still show the exact winners even if it opens days later.
        /// </summary>
        private List<LeaderboardPodiumPlayerData> BuildFinalPodiumSnapshot()
        {
            EnsureBotsPlanned();

            LeaderboardConfigData config = ResolveConfig();
            double eventSeconds = EventSeconds(config);
            double onlineTailSeconds = config.Bots.OnlineTailMinutes * 60d;

            var rows = new List<LeaderboardEntry>(botPlans.Count + 1);
            // Name -> the art indices that name was planned with. Built while the rows are, so the
            // podium can look them up directly afterwards instead of trying to work out which plan
            // a ranked row came from by comparing its sprites - which silently matched the wrong
            // bot whenever the art failed to resolve and every sprite came back null.
            var botArtByName = new Dictionary<string, Vector2Int>(botPlans.Count, StringComparer.Ordinal);

            for (int index = 0; index < botPlans.Count; index++)
            {
                BotPlan plan = botPlans[index];
                BotSnapshot snapshot = BotEvaluator.Evaluate(
                    plan,
                    eventSeconds,
                    eventSeconds,
                    absenceWindows,
                    onlineTailSeconds);

                if (!snapshot.IsVisible)
                {
                    continue;
                }

                botArtByName[plan.Name] = new Vector2Int(plan.AvatarIndex, plan.FrameIndex);

                rows.Add(new LeaderboardEntry(
                    0,
                    plan.Name,
                    snapshot.Score,
                    false,
                    ResolveBotArt(Shared.Profile.ProfileSlot.Avatar, plan.AvatarIndex),
                    ResolveBotArt(Shared.Profile.ProfileSlot.Frame, plan.FrameIndex),
                    BotStatusFormatter.Format(snapshot.Status, eventSeconds - snapshot.LastSeenSeconds),
                    snapshot.ReachedSeconds));
            }

            rows.Add(new LeaderboardEntry(
                0,
                ResolvePlayerName(),
                playerScore,
                true,
                profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Avatar) : null,
                profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Frame) : null,
                BotStatusFormatter.OnlineLabel,
                playerScore > 0 ? eventSeconds : 0d,
                // The player is reading this screen, so they are online by definition.
                Bots.BotActivityStatus.Online));

            IReadOnlyList<LeaderboardEntry> ranked = RankRows(rows);
            var result = new List<LeaderboardPodiumPlayerData>(Mathf.Min(3, ranked.Count));

            // Read once, here, rather than when the popup finally opens: that can be a cooldown
            // later, and the player may have changed their avatar by then.
            string playerAvatarId = profile != null
                ? profile.GetEquipped(Shared.Profile.ProfileSlot.Avatar)?.Id
                : null;
            string playerFrameId = profile != null
                ? profile.GetEquipped(Shared.Profile.ProfileSlot.Frame)?.Id
                : null;

            for (int index = 0; index < ranked.Count && index < 3; index++)
            {
                LeaderboardEntry entry = ranked[index];

                // -1 means "no art recorded", which ResolveBotArt already renders as nothing.
                var art = new Vector2Int(-1, -1);
                if (!entry.IsPlayer && entry.Name != null
                    && botArtByName.TryGetValue(entry.Name, out Vector2Int found))
                {
                    art = found;
                }

                result.Add(new LeaderboardPodiumPlayerData
                {
                    Name = entry.Name,
                    Score = entry.Score,
                    IsPlayer = entry.IsPlayer,
                    AvatarIndex = art.x,
                    FrameIndex = art.y,
                    AvatarId = entry.IsPlayer ? playerAvatarId ?? string.Empty : string.Empty,
                    FrameId = entry.IsPlayer ? playerFrameId ?? string.Empty : string.Empty
                });
            }

            if (profile == null)
            {
                LogSettlement("No ProfileManager assigned - the podium will have no avatars or frames on it. Assign it on the LeaderboardManager scene instance.");
            }

            return result;
        }

        private Sprite ResolvePlayerArt(Shared.Profile.ProfileSlot slot, string id)
        {
            return profile != null ? profile.ResolveArtById(slot, id) : null;
        }

        /// <summary>
        /// Resolves the persisted podium snapshot back into the same LeaderboardEntry shape used by
        /// LeaderboardRowView.
        /// </summary>
        public LeaderboardEntry ResolvePendingPodiumEntry(LeaderboardPodiumPlayerData data, int rank)
        {
            EnsureInitialized();

            if (data == null)
            {
                return new LeaderboardEntry(rank, string.Empty, 0, false);
            }

            // The saved id wins: it is what they were actually wearing when they won. Live
            // equipment is only the fallback, for a record written before ids were stored.
            Sprite avatar = data.IsPlayer
                ? ResolvePlayerArt(Shared.Profile.ProfileSlot.Avatar, data.AvatarId) ?? PlayerAvatar
                : ResolveBotArt(Shared.Profile.ProfileSlot.Avatar, data.AvatarIndex);

            Sprite frame = data.IsPlayer
                ? ResolvePlayerArt(Shared.Profile.ProfileSlot.Frame, data.FrameId) ?? PlayerFrame
                : ResolveBotArt(Shared.Profile.ProfileSlot.Frame, data.FrameIndex);

            return new LeaderboardEntry(
                rank,
                data.Name,
                data.Score,
                data.IsPlayer,
                avatar,
                frame,
                data.IsPlayer ? BotStatusFormatter.OnlineLabel : null,
                0d,
                // Only the player. The rivals on a finished podium are a frozen record - showing
                // them as live would be a lie, and their real status is long gone with the event.
                data.IsPlayer ? Bots.BotActivityStatus.Online : Bots.BotActivityStatus.Offline);
        }

        /// <summary>
        /// What the reward table pays for a given placing. Empty - the usual answer - means that
        /// placing is not in the table.
        ///
        /// Shared with the editor cheats so a hand-triggered payout is built by exactly the same
        /// rules as a real one, and cannot drift away from it.
        /// </summary>
        private List<LeaderboardRewardData> BuildRewardsForRank(int rank)
        {
            var earned = new List<LeaderboardRewardData>();
            LeaderboardConfigData config = ResolveConfig();

            for (int index = 0; index < config.Rewards.Count; index++)
            {
                LeaderboardRewardConfigData row = config.Rewards[index];
                if (row == null || !row.Matches(rank) || row.Rewards == null)
                {
                    continue;
                }

                // Every prize under that placement, not just the first - a rank can pay coins and
                // a booster and a frame together.
                for (int rewardIndex = 0; rewardIndex < row.Rewards.Count; rewardIndex++)
                {
                    LeaderboardRewardItemData reward = row.Rewards[rewardIndex];
                    if (reward == null)
                    {
                        continue;
                    }

                    earned.Add(new LeaderboardRewardData
                    {
                        Kind = reward.Kind,
                        Amount = reward.Amount,
                        ItemId = reward.ItemId,
                        IconKey = reward.IconKey
                    });
                }

                // One row per rank, so nothing below can match as well.
                break;
            }

            return earned;
        }

        /// <summary>
        /// Compiled out of player builds entirely by the Conditional attribute - settlement happens
        /// once an event, so this is worth having permanently in the editor and worth nothing in a
        /// shipped game.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogSettlement(string message)
        {
            Debug.Log("[Leaderboard] " + message, this);
        }

        /// <summary>
        /// The player's rank on the board as it currently stands. Unsafe in the sense that it does
        /// NOT advance time or roll anything - it must not, since RollCycle calls it mid-rollover.
        /// </summary>
        private int ResolvePlayerRankUnsafe()
        {
            EnsureBotsPlanned();

            LeaderboardConfigData config = ResolveConfig();
            double eventSeconds = EventSeconds(config);
            // Evaluated AT THE END, not now: settlement pays for where the player finished, and
            // reading "now" after the event closed would rank them against a frozen board using a
            // later timestamp.
            double onlineTailSeconds = config.Bots.OnlineTailMinutes * 60d;

            int ahead = 0;
            for (int index = 0; index < botPlans.Count; index++)
            {
                BotSnapshot snapshot = BotEvaluator.Evaluate(botPlans[index], eventSeconds, eventSeconds, absenceWindows, onlineTailSeconds);
                // Strictly greater, so ties go to the player - matching RankRows.
                if (snapshot.IsVisible && snapshot.Score > playerScore)
                {
                    ahead++;
                }
            }

            return ahead + 1;
        }

        private void GrantProfileRewards(LeaderboardPendingRewardsData claimed)
        {
            if (profile == null)
            {
                return;
            }

            for (int index = 0; index < claimed.Rewards.Count; index++)
            {
                LeaderboardRewardData reward = claimed.Rewards[index];
                if (reward.Kind == LeaderboardRewardKind.ProfileAvatar)
                {
                    profile.Grant(Shared.Profile.ProfileSlot.Avatar, reward.ItemId);
                }
                else if (reward.Kind == LeaderboardRewardKind.ProfileFrame)
                {
                    profile.Grant(Shared.Profile.ProfileSlot.Frame, reward.ItemId);
                }
            }
        }

        /// <summary>
        /// Lets tests drive the simulation without touching the real clock.
        /// </summary>
        public void SetTimeProvider(ITimeProvider provider)
        {
            if (provider != null)
            {
                timeProvider = provider;
            }
        }

        /// <summary>
        /// Adds a level win's points. heartsRemaining IS the point value - the conversion is 1:1
        /// by design. Callers own the "should this level score at all" decision.
        /// </summary>
        public void AddWinPoints(int heartsRemaining)
        {
            EnsureInitialized();

            int points = Mathf.Max(0, heartsRemaining);
            if (points == 0)
            {
                return;
            }

            playerScore += points;
            saveManager.SavePlayerScore(playerScore);
        }

        /// <summary>
        /// The board, highest score first, with the player's own row included.
        ///
        /// Recomputed from scratch every call: each bot's score is a pure function of its plan and
        /// the time, so there is no simulation to step and no per-level state to keep. Bots that
        /// have not joined yet are simply absent, which is what makes the board fill up over the
        /// event.
        /// </summary>
        public IReadOnlyList<LeaderboardEntry> GetBoard()
        {
            EnsureInitialized();
            // Checked on every read, not only at launch: an event that ended while the board was
            // open has to settle before it is drawn, or the player sees a live board past the end.
            SettleExpiredCycles();
            EnsureBotsPlanned();
            if (stateDirty)
            {
                saveManager.SaveState(state);
                stateDirty = false;
            }

            LeaderboardConfigData config = ResolveConfig();
            double eventSeconds = EventSeconds(config);
            double nowSeconds = Math.Min(ElapsedEventSeconds(), eventSeconds);
            double onlineTailSeconds = config.Bots.OnlineTailMinutes * 60d;
            BotPresenceSettings presence = ResolvePresence(config);

            var rows = new List<LeaderboardEntry>(botPlans.Count + 1);
            for (int index = 0; index < botPlans.Count; index++)
            {
                BotPlan plan = botPlans[index];
                BotSnapshot snapshot = BotEvaluator.Evaluate(plan, nowSeconds, eventSeconds, absenceWindows, onlineTailSeconds, presence);
                if (!snapshot.IsVisible)
                {
                    continue;
                }

                rows.Add(new LeaderboardEntry(
                    0,
                    plan.Name,
                    snapshot.Score,
                    false,
                    ResolveBotArt(Shared.Profile.ProfileSlot.Avatar, plan.AvatarIndex),
                    ResolveBotArt(Shared.Profile.ProfileSlot.Frame, plan.FrameIndex),
                    BotStatusFormatter.Format(snapshot.Status, nowSeconds - snapshot.LastSeenSeconds),
                    snapshot.ReachedSeconds,
                    snapshot.Status));
            }

            rows.Add(new LeaderboardEntry(
                0,
                ResolvePlayerName(),
                playerScore,
                true,
                profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Avatar) : null,
                profile != null ? profile.GetEquippedArt(Shared.Profile.ProfileSlot.Frame) : null,
                BotStatusFormatter.OnlineLabel,
                // The player has been here since the event opened, so at 0 they sit above anyone
                // who joined later and below the bots that were already on the board.
                playerScore > 0 ? nowSeconds : 0d,
                // Reading this screen counts as being online.
                Bots.BotActivityStatus.Online));

            return RankRows(rows);
        }

        /// <summary>
        /// Applies the tie order: score first, then who reached it earliest, then the order rows
        /// were built in - bots by roster index, player last.
        ///
        /// Sorted through an index array because List.Sort is unstable, and the last tie-break IS
        /// the original order; sorting the entries directly would let equal rows swap between
        /// refreshes and make the board twitch.
        /// </summary>
        private static IReadOnlyList<LeaderboardEntry> RankRows(List<LeaderboardEntry> rows)
        {
            var order = new int[rows.Count];
            for (int index = 0; index < order.Length; index++)
            {
                order[index] = index;
            }

            Array.Sort(order, (left, right) =>
            {
                LeaderboardEntry a = rows[left];
                LeaderboardEntry b = rows[right];

                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                // Above zero the player wins every tie, so matching a bot's score visibly overtakes
                // it rather than appearing to do nothing.
                if (a.IsPlayer != b.IsPlayer && a.Score > 0)
                {
                    return a.IsPlayer ? -1 : 1;
                }

                int byReached = a.ReachedSeconds.CompareTo(b.ReachedSeconds);
                return byReached != 0 ? byReached : left.CompareTo(right);
            });

            var ranked = new List<LeaderboardEntry>(rows.Count);
            for (int index = 0; index < order.Length; index++)
            {
                ranked.Add(rows[order[index]].WithRank(index + 1));
            }

            return ranked;
        }

        // =========================================================
        // BOTS
        // =========================================================

        /// <summary>Seconds since this event opened, never negative.</summary>
        private double ElapsedEventSeconds()
        {
            var start = new DateTime(state.CycleStartUtcTicks, DateTimeKind.Utc);
            return Math.Max(0d, (timeProvider.UtcNow - start).TotalSeconds);
        }

        private static double EventSeconds(LeaderboardConfigData config)
        {
            return Math.Max(0.1f, config.CycleHours) * 3600d;
        }

        /// <summary>
        /// Plans the roster for the current event, once. Re-plans only when the event changes -
        /// the plan is a pure function of the seed, so it is never saved and never migrated.
        /// </summary>
        private void EnsureBotsPlanned()
        {
            if (botPlans != null && plannedEventId == state.EventId)
            {
                return;
            }

            LeaderboardConfigData config = ResolveConfig();
            int eventSeed = DeterministicRandom.DeriveSeed(state.InstallSeed, state.EventId);

            botPlans = BotPlanner.Plan(
                eventSeed,
                config.Bots,
                EventSeconds(config),
                ResolveNamePool(),
                ResolveCatalogCount(Shared.Profile.ProfileSlot.Avatar),
                ResolveCatalogCount(Shared.Profile.ProfileSlot.Frame));

            plannedEventId = state.EventId;
        }

        private List<string> ResolveNamePool()
        {
            var names = new List<string>();
            EnsureRivalPool();
            if (rivalPool == null)
            {
                return names;
            }

            for (int index = 0; index < rivalPool.Count; index++)
            {
                names.Add(rivalPool.GetName(index));
            }

            return names;
        }

        private int ResolveCatalogCount(Shared.Profile.ProfileSlot slot)
        {
            return profile != null && profile.Catalog != null ? profile.Catalog.GetAll(slot).Count : 0;
        }

        /// <summary>
        /// The idle-presence settings for the live board, straight from config.
        ///
        /// Percent in config, 0..1 here - the conversion belongs at this edge, so the evaluator
        /// never has to know what units somebody authored in.
        /// </summary>
        private BotPresenceSettings ResolvePresence(LeaderboardConfigData config)
        {
            return new BotPresenceSettings(
                config.Bots.IdleOnlinePercent / 100d,
                config.Bots.IdlePlayingPercent / 100d,
                config.Bots.PresenceBucketMinutes * 60d);
        }

        private Sprite ResolveBotArt(Shared.Profile.ProfileSlot slot, int index)
        {
            return profile != null ? profile.ResolveArtByIndex(slot, index) : null;
        }

        public int PlayerRank
        {
            get
            {
                IReadOnlyList<LeaderboardEntry> board = GetBoard();
                for (int index = 0; index < board.Count; index++)
                {
                    if (board[index].IsPlayer)
                    {
                        return board[index].Rank;
                    }
                }

                return board.Count;
            }
        }


        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// A brand-new event. Only the seed, the clock and the event id are stored - the roster
        /// itself is planned from them on demand.
        /// </summary>
        private LeaderboardStateData CreateState()
        {
            return new LeaderboardStateData
            {
                Version = LeaderboardStateData.CurrentVersion,
                InstallSeed = Environment.TickCount,
                LastSimUtcTicks = timeProvider.UtcNow.Ticks,
                EventId = 0,
                // -1, not 0: event 0 has not been settled yet, and 0 would mean it had.
                SettledEventId = -1,
                CycleStartUtcTicks = timeProvider.UtcNow.Ticks
            };
        }


        /// <summary>
        /// Goes through ProfileManager when there is one, so the name on the board is the same name
        /// the profile picker shows - including its trim/fallback rules. Falls back to reading
        /// PlayerData directly, which is what this did before the profile feature existed.
        /// </summary>
        private string ResolvePlayerName()
        {
            if (profile != null)
            {
                return profile.PlayerName;
            }

            string username = Shared.PlayerData.PlayerData.Instance?.Username;
            return string.IsNullOrWhiteSpace(username) ? "You" : username;
        }


        private void EnsureRivalPool()
        {
            if (rivalPool == null)
            {
                rivalPool = Resources.Load<LeaderboardRivalPoolAsset>("Configs/LeaderboardRivalPool");
            }
        }


        private static LeaderboardConfigData ResolveConfig()
        {
            GlobalConfig config = GlobalConfigRuntimeContext.Config;
            LeaderboardConfigData leaderboard = config?.Leaderboard;
            return leaderboard ?? new LeaderboardConfigData();
        }
    }
}
