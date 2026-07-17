using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>
/// Public inbound webhook entrypoint. A POST to a webhook's token triggers its
/// flow with the request body as the run payload.
/// </summary>
[ApiController]
[Route("api/hooks")]
public sealed class HooksController : ControllerBase
{
    private readonly RelayDbContext _db;
    private readonly IFlowExecutor _executor;

    public HooksController(RelayDbContext db, IFlowExecutor executor)
    {
        _db = db;
        _executor = executor;
    }

    [HttpPost("{token}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Trigger(string token, CancellationToken ct)
    {
        var webhook = await _db.Webhooks
            .Include(w => w.Flow)
            .FirstOrDefaultAsync(w => w.Token == token, ct);

        if (webhook is null || !webhook.IsEnabled)
            return Problem(title: "Webhook not found", detail: "No active webhook for that token.", statusCode: StatusCodes.Status404NotFound);

        if (webhook.Flow is null || !webhook.Flow.IsEnabled)
        {
            return Problem(
                title: "Flow disabled",
                detail: "The flow for this webhook is disabled.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var payload = await ReadBodyAsync(ct);
        var run = await _executor.RunFlowAsync(webhook.FlowId, RunTrigger.Webhook, payload, ct);

        webhook.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Accepted(new { runId = run!.Id, status = run.Status.ToString() });
    }

    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}
