using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Flows;

/// <summary>List-view projection of a flow (no step bodies, just a count).</summary>
public sealed record FlowSummaryDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    bool IsEnabled,
    Guid TriggerConnectionId,
    string TriggerConnectionName,
    int StepCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static FlowSummaryDto From(Flow f) =>
        new(
            f.Id,
            f.WorkspaceId,
            f.Name,
            f.Description,
            f.IsEnabled,
            f.TriggerConnectionId,
            f.TriggerConnection?.Name ?? string.Empty,
            f.Steps.Count,
            f.CreatedAtUtc,
            f.UpdatedAtUtc);
}

/// <summary>Full flow with its ordered steps.</summary>
public sealed record FlowDetailDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    bool IsEnabled,
    Guid TriggerConnectionId,
    string TriggerConnectionName,
    IReadOnlyList<FlowStepDto> Steps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static FlowDetailDto From(Flow f) =>
        new(
            f.Id,
            f.WorkspaceId,
            f.Name,
            f.Description,
            f.IsEnabled,
            f.TriggerConnectionId,
            f.TriggerConnection?.Name ?? string.Empty,
            f.Steps.OrderBy(s => s.Order).Select(FlowStepDto.From).ToList(),
            f.CreatedAtUtc,
            f.UpdatedAtUtc);
}
