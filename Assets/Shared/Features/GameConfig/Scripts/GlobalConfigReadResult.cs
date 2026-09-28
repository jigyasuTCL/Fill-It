using System.Collections.Generic;
using System.Linq;

namespace Shared.GameConfig
{
    public sealed class GlobalConfigReadResult
    {
        private GlobalConfigReadResult(GlobalConfig config, IEnumerable<string> errors, IEnumerable<string> warnings)
        {
            Config = config;
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
            Warnings = (warnings ?? Enumerable.Empty<string>()).ToArray();
        }

        public GlobalConfig Config { get; }
        public string[] Errors { get; }
        public string[] Warnings { get; }
        public bool IsLoaded => Config != null && Errors.Length == 0;

        public static GlobalConfigReadResult Loaded(GlobalConfig config, IEnumerable<string> warnings = null)
        {
            return new GlobalConfigReadResult(config, null, warnings);
        }

        public static GlobalConfigReadResult Failed(IEnumerable<string> errors)
        {
            return new GlobalConfigReadResult(null, errors, null);
        }
    }
}
