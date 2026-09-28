using System.Collections;

using Shared.Analytics;
using Shared.PlayerData;
using UnityEngine;

#if UNITY_ANDROID
using Unity.Notifications.Android;
#elif UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace Shared.Notifications
{
    // Watches app foreground/background transitions and tells LocalNotificationController to
    // clear any local notification whose fire time landed while the app was already open -
    // otherwise the device could display a notification for something the player is already
    // inside. Self-creating like AnalyticsManager - no Inspector fields, no prefab needed.
    public sealed class LocalNotificationAppStateWatcher : MonoBehaviour
    {
        private static LocalNotificationAppStateWatcher instance;

        public static void EnsureExists()
        {
            if (instance)
            {
                return;
            }

            instance = new GameObject(nameof(LocalNotificationAppStateWatcher)).AddComponent<LocalNotificationAppStateWatcher>();
        }

        private void Awake()
        {
            if (instance && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            LocalNotificationController.Instance.Init();
#if UNITY_IOS
            StartCoroutine(RequestIosAuthorization());
#endif
        }

#if UNITY_IOS
        // AuthorizationRequest is async and IDisposable - polled here rather than in
        // LocalNotificationController, since that class is plain C# (no coroutines available).
        private IEnumerator RequestIosAuthorization()
        {
            AuthorizationRequest request = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Sound, false);
            while (!request.IsFinished)
            {
                yield return null;
            }

            LocalNotificationController.Instance.LogNotificationPermissionResult(request.Granted);
            request.Dispose();
        }
#endif

        // Called once by BootLoader, after PlayerData is initialized AND the GamePlay scene has
        // loaded - NOT from Awake (this component is created much earlier, during the Boot scene,
        // before PlayerData.Init has even run - logging from there touched a throwaway default
        // PlayerData instance instead of the real loaded save). The platform launch-source query
        // itself is safe to defer this way: it only ever describes how the app was COLD-STARTED,
        // and stays queryable/unchanged for the rest of the process regardless of when it's read.
        public static void ReportNotificationTapIfLaunchedFromOne()
        {
            bool launchedFromNotification = false;
            string notificationId = null;

#if UNITY_ANDROID
            // Group is the original string id, set at schedule time (see
            // LocalNotificationController.ScheduleNotification) - Android's own int notification
            // id is an irreversible hash of it, so Group is what's actually readable back here.
            // AndroidNotificationIntentData is a reference type (not a struct) in this package
            // version - GetLastNotificationIntent() returns null, not a Nullable<T>, when the app
            // wasn't launched from a notification.
            AndroidNotificationIntentData data = AndroidNotificationCenter.GetLastNotificationIntent();
            launchedFromNotification = data != null;
            notificationId = data?.Notification.Group;
#elif UNITY_IOS
            iOSNotification data = iOSNotificationCenter.GetLastRespondedNotification();
            launchedFromNotification = data != null;
            notificationId = data?.Identifier;
#endif

            PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
            if (launchedFromNotification)
            {
                analytics.NotificationAppearCount++;
            }

            AnalyticsManager.Instance.LogNotificationTap(launchedFromNotification, analytics.NotificationAppearCount, ResolveNotificationCategory(notificationId));
        }

        // Maps a scheduled notification's own string id back to the tracking spec's dc/dr/sw
        // category - matches the ids actually scheduled: LocalNotificationSchedulerAdapter
        // ("daily_reward_ready"), DailyChallengeController.LevelProgression
        // ("daily_challenge_next_day", "daily_challenge_unlock_progress"), SpinWheelManager
        // ("spin_wheel_ready").
        private static string ResolveNotificationCategory(string notificationId)
        {
            switch (notificationId)
            {
                case "daily_challenge_next_day":
                case "daily_challenge_unlock_progress":
                    return Shared.Analytics.AnalyticsEvents.NotificationCategoryValues.DailyChallenge;
                case "daily_reward_ready":
                    return Shared.Analytics.AnalyticsEvents.NotificationCategoryValues.DailyReward;
                case "spin_wheel_ready":
                    return Shared.Analytics.AnalyticsEvents.NotificationCategoryValues.SpinWheel;
                default:
                    return string.Empty;
            }
        }

        // Both callbacks are wired since platforms differ in which one they raise on
        // resume (e.g. Android reliably raises OnApplicationPause(false); some platforms also
        // raise OnApplicationFocus(true)) - CancelAllNotifications() is cheap and idempotent, so
        // a duplicate call from both firing is harmless.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                LocalNotificationController.Instance.SuppressNotificationsOnResume();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                LocalNotificationController.Instance.SuppressNotificationsOnResume();
            }
        }
    }
}
