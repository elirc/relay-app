using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Relay.Api.Security;

/// <summary>
/// Enforces workspace tenancy and role authorization for authenticated requests:
/// <list type="bullet">
/// <item>a route <c>workspaceId</c> that differs from the caller's workspace → 404
/// (a foreign resource must look absent, not merely forbidden);</item>
/// <item>an action carrying <see cref="RequireWorkspaceRoleAttribute"/> that the
/// caller's role does not satisfy → 403.</item>
/// </list>
/// Authentication itself (401) is handled earlier by the authorization middleware
/// via the fallback policy; anonymous endpoints are skipped here.
/// </summary>
public sealed class WorkspaceAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint is null) return;
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) return;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return; // 401 already handled upstream

        // Tenancy: the route's workspace must match the caller's workspace.
        if (context.RouteData.Values.TryGetValue("workspaceId", out var raw)
            && Guid.TryParse(raw?.ToString(), out var routeWorkspaceId)
            && routeWorkspaceId != user.GetWorkspaceId())
        {
            context.Result = Problem(
                context, StatusCodes.Status404NotFound,
                "Workspace not found", $"No workspace with id '{routeWorkspaceId}'.");
            return;
        }

        // Role: enforce any RequireWorkspaceRole requirement (Admin > Member).
        var requirement = endpoint.Metadata.GetMetadata<RequireWorkspaceRoleAttribute>();
        if (requirement is not null && user.GetRole() < requirement.Role)
        {
            context.Result = Problem(
                context, StatusCodes.Status403Forbidden,
                "Insufficient role", $"This action requires the '{requirement.Role}' role.");
        }
    }

    private static ObjectResult Problem(AuthorizationFilterContext context, int status, string title, string detail) =>
        new(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
}
