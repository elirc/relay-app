using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Metrics;
using Relay.Domain.Metrics;
using Relay.Domain.Time;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Read-only run metrics for the workspace dashboard and per-flow views.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}")]
public sealed class MetricsController : ControllerBase
{
    private const int MaxDays = 90;

    private readonly RelayDbContext _db;
    private readonly IClock _clock;

    public MetricsController(RelayDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(WorkspaceMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkspaceMetricsDto>> Workspace(
        Guid workspaceId, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!await _db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct))
            return NotFoundProblem("Workspace", workspaceId);

        days = Math.Clamp(days, 1, MaxDays);
        var (from, since) = Range(days);

        var rows = await _db.Runs
            .AsNoTracking()
            .Where(r => r.Flow!.WorkspaceId == workspaceId && r.StartedAtUtc >= since)
            .Select(r => new
            {
                r.FlowId,
                FlowName = r.Flow!.Name,
                r.Status,
                r.StartedAtUtc,
                r.DurationMs,
            })
            .ToListAsync(ct);

        var all = rows.Select(r => new RunPoint(r.Status, r.StartedAtUtc, r.DurationMs)).ToList();

        var perFlow = rows
            .GroupBy(r => (r.FlowId, r.FlowName))
            .Select(g => new FlowMetricsRow(
                g.Key.FlowId,
                g.Key.FlowName,
                MetricsCalculator.Summarize(g.Select(r => new RunPoint(r.Status, r.StartedAtUtc, r.DurationMs)).ToList())))
            .OrderByDescending(f => f.Summary.TotalRuns)
            .ToList();

        return Ok(new WorkspaceMetricsDto(
            days,
            MetricsCalculator.Summarize(all),
            perFlow,
            MetricsCalculator.OverTime(all, from, days)));
    }

    [HttpGet("flows/{flowId:guid}/metrics")]
    [ProducesResponseType(typeof(FlowMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlowMetricsDto>> Flow(
        Guid workspaceId, Guid flowId, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var flow = await _db.Flows
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == flowId && f.WorkspaceId == workspaceId, ct);
        if (flow is null) return NotFoundProblem("Flow", flowId);

        days = Math.Clamp(days, 1, MaxDays);
        var (from, since) = Range(days);

        var runs = await _db.Runs
            .AsNoTracking()
            .Where(r => r.FlowId == flowId && r.StartedAtUtc >= since)
            .Select(r => new RunPoint(r.Status, r.StartedAtUtc, r.DurationMs))
            .ToListAsync(ct);

        return Ok(new FlowMetricsDto(
            flow.Id,
            flow.Name,
            days,
            MetricsCalculator.Summarize(runs),
            MetricsCalculator.OverTime(runs, from, days)));
    }

    /// <summary>The inclusive [from, today] window: `days` buckets ending today (UTC).</summary>
    private (DateOnly From, DateTimeOffset Since) Range(int days)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var from = today.AddDays(-(days - 1));
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (from, since);
    }

    private ObjectResult NotFoundProblem(string resource, Guid id) => Problem(
        title: $"{resource} not found", detail: $"No {resource.ToLowerInvariant()} with id '{id}'.",
        statusCode: StatusCodes.Status404NotFound);
}
