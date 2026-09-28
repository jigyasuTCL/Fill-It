using System;
using System.Collections.Generic;
using UnityEngine;
using MangraGames;
using Shared.Analytics;
using Shared.GameFlow;
using Shared.GameConfig;
using Shared.LevelMistakeLimit;
using Shared.PlayerData;

// Thin wiring layer only. All rules live in RewardCycleService,
// all persistence in RewardStateStore. Config comes straight out of
// Shared.GameConfig.GlobalConfig.DailyReward via GlobalConfigRuntimeContext -
// no separate fetch/provider step needed.
public class DailyRewardManager : MonoBehaviour
{
    public event Action OnStateChanged;

    public event Action<RewardItem> OnMainRewardClaimed;

    public event Action<SubRewardItem> OnSubRewardClaimed;

    public event Action<RewardRequireType> OnGateRequired;

    public event Action<RewardRequireType> OnGateDeclined;


    [Header("Optional -- drag any component implementing INotificationScheduler")]
    [Tooltip("Schedules a local notification for when an unclaimed main reward has been ready for a while. Leave empty to skip local notifications.")]
    [SerializeField] private MonoBehaviour notificationSchedulerSource;

    [SerializeField] private FakeAdViewerController adViewer;

    private RewardStateStore stateStore;
    private RewardCycleService cycleService;
    private INotificationScheduler notificationScheduler;

    private RewardSaveState state;
    private DailyRewardConfigData config;

    public bool IsConfigLoaded { get; private set; }

    public DailyRewardConfigData Config => config;

    public RewardItem CurrentMainReward =>
        cycleService?.GetCurrentMainReward();

    public List<SubRewardItem> CurrentSubRewards =>
        cycleService?.GetCurrentSubRewards();
    int currentLevel = 0;
    public void loadAwake(int level)
    {
        currentLevel = level;
        stateStore = new RewardStateStore();

        cycleService = new RewardCycleService(
            new SystemTimeProvider());

        notificationScheduler =
            notificationSchedulerSource as INotificationScheduler;

        if (notificationSchedulerSource != null && notificationScheduler == null)
        {
            Debug.LogWarning(
                $"{nameof(notificationSchedulerSource)} on {name} " +
                $"does not implement INotificationScheduler and will be ignored.");
        }

        // Nothing assigned in the Inspector - default to the shared LocalNotificationController
        // instead of leaving notifications off entirely. The Inspector field is still honored
        // above if someone explicitly wires up a different/mock scheduler.
        if (notificationSchedulerSource == null)
        {
            notificationScheduler = new LocalNotificationSchedulerAdapter();
        }

        state = stateStore.Load();

        config = GetDailyRewardConfig();

        cycleService.Configure(config, state, level);

        IsConfigLoaded = true;

        ScheduleReadyNotification();

        OnStateChanged?.Invoke();
    }

    // Pulls DailyReward straight off GlobalConfigRuntimeContext. Falls back
    // to a fresh (default-valued) DailyRewardConfigData if GlobalConfig
    // isn't ready yet, so this never hard-fails the reward system.
    private static DailyRewardConfigData GetDailyRewardConfig()
    {
        GlobalConfig globalConfig =
            Shared.Boot.GlobalConfigRuntimeContext.HasConfig
                ? Shared.Boot.GlobalConfigRuntimeContext.Config
                : null;

        return globalConfig != null && globalConfig.DailyReward != null
            ? globalConfig.DailyReward
            : new DailyRewardConfigData();
    }

    // ---------------------------------------------------------
    // NOTIFICATION
    // ---------------------------------------------------------

