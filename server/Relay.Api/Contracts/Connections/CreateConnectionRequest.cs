using System.ComponentModel.DataAnnotations;

namespace Relay.Api.Contracts.Connections;

/// <summary>
/// Install a connector into a workspace. <c>ConnectorId</c> is nullable so a
/// missing id yields a 400, not a bind to Guid.Empty.
/// </summary>
public sealed record CreateConnectionRequest(
    [Required]
    Guid? ConnectorId,

    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(8000)]
    string? ConfigJson,

    [StringLength(8000)]
    string? CredentialsJson);
