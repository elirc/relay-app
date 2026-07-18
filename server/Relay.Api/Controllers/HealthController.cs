using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Liveness + readiness probe (includes a database check) consumed by the client shell and CI.</summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly RelayDbContext _db;

    public HealthController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken ct)
    {
        var dbOk = await ProbeDatabaseAsync(ct);
        var body = new HealthResponse(
            dbOk ? "ok" : "degraded",
            "relay-api",
            typeof(HealthController).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            new HealthChecks(dbOk ? "ok" : "error"),
            DateTimeOffset.UtcNow);

        return dbOk ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }

    private async Task<bool> ProbeDatabaseAsync(CancellationToken ct)
    {
        try
        {
            return await _db.Database.CanConnectAsync(ct);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Per-dependency check results.</summary>
public sealed record HealthChecks(string Database);

/// <summary>Shape returned by <c>GET /health</c>.</summary>
public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    HealthChecks Checks,
    DateTimeOffset TimestampUtc);
