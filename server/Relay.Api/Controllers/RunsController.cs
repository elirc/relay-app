using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Runs;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Manual flow runs plus run history and retry within a workspace.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}")]
public sealed class RunsController : ControllerBase
{
    private readonly RelayDbContext _db;
    private readonly IFlowExecutor _executor;

    public RunsController(RelayDbContext db, IFlowExecutor executor)
    {
        _db = db;
        _executor = executor;
    }

    [HttpPost("flows/{flowId:guid}/run")]
    [EnableRateLimiting("triggers")]
    [ProducesResponseType(typeof(RunDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RunDetailDto>> RunFlow(
        Guid workspaceId,
        Guid flowId,
        TriggerRunRequest? request,
        CancellationToken ct)
    {
        var flowExists = await _db.Flows.AnyAsync(f => f.Id == flowId && f.WorkspaceId == workspaceId, ct);
        if (!flowExists) return NotFoundProblem("Flow", flowId);

        var run = await _executor.RunFlowAsync(flowId, RunTrigger.Manual, request?.PayloadJson, ct);
        var detail = await LoadDetail(run!.Id, workspaceId, ct);
        return CreatedAtAction(nameof(GetRun), new { workspaceId, runId = run.Id }, detail);
    }

    [HttpGet("runs")]
    [ProducesResponseType(typeof(PagedResult<RunSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<RunSummaryDto>>> ListRuns(
        Guid workspaceId,
        [FromQuery] PaginationQuery pagination,
        [FromQuery] RunStatus? status,
        CancellationToken ct)
    {
        if (!await _db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct))
            return NotFoundProblem("Workspace", workspaceId);

        var query = _db.Runs
            .AsNoTracking()
            .Include(r => r.Flow)
            .Where(r => r.Flow!.WorkspaceId == workspaceId);
        if (status is not null) query = query.Where(r => r.Status == status);

        var result = await query
            .OrderByDescending(r => r.StartedAtUtc)
            .ToPagedResultAsync(pagination, RunSummaryDto.From, ct);
        return Ok(result);
    }

    /// <summary>The dead-letter list: failed runs for the workspace, newest first.</summary>
    [HttpGet("dead-letter")]
    [ProducesResponseType(typeof(PagedResult<RunSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<RunSummaryDto>>> DeadLetter(
        Guid workspaceId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (!await _db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct))
            return NotFoundProblem("Workspace", workspaceId);

        var result = await _db.Runs
            .AsNoTracking()
            .Include(r => r.Flow)
            .Where(r => r.Flow!.WorkspaceId == workspaceId && r.Status == RunStatus.Failed)
            .OrderByDescending(r => r.StartedAtUtc)
            .ToPagedResultAsync(pagination, RunSummaryDto.From, ct);
        return Ok(result);
    }

    [HttpGet("runs/{runId:guid}")]
    [ProducesResponseType(typeof(RunDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunDetailDto>> GetRun(Guid workspaceId, Guid runId, CancellationToken ct)
    {
        var detail = await LoadDetail(runId, workspaceId, ct);
        return detail is null ? NotFoundProblem("Run", runId) : Ok(detail);
    }

    [HttpPost("runs/{runId:guid}/retry")]
    [ProducesResponseType(typeof(RunDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunDetailDto>> Retry(Guid workspaceId, Guid runId, CancellationToken ct)
    {
        var original = await _db.Runs
            .AsNoTracking()
            .Include(r => r.Flow)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Flow!.WorkspaceId == workspaceId, ct);
        if (original is null) return NotFoundProblem("Run", runId);

        var run = await _executor.RunFlowAsync(original.FlowId, RunTrigger.Manual, original.TriggerPayloadJson, ct);
        var detail = await LoadDetail(run!.Id, workspaceId, ct);
        return CreatedAtAction(nameof(GetRun), new { workspaceId, runId = run.Id }, detail);
    }

    /// <summary>Replays a run, skipping the action steps before <c>FromStepOrder</c>.</summary>
    [HttpPost("runs/{runId:guid}/replay")]
    [ProducesResponseType(typeof(RunDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunDetailDto>> Replay(
        Guid workspaceId, Guid runId, ReplayRunRequest? request, CancellationToken ct)
    {
        var original = await _db.Runs
            .AsNoTracking()
            .Include(r => r.Flow)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Flow!.WorkspaceId == workspaceId, ct);
        if (original is null) return NotFoundProblem("Run", runId);

        var fromStep = Math.Max(0, request?.FromStepOrder ?? 0);
        var run = await _executor.RunFlowAsync(
            original.FlowId, RunTrigger.Manual, original.TriggerPayloadJson, ct, fromStepOrder: fromStep);
        var detail = await LoadDetail(run!.Id, workspaceId, ct);
        return CreatedAtAction(nameof(GetRun), new { workspaceId, runId = run.Id }, detail);
    }

    private async Task<RunDetailDto?> LoadDetail(Guid runId, Guid workspaceId, CancellationToken ct)
    {
        var run = await _db.Runs
            .AsNoTracking()
            .Include(r => r.Flow)
            .Include(r => r.StepLogs)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Flow!.WorkspaceId == workspaceId, ct);
        return run is null ? null : RunDetailDto.From(run);
    }

    private ObjectResult NotFoundProblem(string resource, Guid id) => Problem(
        title: $"{resource} not found", detail: $"No {resource.ToLowerInvariant()} with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);
}
