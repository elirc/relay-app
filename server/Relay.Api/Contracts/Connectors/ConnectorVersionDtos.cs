using System.ComponentModel.DataAnnotations;
using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Connectors;

/// <summary>A single schema version of a connector.</summary>
public sealed record ConnectorVersionDto(
    Guid Id,
    Guid ConnectorId,
    int Version,
    string ConfigSchemaJson,
    bool IsDeprecated,
    DateTimeOffset CreatedAtUtc)
{
    public static ConnectorVersionDto From(ConnectorVersion v) =>
        new(v.Id, v.ConnectorId, v.Version, v.ConfigSchemaJson, v.IsDeprecated, v.CreatedAtUtc);
}

/// <summary>Publish a new schema version for a connector.</summary>
public sealed record CreateConnectorVersionRequest(
    [Required]
    [StringLength(8000, MinimumLength = 1)]
    string? ConfigSchemaJson);
