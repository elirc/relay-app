using System.Security.Claims;
using Relay.Domain.Enums;

namespace Relay.Api.Security;

/// <summary>Custom claim type names carried in the issued JWT.</summary>
public static class RelayClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string WorkspaceId = "workspace_id";
    public const string Role = "role";
}

/// <summary>Typed accessors for the relay claims on an authenticated principal.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(RelayClaims.Subject), out var id) ? id : Guid.Empty;

    public static Guid GetWorkspaceId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(RelayClaims.WorkspaceId), out var id) ? id : Guid.Empty;

    public static WorkspaceRole GetRole(this ClaimsPrincipal user) =>
        Enum.TryParse<WorkspaceRole>(user.FindFirstValue(RelayClaims.Role), out var role)
            ? role
            : WorkspaceRole.Member;

    public static string? GetEmail(this ClaimsPrincipal user) => user.FindFirstValue(RelayClaims.Email);

    public static string? GetDisplayName(this ClaimsPrincipal user) => user.FindFirstValue(RelayClaims.Name);
}
