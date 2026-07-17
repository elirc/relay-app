namespace Relay.Domain.Entities;

/// <summary>One action in a flow, executed in ascending <see cref="Order"/>.</summary>
public class FlowStep
{
    public Guid Id { get; set; }

    public Guid FlowId { get; set; }
    public Flow? Flow { get; set; }

    /// <summary>Zero-based position within the flow.</summary>
    public int Order { get; set; }

    public required string Name { get; set; }

    /// <summary>The connection this action runs through.</summary>
    public Guid ConnectionId { get; set; }
    public Connection? Connection { get; set; }

    /// <summary>Action key understood by the connector, e.g. "send_message".</summary>
    public required string Action { get; set; }

    /// <summary>Per-step configuration as a JSON object.</summary>
    public string ConfigJson { get; set; } = "{}";
}
