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

    /// <summary>Instantiates a template into a new disabled draft flow, mapping connector keys to workspace connections.</summary>
    [HttpPost("from-template/{templateId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(FlowDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowDetailDto>> FromTemplate(Guid workspaceId, Guid templateId, CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);

        var template = await _db.FlowTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return Problem(title: "Template not found", detail: $"No template with id '{templateId}'.", statusCode: StatusCodes.Status404NotFound);

        var dto = FlowTemplateDto.From(template);
        var connections = await WorkspaceConnections(workspaceId, ct);

        var issues = new List<string>();
        var trigger = ResolveByKey(connections, dto.TriggerConnectorKey);
        if (trigger is null) issues.Add($"No connection installed for connector '{dto.TriggerConnectorKey}'.");

        var now = DateTimeOffset.UtcNow;
        var steps = new List<FlowStep>();
        for (var i = 0; i < dto.Steps.Count; i++)
        {
            var s = dto.Steps[i];
            var conn = ResolveByKey(connections, s.ConnectorKey);
            if (conn is null) { issues.Add($"No connection installed for connector '{s.ConnectorKey}'."); continue; }
            steps.Add(new FlowStep
            {
                Id = Guid.NewGuid(), Order = i, Name = s.Name, ConnectionId = conn.Id, Action = s.Action,
                ConfigJson = string.IsNullOrWhiteSpace(s.ConfigJson) ? "{}" : s.ConfigJson,
                MaxAttempts = Math.Clamp(s.MaxAttempts, 1, 10), BackoffSeconds = Math.Clamp(s.BackoffSeconds, 0, 3600),
            });
        }

        if (issues.Count > 0)
            return Problem(title: "Cannot instantiate template", detail: string.Join(" ", issues), statusCode: StatusCodes.Status400BadRequest);

        var flow = new Flow
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = dto.Name, Description = dto.Description,
            TriggerConnectionId = trigger!.Id, IsEnabled = false, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        foreach (var s in steps) s.FlowId = flow.Id;
        flow.Steps = steps;

        _db.Flows.Add(flow);
        await _db.SaveChangesAsync(ct);

        var created = await LoadFlow(workspaceId, flow.Id, tracking: false, ct);
        return CreatedAtAction(nameof(Get), new { workspaceId, id = flow.Id }, FlowDetailDto.From(created!));
    }

    /// <summary>Exports a flow as a portable JSON document (references by connector key + connection name).</summary>
    [HttpGet("{id:guid}/export")]
    [ProducesResponseType(typeof(FlowExportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowExportDto>> Export(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var flow = await _db.Flows
            .AsNoTracking()
            .Include(f => f.TriggerConnection).ThenInclude(c => c!.Connector)
            .Include(f => f.Steps).ThenInclude(s => s.Connection).ThenInclude(c => c!.Connector)
            .FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);
        if (flow is null) return FlowNotFound(id);

        var export = new FlowExportDto(
            flow.ExternalId ?? flow.Id.ToString(),
            flow.Name,
            flow.Description,
            new PortableTrigger(
                flow.TriggerConnection?.Connector?.Key ?? string.Empty,
                flow.TriggerConnection?.Name ?? string.Empty),
            flow.Steps.OrderBy(s => s.Order).Select(s => new PortableStep(
                s.Name,
                s.Connection?.Connector?.Key ?? string.Empty,
                s.Connection?.Name ?? string.Empty,
                s.Action, s.ConfigJson, s.MaxAttempts, s.BackoffSeconds)).ToList());
        return Ok(export);
    }

    /// <summary>Imports a portable flow. Idempotent by external id; ?dryRun=true validates without persisting.</summary>
    [HttpPost("import")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ImportResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImportResultDto>> Import(
        Guid workspaceId, [FromQuery] bool dryRun, FlowExportDto document, CancellationToken ct)
    {
        if (!await WorkspaceExists(workspaceId, ct)) return WorkspaceNotFound(workspaceId);

        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(document.Name)) issues.Add("Flow name is required.");
        if (document.Steps.Count == 0) issues.Add("A flow needs at least one step.");

        var connections = await WorkspaceConnections(workspaceId, ct);
        var trigger = Resolve(connections, document.Trigger.ConnectorKey, document.Trigger.ConnectionName);
        if (trigger is null) issues.Add($"No connection for trigger connector '{document.Trigger.ConnectorKey}'.");

        var resolvedSteps = new List<(PortableStep Step, Connection Conn)>();
        foreach (var s in document.Steps)
        {
            var conn = Resolve(connections, s.ConnectorKey, s.ConnectionName);
            if (conn is null) issues.Add($"No connection for step connector '{s.ConnectorKey}'.");
            else resolvedSteps.Add((s, conn));
        }

        Flow? existing = string.IsNullOrWhiteSpace(document.ExternalId)
            ? null
            : await _db.Flows.FirstOrDefaultAsync(f => f.WorkspaceId == workspaceId && f.ExternalId == document.ExternalId, ct);
        var action = existing is null ? "create" : "update";
        var valid = issues.Count == 0;

        if (dryRun)
            return Ok(new ImportResultDto(valid, valid ? action : "invalid", existing?.Id, issues));
        if (!valid)
            return BadRequest(new ImportResultDto(false, "invalid", existing?.Id, issues));

        var now = DateTimeOffset.UtcNow;
        List<FlowStep> BuildImportSteps(Guid flowId) =>
            resolvedSteps.Select((r, i) => new FlowStep
            {
                Id = Guid.NewGuid(), FlowId = flowId, Order = i, Name = r.Step.Name, ConnectionId = r.Conn.Id,
                Action = r.Step.Action, ConfigJson = string.IsNullOrWhiteSpace(r.Step.ConfigJson) ? "{}" : r.Step.ConfigJson,
                MaxAttempts = Math.Clamp(r.Step.MaxAttempts, 1, 10), BackoffSeconds = Math.Clamp(r.Step.BackoffSeconds, 0, 3600),
            }).ToList();

        Guid flowId;
        if (existing is null)
        {
            var flow = new Flow
            {
                Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = document.Name, Description = document.Description,
                ExternalId = string.IsNullOrWhiteSpace(document.ExternalId) ? null : document.ExternalId,
                TriggerConnectionId = trigger!.Id, IsEnabled = false, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            flow.Steps = BuildImportSteps(flow.Id);
            _db.Flows.Add(flow);
            await _db.SaveChangesAsync(ct);
            flowId = flow.Id;
        }
        else
        {
            existing.Name = document.Name;
            existing.Description = document.Description;
            existing.TriggerConnectionId = trigger!.Id;
            existing.UpdatedAtUtc = now;
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.FlowSteps.Where(s => s.FlowId == existing.Id).ExecuteDeleteAsync(ct);
            _db.FlowSteps.AddRange(BuildImportSteps(existing.Id));
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            flowId = existing.Id;
        }

        return Ok(new ImportResultDto(true, action, flowId, []));
    }

    private Task<List<Connection>> WorkspaceConnections(Guid workspaceId, CancellationToken ct) =>
        _db.Connections.Include(c => c.Connector).Where(c => c.WorkspaceId == workspaceId).ToListAsync(ct);

    private static Connection? ResolveByKey(List<Connection> connections, string connectorKey) =>
        connections.FirstOrDefault(c => c.Connector?.Key == connectorKey);

    private static Connection? Resolve(List<Connection> connections, string connectorKey, string connectionName) =>
        connections.FirstOrDefault(c => c.Connector?.Key == connectorKey && c.Name == connectionName)
        ?? connections.FirstOrDefault(c => c.Connector?.Key == connectorKey);

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
