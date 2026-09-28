namespace Shared.Analytics
{
    using System.Collections.Generic;

    public interface IAnalyticsHandler
    {
        // level_start/level_complete/level_fail now all share the exact same 20-field snapshot -
        // see LevelResultEventData and CatFlapGameplayManager.BuildLevelResultEventData for how
        // every field is sourced. LogOutOfLives (below) is untouched by this spec and keeps its
        // own separate params.
        void LogLevelStart(LevelResultEventData data);

        void LogLevelComplete(LevelResultEventData data);

        void LogLevelFail(LevelResultEventData data);

        void LogOutOfLives(int levelNumber, bool isDailyChallenge, int reviveCount, int retryCount);

        void LogBoosterPurchased(string boosterType, int levelNumber);

        void LogAdResult(string adType, string placement, bool success);

        // placement: where the gain happened (e.g. "level_win", "watch_ad_double", "iap_coin_pack",
        // "daily_reward"). balanceAfter: the currency's total right after this gain was applied.
        void LogCurrencySource(string currencyType, int amount, string placement, int balanceAfter);

        // outType: what the currency was spent on (e.g. "booster_hint", "out_of_lives_refill").
        // outCount: how many of that thing this spend bought (usually 1). balanceAfter: the
        // currency's total right after this spend was applied.
        void LogCurrencySink(string currencyType, int amount, string outType, int outCount, int balanceAfter);

        void LogFtueLevelSkip(int levelNumber, int ftueId);

        // hintPurchasedCoin/hintPurchasedAd: exactly one of these is non-zero per call - see
        // AnalyticsEvents.ParamNames.HintPurchasedCoin/HintPurchasedAd.
        void LogHintPurchasedPopup(int levelNumber, int hintPurchasedCoin, int hintPurchasedAd);

        // progressionLivesLost: percent (0-100) of this level's mistake allowance spent so far.
        void LogLivesLost(int levelNumber, float progressionLivesLost);

        // setting/levelWin: which surface the Home click came from - see
        // AnalyticsEvents.ParamNames.Setting/LevelWin. placement:
        // AnalyticsEvents.HomePlacementValues (the more specific version of the same thing). Same
        // shared snapshot as level_start/complete/fail, plus these extra flags.
        void LogHome(LevelResultEventData data, bool setting, bool levelWin, string placement);

        // placement: AnalyticsEvents.RestartPlacementValues.DailyChallenge/InGame.
        void LogRestartLevel(LevelResultEventData data, string placement);

        void LogSettingUi(int levelNumber, bool music, bool sound, bool haptics);

        // Fired on every daily-reward open AND every collect (free or ad) - each call reports the
        // up-to-date totals for all 3, not just whatever just changed. See PlayerAnalyticsData.
        void LogDailyReward(int levelNumber, int dailyEventOpenCount, int freeRewardCollectedCount, int adRewardCollectCount);

        // currentLevelPlay: true only when this call is for actually STARTING a sub-level (not
        // just opening the daily challenge screen). preLevelPlayedCount/totalLevelPlayedCount: how
        // many sub-levels were already played before this one / in total this cycle.
        void LogDailyChallenge(int levelNumber, int dailyEventOpenCount, bool currentLevelPlay, int preLevelPlayedCount, int totalLevelPlayedCount);

        void LogSpinWheel(int levelNumber, int dailyEventOpenCount, int spinPerDay);

        // shopPackPurchase: the purchased pack's id/name - empty on a plain shop-open call.
        // numberOfPackPurchase: running total of packs bought. shopOpen: true only when this call
        // is for the shop being opened (not a purchase).
        void LogIapShop(int levelNumber, string shopPackPurchase, int numberOfPackPurchase, bool shopOpen);

        void LogNotificationPermission(bool permission);

        // notificationGameStart: true only when THIS app launch was actually triggered by tapping
        // a delivered local notification. notificationAppearCount: lifetime count of such
        // launches, including this one if notificationGameStart is true. eventName:
        // AnalyticsEvents.NotificationCategoryValues (dc/dr/sw) identifying which feature's
        // reminder was tapped - empty string when notificationGameStart is false.
        void LogNotificationTap(bool notificationGameStart, int notificationAppearCount, string eventName);

        void LogEvent(string eventName, IDictionary<string, object> parameters);
    }
}
