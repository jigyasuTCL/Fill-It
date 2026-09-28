using System.Collections.Generic;
using System.Linq;

namespace CatFlap.Validation
{
    public sealed class CatFlapRuleValidationResult
    {
        private readonly List<CatFlapRuleViolation> violations = new List<CatFlapRuleViolation>();

        public IReadOnlyList<CatFlapRuleViolation> Violations => violations;
        public bool IsValid => violations.Count == 0;

        public bool IsRuleValid(CatFlapRule rule)
        {
            return violations.All(violation => violation.Rule != rule);
        }

        public void AddViolation(CatFlapRule rule, string message, IEnumerable<string> cellKeys)
        {
            violations.Add(new CatFlapRuleViolation(rule, message, cellKeys));
        }

        /// <summary>
        /// Builds a new result that keeps every violation from this result except the ones that
        /// belong entirely to the given row or entirely to the given column, then adds every
        /// violation from <paramref name="scoped"/> (a fresh <see cref="CatFlapRuleValidator.ValidateLines"/>
        /// check of just that row and column) in their place. Used to fold a single tapped cell's
        /// row/column recheck back into the last full-board result without rescanning every other
        /// line, since a single cell edit can only ever change violations on its own row/column.
        /// </summary>
        public CatFlapRuleValidationResult ReplaceLines(CatFlapRuleValidationResult scoped, int row, int column)
        {
            var merged = new CatFlapRuleValidationResult();
            foreach (CatFlapRuleViolation violation in violations)
            {
                if (!BelongsToLine(violation, row, column))
                {
                    merged.violations.Add(violation);
                }
            }

            if (scoped != null)
            {
                foreach (CatFlapRuleViolation violation in scoped.violations)
                {
                    merged.violations.Add(violation);
                }
            }

            return merged;
        }

        private static bool BelongsToLine(CatFlapRuleViolation violation, int row, int column)
        {
            bool allRow = true;
            bool allColumn = true;
            foreach (string key in violation.CellKeys)
            {
                if (!TryParseKey(key, out int keyRow, out int keyColumn))
                {
                    return false;
                }

                allRow &= keyRow == row;
                allColumn &= keyColumn == column;
            }

            return allRow || allColumn;
        }

        private static bool TryParseKey(string key, out int row, out int column)
        {
            row = -1;
            column = -1;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string[] parts = key.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0], out row) && int.TryParse(parts[1], out column);
        }
    }
}
