using System;

namespace Shared.Lives
{
    public sealed class LivesModel
    {
        public LivesModel(LivesConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            MaxLives = Math.Max(1, config.MaxLives);
            CurrentLives = Clamp(config.InitialLives, 0, MaxLives);
            RegenSeconds = Math.Max(0f, config.RegenSeconds);
            RegenRemainingSeconds = CurrentLives < MaxLives ? RegenSeconds : 0f;
        }

        public int CurrentLives { get; private set; }
        public int MaxLives { get; }
        public float RegenSeconds { get; }
        public float RegenRemainingSeconds { get; private set; }
        public bool IsOutOfLives => CurrentLives <= 0;
        public bool IsFull => CurrentLives >= MaxLives;

        public event EventHandler<LivesChangedEventArgs> LivesChanged = delegate { };
        public event EventHandler OutOfLives = delegate { };

        public bool TrySpendLife()
        {
            if (CurrentLives <= 0)
            {
                return false;
            }

            CurrentLives--;
            if (CurrentLives < MaxLives && RegenRemainingSeconds <= 0f)
            {
                RegenRemainingSeconds = RegenSeconds;
            }

            RaiseLivesChanged(LivesChangeReason.Spend);
            if (CurrentLives <= 0)
            {
                OutOfLives?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        public int RestoreLives(int amount, LivesChangeReason reason)
        {
            if (amount <= 0 || IsFull)
            {
                return 0;
            }

            int before = CurrentLives;
            CurrentLives = Clamp(CurrentLives + amount, 0, MaxLives);
            if (IsFull)
            {
                RegenRemainingSeconds = 0f;
            }

            int restored = CurrentLives - before;
            if (restored > 0)
            {
                RaiseLivesChanged(reason);
            }

            return restored;
        }

        public void Refill()
        {
            CurrentLives = MaxLives;
            RegenRemainingSeconds = 0f;
            RaiseLivesChanged(LivesChangeReason.Refill);
        }

        public void RestoreState(int currentLives, float regenRemainingSeconds)
        {
            CurrentLives = Clamp(currentLives, 0, MaxLives);
            RegenRemainingSeconds = Math.Max(0f, regenRemainingSeconds);
            if (IsFull)
            {
                RegenRemainingSeconds = 0f;
            }

            RaiseLivesChanged(LivesChangeReason.Restore);
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || IsFull || RegenSeconds <= 0f)
            {
                return;
            }

            RegenRemainingSeconds -= deltaSeconds;
            while (RegenRemainingSeconds <= 0f && !IsFull)
            {
                RestoreLives(1, LivesChangeReason.Regenerate);
                if (!IsFull)
                {
                    RegenRemainingSeconds += RegenSeconds;
                }
            }
        }

        private void RaiseLivesChanged(LivesChangeReason reason)
        {
            LivesChanged?.Invoke(this, new LivesChangedEventArgs(CurrentLives, MaxLives, reason));
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
