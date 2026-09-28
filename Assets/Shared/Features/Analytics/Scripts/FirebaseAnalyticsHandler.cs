namespace Shared.Analytics
{
    using System.Collections.Generic;

#if !DEV_MODE
    using Coredian;
    using Coredian.Firebase;
#endif

    public sealed class FirebaseAnalyticsHandler : IAnalyticsHandler
    {
#if !DEV_MODE
        private IFirebaseService firebaseService;

        // Resolved eagerly here (mirrors AdsManager.InitializeAdvertising's own eager
        // Core.GetService<IAdvertisementsService>() call) rather than left fully lazy - Firebase
        // requires IConsentService ([RequiredServices] on IFirebaseService), and IConsentService
        // is what triggers the Usercentrics consent prompt. AnalyticsManager.InitializeAnalytics()
        // constructs this handler right after BootLoader's own scene load, so touching
        // Core.GetService<T>() here is past the same Core.Initialize() risk window AdsManager
        // defers past - if consent resolution were left until the first LogEvent call instead,
        // the consent prompt could appear at an arbitrary point mid-gameplay rather than at boot.
        public FirebaseAnalyticsHandler()
        {
            firebaseService = Core.GetService<IFirebaseService>();
        }

        private IFirebaseService FirebaseService => firebaseService ??= Core.GetService<IFirebaseService>();
#endif

        // DEV_MODE never references Coredian.Firebase at all - not just at the call site, but at
        // the type level (no IFirebaseService field/property exists in this build) - some dev
        // machines have a broken/missing Firebase SDK setup, which fails to even COMPILE, not just
        // throw at runtime, so a runtime-only guard around the call isn't enough. Every LogXxx
        // method below is a complete no-op under DEV_MODE instead.
        public void LogLevelStart(LevelResultEventData data)
        {
#if !DEV_MODE
            // Deliberately its own small param set, NOT AddResultData below - level_start's spec
            // row only ever wants these 8 fields (everything else in LevelResultEventData is
            // always 0 at level start anyway, so the spec doesn't bother sending it).
            var parameters = new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, data?.LevelNumber ?? 0 },
                { AnalyticsEvents.ParamNames.FtueLevel, data?.FtueLevel ?? false },
                { AnalyticsEvents.ParamNames.HardLevel, data?.HardLevel ?? false },
                { AnalyticsEvents.ParamNames.ReviveCount, data?.ReviveCount ?? 0 },
                { AnalyticsEvents.ParamNames.RetryCount, data?.RetryCount ?? 0 },
                { AnalyticsEvents.ParamNames.CoinCount, data?.CoinCount ?? 0 },
                { AnalyticsEvents.ParamNames.HintBoosterCount, data?.HintBoosterCount ?? 0 },
                { AnalyticsEvents.ParamNames.DcId, data?.DcId ?? -1 },
            };
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.LevelStart, parameters);
#endif
        }

        public void LogLevelComplete(LevelResultEventData data)
        {
#if !DEV_MODE
            var parameters = new Dictionary<string, object>();
            AddResultData(parameters, data);
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.LevelComplete, parameters);
#endif
        }

        public void LogLevelFail(LevelResultEventData data)
        {
#if !DEV_MODE
            var parameters = new Dictionary<string, object>();
            AddResultData(parameters, data);
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.LevelFail, parameters);
#endif
        }

