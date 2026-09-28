namespace DailyChallenges
{
    /// <summary>
    /// What DailyChallengeController.DailyChallengeResultResolved hands to listeners right after
    /// a sub-level's Win/Lost result is recorded - everything a result popup needs (x/3 completed,
    /// whether the whole day is now done) without it having to re-derive any of that itself.
    /// </summary>
    public readonly struct DailyChallengeResultInfo
    {
        public DailyChallengeData Data { get; }
        public DailyChallengePlayState Result { get; }
        public int LevelIndex { get; }
        public int CompletedCount { get; }
        public int TotalCount { get; }

        public bool IsDayComplete => CompletedCount >= TotalCount;

        public DailyChallengeResultInfo(DailyChallengeData data, DailyChallengePlayState result, int levelIndex, int completedCount, int totalCount)
        {
            Data = data;
            Result = result;
            LevelIndex = levelIndex;
            CompletedCount = completedCount;
            TotalCount = totalCount;
        }
    }
}
