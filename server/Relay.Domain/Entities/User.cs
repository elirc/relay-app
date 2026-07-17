namespace Relay.Domain.Entities;

/// <summary>A member of a workspace. Credentials are stored as a salted hash.</summary>
public class User
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public required string Email { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>PBKDF2/`HMACSHA256`-derived hash — never the raw password.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
