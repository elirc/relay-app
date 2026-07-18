using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Schedules;
using Relay.Api.Security;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Scheduling;
using Relay.Domain.Time;
using Relay.Infrastructure.Persistence;

namespace Relay.Api.Controllers;

/// <summary>Cron-style schedules that trigger a flow, plus a next-runs preview.</summary>
[ApiController]
[Route("api/workspaces/{workspaceId:guid}/flows/{flowId:guid}/schedules")]
public sealed class SchedulesController : ControllerBase
{
    private const int MaxPreview = 10;

    private readonly RelayDbContext _db;
    private readonly IClock _clock;

    public SchedulesController(RelayDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ScheduleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> List(Guid workspaceId, Guid flowId, CancellationToken ct)
    {
        if (!await FlowExists(workspaceId, flowId, ct)) return FlowNotFound(flowId);

        var schedules = await _db.Schedules
            .AsNoTracking()
            .Where(s => s.FlowId == flowId && s.WorkspaceId == workspaceId)
            .OrderBy(s => s.CreatedAtUtc)
            .Select(s => ScheduleDto.From(s))
            .ToListAsync(ct);
        return Ok(schedules);
    }

    /// <summary>Validates a cron expression and previews its next fire times.</summary>
    [HttpGet("preview")]
    [ProducesResponseType(typeof(SchedulePreviewResponse), StatusCodes.Status200OK)]
    public ActionResult<SchedulePreviewResponse> Preview([FromQuery] string? cron, [FromQuery] int count = 5)
    {
        if (!CronExpression.TryParse(cron, out var parsed))
        {
            return Ok(new SchedulePreviewResponse(false, "Invalid cron expression.", Array.Empty<DateTimeOffset>()));
        }

        var take = Math.Clamp(count, 1, MaxPreview);
        var runs = parsed!.GetNextOccurrences(_clock.UtcNow, take);
        return Ok(new SchedulePreviewResponse(true, null, runs));
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleDto>> Create(
        Guid workspaceId, Guid flowId, ScheduleRequest request, CancellationToken ct)
    {
        if (!await FlowExists(workspaceId, flowId, ct)) return FlowNotFound(flowId);
        if (!CronExpression.TryParse(request.CronExpression, out var cron))
        {
            ModelState.AddModelError(nameof(request.CronExpression), "Invalid cron expression.");
            return ValidationProblem(ModelState);
        }

        var now = _clock.UtcNow;
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            FlowId = flowId,
            CronExpression = request.CronExpression!.Trim(),
            IsEnabled = true,
            NextRunAtUtc = cron!.GetNextOccurrence(now),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { workspaceId, flowId }, ScheduleDto.From(schedule));
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleDto>> Update(
        Guid workspaceId, Guid flowId, Guid id, ScheduleRequest request, CancellationToken ct)
    {
        var schedule = await Load(workspaceId, flowId, id, ct);
        if (schedule is null) return ScheduleNotFound(id);
        if (!CronExpression.TryParse(request.CronExpression, out var cron))
        {
            ModelState.AddModelError(nameof(request.CronExpression), "Invalid cron expression.");
            return ValidationProblem(ModelState);
        }

        var now = _clock.UtcNow;
        schedule.CronExpression = request.CronExpression!.Trim();
        schedule.UpdatedAtUtc = now;
        schedule.NextRunAtUtc = schedule.IsEnabled ? cron!.GetNextOccurrence(now) : null;
        await _db.SaveChangesAsync(ct);
        return Ok(ScheduleDto.From(schedule));
    }

    [HttpPost("{id:guid}/enable")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ScheduleDto>> Enable(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct) =>
        SetEnabled(workspaceId, flowId, id, true, ct);

    [HttpPost("{id:guid}/disable")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ScheduleDto>> Disable(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct) =>
        SetEnabled(workspaceId, flowId, id, false, ct);

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct)
    {
        var schedule = await Load(workspaceId, flowId, id, ct);
        if (schedule is null) return ScheduleNotFound(id);

        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult<ScheduleDto>> SetEnabled(
        Guid workspaceId, Guid flowId, Guid id, bool enabled, CancellationToken ct)
    {
        var schedule = await Load(workspaceId, flowId, id, ct);
        if (schedule is null) return ScheduleNotFound(id);

        var now = _clock.UtcNow;
        schedule.IsEnabled = enabled;
        schedule.UpdatedAtUtc = now;
        // Re-arm from now on enable; clear the next run while disabled.
        schedule.NextRunAtUtc = enabled
            ? ScheduleNextRun(schedule.CronExpression, now)
            : null;
        await _db.SaveChangesAsync(ct);
        return Ok(ScheduleDto.From(schedule));
    }

    private static DateTimeOffset? ScheduleNextRun(string cron, DateTimeOffset now) =>
        CronExpression.TryParse(cron, out var parsed) ? parsed!.GetNextOccurrence(now) : null;

    private Task<Schedule?> Load(Guid workspaceId, Guid flowId, Guid id, CancellationToken ct) =>
        _db.Schedules.FirstOrDefaultAsync(
            s => s.Id == id && s.FlowId == flowId && s.WorkspaceId == workspaceId, ct);

    private Task<bool> FlowExists(Guid workspaceId, Guid flowId, CancellationToken ct) =>
        _db.Flows.AnyAsync(f => f.Id == flowId && f.WorkspaceId == workspaceId, ct);

    private ObjectResult FlowNotFound(Guid id) => Problem(
        title: "Flow not found", detail: $"No flow with id '{id}'.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult ScheduleNotFound(Guid id) => Problem(
        title: "Schedule not found", detail: $"No schedule with id '{id}'.", statusCode: StatusCodes.Status404NotFound);
}
