using System;
using System.Collections.Generic;

namespace FillIt.Rules
{
    /// <summary>
    /// Builds Fillomino puzzles with exactly one solution (plain C#, no Unity - used by the editor
    /// level generator). First a random valid fill is grown region by region, then clues are
    /// removed one at a time in random order, keeping a removal only while the solver still proves
    /// the answer unique, until the requested clue ratio is reached.
    /// </summary>
    public sealed class FillItPuzzleGenerator
    {
        // Relative weight of each region size (index = size). Mid-sized regions dominate, like
        // hand-made Fillomino; 1s and the largest sizes are rarer.
        private static readonly float[] SizeWeights = { 0f, 0.55f, 1.0f, 1.25f, 1.25f, 1.05f, 0.85f, 0.65f, 0.5f, 0.4f };

        private readonly Random random;

        public FillItPuzzleGenerator(int seed)
        {
            random = new Random(seed);
        }

        public sealed class Puzzle
        {
            public int Size;
            public int[] Solution;
            public int[] Clues;
            public int ClueCount;
            public long SolverNodes;
        }

        /// <param name="targetClueRatio">Stop removing clues once this fraction of cells remain as givens (lower = harder).</param>
        public Puzzle Generate(int size, int maxValue, float targetClueRatio, int maxAttempts = 60)
        {
            maxValue = Math.Max(1, Math.Min(9, maxValue));
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int[] solution = GenerateSolution(size, maxValue);
                if (solution == null)
                {
                    continue;
                }

                Puzzle puzzle = CarveClues(size, maxValue, solution, targetClueRatio);
                if (puzzle != null)
                {
                    return puzzle;
                }
            }

            return null;
        }

        /// <summary>A random complete, valid fill, or null if this attempt boxed itself in.</summary>
        public int[] GenerateSolution(int size, int maxValue, int maxAttempts = 400)
        {
            int count = size * size;
            var values = new int[count];
            var region = new List<int>(maxValue);
            var frontier = new List<int>(16);
            var sizes = new List<int>(maxValue);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Array.Clear(values, 0, count);
                bool failed = false;
                for (int start = 0; start < count && !failed; start++)
                {
                    if (values[start] != 0)
                    {
                        continue;
                    }

                    OrderSizes(maxValue, sizes);
                    bool placed = false;
                    for (int sizeIndex = 0; sizeIndex < sizes.Count && !placed; sizeIndex++)
                    {
                        int targetSize = sizes[sizeIndex];
                        // A few random growths per size before giving up on it.
                        for (int growTry = 0; growTry < 4 && !placed; growTry++)
                        {
                            GrowRegion(size, values, start, targetSize, region, frontier);
                            if (region.Count != targetSize || TouchesSameSize(size, values, region, targetSize))
                            {
                                continue;
                            }

                            for (int index = 0; index < region.Count; index++)
                            {
                                values[region[index]] = targetSize;
                            }

                            placed = true;
                        }
                    }

                    failed = !placed;
                }

                if (!failed && FillItRegionAnalyzer.IsValidSolution(size, values))
                {
                    return (int[])values.Clone();
                }
            }

