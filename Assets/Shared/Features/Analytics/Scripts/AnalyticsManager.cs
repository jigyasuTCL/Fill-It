namespace Shared.Analytics
{
    using System.Collections.Generic;

    using UnityEngine;

    /// <summary>
    /// Single shared entry point for logging analytics events. Owns a pluggable list of
    /// IAnalyticsHandler backends (today: just FirebaseAnalyticsHandler) and fans every call out
    /// to all of them, so a second backend can be added later without touching any call site.
    /// </summary>
    public sealed class AnalyticsManager : MonoBehaviour
    {
        private static AnalyticsManager instance;

        public static AnalyticsManager Instance
        {
            get
            {
                if (instance)
                {
                    return instance;
                }

                instance = FindFirstObjectByType<AnalyticsManager>();
                if (!instance)
                {
                    // Unlike AdsManager, this manager has no Inspector-configured fields or
                    // scene-injected references (fakeAdViewer/loadingRoot/etc.) - it's pure logic
                    // plus a handler list, so it doesn't need a placed scene instance or a
                    // Resources prefab to exist first. Self-creating here means analytics events
                    // fire correctly from the very first call, with no Editor wiring step at all.
                    instance = new GameObject(nameof(AnalyticsManager)).AddComponent<AnalyticsManager>();
                }

                return instance;
            }
        }

        private readonly List<IAnalyticsHandler> handlers = new List<IAnalyticsHandler>();
        private bool analyticsInitialized;

        private void Awake()
        {
            if (instance && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            // Handler construction deliberately NOT here - same reasoning as AdsManager.Awake:
            // this runs the instant BootScene's objects are created, before BootLoader's own
            // Start() coroutine (and hence before Core.Initialize() is safe to touch) even begins.
        }

        // Called explicitly by BootLoader once BootScene's own work is done (see AdsManager's
        // InitializeAdvertising for the same pattern/reasoning). Every LogXxx method below also
        // calls this lazily as a fallback, so a missed call site never leaves handlers empty.
        public void InitializeAnalytics()
        {
            if (analyticsInitialized)
            {
                return;
            }

            analyticsInitialized = true;

            handlers.Add(new FirebaseAnalyticsHandler());
            // A second backend, when added, gets appended here.
        }

        public void LogLevelStart(LevelResultEventData data)
        {
            InitializeAnalytics();
            LogToConsole("LevelStart", FormatLevelStartData(data));
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogLevelStart(data);
            }
        }

        // level_start only sends this 8-field subset (see FirebaseAnalyticsHandler.LogLevelStart)
        // - console output mirrors exactly that, not the full 19-field FormatResultData below, so
        // this log line never implies fields level_start doesn't actually send.
        private static string FormatLevelStartData(LevelResultEventData data)
        {
            if (data == null)
            {
                return "resultData=null";
            }

            return $"level={data.LevelNumber}, ftueLevel={data.FtueLevel}, hardLevel={data.HardLevel}, " +
                $"reviveCount={data.ReviveCount}, retryCount={data.RetryCount}, coinCount={data.CoinCount}, " +
                $"hintBoosterCount={data.HintBoosterCount}, dcId={data.DcId}";
        }

        public void LogLevelComplete(LevelResultEventData data)
        {
            InitializeAnalytics();
            LogToConsole("LevelComplete", FormatResultData(data));
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogLevelComplete(data);
            }
        }

        public void LogLevelFail(LevelResultEventData data)
        {
            InitializeAnalytics();
            LogToConsole("LevelFail", FormatResultData(data));
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogLevelFail(data);
            }
        }

        private static string FormatResultData(LevelResultEventData data)
        {
            if (data == null)
            {
                return "resultData=null";
            }

            return $"level={data.LevelNumber}, ftueLevel={data.FtueLevel}, hardLevel={data.HardLevel}, levelTime={data.LevelTime:0.##}, " +
                $"reviveCount={data.ReviveCount}, retryCount={data.RetryCount}, coinCount={data.CoinCount}, " +
                $"hintBoosterUsed={data.HintBoosterUsed}, undoUsed={data.UndoUsed}, totalNotesUsed={data.TotalNotesUsed}, " +
                $"totalNotesApply={data.TotalNotesApply}, notesButtonTapCount={data.NotesButtonTapCount}, " +
                $"livesUsed={data.LivesUsed}, livesLeft={data.LivesLeft}, wrongSolutionCount={data.WrongSolutionCount}, " +
                $"rule1BreakCount={data.Rule1BreakCount}, rule2BreakCount={data.Rule2BreakCount}, dcId={data.DcId}, " +
                $"prePlacedTileTap={data.PrePlacedTileTap}";
        }

        public void LogOutOfLives(int levelNumber, bool isDailyChallenge, int reviveCount, int retryCount)
        {
            InitializeAnalytics();
            LogToConsole("OutOfLives", $"level={levelNumber}, dailyChallenge={isDailyChallenge}, reviveCount={reviveCount}, retryCount={retryCount}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogOutOfLives(levelNumber, isDailyChallenge, reviveCount, retryCount);
            }
        }

        public void LogBoosterPurchased(string boosterType, int levelNumber)
        {
            InitializeAnalytics();
            LogToConsole("BoosterPurchased", $"boosterType={boosterType}, level={levelNumber}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogBoosterPurchased(boosterType, levelNumber);
            }
        }

        public void LogAdResult(string adType, string placement, bool success)
        {
            InitializeAnalytics();
            LogToConsole("AdResult", $"adType={adType}, placement={placement}, success={success}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogAdResult(adType, placement, success);
            }
        }

        public void LogCurrencySource(string currencyType, int amount, string placement, int balanceAfter)
        {
            InitializeAnalytics();
            LogToConsole("CurrencySource", $"currencyType={currencyType}, amount={amount}, placement={placement}, balanceAfter={balanceAfter}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogCurrencySource(currencyType, amount, placement, balanceAfter);
            }
        }

        public void LogCurrencySink(string currencyType, int amount, string outType, int outCount, int balanceAfter)
        {
            InitializeAnalytics();
            LogToConsole("CurrencySink", $"currencyType={currencyType}, amount={amount}, outType={outType}, outCount={outCount}, balanceAfter={balanceAfter}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogCurrencySink(currencyType, amount, outType, outCount, balanceAfter);
            }
        }

        public void LogFtueLevelSkip(int levelNumber, int ftueId)
        {
            InitializeAnalytics();
            LogToConsole("FtueLevelSkip", $"level={levelNumber}, ftueId={ftueId}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogFtueLevelSkip(levelNumber, ftueId);
            }
        }

        public void LogHintPurchasedPopup(int levelNumber, int hintPurchasedCoin, int hintPurchasedAd)
        {
            InitializeAnalytics();
            LogToConsole("HintPurchasedPopup", $"level={levelNumber}, hintPurchasedCoin={hintPurchasedCoin}, hintPurchasedAd={hintPurchasedAd}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogHintPurchasedPopup(levelNumber, hintPurchasedCoin, hintPurchasedAd);
            }
        }

        public void LogLivesLost(int levelNumber, float progressionLivesLost)
        {
            InitializeAnalytics();
            LogToConsole("LivesLost", $"level={levelNumber}, progressionLivesLost={progressionLivesLost:0.##}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogLivesLost(levelNumber, progressionLivesLost);
            }
        }

        public void LogHome(LevelResultEventData data, bool setting, bool levelWin, string placement)
        {
            InitializeAnalytics();
            LogToConsole("Home", $"setting={setting}, levelWin={levelWin}, placement={placement}, {FormatResultData(data)}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogHome(data, setting, levelWin, placement);
            }
        }

        public void LogRestartLevel(LevelResultEventData data, string placement)
        {
            InitializeAnalytics();
            LogToConsole("RestartLevel", $"placement={placement}, {FormatResultData(data)}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogRestartLevel(data, placement);
            }
        }

        public void LogSettingUi(int levelNumber, bool music, bool sound, bool haptics)
        {
            InitializeAnalytics();
            LogToConsole("SettingUi", $"level={levelNumber}, music={music}, sound={sound}, haptics={haptics}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogSettingUi(levelNumber, music, sound, haptics);
            }
        }

        public void LogDailyReward(int levelNumber, int dailyEventOpenCount, int freeRewardCollectedCount, int adRewardCollectCount)
        {
            InitializeAnalytics();
            LogToConsole("DailyReward", $"level={levelNumber}, openCount={dailyEventOpenCount}, freeCollected={freeRewardCollectedCount}, adCollected={adRewardCollectCount}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogDailyReward(levelNumber, dailyEventOpenCount, freeRewardCollectedCount, adRewardCollectCount);
            }
        }

        public void LogDailyChallenge(int levelNumber, int dailyEventOpenCount, bool currentLevelPlay, int preLevelPlayedCount, int totalLevelPlayedCount)
        {
            InitializeAnalytics();
            LogToConsole("DailyChallenge", $"level={levelNumber}, openCount={dailyEventOpenCount}, currentLevelPlay={currentLevelPlay}, preLevelPlayedCount={preLevelPlayedCount}, totalLevelPlayedCount={totalLevelPlayedCount}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogDailyChallenge(levelNumber, dailyEventOpenCount, currentLevelPlay, preLevelPlayedCount, totalLevelPlayedCount);
            }
        }

        public void LogSpinWheel(int levelNumber, int dailyEventOpenCount, int spinPerDay)
        {
            InitializeAnalytics();
            LogToConsole("SpinWheel", $"level={levelNumber}, openCount={dailyEventOpenCount}, spinPerDay={spinPerDay}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogSpinWheel(levelNumber, dailyEventOpenCount, spinPerDay);
            }
        }

        public void LogIapShop(int levelNumber, string shopPackPurchase, int numberOfPackPurchase, bool shopOpen)
        {
            InitializeAnalytics();
            LogToConsole("IapShop", $"level={levelNumber}, shopPackPurchase={shopPackPurchase}, numberOfPackPurchase={numberOfPackPurchase}, shopOpen={shopOpen}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogIapShop(levelNumber, shopPackPurchase, numberOfPackPurchase, shopOpen);
            }
        }

        public void LogNotificationPermission(bool permission)
        {
            InitializeAnalytics();
            LogToConsole("NotificationPermission", $"permission={permission}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogNotificationPermission(permission);
            }
        }

        public void LogNotificationTap(bool notificationGameStart, int notificationAppearCount, string eventName)
        {
            InitializeAnalytics();
            LogToConsole("NotificationTap", $"notificationGameStart={notificationGameStart}, notificationAppearCount={notificationAppearCount}, eventName={eventName}");
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogNotificationTap(notificationGameStart, notificationAppearCount, eventName);
            }
        }

        public void LogEvent(string eventName, IDictionary<string, object> parameters)
        {
            InitializeAnalytics();
            LogToConsole(eventName, FormatParameters(parameters));
            foreach (IAnalyticsHandler handler in handlers)
            {
                handler.LogEvent(eventName, parameters);
            }
        }

        // Single choke point so every LogXxx call above (whatever backend handlers end up doing
        // with it) is also always visible right in the Unity console - lets anyone verify exactly
        // which analytics events actually fired, and with what values, without needing a Firebase
        // DebugView connection or similar. Rich-text colored so it's easy to spot/filter among
        // regular gameplay logs when checking analytics in the Editor (Unity's console renders
        // <color> tags; the tag itself is stripped on device/in Firebase, it's purely cosmetic).
        private const string ConsoleTag = "<color=#00BFFF><b>[Analytics]</b></color>";

        private static void LogToConsole(string eventName, string details)
        {
            // Console printing disabled for now - may re-enable later. Every LogXxx call site
            // still fans out to the real analytics handlers below unaffected; this only silences
            // the Unity console mirror.
            // Debug.Log(string.IsNullOrEmpty(details) ? $"{ConsoleTag} {eventName}" : $"{ConsoleTag} {eventName} ({details})");
        }

        private static string FormatParameters(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            var parts = new List<string>(parameters.Count);
            foreach (KeyValuePair<string, object> pair in parameters)
            {
                parts.Add($"{pair.Key}={pair.Value}");
            }

            return string.Join(", ", parts);
        }
    }
}
