using System;
using System.Collections.Generic;

namespace FillIt.Rules
{
    /// <summary>
    /// Backtracking Fillomino solver (plain C#, no Unity). Used by the level generator to prove a
    /// puzzle has exactly one solution. Values are 1..maxValue; 0 is an empty cell.
    ///
    /// Every empty cell keeps a candidate mask. Propagation: a finished region removes its number
    /// from the cells around it; a region with only one cell of room left must take it; a cell
    /// with one candidate left takes it. Search branches two ways on the most-nearly-complete
    /// region's growth cell - "it joins the region" / "it doesn't" - which keeps the tree small.
    /// </summary>
    public sealed class FillItSolver
    {
        private const int AllBits = 0x3FE; // bits 1..9

        private readonly int size;
        private readonly int count;
        private readonly int maxValue;
        private readonly int fullMask;
        private readonly int[] values;
        private readonly int[] masks;
        private readonly int[][] neighbors;
        private readonly List<long> trail = new List<long>();
        private readonly int[] visitMark;
        private readonly int[] frontierMark;
        private readonly int[] queue;
        private readonly List<int> regionScratch = new List<int>();
        private int visitStamp;
        private int frontierStamp;
        private long nodes;
        private long nodeLimit;
        private int solutionLimit;
        private int solutionCount;
        private int[] firstSolution;

        public FillItSolver(int size, int maxValue = 9)
        {
            this.size = size;
            count = size * size;
            this.maxValue = Math.Max(1, Math.Min(9, maxValue));
            fullMask = AllBits & ((1 << (this.maxValue + 1)) - 1);
            values = new int[count];
            masks = new int[count];
            visitMark = new int[count];
            frontierMark = new int[count];
            queue = new int[count];
            neighbors = new int[count][];
            for (int index = 0; index < count; index++)
            {
                int row = index / size;
                int column = index % size;
                var list = new List<int>(4);
                if (row > 0) list.Add(index - size);
                if (row < size - 1) list.Add(index + size);
                if (column > 0) list.Add(index - 1);
                if (column < size - 1) list.Add(index + 1);
                neighbors[index] = list.ToArray();
            }
        }

        public long NodesVisited => nodes;
        public bool HitNodeLimit { get; private set; }

        /// <summary>Counts solutions up to solutionLimit. HitNodeLimit is set if the search gave up.</summary>
        public int CountSolutions(int[] clues, int solutionLimit = 2, long nodeLimit = 2_000_000)
        {
            return Solve(clues, solutionLimit, nodeLimit, out _);
        }

        public int Solve(int[] clues, int solutionLimit, long nodeLimit, out int[] solution)
        {
            trail.Clear();
            nodes = 0;
            this.nodeLimit = nodeLimit;
            this.solutionLimit = Math.Max(1, solutionLimit);
            solutionCount = 0;
            firstSolution = null;
            HitNodeLimit = false;
            solution = null;

            for (int index = 0; index < count; index++)
            {
                int value = clues[index];
                values[index] = value;
                masks[index] = value > 0 ? 1 << value : fullMask;
            }

            for (int index = 0; index < count; index++)
            {
                if (values[index] > 0 && (values[index] > maxValue || !CheckAround(index)))
                {
                    return 0;
                }
            }

            // Finished given regions already rule their number out of the cells around them.
            for (int index = 0; index < count; index++)
            {
                if (values[index] > 0 && !ExcludeAroundIfComplete(index))
                {
                    return 0;
                }
            }

            Search();
            solution = firstSolution;
            return solutionCount;
        }

        private void Search()
        {
            if (solutionCount >= solutionLimit || HitNodeLimit)
            {
                return;
            }

            if (++nodes > nodeLimit)
            {
                HitNodeLimit = true;
                return;
            }

            int mark = trail.Count;
            if (!Propagate())
            {
                Undo(mark);
                return;
            }

            if (FindGrowthBranch(out int growCell, out int growValue))
            {
                // Branch A: the cell joins the region.
                int branchMark = trail.Count;
                if (PlaceChecked(growCell, growValue))
                {
                    Search();
                }

                Undo(branchMark);

                // Branch B: it doesn't - some other cell must give the region its room.
                if (solutionCount < solutionLimit && !HitNodeLimit && Exclude(growCell, growValue))
                {
                    Search();
                }

                Undo(mark);
                return;
            }

            // No region is still growing - start a new one in the most constrained empty cell.
            int cell = -1;
            int bestBits = int.MaxValue;
            for (int index = 0; index < count; index++)
            {
                if (values[index] != 0)
                {
                    continue;
                }

                int bits = PopCount(masks[index]);
                if (bits < bestBits)
                {
                    bestBits = bits;
                    cell = index;
                    if (bits <= 1)
                    {
                        break;
                    }
                }
            }

            if (cell < 0)
            {
                if (FillItRegionAnalyzer.Analyze(size, values).IsSolved)
                {
                    solutionCount++;
                    if (firstSolution == null)
                    {
                        firstSolution = (int[])values.Clone();
                    }
                }

                Undo(mark);
                return;
            }

            int candidates = masks[cell];
            for (int value = 1; value <= maxValue && solutionCount < solutionLimit && !HitNodeLimit; value++)
            {
                if ((candidates & (1 << value)) == 0)
                {
                    continue;
                }

                int branchMark = trail.Count;
                if (PlaceChecked(cell, value))
                {
                    Search();
                }

                Undo(branchMark);
            }

            Undo(mark);
        }