    // (Re)schedules the "reward is ready" local notification for when the main reward's own
    // cooldown (config.cooldownMinutes, 1 hour by default) finishes - i.e. right when the timer
    // that started on claim reaches zero. Call this whenever mainReadyAtUnixSeconds changes -
    // config load and right after a main claim both do this automatically. Gated on
    // IsSystemUnlocked() so a locked player (below config.unlockLevel) never gets nagged about a
    // feature they can't even open yet - once unlocked, this fires immediately (loadAwake calls
    // it unconditionally on every load, i.e. every session/Main Menu instantiation), which also
    // means a notification wiped by LocalNotificationAppStateWatcher.SuppressNotificationsOnResume
    // (every app foreground cancels ALL scheduled notifications, not just due ones) gets
    // re-armed the very next time this loads, rather than staying silently missing until the
    // player happens to claim a reward.
    [ContextMenu("Notification")]
    public void ScheduleReadyNotification()
    {
        if (notificationScheduler == null || !IsConfigLoaded || !IsSystemUnlocked())
            return;

        // The reward is already ready (mainReadyAtUnixSeconds is 0 for a fresh player, or
        // already in the past for anyone whose cooldown elapsed) - there is no real cooldown
        // fire time to schedule, so nudge 2 hours out instead of leaving the player with no
        // reminder at all (or handing the scheduler a fire time that's already behind us).
        DateTime fireAtUtc = cycleService.GetState() == RewardCycleState.CountingDown
            ? cycleService.GetMainReadyAtUtc()
            : DateTime.UtcNow.AddHours(2);

        notificationScheduler.ScheduleNotification(
            fireAtUtc,
            "Your Daily Reward is waiting! \U0001F381",
            "Grab it now and get back to filling the grid.");
    }

    // Call this when the player opens/foregrounds the app so they don't
    // get nagged by a notification for something they can already see
    // and claim in-app.
    public void CancelReadyNotification()
    {
        notificationScheduler?.CancelScheduledNotification();
    }
    // ---------------------------------------------------------
    // MAIN REWARD
    // ---------------------------------------------------------

    public RewardCycleState GetState()
    {
        return cycleService.GetState();
    }

    public TimeSpan TimeUntilReady()
    {
        return cycleService.TimeUntilReady();
    }

    // ---------------------------------------------------------
    // SUB REWARD
    // ---------------------------------------------------------

    public RewardCycleState GetSubState()
    {
        return cycleService.GetSubState();
    }

    public TimeSpan TimeUntilSubReady()
    {
        return cycleService.TimeUntilSubReady();
    }

    // ---------------------------------------------------------
    // SYSTEM
    // ---------------------------------------------------------

    public bool IsSystemUnlocked()
    {
        return IsConfigLoaded &&
               cycleService.IsSystemUnlocked();
    }

    public bool IsComingSoon()
    {
        return IsConfigLoaded &&
               cycleService.IsComingSoon;
    }

    public bool CanClaimMain()
    {
        return IsConfigLoaded &&
               cycleService.CanClaimMain();
    }

    public bool CanClaimSubReward(string subRewardId)
    {
        return IsConfigLoaded &&
               cycleService.CanClaimSubReward(subRewardId);
    }

    public bool IsSubRewardClaimed(string subRewardId)
    {
        return IsConfigLoaded &&
               cycleService.IsSubRewardClaimed(subRewardId);
    }

    // ---------------------------------------------------------
    // MAIN CLAIM
    // ---------------------------------------------------------

    public void TryClaimMain()
    {
        if (!CanClaimMain())
            return;

        RewardItem current =
            cycleService.GetCurrentMainReward();

        if (!cycleService.RequiresGate(current))
        {
            FinishClaimMain(true);
            return;
        }



        if (current.requireType == RewardRequireType.Ads)
        {
            RequestAd(
                () => FinishClaimMain(true),
                () => OnGateDeclined?.Invoke(
                    current.requireType));
        }
        else if (current.requireType == RewardRequireType.IAP)
        {
            RequestPurchase(
                current.id,
                () => FinishClaimMain(true),
                () => OnGateDeclined?.Invoke(
                    current.requireType));
        }
    }

    private void FinishClaimMain(
        bool requirementFulfilled)
    {
        RewardItem reward =
            cycleService.ClaimMainReward(
                requirementFulfilled);

        if (reward == null)
            return;

        stateStore.Save(state);

        GrantReward(
            reward.type,
            reward.amount);

        LogDailyRewardClaimed(reward.requireType);

        OnMainRewardClaimed?.Invoke(reward);

        // Reward just got claimed, so re-arm the notification for the
        // *next* cycle's ready time instead of leaving it pointed at the
        // one that just got collected.
        ScheduleReadyNotification();

        OnStateChanged?.Invoke();
    }

