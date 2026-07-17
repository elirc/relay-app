using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Connectors;

public sealed record ConnectorDto(
    Guid Id,
    string Key,
    string Name,
    string Description,
    AuthKind AuthKind,
    string ConfigSchemaJson,
    int LatestVersion,
    bool IsLatestDeprecated,
    DateTimeOffset CreatedAtUtc)
{
    public static ConnectorDto From(Connector c)
    {
        var latest = c.Versions.Count > 0
            ? c.Versions.OrderByDescending(v => v.Version).First()
            : null;
        return new(
            c.Id, c.Key, c.Name, c.Description, c.AuthKind, c.ConfigSchemaJson,
            latest?.Version ?? 1,
            latest?.IsDeprecated ?? false,
            c.CreatedAtUtc);
    }
}
