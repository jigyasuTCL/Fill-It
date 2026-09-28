using System.Collections.Generic;

namespace CatFlap.Validation
{
    public sealed class CatFlapRuleViolation
    {
        public CatFlapRuleViolation(CatFlapRule rule, string message, IEnumerable<string> cellKeys)
        {
            Rule = rule;
            Message = message;
            CellKeys = new List<string>(cellKeys);
        }

        public CatFlapRule Rule { get; }
        public string Message { get; }
        public IReadOnlyList<string> CellKeys { get; }
    }
}
