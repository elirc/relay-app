namespace Relay.Domain.Entities;

/// <summary>
/// A predefined flow (trigger + ordered steps) described by connector keys, not
/// specific connections — instantiating it into a workspace maps those keys to
/// the workspace's installed connections.
/// </summary>
public class FlowTemplate
{
    public Guid Id { get; set; }

    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }

    /// <summary>Connector key the trigger connection must use (e.g. "http").</summary>
    public required string TriggerConnectorKey { get; set; }

    /// <summary>JSON array of steps: name, connectorKey, action, configJson, retry policy.</summary>
    public required string StepsJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
