using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Relay.Domain.Entities;

namespace Relay.Api.Security;

/// <summary>Issues signed JWTs carrying a user's identity, workspace, and role.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed bearer token for the given user.</summary>
    (string Token, DateTimeOffset ExpiresAtUtc) CreateToken(User user);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(JwtSettings settings) => _settings = settings;

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_settings.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(RelayClaims.Subject, user.Id.ToString()),
            new(RelayClaims.Email, user.Email),
            new(RelayClaims.Name, user.DisplayName),
            new(RelayClaims.WorkspaceId, user.WorkspaceId.ToString()),
            new(RelayClaims.Role, user.Role.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
