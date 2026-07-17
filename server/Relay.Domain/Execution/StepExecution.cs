namespace Relay.Domain.Execution;

/// <summary>Everything a step needs to run, with no dependency on EF or HTTP.</summary>
public sealed record StepExecutionRequest(
    string ConnectorKey,
    string Action,
    string StepConfigJson,
    string ConnectionConfigJson,
    string? PayloadJson);

/// <summary>Outcome of executing a single step.</summary>
public sealed record StepExecutionResult(bool Success, string? Output, string? Error)
{
    public static StepExecutionResult Ok(string? output = null) => new(true, output, null);
    public static StepExecutionResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Port the flow executor drives to run a step. The app supplies an in-process
/// simulated adapter; tests supply fakes — no real external calls in CI.
/// </summary>
public interface IActionDispatcher
{
    Task<StepExecutionResult> DispatchAsync(StepExecutionRequest request, CancellationToken ct = default);
}
