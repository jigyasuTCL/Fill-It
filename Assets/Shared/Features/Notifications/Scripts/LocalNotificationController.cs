using System;
using System.Text;

using Shared.Analytics;
using Shared.PlayerData;
using UnityEngine;

#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine.Android;
#elif UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace Shared.Notifications
{
    // Single shared entry point for scheduling/canceling device-level local notifications on
    // both Android and iOS. Every notification is keyed by a caller-chosen string id; the same
    // id maps to a stable platform id via ToAndroidId (Android) / used directly as the
    // iOSNotification.Identifier (iOS), so re-scheduling an id always cancels the exact right
    // prior notification with no separate id-mapping bookkeeping/persistence required - the
    // native notification centers already persist scheduled notifications across app restarts.
    public sealed class LocalNotificationController
    {
        private const string AndroidChannelId = "catflap_reminders";

        private static LocalNotificationController instance;

        public static LocalNotificationController Instance => instance ??= new LocalNotificationController();

        private bool initialized;
        // The permission result can resolve (synchronously, for the "already granted" Android
        // check) as early as BootLoader's very first frame, well before PlayerData/AnalyticsManager
        // are ready for real event logging - held here until MarkReadyAndFlushPendingPermission is
        // called (once BootLoader has actually entered gameplay), rather than logged immediately.
        private bool? pendingPermissionGranted;
        private static bool analyticsReadyToLog;

        private LocalNotificationController()
        {
        }

        // Called once by BootLoader, after PlayerData is initialized AND the GamePlay scene has
        // loaded - flushes whatever permission result already resolved before that point, and lets
        // any later-resolving one (the player took a while to respond to the OS prompt) log
        // immediately instead of queuing.
        public static void MarkReadyAndFlushPendingPermission()
        {
            analyticsReadyToLog = true;
            if (Instance.pendingPermissionGranted.HasValue)
            {
                bool granted = Instance.pendingPermissionGranted.Value;
                Instance.pendingPermissionGranted = null;
                Instance.LogNotificationPermissionResultNow(granted);
            }
        }

        public void Init()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

#if UNITY_ANDROID
            AndroidNotificationCenter.Initialize();
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
            {
                Id = AndroidChannelId,
                Name = "Reminders",
                Importance = Importance.Default,
                Description = "Game reminder notifications",
            });

            // POST_NOTIFICATIONS only exists as a runtime (dangerous) permission from Android 13
            // (API 33) onward - requesting it pre-33 would either no-op or show a popup for a
            // permission the OS doesn't gate at all there. global::Vibration.AndroidVersion is the
            // same SystemInfo.operatingSystem-based API level check already used elsewhere in this
            // codebase (see Vibration.cs) for this exact kind of version-gated Android behavior.
            const string postNotificationsPermission = "android.permission.POST_NOTIFICATIONS";
            if (global::Vibration.AndroidVersion >= 33)
            {
                if (Permission.HasUserAuthorizedPermission(postNotificationsPermission))
                {
                    LogNotificationPermissionResult(true);
                }
                else
                {
                    var callbacks = new PermissionCallbacks();
                    callbacks.PermissionGranted += _ => LogNotificationPermissionResult(true);
                    callbacks.PermissionDenied += _ => LogNotificationPermissionResult(false);
                    callbacks.PermissionDeniedAndDontAskAgain += _ => LogNotificationPermissionResult(false);
                    Permission.RequestUserPermission(postNotificationsPermission, callbacks);
                }
            }
            else
            {
                // Not gated on this OS version at all - counts as granted (notifications will show).
                LogNotificationPermissionResult(true);
            }
#elif UNITY_IOS
            // iOS authorization is requested separately by LocalNotificationAppStateWatcher - it
            // needs polling an AuthorizationRequest to completion (a coroutine), which a plain
            // (non-MonoBehaviour) class can't do on its own. Scheduling doesn't require waiting on
            // it: an unauthorized app can still schedule notifications, iOS just won't display them.
#endif
        }

        // Shared by both platforms' permission-result paths (Android's PermissionCallbacks,
        // iOS's AuthorizationRequest polled in LocalNotificationAppStateWatcher). Queues the
        // result instead of logging immediately if it resolved before
        // MarkReadyAndFlushPendingPermission was called (see there) - otherwise logs right away,
        // since by then any later resolution is already happening well after gameplay entry.
        public void LogNotificationPermissionResult(bool granted)
        {
            if (!analyticsReadyToLog)
            {
                pendingPermissionGranted = granted;
                return;
            }

            LogNotificationPermissionResultNow(granted);
        }

        // Logs only once per app lifetime (first time the OS-level result is actually known),
        // since re-checking an already-granted permission on every later Init() call would
        // otherwise re-fire it.
        private void LogNotificationPermissionResultNow(bool granted)
        {
            PlayerAnalyticsData analytics = Shared.PlayerData.PlayerData.Instance.Analytics;
            if (analytics.NotificationPermissionResolved)
            {
                return;
            }

            analytics.NotificationPermissionResolved = true;
            analytics.NotificationPermissionGranted = granted;
            AnalyticsManager.Instance.LogNotificationPermission(granted);
        }

        /// <summary>
        /// Schedules a notification under <paramref name="id"/>, replacing any notification
        /// previously scheduled under the same id.
        /// </summary>
        public void ScheduleNotification(string id, DateTime fireAtLocalTime, string title, string body)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            Init();

            // Replace semantics - always clear this id's previous notification first.
            CancelNotification(id);

            if (fireAtLocalTime <= DateTime.Now)
            {
                return;
            }

