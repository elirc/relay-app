using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Webhooks;
using Relay.Domain.Entities;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Manage the inbound webhook endpoints that trigger a flow.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/flows/{flowId:guid}/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly RelayDbContext _db;

    public WebhooksController(RelayDbContext db) => _db = db;

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
    [ProducesResponseType(typeof(WebhookDto), StatusCodes.Status201Created)]
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

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct)
    {
        var webhook = await _db.Webhooks
            .FirstOrDefaultAsync(w => w.Id == id && w.FlowId == flowId && w.WorkspaceId == workspaceId, ct);
        if (webhook is null)
            return Problem(title: "Webhook not found", detail: $"No webhook with id '{id}'.", statusCode: StatusCodes.Status404NotFound);

        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private Task<bool> FlowExists(Guid workspaceId, Guid flowId, CancellationToken ct) =>
        _db.Flows.AnyAsync(f => f.Id == flowId && f.WorkspaceId == workspaceId, ct);

    private ObjectResult FlowNotFound(Guid id) => Problem(
        title: "Flow not found", detail: $"No flow with id '{id}'.", statusCode: StatusCodes.Status404NotFound);
}
