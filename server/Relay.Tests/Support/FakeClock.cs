using Relay.Domain.Time;

namespace Relay.Tests.Support;

/// <summary>A controllable clock for deterministic scheduling tests.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
