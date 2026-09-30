using System;
using Newtonsoft.Json;

namespace Shared.GameConfig
{
    [Serializable]
    public sealed class GlobalConfig
    {
        private static readonly BoosterConfigData[] DefaultBoosterConfigList =
        {
            new BoosterConfigData { UnLockLevel = 0, UnLockCount = 3, BuyCoinCost = 50, BuyCount = 1 },
            new BoosterConfigData { UnLockLevel = 7, UnLockCount = 3, BuyCoinCost = 75, BuyCount = 1 },
            new BoosterConfigData { UnLockLevel = 10, UnLockCount = 3, BuyCoinCost = 100, BuyCount = 1 }
        };

        [JsonProperty("totalLevels")]
        public int TotalLevels = 100;

        [JsonProperty("gameJoinCoins")]
        public int InitialCoins = 150;

        [JsonProperty("levelAdMultiplier")]
        public int LevelAdMultiplier = 2;

        // The level-win popup's 2x-reward ad button is hidden for every level before this one
        // (1-based level number) - only the Next button shows, and it re-centers into the ad
        // button's slot. 0 (or lower) means the ad button is always shown.
        [JsonProperty("noAdsBeforeLevel")]
        public int NoAdsBeforeLevel = 0;

        // Level Attempt (hearts/mistake-limit) feature gate, same convention as
        // BoosterConfigData.UnLockLevel: 0 (or lower) means always unlocked (the current
        // default), a positive unlock level keeps the heart break FX and attempt-pips HUD hidden
        // until the player reaches it.
        [JsonProperty("levelAttemptUnlockLevel")]
        public int LevelAttemptUnlockLevel = 0;

        // FTUE (tap-highlight tutorial) stays active through this level number (inclusive); 0
        // (or lower) disables FTUE entirely. Same convention as LevelAttemptUnlockLevel/
        // NoAdsBeforeLevel above.
        [JsonProperty("ftueEnabledUntilLevel")]
        public int FtueEnabledUntilLevel = 4;

        [JsonProperty("levelExtraMovesCount")]
        public int LevelExtraMovesCount = 3;

        [JsonProperty("levelExtraMovesOnRevive")]
        public int LevelExtraMovesOnRevive = 3;

        [JsonProperty("levelExtraTime")]
        public int LevelExtraTime = 30;

