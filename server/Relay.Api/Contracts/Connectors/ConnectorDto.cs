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
    DateTimeOffset CreatedAtUtc)
{
    public static ConnectorDto From(Connector c) =>
        new(c.Id, c.Key, c.Name, c.Description, c.AuthKind, c.ConfigSchemaJson, c.CreatedAtUtc);
}