    // ---------------------------------------------------------
    // SUB REWARD CLAIM
    // ---------------------------------------------------------

    public void TryClaimSubReward(
        string subRewardId)
    {
        if (!CanClaimSubReward(subRewardId))
            return;

        SubRewardItem current =
            cycleService.GetCurrentSubRewards()
                .Find(s => s.id == subRewardId);

        if (current == null)
            return;

        // Snapshot which sub-reward set this claim belongs to *before*
        // opening any gate. If watching the ad / completing the IAP takes
        // long enough for the sub cooldown to expire in the meantime, the
        // set will have already rolled over by the time the gate's
        // success callback fires. Passing this snapshot into
        // FinishClaimSubReward lets us still grant the reward the player
        // already earned instead of losing it to the rollover.
        int pendingSetIndex =
            cycleService.CurrentSubCycleIndex;

        if (!cycleService.RequiresGate(current))
        {
            FinishClaimSubReward(
                current,
                pendingSetIndex,
                true);

            return;
        }

        if (current.requireType == RewardRequireType.Ads)
        {
            RequestAd(
                () => FinishClaimSubReward(
                    current,
                    pendingSetIndex,
                    true),

                () => OnGateDeclined?.Invoke(
                    current.requireType));
        }
        else if (current.requireType == RewardRequireType.IAP)
        {
            RequestPurchase(
                current.id,

                () => FinishClaimSubReward(
                    current,
                    pendingSetIndex,
                    true),

                () => OnGateDeclined?.Invoke(
                    current.requireType));
        }
    }

    private void FinishClaimSubReward(
        SubRewardItem pendingItem,
        int pendingSetIndex,
        bool requirementFulfilled)
    {
        if (!requirementFulfilled)
            return;

        // Race-safe: honors the claim even if the sub-reward set rolled
        // over to the next one while an Ads/IAP gate was open. See
        // RewardCycleService.ClaimPendingSubReward for the full reasoning.
        SubRewardItem sub =
            cycleService.ClaimPendingSubReward(
                pendingItem,
                pendingSetIndex);

        if (sub == null)
            return;

        // IMPORTANT:
        // The first sub-reward claim starts the cooldown inside
        // RewardCycleService. Every claim after that keeps the
        // original timer unchanged.
        stateStore.Save(state);

        GrantReward(
            sub.type,
            sub.amount);

        LogDailyRewardClaimed(pendingItem.requireType);

        OnSubRewardClaimed?.Invoke(sub);

        OnStateChanged?.Invoke();
    }

    // Shared by FinishClaimMain/FinishClaimSubReward - increments whichever of
    // PlayerAnalyticsData's 2 collect counters matches this reward's own gate, then reports both
    // (plus the open count) via the single daily_reward analytics event.
    private void LogDailyRewardClaimed(RewardRequireType requireType)
    {
        PlayerAnalyticsData analytics = PlayerData.Instance.Analytics;
        if (requireType == RewardRequireType.Ads)
        {
            analytics.AdRewardCollectCount++;
        }
        else
        {
            analytics.FreeRewardCollectedCount++;
        }

        AnalyticsManager.Instance.LogDailyReward(PlayerData.Instance.CurrentLevelIndex + 1, analytics.DailyRewardOpenCount, analytics.FreeRewardCollectedCount, analytics.AdRewardCollectCount);
    }

    private void GrantReward(
        string type,
        int amount)
    {
        // Route into your existing currency/inventory system.
    }
    public void RequestAd(Action onSuccess, Action onFail)
    {
        AdsManager.Instance.ShowRewarded(AdsManager.RewardedPlacement.DailyReward, success =>
        {
            if (success)
            {
                onSuccess?.Invoke();
            }
            else
            {
                onFail?.Invoke();
            }
        });
    }

    public void RequestPurchase(string itemId, Action onSuccess, Action onFail)
    {
        // FakeAdViewerController only simulates the ad flow (Watch/Skip/No
        // Internet) - there's no fake IAP dialog here yet, so IAP-gated
        // rewards still need a real IRewardGateHandler wired up separately.
        if (adViewer == null)
        {
            onFail?.Invoke();
            return;
        }

        adViewer.Show(onSuccess, onFail);
    }
}