        // Hearts (wrong moves spend lives) or Timer (a countdown per level). Seconds added by a
        // Timer-mode revive come from LevelExtraTime above.
        [JsonProperty("gameplayMode")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public GameplayMode GameplayMode = GameplayMode.Hearts;

        [JsonProperty("hardLevels")]
        public int[] HardLevels = new int[0];

        [JsonProperty("loopLevels")]
        public LoopLevelConfig LoopLevels = new LoopLevelConfig();

        [JsonProperty("boosterConfigList")]
        public BoosterConfigData[] BoosterConfigList = DefaultBoosterConfigList;

        [JsonProperty("resumeSavedLevelOnPlay")]
        public bool ResumeSavedLevelOnPlay = true;

        [JsonProperty("restoreCurrentBoardOnLoad")]
        public bool RestoreCurrentBoardOnLoad = true;

        [JsonProperty("currentLevelIndexSaveKey")]
        public string CurrentLevelIndexSaveKey = "Game.CurrentLevelIndex";

        [JsonProperty("boardStateSaveKeyPrefix")]
        public string BoardStateSaveKeyPrefix = "Game.Board.";

        [JsonProperty("dailyChallenge")]
        public DailyChallengeConfigData DailyChallenge = new DailyChallengeConfigData();

        [JsonProperty("SpinTheWheel")]
        public SpinTheWheelConfigData SpinTheWheel = new SpinTheWheelConfigData();

        #region Daily Rewards

        // All tunables for the daily reward system (DailyRewardManager /
        // RewardCycleService) live in DailyRewardConfigData now instead of
        // Resources/daily_reward_config.json. See Shared/GameConfig/DailyRewardConfigData.cs.
        [JsonProperty("dailyReward")]
        public DailyRewardConfigData DailyReward = new DailyRewardConfigData();

        #endregion

        // Local leaderboard tuning (rival count/rates/unlock level) - see LeaderboardConfigData.
        // Kept here alongside the other feature sections rather than in Remote Config's
        // economyConfig, which is scoped to currency values.
        [JsonProperty("leaderboard")]
        public LeaderboardConfigData Leaderboard = new LeaderboardConfigData();

        // Whether the profile (avatar/frame/name) is live and from which level - see
        // ProfileConfigData. The avatars and frames themselves live in ProfileCatalogAsset, each
        // with its own unlock rule; this is only the gate on the feature as a whole.
        [JsonProperty("profile")]
        public ProfileConfigData Profile = new ProfileConfigData();

        public GlobalConfig CloneNormalized()
        {
            return new GlobalConfig
            {
                TotalLevels = Math.Max(0, TotalLevels),
                InitialCoins = Math.Max(0, InitialCoins),
                LevelAdMultiplier = Math.Max(1, LevelAdMultiplier),
                NoAdsBeforeLevel = Math.Max(0, NoAdsBeforeLevel),
                LevelAttemptUnlockLevel = Math.Max(0, LevelAttemptUnlockLevel),
                FtueEnabledUntilLevel = Math.Max(0, FtueEnabledUntilLevel),
                LevelExtraMovesCount = Math.Max(0, LevelExtraMovesCount),
                LevelExtraMovesOnRevive = Math.Max(0, LevelExtraMovesOnRevive),
                LevelExtraTime = Math.Max(0, LevelExtraTime),
                GameplayMode = GameplayMode,
                HardLevels = NormalizeLevels(HardLevels),
                LoopLevels = (LoopLevels ?? new LoopLevelConfig()).CloneNormalized(),
                BoosterConfigList = NormalizeBoosterConfigList(BoosterConfigList),
                ResumeSavedLevelOnPlay = ResumeSavedLevelOnPlay,
                RestoreCurrentBoardOnLoad = RestoreCurrentBoardOnLoad,
                CurrentLevelIndexSaveKey = string.IsNullOrWhiteSpace(CurrentLevelIndexSaveKey) ? "Game.CurrentLevelIndex" : CurrentLevelIndexSaveKey,
                BoardStateSaveKeyPrefix = string.IsNullOrWhiteSpace(BoardStateSaveKeyPrefix) ? "Game.Board." : BoardStateSaveKeyPrefix,
                DailyChallenge = (DailyChallenge ?? new DailyChallengeConfigData()).CloneNormalized(),
                SpinTheWheel = (SpinTheWheel ?? new SpinTheWheelConfigData()).CloneNormalized(),
                DailyReward = (DailyReward ?? new DailyRewardConfigData()).CloneNormalized(),
                Leaderboard = (Leaderboard ?? new LeaderboardConfigData()).CloneNormalized(),
                Profile = (Profile ?? new ProfileConfigData()).CloneNormalized()
            };
        }

        private static int[] NormalizeLevels(int[] levels)
        {
            if (levels == null)
            {
                return new int[0];
            }

            var normalized = new int[levels.Length];
            for (int index = 0; index < levels.Length; index++)
            {
                normalized[index] = Math.Max(1, levels[index]);
            }

            return normalized;
        }

        private static BoosterConfigData[] NormalizeBoosterConfigList(BoosterConfigData[] configs)
        {
            BoosterConfigData[] source = configs == null || configs.Length == 0 ? DefaultBoosterConfigList : configs;
            var normalized = new BoosterConfigData[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                normalized[index] = (source[index] ?? DefaultBoosterConfigList[Math.Min(index, DefaultBoosterConfigList.Length - 1)]).CloneNormalized();
            }

            return normalized;
        }
    }
}