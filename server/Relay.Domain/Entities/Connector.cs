using Relay.Domain.Enums;

namespace Relay.Domain.Entities;

/// <summary>
/// Catalog entry describing an integration type (e.g. "slack", "http").
/// Global — shared across all workspaces. A <see cref="Connection"/> is a
/// configured installation of one of these.
/// </summary>
public class Connector
{
    public Guid Id { get; set; }

    /// <summary>Stable machine key, e.g. "slack". Unique in the catalog.</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }
    public required string Description { get; set; }
    public AuthKind AuthKind { get; set; }

    /// <summary>
    /// JSON Schema for the connector's current (latest) version. Mirrors the
    /// newest <see cref="ConnectorVersion"/> so single-schema callers keep working.
    /// </summary>
    public required string ConfigSchemaJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
    public ICollection<ConnectorVersion> Versions { get; set; } = new List<ConnectorVersion>();
}
