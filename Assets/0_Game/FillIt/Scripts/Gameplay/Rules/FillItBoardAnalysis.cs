using System.Collections.Generic;
using FillIt.Models;

namespace FillIt.Rules
{
    /// <summary>
    /// Snapshot of every region on a board at one moment: which region each cell belongs to, and
    /// whether each region is open, done, too big or stuck. Built by <see cref="FillItRegionAnalyzer"/>.
    /// </summary>
    public sealed class FillItBoardAnalysis
    {
        private static readonly List<FillItRegion> NoRegions = new List<FillItRegion>();

        public FillItBoardAnalysis(int size, int[] regionOfCell, List<FillItRegion> regions, bool isFull)
        {
            Size = size;
            RegionOfCell = regionOfCell;
            Regions = regions ?? NoRegions;
            IsFull = isFull;

            bool allDone = true;
            for (int index = 0; index < Regions.Count; index++)
            {
                FillItRegion region = Regions[index];
                if (region.IsViolation)
                {
                    HasViolation = true;
                }

                if (region.Status != FillItRegionStatus.Done)
                {
                    allDone = false;
                }
            }

            IsSolved = isFull && allDone;
        }

        public static FillItBoardAnalysis Empty(int size)
        {
            var regionOfCell = new int[size * size];
            for (int index = 0; index < regionOfCell.Length; index++)
            {
                regionOfCell[index] = -1;
            }

            return new FillItBoardAnalysis(size, regionOfCell, new List<FillItRegion>(), false);
        }

        public int Size { get; }
        /// <summary>Region id for each cell, or -1 for an empty cell.</summary>
        public int[] RegionOfCell { get; }
        public List<FillItRegion> Regions { get; }
        public bool IsFull { get; }
        public bool IsSolved { get; }
        public bool HasViolation { get; }

        public FillItRegion GetRegionAt(int index)
        {
            if (index < 0 || index >= RegionOfCell.Length)
            {
                return null;
            }

            int id = RegionOfCell[index];
            return id >= 0 && id < Regions.Count ? Regions[id] : null;
        }

        public FillItRegionStatus GetStatus(int index)
        {
            FillItRegion region = GetRegionAt(index);
            return region != null ? region.Status : FillItRegionStatus.Empty;
        }

        public bool IsViolationCell(int index)
        {
            FillItRegion region = GetRegionAt(index);
            return region != null && region.IsViolation;
        }

        public bool IsRuleBroken(FillItRule rule)
        {
            for (int index = 0; index < Regions.Count; index++)
            {
                if (Regions[index].BrokenRule == rule)
                {
                    return true;
                }
            }

            return false;
        }

        public int CountDoneCells()
        {
            int count = 0;
            for (int index = 0; index < Regions.Count; index++)
            {
                if (Regions[index].Status == FillItRegionStatus.Done)
                {
                    count += Regions[index].Size;
                }
            }

            return count;
        }

        public List<FillItRegion> GetViolations()
        {
            var result = new List<FillItRegion>();
            for (int index = 0; index < Regions.Count; index++)
            {
                if (Regions[index].IsViolation)
                {
                    result.Add(Regions[index]);
                }
            }

            return result;
        }

        public List<FillItCellPosition> GetViolationCells()
        {
            var result = new List<FillItCellPosition>();
            for (int index = 0; index < Regions.Count; index++)
            {
                FillItRegion region = Regions[index];
                if (!region.IsViolation)
                {
                    continue;
                }

                for (int cell = 0; cell < region.Cells.Count; cell++)
                {
                    result.Add(new FillItCellPosition(region.Cells[cell] / Size, region.Cells[cell] % Size));
                }
            }

            return result;
        }
    }
}
