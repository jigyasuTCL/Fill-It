using System;

// Swap SystemTimeProvider for a server-synced one later (e.g. Firebase
// server timestamp) to stop players cheating rewards via device clock.
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}