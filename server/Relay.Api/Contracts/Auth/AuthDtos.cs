using System.ComponentModel.DataAnnotations;
using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Auth;

/// <summary>Login body. Fields are nullable so a missing value is a 400, not a bad bind.</summary>
public sealed record LoginRequest(
    [Required]
    [EmailAddress]
    string? Email,

    [Required]
    string? Password);

/// <summary>The authenticated principal (shared by login + <c>/me</c>).</summary>
public sealed record AuthUserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    WorkspaceRole Role,
    Guid WorkspaceId,
    string WorkspaceName,
    string WorkspaceSlug)
{
    public static AuthUserDto From(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.WorkspaceId,
        user.Workspace?.Name ?? string.Empty,
        user.Workspace?.Slug ?? string.Empty);
}

/// <summary>Successful login: a bearer token, its expiry, and the user profile.</summary>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAtUtc, AuthUserDto User);
