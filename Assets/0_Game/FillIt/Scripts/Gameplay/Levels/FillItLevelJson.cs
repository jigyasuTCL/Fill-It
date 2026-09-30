using System;
using System.Text;
using UnityEngine;

namespace FillIt.Levels
{
    /// <summary>
    /// Level JSON format. Grids are digit strings, row-major; any non-digit character (spaces,
    /// "/", newlines) is ignored, so hand-authored levels can be written one row at a time:
    /// <code>
    /// {
    ///   "id": "level_001",
    ///   "name": "Level 1",
    ///   "difficulty": "Easy",
    ///   "size": 4,
    ///   "levelMistakeLimit": 0,
    ///   "timeLimitSeconds": 60,
    ///   "clues":    "2034/4430/0204/4000",
    ///   "solution": "2234/4434/4234/4214"
    /// }
    /// </code>
    /// A clue digit of 0 is an empty cell. levelMistakeLimit 0 (or omitted) uses the
    /// GameRulesConfig default. timeLimitSeconds is the Timer-mode countdown for this level;
    /// 0 (or omitted) falls back to GameRulesConfig's per-empty-cell estimate.
    /// </summary>
    public static class FillItLevelJson
    {
        [Serializable]
        private sealed class LevelDto
        {
            public string id;
            public string name;
            public string difficulty;
            public int size;
            public int levelMistakeLimit;
            public int timeLimitSeconds;
            public string clues;
            public string solution;
        }

        public static bool TryParse(string json, out FillItLevelData level, out string error)
        {
            level = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Level JSON is empty.";
                return false;
            }

            LevelDto dto;
            try
            {
                dto = JsonUtility.FromJson<LevelDto>(json);
            }
            catch (Exception exception)
            {
                error = $"Level JSON could not be parsed: {exception.Message}";
                return false;
            }

            if (dto == null)
            {
                error = "Level JSON could not be parsed.";
                return false;
            }

            level = new FillItLevelData
            {
                Id = dto.id ?? string.Empty,
                Name = dto.name ?? string.Empty,
                Difficulty = dto.difficulty ?? string.Empty,
                Size = dto.size,
                LevelMistakeLimit = dto.levelMistakeLimit > 0 ? dto.levelMistakeLimit : (int?)null,
                TimeLimitSeconds = dto.timeLimitSeconds > 0 ? dto.timeLimitSeconds : (int?)null,
                Clues = ParseDigits(dto.clues),
                Solution = ParseDigits(dto.solution)
            };

            return true;
        }

        public static string ToJson(FillItLevelData level)
        {
            var dto = new LevelDto
            {
                id = level.Id,
                name = level.Name,
                difficulty = level.Difficulty,
                size = level.Size,
                levelMistakeLimit = level.LevelMistakeLimit ?? 0,
                timeLimitSeconds = level.TimeLimitSeconds ?? 0,
                clues = FormatDigits(level.Clues, level.Size),
                solution = FormatDigits(level.Solution, level.Size)
            };

            return JsonUtility.ToJson(dto, true);
        }

        public static int[] ParseDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Array.Empty<int>();
            }

            int count = 0;
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsDigit(value[index]))
                {
                    count++;
                }
            }

            var digits = new int[count];
            int write = 0;
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsDigit(value[index]))
                {
                    digits[write++] = value[index] - '0';
                }
            }

            return digits;
        }

        /// <summary>Digits grouped one row at a time, separated by "/", e.g. "2034/4430/0204/4000".</summary>
        public static string FormatDigits(int[] digits, int size)
        {
            if (digits == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(digits.Length + size);
            for (int index = 0; index < digits.Length; index++)
            {
                if (size > 0 && index > 0 && index % size == 0)
                {
                    builder.Append('/');
                }

                builder.Append((char)('0' + Mathf.Clamp(digits[index], 0, 9)));
            }

            return builder.ToString();
        }
    }
}
