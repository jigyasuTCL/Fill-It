using CatFlap.Models;
using CatFlap.Runtime;
using Shared.LevelProgression;
using UnityEngine;

namespace CatFlap.UnityAdapters
{
    public sealed class CatFlapCurrentLevelBoardSaveManager : MonoBehaviour
    {
        [SerializeField] private string boardStateKeyPrefix = "CatFlap.Board.";
        [SerializeField] private string progressionFileName = "level-progression.json";
        [SerializeField] private string progressionScope = "Normal";
        [SerializeField] private string progressionId = "CatFlap";
        [SerializeField] private bool keepProgressionHistory;

        private readonly LevelProgressionSaveSystem saveSystem = new LevelProgressionSaveSystem();

        public void ApplySaveKeyPrefix(string saveKeyPrefix)
        {
            if (!string.IsNullOrWhiteSpace(saveKeyPrefix))
            {
                boardStateKeyPrefix = saveKeyPrefix;
            }
        }

        public void Save(CatFlapRuntimeSession session)
        {
            Save(session, 0);
        }

        public void Save(CatFlapRuntimeSession session, int levelIndex)
        {
            Save(session, levelIndex, progressionId);
        }

        /// <summary>
        /// Same save system/file as the no-override overload, but a caller-supplied
        /// progressionId "row" instead of this instance's serialized default - lets one manager
        /// instance keep two callers' saves (e.g. regular campaign vs. Daily Challenge) from
        /// colliding/overwriting each other via RemoveAllExcept, without needing a second
        /// instance.
        /// </summary>
        public void Save(CatFlapRuntimeSession session, int levelIndex, string progressionIdOverride)
        {
            if (session == null || !session.HasLevel)
            {
                return;
            }

            string resolvedId = ResolveProgressionId(progressionIdOverride);

            BoardSaveData data = BoardSaveData.FromSession(session);
            saveSystem.SaveRecord(progressionFileName, new LevelProgressionRecord
            {
                Scope = progressionScope,
                ProgressionId = resolvedId,
                LevelId = CreateKey(session.Level.Id),
                LevelIndex = Mathf.Max(0, levelIndex),
                PayloadJson = JsonUtility.ToJson(data)
            }, keepProgressionHistory);
        }

        public bool TryRestore(CatFlapRuntimeSession session)
        {
            return TryRestore(session, progressionId);
        }

        public bool TryRestore(CatFlapRuntimeSession session, string progressionIdOverride)
        {
            if (session == null || !session.HasLevel)
            {
                return false;
            }

            string resolvedId = ResolveProgressionId(progressionIdOverride);
            bool found = saveSystem.TryLoadRecord(progressionFileName, progressionScope, resolvedId, CreateKey(session.Level.Id), out LevelProgressionRecord record);
            if (!found)
            {
                return false;
            }

            BoardSaveData data = JsonUtility.FromJson<BoardSaveData>(record.PayloadJson);
            if (data == null || data.size != session.Board.RowCount || data.size != session.Board.ColumnCount)
            {
                return false;
            }

            for (int row = 0; row < data.size; row++)
            {
                for (int column = 0; column < data.size; column++)
                {
                    if (!IsValidIndex(data, row, column))
                    {
                        return false;
                    }
                }
            }

            int remainingMistakes = data.remainingMistakes > 0 || data.maxMistakes > 0
                ? data.remainingMistakes
                : data.currentLives;
            session.RestoreLevelProgress(CreateSnapshot(data), remainingMistakes, data.moves, data.elapsedSeconds);
            return true;
        }

        /// <summary>
        /// Raw saved-board values for the given levelId, without needing a live
        /// CatFlapRuntimeSession to restore into - lets a menu screen (mainly the Daily Challenge
        /// calendar, see CatFlapLevelProgressCalculator) read how far a saved-but-not-currently-
        /// loaded level got, without loading that level into the runtime first.
        /// </summary>
        public bool TryReadSavedBoardValues(string levelId, string progressionIdOverride, out SavedBoardValues values)
        {
            values = default;
            if (string.IsNullOrEmpty(levelId))
            {
                return false;
            }

            string resolvedId = ResolveProgressionId(progressionIdOverride);
            if (!saveSystem.TryLoadRecord(progressionFileName, progressionScope, resolvedId, CreateKey(levelId), out LevelProgressionRecord record))
            {
                return false;
            }

            BoardSaveData data = JsonUtility.FromJson<BoardSaveData>(record.PayloadJson);
            if (data == null || data.values == null || data.size <= 0)
            {
                return false;
            }

            values = new SavedBoardValues(data.size, data.values, data.notes, data.locked);
            return true;
        }

