using System.Collections.Generic;
using CatFlap.Levels;

namespace CatFlap.Runtime
{
    public sealed class CatFlapLevelParseResult
    {
        private readonly List<string> errors = new List<string>();

        public CatFlapLevelData Level { get; private set; }
        public IReadOnlyList<string> Errors => errors;
        public bool IsValid => Level != null && errors.Count == 0;

        public static CatFlapLevelParseResult Success(CatFlapLevelData level)
        {
            return new CatFlapLevelParseResult { Level = level };
        }

        public static CatFlapLevelParseResult Failure(IEnumerable<string> messages)
        {
            var result = new CatFlapLevelParseResult();
            if (messages == null)
            {
                return result;
            }

            foreach (string message in messages)
            {
                result.AddError(message);
            }

            return result;
        }

        public void AddError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                errors.Add(message);
            }
        }
    }
}
