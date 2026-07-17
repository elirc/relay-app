using System.ComponentModel.DataAnnotations;

namespace Relay.Api.Contracts.Flows;

/// <summary>One action step in a create/update request. Order is derived from list position.</summary>
public sealed record FlowStepInput(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [Required]
    Guid? ConnectionId,

    [Required]
    [StringLength(100, MinimumLength = 1)]
    string? Action,

    [StringLength(8000)]
    string? ConfigJson);

public sealed record CreateFlowRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(2000)]
    string? Description,

    [Required]
    Guid? TriggerConnectionId,

    [Required]
    [MinLength(1, ErrorMessage = "A flow needs at least one action step.")]
    List<FlowStepInput>? Steps);

public sealed record UpdateFlowRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string? Name,

    [StringLength(2000)]
    string? Description,

    [Required]
    Guid? TriggerConnectionId,

    [Required]
    [MinLength(1, ErrorMessage = "A flow needs at least one action step.")]
    List<FlowStepInput>? Steps);
