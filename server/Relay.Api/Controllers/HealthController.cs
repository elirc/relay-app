using Microsoft.AspNetCore.Mvc;

namespace Relay.Api.Controllers;

/// <summary>Liveness probe consumed by the client shell and CI smoke tests.</summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() =>
        Ok(new HealthResponse("ok", "relay-api", DateTimeOffset.UtcNow));
}

/// <summary>Shape returned by <c>GET /health</c>.</summary>
public sealed record HealthResponse(string Status, string Service, DateTimeOffset TimestampUtc);
