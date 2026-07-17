using Relay.Domain.Enums;

namespace Relay.Domain.Entities;

/// <summary>Execution record for a single step within a run.</summary>
public class RunStepLog
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }
    public Run? Run { get; set; }

    /// <summary>The flow step this log corresponds to (null for the trigger).</summary>
    public Guid? FlowStepId { get; set; }
    public FlowStep? FlowStep { get; set; }

    /// <summary>Ordinal used to render the timeline (trigger = 0).</summary>
    public int StepOrder { get; set; }

    public required string Name { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;

    /// <summary>Human-readable log line(s) produced by the step.</summary>
    public string? Message { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long DurationMs { get; set; }
}
