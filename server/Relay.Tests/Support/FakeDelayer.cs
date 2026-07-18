using Relay.Domain.Execution;

namespace Relay.Tests.Support;

/// <summary>Records requested backoff delays without waiting.</summary>
public sealed class FakeDelayer : IDelayer
{
    public List<TimeSpan> Delays { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
