using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Flows;

public sealed record FlowStepDto(
    Guid Id,
    int Order,
    string Name,
    Guid ConnectionId,
    string ConnectionName,
    string Action,
    string ConfigJson,
    int MaxAttempts,
    int BackoffSeconds)
{
    public static FlowStepDto From(FlowStep s) =>
        new(s.Id, s.Order, s.Name, s.ConnectionId, s.Connection?.Name ?? string.Empty, s.Action,
            s.ConfigJson, s.MaxAttempts, s.BackoffSeconds);
}
