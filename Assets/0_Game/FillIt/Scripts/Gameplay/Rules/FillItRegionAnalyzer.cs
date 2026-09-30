using System.Collections.Generic;
using FillIt.Models;

namespace FillIt.Rules
{
    /// <summary>
    /// Fillomino rules: orthogonally connected cells holding the same number form a region, and every
    /// region must contain exactly as many cells as its number (so two different regions of the same
    /// size can never touch). A board is solved once it is full and every region is Done - any valid
    /// fill counts, not just the level's stored solution.
    /// </summary>
    public static class FillItRegionAnalyzer
    {
        public static FillItBoardAnalysis Analyze(FillItBoardModel board)
        {
            return board == null ? null : Analyze(board.Size, board.CopyValues());
        }

        public static FillItBoardAnalysis Analyze(int size, int[] values)
        {
            int count = size * size;
            var regionOfCell = new int[count];
            for (int index = 0; index < count; index++)
            {
                regionOfCell[index] = -1;
            }

            var regions = new List<FillItRegion>();
            var stack = new Stack<int>();
            bool isFull = true;

            for (int start = 0; start < count; start++)
            {
                int value = values[start];
                if (value <= 0)
                {
                    isFull = false;
                    continue;
                }

                if (regionOfCell[start] >= 0)
                {
                    continue;
                }

                int id = regions.Count;
                var cells = new List<int> { start };
                bool openEdge = false;
                regionOfCell[start] = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    int row = cell / size;
                    int column = cell % size;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int neighborRow = row + (direction == 0 ? -1 : direction == 1 ? 1 : 0);
                        int neighborColumn = column + (direction == 2 ? -1 : direction == 3 ? 1 : 0);
                        if (neighborRow < 0 || neighborRow >= size || neighborColumn < 0 || neighborColumn >= size)
                        {
                            continue;
                        }

                        int neighbor = neighborRow * size + neighborColumn;
                        int neighborValue = values[neighbor];
                        if (neighborValue <= 0)
                        {
                            openEdge = true;
                        }
                        else if (neighborValue == value && regionOfCell[neighbor] < 0)
                        {
                            regionOfCell[neighbor] = id;
                            cells.Add(neighbor);
                            stack.Push(neighbor);
                        }
                    }
                }

                regions.Add(new FillItRegion(id, value, cells, openEdge));
            }

            return new FillItBoardAnalysis(size, regionOfCell, regions, isFull);
        }

        /// <summary>True when the values describe a complete, valid Fillomino fill.</summary>
        public static bool IsValidSolution(int size, int[] values)
        {
            if (values == null || values.Length != size * size)
            {
                return false;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] < 1 || values[index] > FillItBoardModel.MaxValue)
                {
                    return false;
                }
            }

            return Analyze(size, values).IsSolved;
        }
    }
}
