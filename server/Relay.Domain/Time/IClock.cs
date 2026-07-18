namespace Relay.Domain.Time;

/// <summary>
/// Abstracts "now" so scheduling is deterministic in tests. The app supplies a
/// system clock; tests supply a fake one they can advance.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
