using System;

namespace DailyChallenges
{
    public enum DailyChallengePlayState
    {
        NotPlayed = -1,
        Lost = 0,
        Win = 1,

        // The player has opened today's puzzle and is mid-attempt - set on a level once gameplay
        // starts, and cleared by SetLevelState(Win/Lost) once it resolves. Lets calendar UI show
        // an in-progress state distinct from "never opened" (see
        // DailyChallengeDateButton.unlockedWithProgressStateRoot).
        Played = 2
    }

    [Serializable]
    public sealed class DailyChallengeData
    {
        public int Year { get; private set; }
        public int Month { get; private set; }
        public int Day { get; private set; }

        public int PuzzleIndex { get; private set; } = -1;
        public bool IsPuzzleAssigned => PuzzleIndex >= 0;

        public bool IsUnlocked { get; set; } = true;

        // public LevelsDetail LevelDetails { get; private set; }

        public DailyChallengePlayState[] LevelStates { get; private set; } =
            new[] { DailyChallengePlayState.NotPlayed };

        public int LevelCount => LevelStates == null ? 0 : LevelStates.Length;

        public bool HasWin
        {
            get
            {
                if (LevelStates == null || LevelStates.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < LevelStates.Length; i++)
                {
                    if (LevelStates[i] != DailyChallengePlayState.Win)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool HasLost
        {
            get
            {
                if (LevelStates == null || LevelStates.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < LevelStates.Length; i++)
                {
                    if (LevelStates[i] == DailyChallengePlayState.Lost)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool CanSelect => IsUnlocked && !HasWin;

        public bool HasPlayed
        {
            get
            {
                if (LevelStates == null || LevelStates.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < LevelStates.Length; i++)
                {
                    if (LevelStates[i] == DailyChallengePlayState.Played)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public DailyChallengePlayState PlayState
        {
            get
            {
                if (HasWin)
                {
                    return DailyChallengePlayState.Win;
                }

                if (HasLost)
                {
                    return DailyChallengePlayState.Lost;
                }

                if (HasPlayed)
                {
                    return DailyChallengePlayState.Played;
                }

                return DailyChallengePlayState.NotPlayed;
            }
        }

        public DailyChallengeData(int year, int month, int day, int puzzleIndex = -1, int[] levelStates = null)
        {
            Year = year;
            Month = month;
            Day = day;
            PuzzleIndex = puzzleIndex >= 0 ? puzzleIndex : -1;

            SetLevelStates(levelStates);
        }

        public void SetPuzzleIndex(int newPuzzleIndex)
        {
            if (newPuzzleIndex >= 0)
            {
                PuzzleIndex = newPuzzleIndex;
            }
        }

        // public void SetLevelDetails(LevelsDetail levelDetail)
        // {
        //     LevelDetails = levelDetail;

        //     if (levelDetail == null)
        //     {
        //         SetLevelCount(1);
        //         return;
        //     }

        //     SetLevelCount(Math.Max(1, levelDetail.subLevelCount));
        // }

        public void SetLevelCount(int newLevelCount)
        {
            if (newLevelCount <= 0)
            {
                newLevelCount = 1;
            }

            var oldStates = LevelStates ?? new[] { DailyChallengePlayState.NotPlayed };
            var newStates = new DailyChallengePlayState[newLevelCount];

            for (int i = 0; i < newLevelCount; i++)
            {
                newStates[i] = i < oldStates.Length ? oldStates[i] : DailyChallengePlayState.NotPlayed;
            }

            LevelStates = newStates;
        }

        public void SetPlayState(DailyChallengePlayState state)
        {
            if (LevelStates == null || LevelStates.Length == 0)
            {
                LevelStates = new[] { state };
                return;
            }

            for (int i = 0; i < LevelStates.Length; i++)
            {
                LevelStates[i] = state;
            }
        }

        public void SetLevelStates(int[] rawStates)
        {
            if (rawStates == null || rawStates.Length == 0)
            {
                LevelStates = new[] { DailyChallengePlayState.NotPlayed };
                return;
            }

            LevelStates = new DailyChallengePlayState[rawStates.Length];
            for (int i = 0; i < rawStates.Length; i++)
            {
                LevelStates[i] = ConvertFromInt(rawStates[i]);
            }
        }

        public void SetLevelState(int levelIndex, DailyChallengePlayState state)
        {
            if (levelIndex < 0 || levelIndex >= LevelStates.Length)
            {
                return;
            }

            LevelStates[levelIndex] = state;
        }

        public DailyChallengePlayState GetLevelState(int levelIndex)
        {
            if (levelIndex < 0 || levelIndex >= LevelStates.Length)
            {
                return DailyChallengePlayState.NotPlayed;
            }

            return LevelStates[levelIndex];
        }

        private string namePrefix = "Puzzle";
        private string nameMiddle = "_";

        public void SetNamingFormat(string prefix, string middle)
        {
            namePrefix = string.IsNullOrEmpty(prefix) ? "Puzzle" : prefix;
            nameMiddle = middle ?? "_";
        }

        public string GetPuzzleName(int levelIndex)
        {
            if (!IsPuzzleAssigned)
            {
                return "Unassigned";
            }

            if (levelIndex < 0 || levelIndex >= LevelStates.Length)
            {
                return $"{namePrefix}{nameMiddle}{PuzzleIndex:00}{nameMiddle}00";
            }

            return $"{namePrefix}{nameMiddle}{PuzzleIndex:00}{nameMiddle}{levelIndex + 1:00}";
        }

        public string GetCurrentPuzzleName()
        {
            if (!IsPuzzleAssigned)
            {
                return "Unassigned";
            }

            if (LevelStates == null || LevelStates.Length == 0)
            {
                return $"{namePrefix}{nameMiddle}{PuzzleIndex:00}{nameMiddle}01";
            }

            for (int i = 0; i < LevelStates.Length; i++)
            {
                if (LevelStates[i] != DailyChallengePlayState.Win)
                {
                    return $"{namePrefix}{nameMiddle}{PuzzleIndex:00}{nameMiddle}{i + 1:00}";
                }
            }

            return $"{namePrefix}{nameMiddle}{PuzzleIndex:00}{nameMiddle}{LevelStates.Length:00}";
        }

        private static DailyChallengePlayState ConvertFromInt(int value)
        {
            switch (value)
            {
                case 1:
                    return DailyChallengePlayState.Win;
                case 0:
                    return DailyChallengePlayState.Lost;
                case 2:
                    return DailyChallengePlayState.Played;
                case -1:
                    return DailyChallengePlayState.NotPlayed;
                default:
                    return DailyChallengePlayState.NotPlayed;
            }
        }
    }
}
