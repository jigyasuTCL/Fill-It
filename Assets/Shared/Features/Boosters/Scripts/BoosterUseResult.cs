namespace Shared.Boosters
{
    public readonly struct BoosterUseResult
    {
        public BoosterUseResult(bool success, BoosterType boosterType, string message)
        {
            Success = success;
            BoosterType = boosterType;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public BoosterType BoosterType { get; }
        public string Message { get; }

        public static BoosterUseResult Failed(BoosterType boosterType, string message)
        {
            return new BoosterUseResult(false, boosterType, message);
        }

        public static BoosterUseResult Used(BoosterType boosterType, string message)
        {
            return new BoosterUseResult(true, boosterType, message);
        }
    }
}
