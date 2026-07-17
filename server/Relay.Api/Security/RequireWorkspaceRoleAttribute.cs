using Relay.Domain.Enums;

namespace Relay.Api.Security;

/// <summary>
/// Marks an action (or controller) as requiring at least the given workspace
/// role. Enforced by <see cref="WorkspaceAuthorizationFilter"/> so that a
/// foreign-workspace access (404) is reported before an insufficient role (403).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireWorkspaceRoleAttribute : Attribute
{
    public RequireWorkspaceRoleAttribute(WorkspaceRole role) => Role = role;

    public WorkspaceRole Role { get; }
}
