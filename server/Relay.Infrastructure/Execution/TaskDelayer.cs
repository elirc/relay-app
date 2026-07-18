using Relay.Domain.Execution;

namespace Relay.Infrastructure.Execution;

/// <summary>Real backoff waits via <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class TaskDelayer : IDelayer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default) => Task.Delay(delay, ct);
}
