namespace Relay.Domain.Execution;

/// <summary>
/// Abstracts waiting between retry attempts so backoff is real in the app but
/// instant (and observable) in tests.
/// </summary>
public interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct = default);
}

/// <summary>Default no-wait delayer (used when none is injected, e.g. in unit tests).</summary>
public sealed class ImmediateDelayer : IDelayer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default) => Task.CompletedTask;
}
