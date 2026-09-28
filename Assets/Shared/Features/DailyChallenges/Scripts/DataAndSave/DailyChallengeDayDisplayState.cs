namespace DailyChallenges
{
    /// <summary>
    /// Presentation-facing state for a single calendar day, resolved by
    /// DailyChallengeController.GetDayDisplayState. Kept separate from
    /// DailyChallengePlayState (the underlying win/lose/not-played data) so calendar cell UI can
    /// map each value to a color/icon without owning any progression logic itself.
    /// </summary>
    public enum DailyChallengeDayDisplayState
    {
        Upcoming,   // Future date, not reachable yet.
        Locked,     // Past/missed date, not yet unlocked (requires a rewarded ad to play).
        Current,    // Today's challenge, playable, nothing lost yet.
        Available,  // A missed day already ad-unlocked, or today after a watched retry ad.
        Failed,     // At least one sub-level lost, still selectable/retryable.
        Completed   // All sub-levels won - no longer selectable.
    }
}
