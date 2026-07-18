using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Domain.Execution;

/// <summary>Runs a flow end to end, persisting a <see cref="Run"/> with per-step logs.</summary>
public interface IFlowExecutor
{
    /// <summary>
    /// Executes the flow identified by <paramref name="flowId"/>. Returns the
    /// completed run, or null if no such flow exists.
    /// </summary>
    /// <param name="fromStepOrder">
    /// Zero-based action-step order to start from; earlier steps are logged as
    /// skipped (used for dead-letter replay). 0 runs the whole flow.
    /// </param>
    /// <param name="idempotencyKey">
    /// When set, recorded on the run so duplicate deliveries can be de-duplicated
    /// by the caller.
    /// </param>
    Task<Run?> RunFlowAsync(
        Guid flowId,
        RunTrigger trigger,
        string? payloadJson,
        CancellationToken ct = default,
        int fromStepOrder = 0,
        string? idempotencyKey = null);
}
