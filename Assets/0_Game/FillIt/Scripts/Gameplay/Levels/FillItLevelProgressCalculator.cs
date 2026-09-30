namespace FillIt.Levels
{
    /// <summary>
    /// How far a (saved or live) board has got: the percent of non-clue cells holding their
    /// correct solution number. Used by the Daily Challenge calendar and lives-lost analytics.
    /// </summary>
    public static class FillItLevelProgressCalculator
    {
        public static float CalculatePercent(FillItLevelData level, int[] values)
        {
            if (level == null || values == null || level.Clues == null || level.Solution == null)
            {
                return 0f;
            }

            int count = level.Size * level.Size;
            if (values.Length != count || level.Clues.Length != count || level.Solution.Length != count)
            {
                return 0f;
            }

            int editable = 0;
            int correct = 0;
            for (int index = 0; index < count; index++)
            {
                if (level.Clues[index] != 0)
                {
                    continue;
                }

                editable++;
                if (values[index] == level.Solution[index])
                {
                    correct++;
                }
            }

            return editable <= 0 ? 100f : (float)correct / editable * 100f;
        }
    }
}
