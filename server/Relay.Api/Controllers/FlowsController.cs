using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>CRUD + enable/disable for flows (trigger + ordered action steps) in a workspace.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/flows")]
public sealed class FlowsController : ControllerBase
{
    private readonly RelayDbContext _db;

    public FlowsController(RelayDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<FlowSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<FlowSummaryDto>>> List(
        Guid workspaceId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);

        var result = await _db.Flows
            .AsNoTracking()
            .Include(f => f.TriggerConnection)
            .Include(f => f.Steps)
            .Where(f => f.WorkspaceId == workspaceId)
            .OrderBy(f => f.Name)
            .ToPagedResultAsync(pagination, FlowSummaryDto.From, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FlowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDetailDto>> Get(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var flow = await LoadFlow(workspaceId, id, tracking: false, ct);
        return flow is null ? FlowNotFound(id) : Ok(FlowDetailDto.From(flow));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(FlowDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDetailDto>> Create(
        Guid workspaceId,
        CreateFlowRequest request,
        CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);
        if (!await ValidateGraphAsync(workspaceId, request.TriggerConnectionId!.Value, request.Steps!, ct))
            return ValidationProblem(ModelState);

        var now = DateTimeOffset.UtcNow;
        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name!.Trim(),
            Description = request.Description?.Trim(),
            TriggerConnectionId = request.TriggerConnectionId!.Value,
            IsEnabled = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        flow.Steps = BuildSteps(request.Steps!, flow.Id);

        _db.Flows.Add(flow);
        await _db.SaveChangesAsync(ct);

        var created = await LoadFlow(workspaceId, flow.Id, tracking: false, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId, id = flow.Id }, FlowDetailDto.From(created!));
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(FlowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDetailDto>> Update(
        Guid workspaceId,
        Guid id,
        UpdateFlowRequest request,
        CancellationToken ct)
    {
        var flow = await _db.Flows.FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);
        if (flow is null) return FlowNotFound(id);
        if (!await ValidateGraphAsync(workspaceId, request.TriggerConnectionId!.Value, request.Steps!, ct))
            return ValidationProblem(ModelState);

        flow.Name = request.Name!.Trim();
        flow.Description = request.Description?.Trim();
        flow.TriggerConnectionId = request.TriggerConnectionId!.Value;
        flow.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Replace the ordered step list wholesale. Delete the old rows first (a
        // direct DELETE that bypasses the change tracker), then insert the new
        // ones — in one transaction — so the unique (FlowId, Order) index never
        // sees two rows share an order mid-save.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.FlowSteps.Where(s => s.FlowId == id).ExecuteDeleteAsync(ct);
        _db.FlowSteps.AddRange(BuildSteps(request.Steps!, id));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var updated = await LoadFlow(workspaceId, id, tracking: false, ct);
        return Ok(FlowDetailDto.From(updated!));
    }

    [HttpPost("{id:guid}/enable")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(FlowSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FlowSummaryDto>> Enable(Guid workspaceId, Guid id, CancellationToken ct) =>
        SetEnabled(workspaceId, id, true, ct);

    [HttpPost("{id:guid}/disable")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(FlowSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<FlowSummaryDto>> Disable(Guid workspaceId, Guid id, CancellationToken ct) =>
        SetEnabled(workspaceId, id, false, ct);

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var flow = await _db.Flows.FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);
        if (flow is null) return FlowNotFound(id);

        _db.Flows.Remove(flow);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult<FlowSummaryDto>> SetEnabled(Guid workspaceId, Guid id, bool enabled, CancellationToken ct)
    {
        var flow = await _db.Flows
            .Include(f => f.TriggerConnection)
            .Include(f => f.Steps)
            .FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);
        if (flow is null) return FlowNotFound(id);

        flow.IsEnabled = enabled;
        flow.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(FlowSummaryDto.From(flow));
    }

    /// <summary>Ensures the trigger and every step connection live in this workspace.</summary>
    private async Task<bool> ValidateGraphAsync(
        Guid workspaceId,
        Guid triggerConnectionId,
        List<FlowStepInput> steps,
        CancellationToken ct)
    {
        var validIds = (await _db.Connections
            .Where(c => c.WorkspaceId == workspaceId)
            .Select(c => c.Id)
            .ToListAsync(ct)).ToHashSet();

        if (!validIds.Contains(triggerConnectionId))
        {
            ModelState.AddModelError(nameof(CreateFlowRequest.TriggerConnectionId),
                "Trigger connection was not found in this workspace.");
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var connectionId = steps[i].ConnectionId;
            if (connectionId is null || !validIds.Contains(connectionId.Value))
            {
                ModelState.AddModelError($"Steps[{i}].ConnectionId",
                    "Step connection was not found in this workspace.");
            }
        }

        return ModelState.IsValid;
    }

    private static List<FlowStep> BuildSteps(List<FlowStepInput> inputs, Guid flowId) =>
        inputs.Select((s, index) => new FlowStep
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            Order = index,
            Name = s.Name!.Trim(),
            ConnectionId = s.ConnectionId!.Value,
            Action = s.Action!.Trim(),
            ConfigJson = string.IsNullOrWhiteSpace(s.ConfigJson) ? "{}" : s.ConfigJson,
            MaxAttempts = Math.Clamp(s.MaxAttempts ?? 3, 1, 10),
            BackoffSeconds = Math.Clamp(s.BackoffSeconds ?? 0, 0, 3600),
        }).ToList();

    private async Task<Flow?> LoadFlow(Guid workspaceId, Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<Flow> query = _db.Flows
            .Include(f => f.TriggerConnection)
            .Include(f => f.Steps).ThenInclude(s => s.Connection);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);
    }

    private Task<bool> WorkspaceExists(Guid workspaceId, CancellationToken ct) =>
        _db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct);

    private ObjectResult WorkspaceNotFound(Guid id) => Problem(
        title: "Workspace not found", detail: $"No workspace with id '{id}'.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult FlowNotFound(Guid id) => Problem(
        title: "Flow not found", detail: $"No flow with id '{id}'.", statusCode: StatusCodes.Status404NotFound);
}