#if UNITY_ANDROID
            AndroidNotification notification = new AndroidNotification
            {
                Title = title,
                Text = body,
                FireTime = fireAtLocalTime,
                // Android's own notification id (below) is an irreversible int hash of id - Group
                // is the one field the OS actually persists verbatim, so this is what lets a cold
                // tap-launch (see LocalNotificationAppStateWatcher.ReportNotificationTapIfLaunchedFromOne)
                // recover which original string id was tapped, purely from AndroidNotificationCenter.
                // GetLastNotificationIntent() - no in-memory id map needed (which wouldn't survive
                // the app having been fully killed anyway).
                Group = id,
            };
            AndroidNotificationCenter.SendNotificationWithExplicitID(notification, AndroidChannelId, ToAndroidId(id));
#elif UNITY_IOS
            iOSNotification notification = new iOSNotification
            {
                Identifier = id,
                Title = title,
                Body = body,

                // Requirement: a notification must not display while the player already has the
                // app open. iOS has no automatic suppression - ShowInForeground = false is what
                // stops it from presenting a banner/sound if the fire time lands while active.
                ShowInForeground = false,
                Trigger = new iOSNotificationTimeIntervalTrigger
                {
                    TimeInterval = fireAtLocalTime - DateTime.Now,
                    Repeats = false,
                },
            };
            iOSNotificationCenter.ScheduleNotification(notification);
#endif
        }

        /// <summary>
        /// Cancels the notification scheduled under <paramref name="id"/>, including removing it
        /// from the tray/notification center if it already fired.
        /// </summary>
        public void CancelNotification(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

#if UNITY_ANDROID
            int androidId = ToAndroidId(id);
            AndroidNotificationCenter.CancelNotification(androidId);
            AndroidNotificationCenter.CancelDisplayedNotification(androidId);
#elif UNITY_IOS
            iOSNotificationCenter.RemoveScheduledNotification(id);
            iOSNotificationCenter.RemoveDeliveredNotification(id);
#endif
        }

        /// <summary>
        /// Cancels every notification this controller has scheduled or delivered.
        /// </summary>
        public void CancelAllNotifications()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
#endif
        }

        // Called by LocalNotificationAppStateWatcher whenever the app becomes active. iOS's
        // ShowInForeground = false already stops a due notification from presenting while the app
        // is open, but Android has no such flag - a due notification always reaches the tray
        // there. Clearing everything the instant the app becomes active is what covers the exact
        // race where a notification's fire time lands right as the player opens the app.
        public void SuppressNotificationsOnResume()
        {
            CancelAllNotifications();
        }

        private static int ToAndroidId(string id)
        {
            // A stable (non-randomized) hash - string.GetHashCode() is not guaranteed stable
            // across processes/runtimes, which would break "same id -> same Android notification
            // id" across app restarts.
            unchecked
            {
                const int fnvOffsetBasis = unchecked((int)2166136261);
                const int fnvPrime = 16777619;

                int hash = fnvOffsetBasis;
                byte[] bytes = Encoding.UTF8.GetBytes(id);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= fnvPrime;
                }

                return hash & int.MaxValue;
            }
        }
    }
}
