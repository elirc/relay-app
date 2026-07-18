namespace Relay.Domain.Entities;

/// <summary>
/// A cron-style schedule that triggers a flow. The in-process scheduler runs a
/// schedule when <see cref="NextRunAtUtc"/> is due, then advances it.
/// </summary>
public class Schedule
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public Guid FlowId { get; set; }
    public Flow? Flow { get; set; }

    /// <summary>Standard 5-field cron expression (minute hour day-of-month month day-of-week).</summary>
    public required string CronExpression { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>When the schedule next fires (null once exhausted or while disabled).</summary>
    public DateTimeOffset? NextRunAtUtc { get; set; }
    public DateTimeOffset? LastRunAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
