using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Domain.Security;
using Relay.Domain.Time;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>
/// Public inbound webhook entrypoint. A POST to a webhook's token triggers its
/// flow with the request body as the run payload. Unauthenticated by design —
/// the unguessable token is the credential, optionally reinforced by an HMAC
/// signature. Every attempt is recorded in the webhook's delivery log.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/hooks")]
public sealed class HooksController : ControllerBase
{
    // How far a signed request's timestamp may drift from now (replay window).
    private static readonly TimeSpan SignatureWindow = TimeSpan.FromMinutes(5);

    private readonly RelayDbContext _db;
    private readonly IFlowExecutor _executor;
    private readonly ISecretProtector _protector;
    private readonly IClock _clock;

    public HooksController(RelayDbContext db, IFlowExecutor executor, ISecretProtector protector, IClock clock)
    {
        _db = db;
        _executor = executor;
        _protector = protector;
        _clock = clock;
    }

    [HttpPost("{token}")]
    [EnableRateLimiting("triggers")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Trigger(string token, CancellationToken ct)
    {
        var webhook = await _db.Webhooks
            .Include(w => w.Flow)
            .FirstOrDefaultAsync(w => w.Token == token, ct);

        if (webhook is null || !webhook.IsEnabled)
            return Problem(title: "Webhook not found", detail: "No active webhook for that token.", statusCode: StatusCodes.Status404NotFound);

        // Read the body once — it is both the signed payload and the run input.
        var body = await ReadBodyAsync(ct);

        // Signature verification (when enabled): missing / bad / expired → 401.
        if (webhook.RequireSignature && !string.IsNullOrWhiteSpace(webhook.SigningSecret))
        {
            var timestamp = Request.Headers[WebhookHeaders.Timestamp].ToString();
            var signature = Request.Headers[WebhookHeaders.Signature].ToString();

            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature))
                return await Reject(webhook, WebhookDeliveryOutcome.MissingSignature, "Signature or timestamp header missing.", ct);

            if (!IsTimestampFresh(timestamp))
                return await Reject(webhook, WebhookDeliveryOutcome.TimestampExpired, "Timestamp outside the allowed window.", ct);

            var secret = _protector.Reveal(webhook.SigningSecret);
            if (!WebhookSignature.Verify(secret, timestamp, body ?? string.Empty, signature))
                return await Reject(webhook, WebhookDeliveryOutcome.InvalidSignature, "Signature did not match.", ct);
        }

        if (webhook.Flow is null || !webhook.Flow.IsEnabled)
        {
            await LogDelivery(webhook, false, WebhookDeliveryOutcome.FlowDisabled, null, "Flow is disabled.", ct);
            await _db.SaveChangesAsync(ct);
            return Problem(title: "Flow disabled", detail: "The flow for this webhook is disabled.", statusCode: StatusCodes.Status409Conflict);
        }

        // Idempotency: a duplicate delivery keyed the same reuses the original run.
        var idempotencyKey = ReadIdempotencyKey();
        if (idempotencyKey is not null)
        {
            var existing = await _db.Runs.AsNoTracking()
                .FirstOrDefaultAsync(r => r.FlowId == webhook.FlowId && r.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                await LogDelivery(webhook, true, WebhookDeliveryOutcome.Duplicate, existing.Id, "Deduplicated by idempotency key.", ct);
                await _db.SaveChangesAsync(ct);
                return Accepted(new { runId = existing.Id, status = existing.Status.ToString(), deduplicated = true });
            }
        }

        var run = await _executor.RunFlowAsync(
            webhook.FlowId, RunTrigger.Webhook, body, ct, idempotencyKey: idempotencyKey);

        webhook.LastTriggeredAtUtc = _clock.UtcNow;
        await LogDelivery(webhook, true, WebhookDeliveryOutcome.Delivered, run!.Id, null, ct);
        await _db.SaveChangesAsync(ct);

        return Accepted(new { runId = run.Id, status = run.Status.ToString(), deduplicated = false });
    }

    private async Task<IActionResult> Reject(Webhook webhook, WebhookDeliveryOutcome outcome, string detail, CancellationToken ct)
    {
        await LogDelivery(webhook, false, outcome, null, detail, ct);
        await _db.SaveChangesAsync(ct);
        return Problem(title: "Signature verification failed", detail: detail, statusCode: StatusCodes.Status401Unauthorized);
    }

    private Task LogDelivery(Webhook webhook, bool success, WebhookDeliveryOutcome outcome, Guid? runId, string? detail, CancellationToken ct)
    {
        _db.WebhookDeliveries.Add(new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            WorkspaceId = webhook.WorkspaceId,
            ReceivedAtUtc = _clock.UtcNow,
            Success = success,
            Outcome = outcome,
            RunId = runId,
            Detail = detail,
        });
        return Task.CompletedTask;
    }

    private bool IsTimestampFresh(string timestamp)
    {
        if (!long.TryParse(timestamp, out var unixSeconds)) return false;
        var sent = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var drift = (_clock.UtcNow - sent).Duration();
        return drift <= SignatureWindow;
    }

    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private string? ReadIdempotencyKey()
    {
        var value = Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
