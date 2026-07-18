using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Webhooks;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Security;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Manage the inbound webhook endpoints that trigger a flow.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/flows/{flowId:guid}/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly RelayDbContext _db;
    private readonly ISecretProtector _protector;

    public WebhooksController(RelayDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WebhookDto>>> List(Guid workspaceId, Guid flowId, CancellationToken ct)
    {
        if (!await FlowExists(workspaceId, flowId, ct)) return FlowNotFound(flowId);

        var hooks = await _db.Webhooks
            .AsNoTracking()
            .Where(w => w.FlowId == flowId && w.WorkspaceId == workspaceId)
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(hooks.Select(w => WebhookDto.From(w, BaseUrl())).ToList());
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WebhookDto>> Create(Guid workspaceId, Guid flowId, CancellationToken ct)
    {
        if (!await FlowExists(workspaceId, flowId, ct)) return FlowNotFound(flowId);

        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            FlowId = flowId,
            Token = GenerateToken(),
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { workspaceId, flowId }, WebhookDto.From(webhook, BaseUrl()));
    }

    /// <summary>Generates (or rotates) the HMAC signing secret; returns it once and turns on verification.</summary>
    [HttpPost("{id:guid}/signing-secret")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(SigningSecretResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SigningSecretResponse>> GenerateSigningSecret(
        Guid workspaceId, Guid flowId, Guid id, CancellationToken ct)
    {
        var webhook = await LoadWebhook(workspaceId, flowId, id, ct);
        if (webhook is null) return WebhookNotFound(id);

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        webhook.SigningSecret = _protector.Protect(secret);
        webhook.RequireSignature = true;
        await _db.SaveChangesAsync(ct);

        // Shown once — callers must store it now.
        return Ok(new SigningSecretResponse(secret, WebhookHeaders.Timestamp, WebhookHeaders.Signature));
    }

    /// <summary>Removes the signing secret and turns off signature verification.</summary>
    [HttpDelete("{id:guid}/signing-secret")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSigningSecret(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct)
    {
        var webhook = await LoadWebhook(workspaceId, flowId, id, ct);
        if (webhook is null) return WebhookNotFound(id);

        webhook.SigningSecret = null;
        webhook.RequireSignature = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The delivery log for a webhook (newest first), classified by outcome.</summary>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType(typeof(PagedResult<WebhookDeliveryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<WebhookDeliveryDto>>> Deliveries(
        Guid workspaceId, Guid flowId, Guid id, [FromQuery] PaginationQuery pagination, CancellationToken ct)
    {
        if (await LoadWebhook(workspaceId, flowId, id, ct) is null) return WebhookNotFound(id);

        var result = await _db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.WebhookId == id)
            .OrderByDescending(d => d.ReceivedAtUtc)
            .ToPagedResultAsync(pagination, WebhookDeliveryDto.From, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct)
    {
        var webhook = await LoadWebhook(workspaceId, flowId, id, ct);
        if (webhook is null) return WebhookNotFound(id);

        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<Webhook?> LoadWebhook(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct) =>
        _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.FlowId == flowId && w.WorkspaceId == workspaceId, ct);

    private ObjectResult WebhookNotFound(Guid id) => Problem(
        title: "Webhook not found", detail: $"No webhook with id '{id}'.", statusCode: StatusCodes.Status404NotFound);

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private Task<bool> FlowExists(Guid workspaceId, Guid flowId, CancellationToken ct) =>
        _db.Flows.AnyAsync(f => f.Id == flowId && f.WorkspaceId == workspaceId, ct);

    private ObjectResult FlowNotFound(Guid id) => Problem(
        title: "Flow not found", detail: $"No flow with id '{id}'.", statusCode: StatusCodes.Status404NotFound);
}
