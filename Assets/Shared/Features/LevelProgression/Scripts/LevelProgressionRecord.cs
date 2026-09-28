using System;

namespace Shared.LevelProgression
{
    [Serializable]
    public sealed class LevelProgressionRecord
    {
        public string Scope = "Normal";
        public string ProgressionId = string.Empty;
        public string LevelId = string.Empty;
        public int LevelIndex;
        public string PayloadJson = string.Empty;
        public string CreatedUtc = DateTime.UtcNow.ToString("O");
        public string LastUpdatedUtc = DateTime.UtcNow.ToString("O");

        public LevelProgressionRecord CloneNormalized()
        {
            return new LevelProgressionRecord
            {
                Scope = string.IsNullOrWhiteSpace(Scope) ? "Normal" : Scope,
                ProgressionId = ProgressionId ?? string.Empty,
                LevelId = LevelId ?? string.Empty,
                LevelIndex = Math.Max(0, LevelIndex),
                PayloadJson = PayloadJson ?? string.Empty,
                CreatedUtc = string.IsNullOrWhiteSpace(CreatedUtc) ? DateTime.UtcNow.ToString("O") : CreatedUtc,
                LastUpdatedUtc = string.IsNullOrWhiteSpace(LastUpdatedUtc) ? DateTime.UtcNow.ToString("O") : LastUpdatedUtc
            };
        }

        public void Touch()
        {
            LastUpdatedUtc = DateTime.UtcNow.ToString("O");
        }
    }
}
