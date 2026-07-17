using System.ComponentModel.DataAnnotations;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Connectors;

/// <summary>
/// Body for creating a catalog connector. Attributes sit on the constructor
/// parameters (not <c>[property:]</c>) — for positional records .NET validates
/// the parameters, and value-type fields are nullable so a missing value fails
/// validation (400) instead of silently binding to a default.
/// </summary>
public sealed record CreateConnectorRequest(
    [Required]
    [RegularExpression("^[a-z0-9_-]{2,50}$",
        ErrorMessage = "Key must be 2-50 chars of lowercase letters, digits, '-' or '_'.")]
    string? Key,

    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(2000)]
    string? Description,

    [Required]
    AuthKind? AuthKind,

    [StringLength(8000)]
    string? ConfigSchemaJson);
