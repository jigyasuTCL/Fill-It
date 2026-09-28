using System;

namespace Shared.Lives
{
    public sealed class LivesChangedEventArgs : EventArgs
    {
        public LivesChangedEventArgs(int currentLives, int maxLives, LivesChangeReason reason)
        {
            CurrentLives = currentLives;
            MaxLives = maxLives;
            Reason = reason;
        }

        public int CurrentLives { get; }
        public int MaxLives { get; }
        public LivesChangeReason Reason { get; }
    }
}