            return null;
        }

        private Puzzle CarveClues(int size, int maxValue, int[] solution, float targetClueRatio)
        {
            int count = size * size;
            var clues = (int[])solution.Clone();
            var solver = new FillItSolver(size, maxValue);
            int minimumClues = (int)Math.Ceiling(count * Math.Max(0f, Math.Min(1f, targetClueRatio)));
            int clueCount = count;

            var order = new List<int>(count);
            for (int index = 0; index < count; index++)
            {
                order.Add(index);
            }

            Shuffle(order);
            for (int index = 0; index < order.Count && clueCount > minimumClues; index++)
            {
                int cell = order[index];
                int saved = clues[cell];
                clues[cell] = 0;
                int solutions = solver.CountSolutions(clues, 2, 400_000);
                if (solutions != 1 || solver.HitNodeLimit)
                {
                    clues[cell] = saved;
                    continue;
                }

                clueCount--;
            }

            // Final proof (and the effort it took, as a rough difficulty signal).
            int finalCount = solver.Solve(clues, 2, 2_000_000, out int[] solved);
            if (finalCount != 1 || solver.HitNodeLimit || solved == null)
            {
                return null;
            }

            for (int index = 0; index < count; index++)
            {
                if (solved[index] != solution[index])
                {
                    return null;
                }
            }

            return new Puzzle
            {
                Size = size,
                Solution = solution,
                Clues = clues,
                ClueCount = clueCount,
                SolverNodes = solver.NodesVisited
            };
        }

        // Random connected growth from start through empty cells, preferring cells that touch the
        // region on more sides so shapes stay compact rather than stringy.
        private void GrowRegion(int size, int[] values, int start, int targetSize, List<int> region, List<int> frontier)
        {
            region.Clear();
            region.Add(start);
            while (region.Count < targetSize)
            {
                frontier.Clear();
                for (int index = 0; index < region.Count; index++)
                {
                    AddEmptyNeighbors(size, values, region[index], region, frontier);
                }

                if (frontier.Count == 0)
                {
                    return;
                }

                int pick = PickCompact(size, frontier, region);
                region.Add(pick);
            }
        }

        private int PickCompact(int size, List<int> frontier, List<int> region)
        {
            float total = 0f;
            var weights = new float[frontier.Count];
            for (int index = 0; index < frontier.Count; index++)
            {
                int touching = 0;
                int cell = frontier[index];
                for (int r = 0; r < region.Count; r++)
                {
                    if (AreNeighbors(size, cell, region[r]))
                    {
                        touching++;
                    }
                }

                weights[index] = 1f + touching * touching;
                total += weights[index];
            }

            double roll = random.NextDouble() * total;
            for (int index = 0; index < frontier.Count; index++)
            {
                roll -= weights[index];
                if (roll <= 0)
                {
                    return frontier[index];
                }
            }

            return frontier[frontier.Count - 1];
        }

        private static void AddEmptyNeighbors(int size, int[] values, int cell, List<int> region, List<int> frontier)
        {
            int row = cell / size;
            int column = cell % size;
            TryAdd(size, values, row - 1, column, region, frontier);
            TryAdd(size, values, row + 1, column, region, frontier);
            TryAdd(size, values, row, column - 1, region, frontier);
            TryAdd(size, values, row, column + 1, region, frontier);
        }

        private static void TryAdd(int size, int[] values, int row, int column, List<int> region, List<int> frontier)
        {
            if (row < 0 || row >= size || column < 0 || column >= size)
            {
                return;
            }

            int cell = row * size + column;
            if (values[cell] == 0 && !region.Contains(cell) && !frontier.Contains(cell))
            {
                frontier.Add(cell);
            }
        }

        private static bool TouchesSameSize(int size, int[] values, List<int> region, int targetSize)
        {
            for (int index = 0; index < region.Count; index++)
            {
                int cell = region[index];
                int row = cell / size;
                int column = cell % size;
                if ((row > 0 && values[cell - size] == targetSize) ||
                    (row < size - 1 && values[cell + size] == targetSize) ||
                    (column > 0 && values[cell - 1] == targetSize) ||
                    (column < size - 1 && values[cell + 1] == targetSize))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreNeighbors(int size, int a, int b)
        {
            int rowA = a / size;
            int rowB = b / size;
            int columnA = a % size;
            int columnB = b % size;
            return Math.Abs(rowA - rowB) + Math.Abs(columnA - columnB) == 1;
        }

        // Weighted random order of region sizes 1..maxValue (every size appears once).
        private void OrderSizes(int maxValue, List<int> sizes)
        {
            sizes.Clear();
            var pool = new List<int>(maxValue);
            for (int value = 1; value <= maxValue; value++)
            {
                pool.Add(value);
            }

            while (pool.Count > 0)
            {
                float total = 0f;
                for (int index = 0; index < pool.Count; index++)
                {
                    total += SizeWeights[pool[index]];
                }

                double roll = random.NextDouble() * total;
                int chosen = pool.Count - 1;
                for (int index = 0; index < pool.Count; index++)
                {
                    roll -= SizeWeights[pool[index]];
                    if (roll <= 0)
                    {
                        chosen = index;
                        break;
                    }
                }

                sizes.Add(pool[chosen]);
                pool.RemoveAt(chosen);
            }
        }

        private void Shuffle(List<int> list)
        {
            for (int index = list.Count - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (list[index], list[swap]) = (list[swap], list[index]);
            }
        }
    }
}
