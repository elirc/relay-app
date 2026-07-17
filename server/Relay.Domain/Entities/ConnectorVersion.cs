namespace Relay.Domain.Entities;

/// <summary>
/// A versioned config schema for a <see cref="Connector"/>. Connections are
/// validated against the specific version they were installed on, and a version
/// can be deprecated to steer new installs toward a newer one.
/// </summary>
public class ConnectorVersion
{
    public Guid Id { get; set; }

    public Guid ConnectorId { get; set; }
    public Connector? Connector { get; set; }

    /// <summary>Monotonic version number within the connector (1, 2, 3, …).</summary>
    public int Version { get; set; }

    /// <summary>JSON Schema a connection's config must satisfy for this version.</summary>
    public required string ConfigSchemaJson { get; set; }

    /// <summary>Deprecated versions still validate existing connections but reject new installs.</summary>
    public bool IsDeprecated { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
