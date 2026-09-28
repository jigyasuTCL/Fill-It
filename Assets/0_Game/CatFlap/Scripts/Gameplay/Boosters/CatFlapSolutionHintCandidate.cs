using CatFlap.Models;

namespace CatFlap.Boosters
{
    /// <summary>
    /// A single candidate move considered by <see cref="CatFlapSolutionHintProvider"/>.
    /// Adapted from the spec's suggested HintCandidate, using this project's own
    /// <see cref="CatFlapCellPosition"/>/<see cref="CatFlapCellValue"/> types instead of
    /// introducing UnityEngine.Vector2Int into the pure-C# engine layer.
    /// </summary>
    public sealed class CatFlapSolutionHintCandidate
    {
        public CatFlapCellPosition Cell;
        public CatFlapCellValue CorrectValue;
        public CatFlapHintReason Reason;
        public bool IsWrongTile;
        public bool IsInViolation;
        public bool IsForced;
        public bool HasCorrectNote;
        public int ViolationCount;
        public int FollowOnForcedCount;
        public int RowRemaining;
        public int ColumnRemaining;
        public int DistanceFromLastTap = int.MaxValue;
        public int DifficultyBefore;
        public int DifficultyAfter;

        public int DifficultyRemoved => DifficultyBefore - DifficultyAfter;

        public int CompletionScore => System.Math.Min(RowRemaining, ColumnRemaining);
    }
}
