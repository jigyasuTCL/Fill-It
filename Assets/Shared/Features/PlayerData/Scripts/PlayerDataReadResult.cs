using System.Collections.Generic;
using System.Linq;

namespace Shared.PlayerData
{
    public sealed class PlayerDataReadResult
    {
        private PlayerDataReadResult(PlayerData data, IEnumerable<string> errors)
        {
            Data = data;
            Errors = (errors ?? Enumerable.Empty<string>()).ToArray();
        }

        public PlayerData Data { get; }
        public string[] Errors { get; }
        public bool IsLoaded => Data != null && Errors.Length == 0;

        public static PlayerDataReadResult Loaded(PlayerData data)
        {
            return new PlayerDataReadResult(data, null);
        }

        public static PlayerDataReadResult Failed(IEnumerable<string> errors)
        {
            return new PlayerDataReadResult(null, errors);
        }
    }
}