#if !DEV_MODE
        // Shared by LevelComplete/LevelFail/Home/RestartLevel - the exact same 19-field snapshot
        // (see LevelResultEventData) is sent by all 4 of those. LevelStart sends its own smaller
        // subset instead (see LogLevelStart above), not this.
        private static void AddResultData(Dictionary<string, object> parameters, LevelResultEventData data)
        {
            if (data == null)
            {
                return;
            }

            parameters[AnalyticsEvents.ParamNames.LevelNo] = data.LevelNumber;
            parameters[AnalyticsEvents.ParamNames.FtueLevel] = data.FtueLevel;
            parameters[AnalyticsEvents.ParamNames.HardLevel] = data.HardLevel;
            parameters[AnalyticsEvents.ParamNames.ReviveCount] = data.ReviveCount;
            parameters[AnalyticsEvents.ParamNames.RetryCount] = data.RetryCount;
            parameters[AnalyticsEvents.ParamNames.LevelTime] = data.LevelTime;
            parameters[AnalyticsEvents.ParamNames.CoinCount] = data.CoinCount;
            parameters[AnalyticsEvents.ParamNames.HintBoosterUsed] = data.HintBoosterUsed;
            parameters[AnalyticsEvents.ParamNames.UndoUsed] = data.UndoUsed;
            parameters[AnalyticsEvents.ParamNames.TotalNotesUsed] = data.TotalNotesUsed;
            parameters[AnalyticsEvents.ParamNames.TotalNotesApply] = data.TotalNotesApply;
            parameters[AnalyticsEvents.ParamNames.NotesButtonTapCount] = data.NotesButtonTapCount;
            parameters[AnalyticsEvents.ParamNames.LivesUsed] = data.LivesUsed;
            parameters[AnalyticsEvents.ParamNames.LivesLeft] = data.LivesLeft;
            parameters[AnalyticsEvents.ParamNames.WrongSolutionCount] = data.WrongSolutionCount;
            parameters[AnalyticsEvents.ParamNames.Rule1BreakCount] = data.Rule1BreakCount;
            parameters[AnalyticsEvents.ParamNames.Rule2BreakCount] = data.Rule2BreakCount;
            parameters[AnalyticsEvents.ParamNames.DcId] = data.DcId;
            parameters[AnalyticsEvents.ParamNames.PrePlacedTileTap] = data.PrePlacedTileTap;
        }
#endif

        public void LogOutOfLives(int levelNumber, bool isDailyChallenge, int reviveCount, int retryCount)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.OutOfLives, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNumber, levelNumber },
                { AnalyticsEvents.ParamNames.IsDailyChallenge, isDailyChallenge },
                { AnalyticsEvents.ParamNames.ReviveCount, reviveCount },
                { AnalyticsEvents.ParamNames.RetryCount, retryCount },
            });
#endif
        }

        public void LogBoosterPurchased(string boosterType, int levelNumber)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.BoosterPurchased, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.BoosterType, boosterType },
                { AnalyticsEvents.ParamNames.LevelNumber, levelNumber },
            });
#endif
        }

        public void LogAdResult(string adType, string placement, bool success)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.AdResult, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.AdType, adType },
                { AnalyticsEvents.ParamNames.Placement, placement },
                { AnalyticsEvents.ParamNames.Success, success },
            });
#endif
        }

        public void LogCurrencySource(string currencyType, int amount, string placement, int balanceAfter)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.CurrencySource, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.CurrencyType, currencyType },
                { AnalyticsEvents.ParamNames.Amount, amount },
                { AnalyticsEvents.ParamNames.Placement, placement },
                { AnalyticsEvents.ParamNames.BalanceAfter, balanceAfter },
            });
#endif
        }

        public void LogCurrencySink(string currencyType, int amount, string outType, int outCount, int balanceAfter)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.CurrencySink, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.CurrencyType, currencyType },
                { AnalyticsEvents.ParamNames.Amount, amount },
                { AnalyticsEvents.ParamNames.OutType, outType },
                { AnalyticsEvents.ParamNames.OutCount, outCount },
                { AnalyticsEvents.ParamNames.BalanceAfter, balanceAfter },
            });
#endif
        }

        public void LogFtueLevelSkip(int levelNumber, int ftueId)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.FtueLevelSkip, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.FtueId, ftueId },
            });
#endif
        }

        public void LogHintPurchasedPopup(int levelNumber, int hintPurchasedCoin, int hintPurchasedAd)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.HintPurchasedPopup, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.HintPurchasedCoin, hintPurchasedCoin },
                { AnalyticsEvents.ParamNames.HintPurchasedAd, hintPurchasedAd },
            });
