namespace CatFlap.Boosters
{
    /// <summary>
    /// Reason a hint candidate was chosen. Mirrors the priority order used by
    /// <see cref="CatFlapSolutionHintProvider"/> for stored-solution levels (Mode A).
    /// Mode B (no stored solution) keeps using the legacy string reason keys produced
    /// by <see cref="CatFlapHintService"/> ("OneMissingLine"/"MaxTwoAdjacent"/"LeastEmptyLine"/"Random").
    /// </summary>
    public enum CatFlapHintReason
    {
        WrongTileInViolation,
        WrongTile,
        ForcedAdjacentPair,
        ForcedSandwich,
        ForcedLineMaximum,
        UnlocksForcedCells,
        ClosestToCompletion,
        CorrectNote,
        RuleBased
    }
}
