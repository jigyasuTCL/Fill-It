using System;
using System.Collections.Generic;
using System.Linq;

namespace Shared.LevelProgression
{
    [Serializable]
    public sealed class LevelProgressionData
    {
        public List<LevelProgressionRecord> Records = new List<LevelProgressionRecord>();

        public LevelProgressionData CloneNormalized()
        {
            var clone = new LevelProgressionData();
            if (Records == null)
            {
                return clone;
            }

            foreach (LevelProgressionRecord record in Records)
            {
                if (record != null)
                {
                    clone.Upsert(record.CloneNormalized());
                }
            }

            return clone;
        }

        public bool TryGet(string scope, string progressionId, string levelId, out LevelProgressionRecord record)
        {
            record = Find(scope, progressionId, levelId);
            return record != null;
        }

        public void Upsert(LevelProgressionRecord record)
        {
            if (record == null)
            {
                return;
            }

            LevelProgressionRecord normalized = record.CloneNormalized();
            LevelProgressionRecord existing = Find(normalized.Scope, normalized.ProgressionId, normalized.LevelId);
            if (existing == null)
            {
                Records.Add(normalized);
                return;
            }

            existing.LevelIndex = normalized.LevelIndex;
            existing.PayloadJson = normalized.PayloadJson;
            existing.LastUpdatedUtc = normalized.LastUpdatedUtc;
        }

        public bool Remove(string scope, string progressionId, string levelId)
        {
            LevelProgressionRecord existing = Find(scope, progressionId, levelId);
            return existing != null && Records.Remove(existing);
        }

        public bool RemoveAllExcept(string scope, string progressionId, string levelIdToKeep)
        {
            if (Records == null)
            {
                return false;
            }

            string normalizedScope = string.IsNullOrWhiteSpace(scope) ? "Normal" : scope;
            string normalizedProgressionId = progressionId ?? string.Empty;
            string normalizedLevelIdToKeep = levelIdToKeep ?? string.Empty;
            int removedCount = Records.RemoveAll(record =>
                record != null &&
                string.Equals(record.Scope, normalizedScope, StringComparison.Ordinal) &&
                string.Equals(record.ProgressionId ?? string.Empty, normalizedProgressionId, StringComparison.Ordinal) &&
                !string.Equals(record.LevelId ?? string.Empty, normalizedLevelIdToKeep, StringComparison.Ordinal));

            return removedCount > 0;
        }

        private LevelProgressionRecord Find(string scope, string progressionId, string levelId)
        {
            string normalizedScope = string.IsNullOrWhiteSpace(scope) ? "Normal" : scope;
            string normalizedProgressionId = progressionId ?? string.Empty;
            string normalizedLevelId = levelId ?? string.Empty;
            return Records?.FirstOrDefault(record =>
                record != null &&
                string.Equals(record.Scope, normalizedScope, StringComparison.Ordinal) &&
                string.Equals(record.ProgressionId ?? string.Empty, normalizedProgressionId, StringComparison.Ordinal) &&
                string.Equals(record.LevelId ?? string.Empty, normalizedLevelId, StringComparison.Ordinal));
        }
    }
}