        // Places and runs every local check plus the "finished region excludes its neighbours" rule.
        private bool PlaceChecked(int cell, int value)
        {
            if ((masks[cell] & (1 << value)) == 0)
            {
                return false;
            }

            Place(cell, value);
            return CheckAround(cell) && ExcludeAroundIfComplete(cell);
        }

        private bool Propagate()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;

                // Naked singles.
                for (int index = 0; index < count; index++)
                {
                    if (values[index] != 0)
                    {
                        continue;
                    }

                    int mask = masks[index];
                    if (mask == 0)
                    {
                        return false;
                    }

                    if ((mask & (mask - 1)) == 0)
                    {
                        if (!PlaceChecked(index, BitIndex(mask)))
                        {
                            return false;
                        }

                        changed = true;
                    }
                }

                // Regions with exactly one cell of room must grow into it.
                NewStamp();
                for (int start = 0; start < count && !changed; start++)
                {
                    int value = values[start];
                    if (value == 0 || visitMark[start] == visitStamp)
                    {
                        continue;
                    }

                    int regionSize = FloodRegion(start, value, out int frontierCell, out int frontierCount);
                    if (regionSize > value)
                    {
                        return false;
                    }

                    if (regionSize == value)
                    {
                        continue;
                    }

                    if (frontierCount == 0)
                    {
                        return false;
                    }

                    if (frontierCount == 1)
                    {
                        if (!PlaceChecked(frontierCell, value))
                        {
                            return false;
                        }

                        changed = true;
                    }
                }
            }

