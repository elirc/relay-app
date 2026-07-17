namespace Relay.Domain.Enums;

/// <summary>
/// A user's authority within their workspace. Ordered so a higher value
/// satisfies a lower requirement (Admin can do anything a Member can).
/// </summary>
public enum WorkspaceRole
{
    Member = 0,
    Admin = 1,
}
