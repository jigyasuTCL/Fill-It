using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DailyChallenges
{
    public sealed partial class DailyChallengeController
    {
        #region Persistence and data generation

        private static string GetDateKey(int year, int month, int day)
        {
            return new DateTime(year, month, day).ToString("yyyy-MM-dd");
        }

        private static string GetDateKey(DailyChallengeData data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            return GetDateKey(data.Year, data.Month, data.Day);
        }

        private DailyChallengeJsonConfig LoadOrCreateJsonConfig(int year)
        {
            var savedJson = LoadConfigJsonFromPlayerData(year);
            if (!string.IsNullOrEmpty(savedJson))
            {
                try
                {
                    var config = JsonUtility.FromJson<DailyChallengeJsonConfig>(savedJson);
                    if (config != null && config.monthlyStates != null && config.monthlyStates.Length == 12)
                    {
                        return config;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to parse daily challenge save for year {year}: {e.Message}");
                }
            }

            // No save yet for this year - fall back to a bundled seed under Resources, if one was
            // ever shipped/exported (see DailyChallengeController.ExportJsonConfig). This is
            // read-only shipped content, not a runtime save file.
            var resourceName = GetJsonResourceName(year);
            var seedJson = Resources.Load<TextAsset>(resourceName)?.text;
            if (!string.IsNullOrEmpty(seedJson))
            {
                try
                {
                    var seedConfig = JsonUtility.FromJson<DailyChallengeJsonConfig>(seedJson);
                    if (seedConfig != null && seedConfig.monthlyStates != null && seedConfig.monthlyStates.Length == 12)
                    {
                        jsonConfig = seedConfig;
                        SaveJsonConfig();
                        return seedConfig;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to parse daily challenge seed '{resourceName}': {e.Message}");
                }
            }

            var defaultConfig = CreateDefaultJsonConfig(year);
            jsonConfig = defaultConfig;
            SaveJsonConfig();
            return defaultConfig;
        }

        // All Daily Challenge state (puzzle assignments, per-day/per-sublevel win/lose, ad-watch
        // tracking, attempt-in-progress) lives inside one PlayerData.Variables entry per year -
        // there is no Daily Challenge-specific save file or PlayerPrefs key anywhere.
        private static string GetPlayerDataKey(int year)
        {
            return $"DailyChallenge.Config.{year}";
        }

        private static string LoadConfigJsonFromPlayerData(int year)
        {
            var data = Shared.PlayerData.PlayerData.Instance;
            return data.TryGetString(GetPlayerDataKey(year), out string json) ? json : null;
        }

        private static void SaveConfigJsonToPlayerData(int year, string json)
        {
            var data = Shared.PlayerData.PlayerData.Instance;
            data.SetString(GetPlayerDataKey(year), json);

            string fileName = string.IsNullOrWhiteSpace(Shared.PlayerData.PlayerDataRuntimeContext.FileName)
                ? Shared.PlayerData.PlayerDataDefaults.FileName
                : Shared.PlayerData.PlayerDataRuntimeContext.FileName;
            new Shared.PlayerData.PlayerDataSaveSystem().Save(fileName, data);
        }

        private DailyChallengeJsonConfig CreateDefaultJsonConfig(int year)
        {
            var monthlyStates = new DailyChallengeMonthState[12];
            for (var monthIndex = 1; monthIndex <= 12; monthIndex++)
            {
                monthlyStates[monthIndex - 1] = new DailyChallengeMonthState
                {
                    levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, monthIndex)),
                };
            }

            return new DailyChallengeJsonConfig
            {
                year = year,
                monthlyStates = monthlyStates,
                nextPuzzleIndex = 1,
            };
        }

        private DailyChallengeLevelData[] CreateDefaultLevelDataArray(int length)
        {
            var levelData = new DailyChallengeLevelData[length];
            for (var index = 0; index < length; index++)
            {
                levelData[index] = new DailyChallengeLevelData
                {
                    puzzleIndex = -1,
                    levelStates = Array.Empty<int>()
                };
            }

            return levelData;
        }

        private DailyChallengeMonthState[] CreateDefaultMonthlyStates(int year)
        {
            var monthlyStates = new DailyChallengeMonthState[12];
            for (var monthIndex = 1; monthIndex <= 12; monthIndex++)
            {
                monthlyStates[monthIndex - 1] = new DailyChallengeMonthState
                {
                    levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, monthIndex)),
                };
            }

            return monthlyStates;
        }

        private void SaveJsonConfig()
        {
            if (jsonConfig == null)
            {
                return;
            }

            SaveConfigJsonToPlayerData(jsonConfig.year, JsonUtility.ToJson(jsonConfig));
        }

        private string GetJsonResourceName(int year)
        {
            return $"daily_challenge_config_{year}";
        }

        private IReadOnlyList<DailyChallengeData> CreateDailyChallengeData(int year, int month)
        {
            var today = DailyChallengeClock.Now.Date;
            var totalDays = DateTime.DaysInMonth(year, month);
            var results = new List<DailyChallengeData>(totalDays);

            if (jsonConfig == null)
            {
                jsonConfig = CreateDefaultJsonConfig(year);
            }

            if (jsonConfig.monthlyStates == null || jsonConfig.monthlyStates.Length != 12)
            {
                jsonConfig.monthlyStates = CreateDefaultMonthlyStates(year);
            }

            var monthState = jsonConfig.monthlyStates[month - 1];
            if (monthState == null || monthState.levelData == null || monthState.levelData.Length != totalDays)
            {
                monthState = new DailyChallengeMonthState
                {
                    levelData = CreateDefaultLevelDataArray(totalDays),
                };

                jsonConfig.monthlyStates[month - 1] = monthState;
                SaveJsonConfig();
            }

            for (var day = 1; day <= totalDays; day++)
            {
                var entryDate = new DateTime(year, month, day);
                var unlocked = entryDate <= today;

                int puzzleIndex = -1;
                int[] loadedLevelStates = null;

                if (monthState.levelData != null && day - 1 < monthState.levelData.Length)
                {
                    var levelData = monthState.levelData[day - 1];
                    if (levelData != null)
                    {
                        puzzleIndex = levelData.puzzleIndex;
                        if (levelData.levelStates != null && levelData.levelStates.Length > 0)
                        {
                            loadedLevelStates = levelData.levelStates;
                        }
                    }
                }

                if (puzzleIndex < 0)
                {
                    puzzleIndex = -1;
                    loadedLevelStates = new[] { (int)DailyChallengePlayState.NotPlayed };
                }

                var data = new DailyChallengeData(
                    year,
                    month,
                    day,
                    puzzleIndex,
                    loadedLevelStates ?? new[] { (int)DailyChallengePlayState.NotPlayed });

                data.IsUnlocked = unlocked;
                data.SetNamingFormat(
                    dailyChallengeManager != null ? dailyChallengeManager.PuzzleNamePrefix : "Puzzle",
                    dailyChallengeManager != null ? dailyChallengeManager.PuzzleNameMiddle : "_");

                results.Add(data);
            }

            return results;
        }

        private void SaveStateToJson(int day, DailyChallengePlayState state)
        {
            if (jsonConfig == null || jsonConfig.monthlyStates == null || day < 1 || month < 1 || month > 12)
            {
                return;
            }

            if (jsonConfig.monthlyStates.Length != 12)
            {
                jsonConfig.monthlyStates = CreateDefaultMonthlyStates(year);
            }

            var monthState = jsonConfig.monthlyStates[month - 1];
            if (monthState == null)
            {
                monthState = new DailyChallengeMonthState
                {
                    levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, month))
                };
                jsonConfig.monthlyStates[month - 1] = monthState;
            }

            if (monthState.levelData == null || monthState.levelData.Length != DateTime.DaysInMonth(year, month))
            {
                monthState.levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, month));
            }

            if (day > monthState.levelData.Length)
            {
                return;
            }

            var levelData = monthState.levelData[day - 1];
            if (levelData == null)
            {
                levelData = new DailyChallengeLevelData();
                monthState.levelData[day - 1] = levelData;
            }

            if (levelData.levelStates == null || levelData.levelStates.Length == 0)
            {
                levelData.levelStates = new[] { (int)DailyChallengePlayState.NotPlayed };
            }

            for (int i = 0; i < levelData.levelStates.Length; i++)
            {
                levelData.levelStates[i] = (int)state;
            }

            monthState.levelData[day - 1] = levelData;
            jsonConfig.monthlyStates[month - 1] = monthState;
            SaveJsonConfig();
        }

        private void SaveLevelDataToJson(int day, DailyChallengeData data)
        {
            if (data == null || jsonConfig == null || jsonConfig.monthlyStates == null || day < 1 || month < 1 || month > 12)
            {
                return;
            }

            if (jsonConfig.monthlyStates.Length != 12)
            {
                jsonConfig.monthlyStates = CreateDefaultMonthlyStates(year);
            }

            var monthState = jsonConfig.monthlyStates[month - 1];
            if (monthState == null)
            {
                monthState = new DailyChallengeMonthState
                {
                    levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, month))
                };
                jsonConfig.monthlyStates[month - 1] = monthState;
            }

            if (monthState.levelData == null || monthState.levelData.Length != DateTime.DaysInMonth(year, month))
            {
                monthState.levelData = CreateDefaultLevelDataArray(DateTime.DaysInMonth(year, month));
            }

            if (day > monthState.levelData.Length)
            {
                return;
            }

            var saveData = new DailyChallengeLevelData
            {
                puzzleIndex = data.IsPuzzleAssigned ? data.PuzzleIndex : -1,
                levelStates = data.LevelStates.Select(s => (int)s).ToArray()
            };

            monthState.levelData[day - 1] = saveData;
            jsonConfig.monthlyStates[month - 1] = monthState;
            SaveJsonConfig();
        }

        #endregion
    }
}
