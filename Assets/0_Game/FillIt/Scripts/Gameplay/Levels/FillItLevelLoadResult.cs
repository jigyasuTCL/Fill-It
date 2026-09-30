using System;
using System.Collections.Generic;

namespace FillIt.Levels
{
    public sealed class FillItLevelLoadResult
    {
        private FillItLevelLoadResult(FillItLevelData level, IReadOnlyList<string> errors)
        {
            Level = level;
            Errors = errors ?? Array.Empty<string>();
        }

        public FillItLevelData Level { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsLoaded => Level != null && Errors.Count == 0;

        public static FillItLevelLoadResult Success(FillItLevelData level)
        {
            return new FillItLevelLoadResult(level, Array.Empty<string>());
        }

        public static FillItLevelLoadResult Failure(IReadOnlyList<string> errors)
        {
            return new FillItLevelLoadResult(null, errors);
        }

        public static FillItLevelLoadResult Failure(string error)
        {
            return new FillItLevelLoadResult(null, new[] { error });
        }
    }
}
