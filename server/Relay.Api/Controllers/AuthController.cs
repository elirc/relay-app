using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Auth;
using Relay.Api.Security;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Security;

namespace Relay.Api.Controllers;

/// <summary>Password login (issuing a workspace-scoped JWT) and current-user lookup.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RelayDbContext _db;
    private readonly IJwtTokenService _tokens;

    public AuthController(RelayDbContext db, IJwtTokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email!.Trim();

        // Email is unique per workspace, not globally, so match on the account
        // whose stored hash verifies. A constant reply avoids leaking which part
        // (email vs password) was wrong.
        var candidates = await _db.Users
            .Include(u => u.Workspace)
            .Where(u => u.Email == email)
            .ToListAsync(ct);

        var user = candidates.FirstOrDefault(u => PasswordHasher.Verify(request.Password!, u.PasswordHash));
        if (user is null)
        {
            return Problem(
                title: "Invalid credentials",
                detail: "Email or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var (token, expiresAt) = _tokens.CreateToken(user);
        return Ok(new LoginResponse(token, expiresAt, AuthUserDto.From(user)));
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Workspace)
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), ct);
        return user is null
            ? Problem(title: "Unknown user", statusCode: StatusCodes.Status401Unauthorized)
            : Ok(AuthUserDto.From(user));
    }
}
