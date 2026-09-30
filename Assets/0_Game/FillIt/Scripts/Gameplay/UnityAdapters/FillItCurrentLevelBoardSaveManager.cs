using FillIt.Models;
using FillIt.Runtime;
using Shared.LevelProgression;
using UnityEngine;

namespace FillIt.UnityAdapters
{
    /// <summary>
    /// Persists the in-progress board of the current level through the shared Level Progression
    /// save file, so quitting mid-level resumes exactly where the player left off. One manager
    /// instance serves both the campaign and Daily Challenge - each passes its own progressionId
    /// "row" so their saves never collide.
    /// </summary>
    public sealed class FillItCurrentLevelBoardSaveManager : MonoBehaviour
    {
        [SerializeField] private string boardStateKeyPrefix = "FillIt.Board.";
        [SerializeField] private string progressionFileName = "level-progression.json";
        [SerializeField] private string progressionScope = "Normal";
        [SerializeField] private string progressionId = "FillIt";
        [SerializeField] private bool keepProgressionHistory;

        private readonly LevelProgressionSaveSystem saveSystem = new LevelProgressionSaveSystem();

        public void ApplySaveKeyPrefix(string saveKeyPrefix)
        {
            if (!string.IsNullOrWhiteSpace(saveKeyPrefix))
            {
                boardStateKeyPrefix = saveKeyPrefix;
            }
        }

        public void Save(FillItRuntimeSession session, int levelIndex)
        {
            Save(session, levelIndex, progressionId);
        }

        public void Save(FillItRuntimeSession session, int levelIndex, string progressionIdOverride)
        {
            if (session == null || !session.HasLevel)
            {
                return;
            }

            BoardSaveData data = BoardSaveData.FromSession(session);
            saveSystem.SaveRecord(progressionFileName, new LevelProgressionRecord
            {
                Scope = progressionScope,
                ProgressionId = ResolveProgressionId(progressionIdOverride),
                LevelId = CreateKey(session.Level.Id),
                LevelIndex = Mathf.Max(0, levelIndex),
                PayloadJson = JsonUtility.ToJson(data)
            }, keepProgressionHistory);
        }

        public bool TryRestore(FillItRuntimeSession session)
        {
            return TryRestore(session, progressionId);
        }

        public bool TryRestore(FillItRuntimeSession session, string progressionIdOverride)
        {
            if (session == null || !session.HasLevel)
            {
                return false;
            }

            if (!TryLoad(session.Level.Id, progressionIdOverride, out BoardSaveData data) ||
                data.size != session.Board.Size ||
                data.values == null ||
                data.values.Length != session.Board.CellCount)
            {
                return false;
            }

            bool[] hinted = data.hinted != null && data.hinted.Length == data.values.Length ? data.hinted : new bool[data.values.Length];
            session.RestoreLevelProgress(new FillItBoardSnapshot(data.size, data.values, hinted), data.remainingMistakes, data.moves, data.elapsedSeconds);
            // Only when the save was made in Timer mode too - switching modes starts a fresh clock.
            if (data.timerEnabled)
            {
                session.RestoreRemainingTime(data.remainingSeconds);
            }

            return true;
        }

        /// <summary>
        /// Raw saved values for a level that isn't loaded - lets the Daily Challenge calendar show
        /// progress without loading each day's level into the runtime.
        /// </summary>
        public bool TryReadSavedBoardValues(string levelId, string progressionIdOverride, out int size, out int[] values)
        {
            size = 0;
            values = null;
            if (string.IsNullOrEmpty(levelId) || !TryLoad(levelId, progressionIdOverride, out BoardSaveData data) || data.values == null || data.size <= 0)
            {
                return false;
            }

            size = data.size;
            values = data.values;
            return true;
        }

        public void Clear(FillItRuntimeSession session)
        {
            Clear(session, progressionId);
        }

        public void Clear(FillItRuntimeSession session, string progressionIdOverride)
        {
            if (session == null || session.Level == null)
            {
                return;
            }

            saveSystem.ClearRecord(progressionFileName, progressionScope, ResolveProgressionId(progressionIdOverride), CreateKey(session.Level.Id));
        }

        private bool TryLoad(string levelId, string progressionIdOverride, out BoardSaveData data)
        {
            data = null;
            if (!saveSystem.TryLoadRecord(progressionFileName, progressionScope, ResolveProgressionId(progressionIdOverride), CreateKey(levelId), out LevelProgressionRecord record))
            {
                return false;
            }

            data = JsonUtility.FromJson<BoardSaveData>(record.PayloadJson);
            return data != null;
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
            public bool[] hinted;
            public int remainingMistakes;
            public int maxMistakes;
            public int moves;
            public float elapsedSeconds;
            public bool timerEnabled;
            public float remainingSeconds;

            public static BoardSaveData FromSession(FillItRuntimeSession session)
            {
                FillItBoardSnapshot snapshot = session.Board.CreateSnapshot();
                FillItProgressSnapshot progress = session.Progress;
                return new BoardSaveData
                {
                    size = snapshot.Size,
                    values = snapshot.Values,
                    hinted = snapshot.Hinted,
                    remainingMistakes = session.LevelMistakeLimit?.RemainingMistakes ?? 0,
                    maxMistakes = session.LevelMistakeLimit?.MaxMistakes ?? 0,
                    moves = progress.Moves,
                    elapsedSeconds = progress.ElapsedSeconds,
                    timerEnabled = session.TimerEnabled,
                    remainingSeconds = session.RemainingSeconds
                };
            }
        }
    }
}
