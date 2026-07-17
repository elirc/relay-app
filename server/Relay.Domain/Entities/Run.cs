using Relay.Domain.Enums;

namespace Relay.Domain.Entities;

/// <summary>One execution of a flow, with per-step logs and retry bookkeeping.</summary>
public class Run
{
    public Guid Id { get; set; }

    public Guid FlowId { get; set; }
    public Flow? Flow { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Pending;
    public RunTrigger Trigger { get; set; } = RunTrigger.Manual;

    /// <summary>Optional JSON payload that started the run (e.g. webhook body).</summary>
    public string? TriggerPayloadJson { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long DurationMs { get; set; }

    public int RetryCount { get; set; }
    public string? Error { get; set; }

    public ICollection<RunStepLog> StepLogs { get; set; } = new List<RunStepLog>();
}
