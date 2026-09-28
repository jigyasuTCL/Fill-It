using System;
using System.Collections.Generic;
using Shared.GameConfig;

public enum RewardCycleState
{
    Ready,
    CountingDown
}

// Pure logic, no MonoBehaviour, no PlayerPrefs, no config loading.
//
// Main rewards and sub rewards are two independent cycles.
// The sub-reward cooldown starts when the FIRST sub reward is claimed.
//
// Once started, that cooldown is never restarted by subsequent
// sub-reward claims.
public class RewardCycleService
{
    private readonly ITimeProvider timeProvider;

    private DailyRewardConfigData config;
    private RewardSaveState state;
    int currentLevel = 0;
    public RewardCycleService(
        ITimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public void Configure(
        DailyRewardConfigData config,
        RewardSaveState state,int level)
    {
        currentLevel = level;
        this.config = config;
        this.state = state;

        // Guard against an out-of-range index if a list shrank
        // since the last save.
        state.mainCycleIndex =
            config.mainRewards.Count > 0
                ? ((state.mainCycleIndex %
                    config.mainRewards.Count) +
                    config.mainRewards.Count) %
                    config.mainRewards.Count
                : 0;

        state.subCycleIndex =
            config.subRewardSets.Count > 0
                ? ((state.subCycleIndex %
                    config.subRewardSets.Count) +
                    config.subRewardSets.Count) %
                    config.subRewardSets.Count
                : 0;
    }

    private long NowUnix =>
        ((DateTimeOffset)timeProvider.UtcNow)
        .ToUnixTimeSeconds();

    // =========================================================
    // SYSTEM GATING
    // =========================================================

    public bool IsSystemUnlocked()
    {
        if (config == null)
            return false;

        if (!config.enableRestriction)
            return false;

        if (config.unlockLevel == -1)
            return false;

        return currentLevel >=
               config.unlockLevel;
    }

    public bool IsComingSoon =>
        config != null && config.unlockLevel == -1;

    public bool RequiresGate(
        BaseRewardItem item)
    {
        return item != null &&
               item.requireType !=
               RewardRequireType.Free;
    }

    // =========================================================
    // MAIN REWARD
    // =========================================================

    public RewardCycleState GetState()
    {
        return NowUnix >=
               state.mainReadyAtUnixSeconds
            ? RewardCycleState.Ready
            : RewardCycleState.CountingDown;
    }

    public TimeSpan TimeUntilReady()
    {
        long remaining =
            state.mainReadyAtUnixSeconds -
            NowUnix;

        return remaining > 0
            ? TimeSpan.FromSeconds(remaining)
            : TimeSpan.Zero;
    }

    // When an unclaimed main reward should nudge the player via a local
    // notification (see INotificationScheduler). Fires config.NotificationMinutes
    // after the reward becomes ready, so we don't nag the instant it's ready.
    // Callers should reschedule for this time whenever mainReadyAtUnixSeconds
    // changes (config load, claim) and cancel it once the reward is claimed.
    public DateTime GetMainNotificationTimeUtc()
    {
        long fireAtUnix =
            state.mainReadyAtUnixSeconds +
            (long)(config.NotificationMinutes * 60f);

        return DateTimeOffset
            .FromUnixTimeSeconds(fireAtUnix)
            .UtcDateTime;
    }

    // When the main reward's own cooldown (config.cooldownMinutes, started by ClaimMainReward)
    // finishes - i.e. the moment it becomes claimable again. Used to schedule the "reward is
    // ready" local notification for exactly that timer, as opposed to GetMainNotificationTimeUtc's
    // separate ready+NotificationMinutes nag-delay above.
    public DateTime GetMainReadyAtUtc()
    {
        return DateTimeOffset
            .FromUnixTimeSeconds(state.mainReadyAtUnixSeconds)
            .UtcDateTime;
    }

    public RewardItem GetCurrentMainReward()
    {
        if (config == null ||
            config.mainRewards.Count == 0)
        {
            return null;
        }

        return config.mainRewards[
            state.mainCycleIndex %
            config.mainRewards.Count];
    }

    public bool CanClaimMain()
    {
        if (config == null)
            return false;

        if (GetState() != RewardCycleState.Ready)
            return false;

        if (!IsSystemUnlocked())
            return false;

        return GetCurrentMainReward() != null;
    }

    public RewardItem ClaimMainReward(
        bool requirementFulfilled = true)
    {
        if (!CanClaimMain())
            return null;

        RewardItem current =
            GetCurrentMainReward();

        if (RequiresGate(current) &&
            !requirementFulfilled)
        {
            return null;
        }

        state.mainReadyAtUnixSeconds =
            NowUnix +
            (long)(config.cooldownMinutes * 60f);

        state.mainCycleIndex =
            config.mainRewards.Count > 0
                ? (state.mainCycleIndex + 1) %
                  config.mainRewards.Count
                : 0;

        return current;
    }

    // =========================================================
    // SUB REWARDS
    // =========================================================

    // Snapshot this before starting an Ads/IAP gate for a sub reward, and
    // pass it back into ClaimPendingSubReward once the gate resolves - see
    // that method for why.
    public int CurrentSubCycleIndex => state.subCycleIndex;

    public RewardCycleState GetSubState()
    {
        EnsureSubCycleAdvanced();

        return state.subReadyAtUnixSeconds == 0
            ? RewardCycleState.Ready
            : RewardCycleState.CountingDown;
    }

    public TimeSpan TimeUntilSubReady()
    {
        EnsureSubCycleAdvanced();

        long remaining =
            state.subReadyAtUnixSeconds -
            NowUnix;

        return remaining > 0
            ? TimeSpan.FromSeconds(remaining)
            : TimeSpan.Zero;
    }

    public List<SubRewardItem> GetCurrentSubRewards()
    {
        EnsureSubCycleAdvanced();

        if (config == null ||
            config.subRewardSets.Count == 0)
        {
            return new List<SubRewardItem>();
        }

        return config.subRewardSets[
            state.subCycleIndex %
            config.subRewardSets.Count
        ].subRewards;
    }

    public bool CanClaimSubReward(
        string subRewardId)
    {
        if (config == null)
            return false;

        if (!IsSystemUnlocked())
            return false;

        EnsureSubCycleAdvanced();

        // Already claimed
        if (state.claimedSubRewardIds.Contains(
            subRewardId))
        {
            return false;
        }

        List<SubRewardItem> current =
            GetCurrentSubRewards();

        int index =
            current.FindIndex(
                s => s.id == subRewardId);

        if (index < 0)
            return false;

        // Maintain sequential unlocking.
        //
        // Example:
        // Reward 0 -> claimable
        // Reward 1 -> locked until Reward 0 is claimed
        // Reward 2 -> locked until Reward 1 is claimed
        for (int i = 0; i < index; i++)
        {
            if (!state.claimedSubRewardIds.Contains(
                current[i].id))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsSubRewardClaimed(
        string subRewardId)
    {
        return state != null &&
               state.claimedSubRewardIds.Contains(
                   subRewardId);
    }

    // =========================================================
    // SUB REWARD CLAIM
    // =========================================================

    public SubRewardItem ClaimSubReward(
        string subRewardId,
        bool requirementFulfilled = true)
    {
        if (!CanClaimSubReward(subRewardId))
            return null;

        List<SubRewardItem> currentSet =
            GetCurrentSubRewards();

        SubRewardItem sub =
            currentSet.Find(
                s => s.id == subRewardId);

        if (sub == null)
            return null;

        if (RequiresGate(sub) &&
            !requirementFulfilled)
        {
            return null;
        }

        // -----------------------------------------------------
        // Claim the reward
        // -----------------------------------------------------

        state.claimedSubRewardIds.Add(
            subRewardId);

        // -----------------------------------------------------
        // IMPORTANT:
        //
        // Start the sub-reward cooldown ONLY on the FIRST
        // sub-reward claim.
        //
        // Subsequent claims do NOT modify this timestamp.
        // -----------------------------------------------------

        if (state.subReadyAtUnixSeconds == 0)
        {
            state.subReadyAtUnixSeconds =
                NowUnix +
                (long)(
                    config.subCooldownMinutes *
                    60f);
        }

        return sub;
    }

    // =========================================================
    // SUB REWARD CLAIM (POST-GATE, RACE-SAFE)
    // =========================================================

    // Use this instead of ClaimSubReward() to finish a claim that went
    // through an Ads/IAP gate.
    //
    // Why: CanClaimSubReward() / ClaimSubReward() both call
    // EnsureSubCycleAdvanced() first, which can roll the sub-reward set
    // over mid-flight. E.g. 1 minute is left on the sub cooldown, the
    // player taps claim, and the rewarded ad takes 2 minutes to watch -
    // by the time the ad's success callback fires, the set has already
    // advanced and claimedSubRewardIds has been cleared for the new set.
    // Re-validating against that new state would silently drop a reward
    // the player already earned.
    //
    // Instead, the caller snapshots CurrentSubCycleIndex and the
    // SubRewardItem *before* opening the gate, and passes both back here.
    // If nothing rolled over, this behaves exactly like ClaimSubReward().
    // If it did roll over, the earned reward is still granted - the new
    // set is left untouched and simply becomes visible on the next
    // refresh, i.e. "collect the click reward, then refresh".
    public SubRewardItem ClaimPendingSubReward(
        SubRewardItem pendingItem,
        int pendingSetIndex)
    {
        if (pendingItem == null ||
            config == null ||
            state == null)
        {
            return null;
        }

        // Set rolled over while the gate was open. The player already
        // fulfilled the requirement for an item from the *previous* set,
        // so grant it directly without touching the new set's
        // claimed-ids or timers.
        if (pendingSetIndex != state.subCycleIndex)
        {
            return pendingItem;
        }

        // Still the same set - normal path, same rules as ClaimSubReward,
        // just without re-running EnsureSubCycleAdvanced() again.
        if (state.claimedSubRewardIds.Contains(pendingItem.id))
            return null;

        state.claimedSubRewardIds.Add(pendingItem.id);

        if (state.subReadyAtUnixSeconds == 0)
        {
            state.subReadyAtUnixSeconds =
                NowUnix +
                (long)(config.subCooldownMinutes * 60f);
        }

        return pendingItem;
    }

    // =========================================================
    // SUB CYCLE ADVANCE
    // =========================================================

    private void EnsureSubCycleAdvanced()
    {
        if (config == null ||
            config.subRewardSets.Count == 0)
        {
            return;
        }

        // No sub-reward has been claimed yet,
        // so there is no cooldown running.
        if (state.subReadyAtUnixSeconds == 0)
            return;

        // Cooldown still running.
        if (NowUnix <
            state.subReadyAtUnixSeconds)
        {
            return;
        }

        // -----------------------------------------------------
        // Cooldown expired.
        //
        // Move to the next sub-reward set.
        // -----------------------------------------------------

        state.subCycleIndex =
            (state.subCycleIndex + 1) %
            config.subRewardSets.Count;

        // Start the new set fresh.
        state.claimedSubRewardIds.Clear();

        // No cooldown until the FIRST reward of this new
        // set is claimed.
        state.subReadyAtUnixSeconds = 0;
    }
}