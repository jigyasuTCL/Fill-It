using System;
using Newtonsoft.Json;

namespace Shared.GameConfig
{
    [Serializable]
    public sealed class LoopLevelConfig
    {
        [JsonProperty("easyLevels")]
        public int[] EasyLevels = new int[0];

        [JsonProperty("mediumLevels")]
        public int[] MediumLevels = new int[0];

        [JsonProperty("hardLevels")]
        public int[] HardLevels = new int[0];

        // How EasyLevels/MediumLevels/HardLevels interleave into one loop set - "2e_2m_1h" means
        // 2 easy, then 2 medium, then 1 hard, repeated as many times as the (freshly shuffled)
        // pools allow - see CatFlapLoopLevelSequencer.
        [JsonProperty("pattern")]
        public string Pattern = DefaultPattern;

        public const string DefaultPattern = "2e_2m_1h";

        public LoopLevelConfig CloneNormalized()
        {
            return new LoopLevelConfig
            {
                EasyLevels = NormalizeLevels(EasyLevels),
                MediumLevels = NormalizeLevels(MediumLevels),
                HardLevels = NormalizeLevels(HardLevels),
                Pattern = string.IsNullOrWhiteSpace(Pattern) ? DefaultPattern : Pattern
            };
        }

        private static int[] NormalizeLevels(int[] levels)
        {
            if (levels == null)
            {
                return new int[0];
            }

            var normalized = new int[levels.Length];
            for (int index = 0; index < levels.Length; index++)
            {
                normalized[index] = Math.Max(1, levels[index]);
            }

            return normalized;
        }
    }
}
