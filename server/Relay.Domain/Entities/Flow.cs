namespace Relay.Domain.Entities;

/// <summary>
/// An automation: one trigger connection plus an ordered list of action
/// <see cref="FlowStep"/>s. Disabled flows do not execute.
/// </summary>
public class Flow
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>The connection whose events start this flow.</summary>
    public Guid TriggerConnectionId { get; set; }
    public Connection? TriggerConnection { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<FlowStep> Steps { get; set; } = new List<FlowStep>();
    public ICollection<Run> Runs { get; set; } = new List<Run>();
    public ICollection<Webhook> Webhooks { get; set; } = new List<Webhook>();
    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
}
