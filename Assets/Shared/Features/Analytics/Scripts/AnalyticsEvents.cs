namespace Shared.Analytics
{
    public static class AnalyticsEvents
    {
        public static class EventNames
        {
            public const string LevelStart = "level_start";
            public const string LevelComplete = "level_complete";
            public const string LevelFail = "level_fail";
            public const string OutOfLives = "out_of_lives";
            public const string BoosterPurchased = "booster_purchased";
            public const string AdResult = "ad_result";
            public const string CurrencySource = "currency_source";
            public const string CurrencySink = "currency_sink";
            public const string FtueLevelSkip = "ftue_level_skip";
            public const string HintPurchasedPopup = "hint_puchased_popup";
            public const string LivesLost = "lives_lost";
            public const string Home = "home";
            public const string RestartLevel = "restart level";
            public const string SettingUi = "setting_ui";
            public const string DailyReward = "daily_reward";
            public const string DailyChallenge = "daily_challenge";
            public const string SpinWheel = "spin_wheel";
            public const string IapShop = "iap_shop";
            public const string NotificationPermission = "notification_permission";
            public const string NotificationTap = "notification_tap";
        }

        public static class ParamNames
        {
            // LevelNumber/IsDailyChallenge: OutOfLives only now - every other event that used to
            // carry these switched to the shared LevelResultEventData snapshot (LevelNo/DcId).
            public const string LevelNumber = "level_number";
            public const string IsDailyChallenge = "is_daily_challenge";
            public const string BoosterType = "booster_type";
            public const string AdType = "ad_type";
            public const string Placement = "placement";
            public const string Success = "success";
            // LevelFail/OutOfLives: how many times the player has already revived/retried THIS
            // level number since first entering it - see CatFlapGameplayManager.levelReviveCount/
            // levelRetryCount.
            public const string ReviveCount = "revive_count";
            public const string RetryCount = "retry_count";
            public const string CurrencyType = "currency_type";
            public const string Amount = "amount";
            public const string BalanceAfter = "balance_after";
            // CurrencySink only: what the currency was spent on (e.g. "booster_hint",
            // "out_of_lives_refill", "booster_undo") and how many of that thing it bought.
            public const string OutType = "out_type";
            public const string OutCount = "out_count";

            // "level_no" (not "level_number") - matches the tracking spec's own literal column
            // name for every event added after the original 8. Kept distinct from LevelNumber
            // above so the already-shipped events/dashboards for level_start/level_complete/
            // level_fail/out_of_lives aren't renamed retroactively.
            public const string LevelNo = "level_no";
            // FtueLevelSkip only - 1-based, which step of THIS level's walkthrough the player was
            // on when they skipped - see CatFlapFtueDriver.CurrentStepNumber.
            public const string FtueId = "ftue_id";
            // HintPurchasedPopup only - exactly one of these is non-zero per event: coins spent
            // via the coin-purchase path, or 1 via the watch-ad path.
            public const string HintPurchasedCoin = "hint_purchased_coin";
            public const string HintPurchasedAd = "hint_purchased_ad";
            // LivesLost only - percent (0-100) of this level's total mistake allowance spent so
            // far, including the mistake that just triggered this event.
            public const string ProgressionLivesLost = "progression_lives_lost";
            // Home only - which surface the Home click came from; both false covers any other
            // "return to menu" path (e.g. Daily Challenge back navigation). Placement (see
            // HomePlacementValues) is the more specific version of the same thing.
            public const string Setting = "setting";
            public const string LevelWin = "level_win";

            // Shared by LevelStart/LevelComplete/LevelFail/Home/RestartLevel - see
            // LevelResultEventData for what populates each.
            public const string LevelTime = "level_time";
            public const string CoinCount = "coin_count";
            // LevelStart only - see LevelResultEventData.HintBoosterCount.
            public const string HintBoosterCount = "hint_booster_count";
            public const string HintBoosterUsed = "hint_booster_used";
            public const string UndoUsed = "undo_used";
            public const string TotalNotesUsed = "total_notes_used";
            public const string TotalNotesApply = "total_notes_apply";
            public const string NotesButtonTapCount = "notes_button_tap_count";
            public const string LivesUsed = "lives_used";
            public const string LivesLeft = "lives_left";
            public const string WrongSolutionCount = "wrong_solution_count";
            public const string Rule1BreakCount = "rule_1_break_count";
            public const string Rule2BreakCount = "rule_2_break_count";
            public const string DcId = "dc_id";
            public const string PrePlacedTileTap = "pre_palced_tile_tap";
            public const string HardLevel = "hard_level";
            public const string FtueLevel = "ftue_level";

            // SettingUi only.
            public const string Music = "music";
            public const string Sound = "sound";
            public const string Haptics = "haptics";

            // DailyReward only - see PlayerAnalyticsData.
            public const string DailyEventOpenCount = "daily_event_open_count";
            public const string FreeRewardCollectedCount = "free_reward_collected_count";
            public const string AdRewardCollectCount = "ad_reward_collect_count";

            // DailyChallenge only.
            public const string CurrentLevelPlay = "current_level_play";
            public const string PreLevelPlayedCount = "pre_level_played_count";
            public const string TotalLevelPlayedCount = "total_level_played_count";

            // SpinWheel only.
            public const string SpinPerDay = "spin_per_day";

            // IapShop only.
            public const string ShopPackPurchase = "shop_pack_purchase";
            public const string NumberOfPackPurchase = "number_of_pack_purchase";
            public const string ShopOpen = "shop_open";

            // NotificationPermission only.
            public const string Permission = "permission";

            // NotificationTap only - which feature's reminder notification this launch came from
            // (see NotificationCategoryValues); empty string when notificationGameStart is false.
            public const string NotificationGameStart = "notification_game_start";
            public const string NotificationAppearCount = "notification_appear_count";
            public const string NotificationEventName = "event_name";
        }

        // RestartLevel's Placement value - "dc" (Daily Challenge attempt) or "ingame" (a regular
        // level).
        public static class RestartPlacementValues
        {
            public const string DailyChallenge = "dc";
            public const string InGame = "ingame";
        }

        // Home's Placement value - which surface the Home click actually came from.
        public static class HomePlacementValues
        {
            // Out of Lives popup's "Home"/Give Up button.
            public const string OutOfLives = "outoflives";
            // In-level Settings popup's Home button.
            public const string InGame = "ingame";
            // Level Win popup's Home button.
            public const string Win = "win";
        }

        // NotificationTap's NotificationEventName value - matches the 3 local-notification ids
        // actually scheduled (see LocalNotificationSchedulerAdapter/DailyChallengeController/
        // SpinWheelManager's own ScheduleNotification calls).
        public static class NotificationCategoryValues
        {
            public const string DailyChallenge = "dc";
            public const string DailyReward = "dr";
            public const string SpinWheel = "sw";
        }

        public static class AdTypeValues
        {
            public const string Rewarded = "rewarded";
            public const string Interstitial = "interstitial";
        }

        public static class CurrencyTypeValues
        {
            // Single in-game currency today (see BoosterWallet.CurrentCoins/PlayerData.TotalCoins) -
            // "coins" in some UI copy/StoreConfig item names is the exact same currency, not a
            // second one.
            public const string Coins = "coins";
        }
    }
}
