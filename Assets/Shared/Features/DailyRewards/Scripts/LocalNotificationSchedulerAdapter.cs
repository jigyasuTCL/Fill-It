using System;

using Shared.Notifications;

// Default INotificationScheduler implementation backed by the shared LocalNotificationController.
// DailyRewardManager falls back to this when notificationSchedulerSource is left unassigned in the
// Inspector, so daily reward notifications work out of the box instead of silently doing nothing -
// the Inspector field remains available for swapping in a different/mock scheduler if ever needed.
public sealed class LocalNotificationSchedulerAdapter : INotificationScheduler
{
    private const string NotificationId = "daily_reward_ready";

    public void ScheduleNotification(DateTime fireAtUtc, string title, string body)
    {
        LocalNotificationController.Instance.ScheduleNotification(NotificationId, fireAtUtc.ToLocalTime(), title, body);
    }

    public void CancelScheduledNotification()
    {
        LocalNotificationController.Instance.CancelNotification(NotificationId);
    }
}
