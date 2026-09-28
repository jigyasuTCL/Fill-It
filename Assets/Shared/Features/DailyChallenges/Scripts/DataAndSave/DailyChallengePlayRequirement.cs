namespace DailyChallenges
{
    /// <summary>
    /// What a caller (Main Menu widget, calendar popup) must do before Daily Challenge gameplay
    /// can start for a given day. Daily Challenge decides this; callers only react to it - see
    /// DailyChallengeController.GetPlayRequirement.
    /// </summary>
    public enum DailyChallengePlayRequirement
    {
        Free,
        RewardedAd,
        Completed,
        Unavailable
    }
}
