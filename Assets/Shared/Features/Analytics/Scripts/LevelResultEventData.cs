namespace Shared.Analytics
{
    /// <summary>
    /// The tracking spec's shared per-level snapshot. level_complete/level_fail/home/restart level
    /// all send the full set of fields below; level_start sends only a small subset of these
    /// (level_no/ftue_level/hard_level/revive_count/retry_count/coin_count/hint_booster_count/
    /// dc_id - see FirebaseAnalyticsHandler.LogLevelStart) since most of the others are always 0 at
    /// that point anyway. A plain data holder, not passed as positional arguments - that many
    /// same-typed (int/bool) arguments in a row is a real foot-gun (silently swapped values), so
    /// callers build one of these with named field assignments instead. See
    /// CatFlapGameplayManager.BuildLevelResultEventData for how every field here is actually
    /// sourced.
    /// </summary>
    public sealed class LevelResultEventData
    {
        public int LevelNumber;
        public bool FtueLevel;
        public bool HardLevel;
        // Elapsed session time in seconds - 0 at level_start, the real elapsed time by the time
        // level_complete/level_fail/home/restart fire. Not sent by level_start.
        public float LevelTime;
        public int ReviveCount;
        public int RetryCount;
        public int CoinCount;
        // LevelStart only - hint booster charges currently OWNED (a wallet snapshot), distinct
        // from HintBoosterUsed below (an attempt-scoped spend counter, always 0 at level start).
        public int HintBoosterCount;
        public int HintBoosterUsed;
        public int UndoUsed;
        public int TotalNotesUsed;
        public int TotalNotesApply;
        public int NotesButtonTapCount;
        public int LivesUsed;
        public int LivesLeft;
        public int WrongSolutionCount;
        public int Rule1BreakCount;
        public int Rule2BreakCount;
        // -1 for a non-Daily-Challenge attempt - see CatFlapGameplayManager.
        // dailyChallengeSubLevelIndex (1-based here, matching CurrentLevelNumber's own convention).
        public int DcId;
        public int PrePlacedTileTap;
    }
}
