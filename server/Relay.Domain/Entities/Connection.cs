using Relay.Domain.Enums;

namespace Relay.Domain.Entities;

/// <summary>
/// An installed, configured instance of a <see cref="Connector"/> within a
/// workspace, holding its config and (opaque) stored credentials.
/// </summary>
public class Connection
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public Guid ConnectorId { get; set; }
    public Connector? Connector { get; set; }

    /// <summary>The connector schema version this connection was validated against.</summary>
    public Guid? ConnectorVersionId { get; set; }
    public ConnectorVersion? ConnectorVersion { get; set; }

    public required string Name { get; set; }

    /// <summary>Connector-specific configuration as a JSON object.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>Stored secret material as a JSON object. Never returned to clients.</summary>
    public string CredentialsJson { get; set; } = "{}";

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