        public readonly struct SavedBoardValues
        {
            public SavedBoardValues(int size, int[] values, bool[] notes, bool[] locked)
            {
                Size = size;
                Values = values;
                Notes = notes;
                Locked = locked;
            }

            public int Size { get; }
            public int[] Values { get; }
            public bool[] Notes { get; }
            public bool[] Locked { get; }
        }

        public void Clear(CatFlapRuntimeSession session)
        {
            Clear(session, progressionId);
        }

        public void Clear(CatFlapRuntimeSession session, string progressionIdOverride)
        {
            if (session == null || session.Level == null)
            {
                Debug.LogWarning($"CatFlapCurrentLevelBoardSaveManager.Clear: no-op (session={session != null}, session.Level={(session != null ? session.Level != null : (bool?)null)}).");
                return;
            }

            string resolvedId = ResolveProgressionId(progressionIdOverride);
            saveSystem.ClearRecord(progressionFileName, progressionScope, resolvedId, CreateKey(session.Level.Id));
        }

        private string CreateKey(string levelId)
        {
            return boardStateKeyPrefix + levelId;
        }

        private string ResolveProgressionId(string progressionIdOverride)
        {
            return string.IsNullOrWhiteSpace(progressionIdOverride) ? progressionId : progressionIdOverride;
        }

        [System.Serializable]
        private sealed class BoardSaveData
        {
            public int size;
            public int[] values;
            public bool[] notes;
            public bool[] locked;
            public int remainingMistakes;
            public int maxMistakes;
            public int currentLives;
            public float livesRegenRemainingSeconds;
            public int moves;
            public float elapsedSeconds;

            public static BoardSaveData FromSession(CatFlapRuntimeSession session)
            {
                int size = session.Board.RowCount;
                CatFlap.Progress.CatFlapProgressSnapshot progress = session.Progress;
                var data = new BoardSaveData
                {
                    size = size,
                    values = new int[size * size],
                    notes = new bool[size * size],
                    locked = new bool[size * size],
                    remainingMistakes = session.LevelMistakeLimit?.RemainingMistakes ?? 0,
                    maxMistakes = session.LevelMistakeLimit?.MaxMistakes ?? 0,
                    currentLives = session.LevelMistakeLimit?.RemainingMistakes ?? 0,
                    livesRegenRemainingSeconds = 0f,
                    moves = progress.Moves,
                    elapsedSeconds = progress.ElapsedSeconds
                };

                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column++)
                    {
                        int index = row * size + column;
                        CatFlapCellModel cell = session.Board.GetCell(row, column);
                        data.values[index] = (int)cell.Value;
                        data.notes[index] = cell.IsNote;
                        data.locked[index] = cell.IsLocked;
                    }
                }

                return data;
            }
        }

        private static bool IsValidIndex(BoardSaveData data, int row, int column)
        {
            int index = row * data.size + column;
            int expectedLength = data.size * data.size;
            return data.values != null &&
                   data.notes != null &&
                   data.locked != null &&
                   data.values.Length == expectedLength &&
                   data.notes.Length == expectedLength &&
                   data.locked.Length == expectedLength &&
                   index >= 0 &&
                   index < expectedLength;
        }

        private static CatFlap.Models.CatFlapBoardSnapshot CreateSnapshot(BoardSaveData data)
        {
            var values = new CatFlapCellValue[data.size, data.size];
            var locked = new bool[data.size, data.size];
            var notes = new bool[data.size, data.size];
            for (int row = 0; row < data.size; row++)
            {
                for (int column = 0; column < data.size; column++)
                {
                    int index = row * data.size + column;
                    values[row, column] = (CatFlapCellValue)data.values[index];
                    locked[row, column] = data.locked[index];
                    notes[row, column] = data.notes[index];
                }
            }

            return new CatFlap.Models.CatFlapBoardSnapshot(values, locked, notes);
        }
    }
}