            return true;
        }

        // The open region with the fewest cells still missing (ties: fewest growth cells), and one
        // of its growth cells.
        private bool FindGrowthBranch(out int cell, out int value)
        {
            cell = -1;
            value = 0;
            int bestMissing = int.MaxValue;
            int bestFrontier = int.MaxValue;
            NewStamp();
            for (int start = 0; start < count; start++)
            {
                int regionValue = values[start];
                if (regionValue == 0 || visitMark[start] == visitStamp)
                {
                    continue;
                }

                int regionSize = FloodRegion(start, regionValue, out int frontierCell, out int frontierCount);
                int missing = regionValue - regionSize;
                if (missing <= 0 || frontierCount == 0)
                {
                    continue;
                }

                if (missing < bestMissing || (missing == bestMissing && frontierCount < bestFrontier))
                {
                    bestMissing = missing;
                    bestFrontier = frontierCount;
                    cell = frontierCell;
                    value = regionValue;
                }
            }

            return cell >= 0;
        }

        // After a placement at cell: its region (and every neighbouring region) must not be over its
        // number and must still be able to reach it.
        private bool CheckAround(int cell)
        {
            if (!RegionCanStillWork(cell))
            {
                return false;
            }

            int[] cellNeighbors = neighbors[cell];
            for (int n = 0; n < cellNeighbors.Length; n++)
            {
                int neighbor = cellNeighbors[n];
                if (values[neighbor] != 0 && values[neighbor] != values[cell] && !RegionCanStillWork(neighbor))
                {
                    return false;
                }
            }

            return true;
        }

        // When the region through cell is finished, no empty cell touching it may take its number.
        private bool ExcludeAroundIfComplete(int cell)
        {
            int value = values[cell];
            NewStamp();
            int regionSize = FloodRegion(cell, value, out _, out _);
            if (regionSize != value)
            {
                return true;
            }

            regionScratch.Clear();
            for (int index = 0; index < regionSize; index++)
            {
                regionScratch.Add(queue[index]);
            }

            int bit = 1 << value;
            for (int index = 0; index < regionScratch.Count; index++)
            {
                int[] cellNeighbors = neighbors[regionScratch[index]];
                for (int n = 0; n < cellNeighbors.Length; n++)
                {
                    int neighbor = cellNeighbors[n];
                    if (values[neighbor] == 0 && (masks[neighbor] & bit) != 0 && !Exclude(neighbor, value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool RegionCanStillWork(int cell)
        {
            int value = values[cell];
            NewStamp();
            int regionSize = FloodRegion(cell, value, out _, out int frontierCount);
            if (regionSize > value)
            {
                return false;
            }

            if (regionSize == value)
            {
                return true;
            }

            if (frontierCount == 0)
            {
                return false;
            }

            // Upper bound on how big the region could still get: everything reachable through empty
            // cells that may still take this number, or cells already holding it.
            int bit = 1 << value;
            int reach = 0;
            NewStamp();
            int head = 0;
            int tail = 0;
            queue[tail++] = cell;
            visitMark[cell] = visitStamp;
            while (head < tail)
            {
                int current = queue[head++];
                if (++reach >= value)
                {
                    return true;
                }

                int[] currentNeighbors = neighbors[current];
                for (int n = 0; n < currentNeighbors.Length; n++)
                {
                    int next = currentNeighbors[n];
                    if (visitMark[next] == visitStamp)
                    {
                        continue;
                    }

                    int nextValue = values[next];
                    if (nextValue == value || (nextValue == 0 && (masks[next] & bit) != 0))
                    {
                        visitMark[next] = visitStamp;
                        queue[tail++] = next;
                    }
                }
            }

            return false;
        }

        // Flood-fills the same-valued region containing start (marking it with the current visit
        // stamp; its cells end up in queue[0..size)). Returns its size, one growth cell (an empty
        // neighbour that may still take this number) and how many distinct growth cells it has.
        private int FloodRegion(int start, int value, out int frontierCell, out int frontierCount)
        {
            frontierCell = -1;
            frontierCount = 0;
            int bit = 1 << value;
            frontierStamp++;
            if (frontierStamp == int.MaxValue)
            {
                Array.Clear(frontierMark, 0, frontierMark.Length);
                frontierStamp = 1;
            }

            int head = 0;
            int tail = 0;
            queue[tail++] = start;
            visitMark[start] = visitStamp;
            while (head < tail)
            {
                int current = queue[head++];
                int[] currentNeighbors = neighbors[current];
                for (int n = 0; n < currentNeighbors.Length; n++)
                {
                    int next = currentNeighbors[n];
                    int nextValue = values[next];
                    if (nextValue == 0)
                    {
                        if ((masks[next] & bit) != 0 && frontierMark[next] != frontierStamp)
                        {
                            frontierMark[next] = frontierStamp;
                            frontierCount++;
                            frontierCell = next;
                        }
                    }
                    else if (nextValue == value && visitMark[next] != visitStamp)
                    {
                        visitMark[next] = visitStamp;
                        queue[tail++] = next;
                    }
                }
            }

            return tail;
        }

        private void Place(int cell, int value)
        {
            trail.Add(Pack(cell, values[cell], masks[cell]));
            values[cell] = value;
            masks[cell] = 1 << value;
        }

        private bool Exclude(int cell, int value)
        {
            int bit = 1 << value;
            if ((masks[cell] & bit) == 0)
            {
                return true;
            }

            trail.Add(Pack(cell, values[cell], masks[cell]));
            masks[cell] &= ~bit;
            return masks[cell] != 0;
        }

        private void Undo(int mark)
        {
            for (int index = trail.Count - 1; index >= mark; index--)
            {
                long entry = trail[index];
                int cell = (int)(entry & 0xFFFF);
                values[cell] = (int)((entry >> 16) & 0xFF);
                masks[cell] = (int)((entry >> 24) & 0xFFFF);
            }

            trail.RemoveRange(mark, trail.Count - mark);
        }

        private static long Pack(int cell, int value, int mask)
        {
            return (long)cell | ((long)value << 16) | ((long)mask << 24);
        }

        private void NewStamp()
        {
            visitStamp++;
            if (visitStamp == int.MaxValue)
            {
                Array.Clear(visitMark, 0, visitMark.Length);
                visitStamp = 1;
            }
        }

        private static int PopCount(int value)
        {
            int bits = 0;
            while (value != 0)
            {
                value &= value - 1;
                bits++;
            }

            return bits;
        }

        private static int BitIndex(int singleBit)
        {
            int index = 0;
            while (singleBit > 1)
            {
                singleBit >>= 1;
                index++;
            }

            return index;
        }
    }
}
