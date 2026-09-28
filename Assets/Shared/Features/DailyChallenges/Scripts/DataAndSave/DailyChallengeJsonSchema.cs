using System;
using System.Collections.Generic;
using UnityEngine;

namespace DailyChallenges
{
    [Serializable]
    public sealed class DailyChallengeLevelData
    {
        public int[] levelStates;
        public int puzzleIndex;
    }

    [Serializable]
    public sealed class DailyChallengeMonthState
    {
        // public int month;
        // public int[] states;
        public DailyChallengeLevelData[] levelData;
    }

    [Serializable]
    public sealed class DailyChallengeJsonConfig
    {
        public int year;
        public DailyChallengeMonthState[] monthlyStates;
        public int nextPuzzleIndex;

        // Crash/force-quit recovery: set true right before entering Daily Challenge gameplay and
        // cleared on any legitimate exit (win, lose, or a handled quit). If this is still true the
        // next time the config loads, the previous attempt never reported a result - see
        // DailyChallengeController.RecoverInterruptedAttempt.
        public bool attemptInProgress;
        public int attemptYear;
        public int attemptMonth;
        public int attemptDay;
        public int attemptLevelIndex = -1;

        // Ad-watch tracking (moved off PlayerPrefs - see DailyChallengeController.AdSystem.cs).
        // lastAdResetDate is "yyyy-MM-dd"; adWatchedDates is a comma-joined list of the same
        // format, mirroring the string layout the old PlayerPrefs values used.
        public string lastAdResetDate = string.Empty;
        public string adWatchedDates = string.Empty;

        // Reward-tier progress tracking (see DailyChallengeConfigData.RewardTiers and
        // DailyChallengeController.Rewards.cs). Both are comma-joined lists, mirroring
        // adWatchedDates' string layout so no schema migration is needed later.
        //
        // completedChallengeDates: "yyyy-MM-dd" for every day fully won at least once, across all
        // time - GetTotalCompletedDays() is just this list's distinct count;
        // GetCompletedDaysThisMonth() filters it to the real current month.
        //
        // claimedRewardTierDays: the DaysRequired of every reward tier already granted THIS
        // month - the reward track is a monthly milestone strip, so this resets whenever
        // rewardTierMonthKey no longer matches the real current month (see
        // DailyChallengeController.EnsureRewardTierMonthCurrent).
        //
        // rewardTierMonthKey: "yyyy-MM" of the month claimedRewardTierDays currently belongs to.
        public string completedChallengeDates = string.Empty;
        public string claimedRewardTierDays = string.Empty;
        public string rewardTierMonthKey = string.Empty;
    }

}


