using System;

namespace Shared.PlayerData
{
    /// <summary>
    /// Persisted counters that exist purely to feed analytics events with no other natural home in
    /// PlayerData's own core progression fields - see AnalyticsEvents (daily_reward, daily_challenge,
    /// spin_wheel, iap_shop). Saved/loaded as part of PlayerData itself (see PlayerData.Analytics),
    /// through the exact same PlayerDataSaveSystem path as every other field there.
    /// </summary>
    [Serializable]
    public sealed class PlayerAnalyticsData
    {
        public int DailyRewardOpenCount;
        public int FreeRewardCollectedCount;
        public int AdRewardCollectCount;

        public int DailyChallengeOpenCount;
        public int DailyChallengeTotalLevelsPlayed;

        public int SpinWheelOpenCount;
        // Resets to 0 whenever LastSpinDateUtc is no longer today (UTC) - see
        // SpinWheelManager/CatFlapGameplayManager's own call site for the reset check.
        public int SpinsToday;
        public string LastSpinDateUtc = string.Empty;

        public int IapPackPurchaseCount;

        // Lifetime count of app launches actually triggered by tapping a delivered local
        // notification - see LocalNotificationAppStateWatcher's launch-source check. Only ever
        // incremented, never reset.
        public int NotificationAppearCount;
        // Set once the platform permission prompt has been resolved (granted OR denied) - see
        // LocalNotificationController.Init/LocalNotificationAppStateWatcher's iOS authorization
        // request. Left false until that first resolves.
        public bool NotificationPermissionGranted;
        public bool NotificationPermissionResolved;

        public PlayerAnalyticsData Clone()
        {
            return new PlayerAnalyticsData
            {
                DailyRewardOpenCount = DailyRewardOpenCount,
                FreeRewardCollectedCount = FreeRewardCollectedCount,
                AdRewardCollectCount = AdRewardCollectCount,
                DailyChallengeOpenCount = DailyChallengeOpenCount,
                DailyChallengeTotalLevelsPlayed = DailyChallengeTotalLevelsPlayed,
                SpinWheelOpenCount = SpinWheelOpenCount,
                SpinsToday = SpinsToday,
                LastSpinDateUtc = LastSpinDateUtc ?? string.Empty,
                IapPackPurchaseCount = IapPackPurchaseCount,
                NotificationAppearCount = NotificationAppearCount,
                NotificationPermissionGranted = NotificationPermissionGranted,
                NotificationPermissionResolved = NotificationPermissionResolved,
            };
        }
    }
}
