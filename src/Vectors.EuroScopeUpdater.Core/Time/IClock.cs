namespace Vectors.EuroScopeUpdater.Core.Time;

/// <summary>Abstracts the current time so operations are deterministic and testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
