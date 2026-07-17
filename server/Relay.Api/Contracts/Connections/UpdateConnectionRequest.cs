using System.ComponentModel.DataAnnotations;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Connections;

/// <summary>
/// Update a connection. A null <c>CredentialsJson</c> leaves stored credentials
/// untouched; pass "{}" to clear them.
/// </summary>
public sealed record UpdateConnectionRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(8000)]
    string? ConfigJson,

    [StringLength(8000)]
    string? CredentialsJson,

    [Required]
    ConnectionStatus? Status);
