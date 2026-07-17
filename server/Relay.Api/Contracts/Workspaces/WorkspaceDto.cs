using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Workspaces;

public sealed record WorkspaceDto(Guid Id, string Name, string Slug, DateTimeOffset CreatedAtUtc)
{
    public static WorkspaceDto From(Workspace w) => new(w.Id, w.Name, w.Slug, w.CreatedAtUtc);
}
