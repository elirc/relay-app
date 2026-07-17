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
    Task<Run?> RunFlowAsync(
        Guid flowId,
        RunTrigger trigger,
        string? payloadJson,
        CancellationToken ct = default);
}
