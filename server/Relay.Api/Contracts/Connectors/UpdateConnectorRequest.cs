using System.ComponentModel.DataAnnotations;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Connectors;

/// <summary>Body for updating a catalog connector. The immutable Key is not editable.</summary>
public sealed record UpdateConnectorRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(2000)]
    string? Description,

    [Required]
    AuthKind? AuthKind,

    [StringLength(8000)]
    string? ConfigSchemaJson);
