using System.Collections.Generic;
using CatFlap.Levels;

namespace CatFlap.Runtime
{
    public sealed class CatFlapRuntimeLevelLoadResult
    {
        private readonly List<string> errors = new List<string>();
        private readonly List<string> warnings = new List<string>();

        public CatFlapLevelData Level { get; private set; }
        public IReadOnlyList<string> Errors => errors;
        public IReadOnlyList<string> Warnings => warnings;
        public bool IsLoaded => Level != null && errors.Count == 0;

        public static CatFlapRuntimeLevelLoadResult Loaded(CatFlapLevelData level, IEnumerable<string> warnings)
        {
            var result = new CatFlapRuntimeLevelLoadResult { Level = level };
            result.AddWarnings(warnings);
            return result;
        }

        public static CatFlapRuntimeLevelLoadResult Failed(IEnumerable<string> errors, IEnumerable<string> warnings = null)
        {
            var result = new CatFlapRuntimeLevelLoadResult();
            result.AddErrors(errors);
            result.AddWarnings(warnings);
            return result;
        }

        private void AddErrors(IEnumerable<string> messages)
        {
            if (messages == null)
            {
                return;
            }

            foreach (string message in messages)
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    errors.Add(message);
                }
            }
        }

        private void AddWarnings(IEnumerable<string> messages)
        {
            if (messages == null)
            {
                return;
            }

            foreach (string message in messages)
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    warnings.Add(message);
                }
            }
        }
    }
}
