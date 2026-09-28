using System;

// Optional. If DailyRewardManager has one of these wired up, it will use
// it to schedule a local (device-level) notification for when the main
// reward has been ready for a while and still unclaimed - so players
// still get nudged even with the app closed. If left null, the system
// just relies on the in-app NotificationImage badge instead.
public interface INotificationScheduler
{
    void ScheduleNotification(DateTime fireAtUtc, string title, string body);
    void CancelScheduledNotification();
}

// Sketch of a real implementation using Unity's Mobile Notifications
// package - fill in with your actual notification setup:
//
// public class MobileNotificationScheduler : MonoBehaviour, INotificationScheduler
// {
//     private int scheduledId = -1;
//
//     public void ScheduleNotification(DateTime fireAtUtc, string title, string body)
//     {
//         CancelScheduledNotification();
//
//         if (fireAtUtc <= DateTime.UtcNow)
//             return;
//
// #if UNITY_ANDROID
//         var notification = new AndroidNotification
//         {
//             Title = title,
//             Text = body,
//             FireTime = fireAtUtc.ToLocalTime()
//         };
//         scheduledId = AndroidNotificationCenter.SendNotification(notification, "daily_reward_channel");
// #elif UNITY_IOS
//         var timeTrigger = new iOSNotificationTimeIntervalTrigger
//         {
//             TimeInterval = fireAtUtc - DateTime.UtcNow,
//             Repeats = false
//         };
//         var notification = new iOSNotification
//         {
//             Title = title,
//             Body = body,
//             Trigger = timeTrigger
//         };
//         iOSNotificationCenter.ScheduleNotification(notification);
// #endif
//     }
//
//     public void CancelScheduledNotification()
//     {
// #if UNITY_ANDROID
//         if (scheduledId >= 0)
//             AndroidNotificationCenter.CancelNotification(scheduledId);
// #endif
//         scheduledId = -1;
//     }
// }