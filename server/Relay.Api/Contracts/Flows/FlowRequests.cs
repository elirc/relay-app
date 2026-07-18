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
    string? ConfigJson,

    /// <summary>Retry policy: max attempts (1-10, default 3).</summary>
    [Range(1, 10)]
    int? MaxAttempts = null,

    /// <summary>Retry policy: fixed backoff seconds between attempts (0-3600, default 0).</summary>
    [Range(0, 3600)]
    int? BackoffSeconds = null);

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
    List<FlowStepInput>? Steps,

    /// <summary>Optimistic-concurrency guard: the token the client last read. A mismatch → 409.</summary>
    Guid? ExpectedConcurrencyToken = null);
