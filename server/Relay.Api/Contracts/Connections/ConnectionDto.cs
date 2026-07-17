using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Connections;

/// <summary>
/// Connection projection safe to return to clients — the stored
/// <c>CredentialsJson</c> is never included, only whether credentials are set.
/// </summary>
public sealed record ConnectionDto(
    Guid Id,
    Guid WorkspaceId,
    Guid ConnectorId,
    string ConnectorKey,
    string ConnectorName,
    string Name,
    string ConfigJson,
    bool HasCredentials,
    ConnectionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ConnectionDto From(Connection c) =>
        new(
            c.Id,
            c.WorkspaceId,
            c.ConnectorId,
            c.Connector?.Key ?? string.Empty,
            c.Connector?.Name ?? string.Empty,
            c.Name,
            c.ConfigJson,
            !string.IsNullOrWhiteSpace(c.CredentialsJson) && c.CredentialsJson != "{}",
            c.Status,
            c.CreatedAtUtc,
            c.UpdatedAtUtc);
}
