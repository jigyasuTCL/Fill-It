using System.Collections.Generic;
using System.Text;

namespace FillIt.Rules
{
    /// <summary>An orthogonally connected group of cells that all hold the same number.</summary>
    public sealed class FillItRegion
    {
        private string signature;

        public FillItRegion(int id, int value, List<int> cells, bool hasOpenEdge)
        {
            Id = id;
            Value = value;
            Cells = cells;
            HasOpenEdge = hasOpenEdge;
            Status = cells.Count == value
                ? FillItRegionStatus.Done
                : cells.Count > value
                    ? FillItRegionStatus.Over
                    : hasOpenEdge ? FillItRegionStatus.Open : FillItRegionStatus.Stuck;
        }

        public int Id { get; }
        public int Value { get; }
        public List<int> Cells { get; }
        public int Size => Cells.Count;
        public bool HasOpenEdge { get; }
        public FillItRegionStatus Status { get; }
        public bool IsViolation => Status == FillItRegionStatus.Over || Status == FillItRegionStatus.Stuck;

        public FillItRule? BrokenRule
        {
            get
            {
                switch (Status)
                {
                    case FillItRegionStatus.Over:
                        return FillItRule.RegionTooBig;
                    case FillItRegionStatus.Stuck:
                        return FillItRule.RegionTooSmall;
                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Stable identity for "this exact broken region" - value, status and sorted cells. Used to
        /// charge a life only once for the same violation, however many times it gets re-validated.
        /// </summary>
        public string Signature
        {
            get
            {
                if (signature != null)
                {
                    return signature;
                }

                var sorted = new List<int>(Cells);
                sorted.Sort();
                var builder = new StringBuilder();
                builder.Append(Value).Append(Status == FillItRegionStatus.Over ? 'o' : Status == FillItRegionStatus.Stuck ? 's' : 'n').Append(':');
                for (int index = 0; index < sorted.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(sorted[index]);
                }

                signature = builder.ToString();
                return signature;
            }
        }
    }
}
