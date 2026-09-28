namespace CatFlap.Levels
{
    /// <summary>
    /// Progress-% math for a level: how much of the puzzle a saved board has actually solved,
    /// as a percentage of the cells that were EMPTY (not given) at level start. Given clue cells
    /// are trivially "correct" and never count as player progress, on either side of the ratio.
    /// Pure data-in/data-out - takes a level's clues/solution and a saved board's raw values, no
    /// runtime session or board model required. See CatFlapCurrentLevelBoardSaveManager for
    /// reading a saved board without loading the level, mainly for Daily Challenge calendar UI.
    /// </summary>
    public static class CatFlapLevelProgressCalculator
    {
        public readonly struct Result
        {
            public Result(int emptyCellCount, int correctlyPlacedCount)
            {
                EmptyCellCount = emptyCellCount;
                CorrectlyPlacedCount = correctlyPlacedCount;
            }

            /// <summary>Cells with no given clue at level start - the denominator of ProgressPercent.</summary>
            public int EmptyCellCount { get; }

            /// <summary>Of those empty cells, how many currently hold their solution value (committed, not a note).</summary>
            public int CorrectlyPlacedCount { get; }

            /// <summary>0 when EmptyCellCount is 0 (an all-given level) rather than dividing by zero.</summary>
            public float ProgressPercent => EmptyCellCount > 0 ? 100f * CorrectlyPlacedCount / EmptyCellCount : 0f;
        }

        public static Result Calculate(CatFlapLevelData level, int size, int[] values, bool[] notes)
        {
            return new Result(CountEmptyCellsAtStart(level), CountCorrectlyPlaced(level, size, values, notes));
        }

        /// <summary>Empty space at level start: clue cells with no given value.</summary>
        public static int CountEmptyCellsAtStart(CatFlapLevelData level)
        {
            if (level?.Clues == null)
            {
                return 0;
            }

            int count = 0;
            for (int row = 0; row < level.Clues.Length; row++)
            {
                int?[] cells = level.Clues[row]?.Cells;
                if (cells == null)
                {
                    continue;
                }

                for (int column = 0; column < cells.Length; column++)
                {
                    if (!cells[column].HasValue)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Cells that were empty at level start AND currently hold their solution value in the
        /// saved board (committed, not a tentative note) - given clues never count, whether or
        /// not they happen to still match the solution.
        /// </summary>
        public static int CountCorrectlyPlaced(CatFlapLevelData level, int size, int[] values, bool[] notes)
        {
            if (level?.Clues == null || level.Solution == null || values == null)
            {
                return 0;
            }

            int count = 0;
            for (int row = 0; row < size && row < level.Clues.Length && row < level.Solution.Length; row++)
            {
                int?[] clueCells = level.Clues[row]?.Cells;
                int?[] solutionCells = level.Solution[row]?.Cells;
                if (clueCells == null || solutionCells == null)
                {
                    continue;
                }

                for (int column = 0; column < size && column < clueCells.Length && column < solutionCells.Length; column++)
                {
                    if (clueCells[column].HasValue)
                    {
                        continue;
                    }

                    int index = row * size + column;
                    if (index >= values.Length || (notes != null && index < notes.Length && notes[index]))
                    {
                        continue;
                    }

                    int? solutionValue = solutionCells[column];
                    if (solutionValue.HasValue && values[index] == solutionValue.Value)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
