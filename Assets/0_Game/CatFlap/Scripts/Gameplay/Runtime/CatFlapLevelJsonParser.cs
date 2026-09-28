using System;
using System.Collections.Generic;
using CatFlap.Levels;
using Newtonsoft.Json.Linq;

namespace CatFlap.Runtime
{
    public sealed class CatFlapLevelJsonParser
    {
        public CatFlapLevelParseResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return CatFlapLevelParseResult.Failure(new[] { "Level JSON is empty." });
            }

            JObject levelObject;
            try
            {
                levelObject = JObject.Parse(json);
            }
            catch (Exception exception) when (exception is Newtonsoft.Json.JsonException || exception is ArgumentException)
            {
                return CatFlapLevelParseResult.Failure(new[] { $"Level JSON could not be parsed: {exception.Message}" });
            }

            if (levelObject == null)
            {
                return CatFlapLevelParseResult.Failure(new[] { "Level JSON did not contain a level object." });
            }

            var errors = new List<string>();
            CatFlapLevelData level = Convert(levelObject, errors);
            if (errors.Count > 0)
            {
                return CatFlapLevelParseResult.Failure(errors);
            }

            return CatFlapLevelParseResult.Success(level);
        }

        private static CatFlapLevelData Convert(JObject levelObject, List<string> errors)
        {
            int size = ReadInt(levelObject, "size", 0);
            int index = ReadInt(levelObject, "index", 0);
            string id = ReadString(levelObject, "id");
            if (string.IsNullOrWhiteSpace(id) && index > 0)
            {
                id = $"level_{index:000}";
            }

            int? levelMistakeLimit = ReadOptionalInt(levelObject, "levelMistakeLimit") ??
                                     ReadOptionalInt(levelObject, "mistakeLimit") ??
                                     ReadOptionalInt(levelObject, "lives");

            return new CatFlapLevelData
            {
                Id = id,
                Name = ReadString(levelObject, "name"),
                Difficulty = ReadString(levelObject, "difficulty"),
                Size = size,
                Lives = levelMistakeLimit ?? 0,
                LevelMistakeLimit = levelMistakeLimit,
                Solution = ConvertGrid("solution", levelObject["solution"], size, false, errors),
                Clues = ConvertGrid("clues", levelObject["clues"], size, true, errors)
            };
        }

        private static CatFlapLevelRow[] ConvertGrid(string label, JToken grid, int size, bool allowEmpty, List<string> errors)
        {
            if (grid == null)
            {
                errors.Add($"Level {label} grid is missing.");
                return Array.Empty<CatFlapLevelRow>();
            }

            if (grid.Type != JTokenType.Array)
            {
                errors.Add($"Level {label} grid must be an array.");
                return Array.Empty<CatFlapLevelRow>();
            }

            var gridArray = (JArray)grid;
            var rows = new CatFlapLevelRow[gridArray.Count];
            for (int row = 0; row < gridArray.Count; row++)
            {
                JToken rowToken = gridArray[row];
                JToken cellsToken = rowToken.Type == JTokenType.Object ? rowToken["cells"] : rowToken;
                if (cellsToken == null || cellsToken.Type != JTokenType.Array)
                {
                    errors.Add($"Level {label} row {row} is missing cells.");
                    rows[row] = new CatFlapLevelRow(Array.Empty<int?>());
                    continue;
                }

                var cellsArray = (JArray)cellsToken;
                var cells = new int?[cellsArray.Count];
                for (int column = 0; column < cellsArray.Count; column++)
                {
                    JToken cellToken = cellsArray[column];
                    if (allowEmpty && (cellToken.Type == JTokenType.Null || ReadCellInt(cellToken) == -1))
                    {
                        cells[column] = null;
                    }
                    else
                    {
                        int? value = ReadCellInt(cellToken);
                        if (value == 0 || value == 1)
                        {
                            cells[column] = value;
                        }
                        else
                        {
                            errors.Add($"Level {label} cell ({row},{column}) must be {(allowEmpty ? "null, -1, 0, or 1" : "0 or 1")}.");
                        }
                    }
                }

                rows[row] = new CatFlapLevelRow(cells);
            }

            if (size > 0 && gridArray.Count != size)
            {
                errors.Add($"Level {label} grid row count must match size {size}.");
            }

            return rows;
        }

        private static string ReadString(JObject levelObject, string propertyName)
        {
            JToken token = levelObject[propertyName];
            return token != null && token.Type != JTokenType.Null ? token.Value<string>() ?? string.Empty : string.Empty;
        }

        private static int ReadInt(JObject levelObject, string propertyName, int fallback)
        {
            JToken token = levelObject[propertyName];
            int? value = ReadCellInt(token);
            return value ?? fallback;
        }

        private static int? ReadOptionalInt(JObject levelObject, string propertyName)
        {
            return ReadCellInt(levelObject[propertyName]);
        }

        private static int? ReadCellInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            return null;
        }
    }
}
