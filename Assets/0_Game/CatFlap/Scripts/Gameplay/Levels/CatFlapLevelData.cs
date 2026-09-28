using System;

namespace CatFlap.Levels
{
    [Serializable]
    public sealed class CatFlapLevelData
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Difficulty = string.Empty;
        public int Size;
        public int Lives;
        public int? LevelMistakeLimit;
        public CatFlapLevelRow[] Solution = Array.Empty<CatFlapLevelRow>();
        public CatFlapLevelRow[] Clues = Array.Empty<CatFlapLevelRow>();
    }
}
