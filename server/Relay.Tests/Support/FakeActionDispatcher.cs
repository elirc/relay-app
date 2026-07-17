using Relay.Domain.Execution;

namespace Relay.Tests.Support;

/// <summary>In-memory dispatcher fake — no external calls. Configure per-request behavior.</summary>
public sealed class FakeActionDispatcher : IActionDispatcher
{
    public Func<StepExecutionRequest, StepExecutionResult> Handler { get; set; } =
        _ => StepExecutionResult.Ok("ok");

    public int Calls { get; private set; }

    public Task<StepExecutionResult> DispatchAsync(StepExecutionRequest request, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Handler(request));
    }
}