#endif
        }

        public void LogLivesLost(int levelNumber, float progressionLivesLost)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.LivesLost, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.ProgressionLivesLost, progressionLivesLost },
            });
#endif
        }

        public void LogHome(LevelResultEventData data, bool setting, bool levelWin, string placement)
        {
#if !DEV_MODE
            var parameters = new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.Setting, setting },
                { AnalyticsEvents.ParamNames.LevelWin, levelWin },
                { AnalyticsEvents.ParamNames.Placement, placement ?? string.Empty },
            };
            AddResultData(parameters, data);
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.Home, parameters);
#endif
        }

        public void LogRestartLevel(LevelResultEventData data, string placement)
        {
#if !DEV_MODE
            var parameters = new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.Placement, placement ?? string.Empty },
            };
            AddResultData(parameters, data);
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.RestartLevel, parameters);
#endif
        }

        public void LogSettingUi(int levelNumber, bool music, bool sound, bool haptics)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.SettingUi, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.Music, music },
                { AnalyticsEvents.ParamNames.Sound, sound },
                { AnalyticsEvents.ParamNames.Haptics, haptics },
            });
#endif
        }

        public void LogDailyReward(int levelNumber, int dailyEventOpenCount, int freeRewardCollectedCount, int adRewardCollectCount)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.DailyReward, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.DailyEventOpenCount, dailyEventOpenCount },
                { AnalyticsEvents.ParamNames.FreeRewardCollectedCount, freeRewardCollectedCount },
                { AnalyticsEvents.ParamNames.AdRewardCollectCount, adRewardCollectCount },
            });
#endif
        }

        public void LogDailyChallenge(int levelNumber, int dailyEventOpenCount, bool currentLevelPlay, int preLevelPlayedCount, int totalLevelPlayedCount)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.DailyChallenge, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.DailyEventOpenCount, dailyEventOpenCount },
                { AnalyticsEvents.ParamNames.CurrentLevelPlay, currentLevelPlay },
                { AnalyticsEvents.ParamNames.PreLevelPlayedCount, preLevelPlayedCount },
                { AnalyticsEvents.ParamNames.TotalLevelPlayedCount, totalLevelPlayedCount },
            });
#endif
        }

        public void LogSpinWheel(int levelNumber, int dailyEventOpenCount, int spinPerDay)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.SpinWheel, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.DailyEventOpenCount, dailyEventOpenCount },
                { AnalyticsEvents.ParamNames.SpinPerDay, spinPerDay },
            });
#endif
        }

        public void LogIapShop(int levelNumber, string shopPackPurchase, int numberOfPackPurchase, bool shopOpen)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.IapShop, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.LevelNo, levelNumber },
                { AnalyticsEvents.ParamNames.ShopPackPurchase, shopPackPurchase ?? string.Empty },
                { AnalyticsEvents.ParamNames.NumberOfPackPurchase, numberOfPackPurchase },
                { AnalyticsEvents.ParamNames.ShopOpen, shopOpen },
            });
#endif
        }

        public void LogNotificationPermission(bool permission)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.NotificationPermission, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.Permission, permission },
            });
#endif
        }

        public void LogNotificationTap(bool notificationGameStart, int notificationAppearCount, string eventName)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(AnalyticsEvents.EventNames.NotificationTap, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamNames.NotificationGameStart, notificationGameStart },
                { AnalyticsEvents.ParamNames.NotificationAppearCount, notificationAppearCount },
                { AnalyticsEvents.ParamNames.NotificationEventName, eventName ?? string.Empty },
            });
#endif
        }

        public void LogEvent(string eventName, IDictionary<string, object> parameters)
        {
#if !DEV_MODE
            FirebaseService?.LogEvent(eventName, parameters);
#endif
        }
    }
}
