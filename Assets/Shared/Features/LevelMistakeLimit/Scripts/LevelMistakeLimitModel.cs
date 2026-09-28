using System;

namespace Shared.LevelMistakeLimit
{
    public sealed class LevelMistakeLimitModel
    {
        public LevelMistakeLimitModel(int maxMistakes)
        {
            MaxMistakes = Math.Max(1, maxMistakes);
            RemainingMistakes = MaxMistakes;
        }

        public int RemainingMistakes { get; private set; }

        public int MaxMistakes { get; }

        public bool IsOutOfMistakes => RemainingMistakes <= 0;

        public bool TrySpendMistake()
        {
            if (RemainingMistakes <= 0)
            {
                return false;
            }

            RemainingMistakes--;
            return true;
        }

        public void AddMistakes(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            RemainingMistakes = Math.Min(MaxMistakes, RemainingMistakes + amount);
        }

        public void Refill()
        {
            RemainingMistakes = MaxMistakes;
        }

        public void RestoreState(int remainingMistakes)
        {
            RemainingMistakes = Math.Max(0, Math.Min(MaxMistakes, remainingMistakes));
        }
    }
}
