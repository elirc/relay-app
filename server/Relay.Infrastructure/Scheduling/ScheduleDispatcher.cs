using Microsoft.EntityFrameworkCore;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Domain.Scheduling;
using Relay.Domain.Time;
using Relay.Infrastructure.Persistence;

namespace Relay.Infrastructure.Scheduling;

/// <summary>
/// The testable core of scheduling: on each tick it runs every enabled schedule
/// whose next run is due (for an enabled flow) through the same
/// <see cref="IFlowExecutor"/>, then advances its next run from the cron. Drive
/// it directly with a fake clock in tests; the hosted service ticks it in the app.
/// </summary>
public sealed class ScheduleDispatcher
{
    private readonly RelayDbContext _db;
    private readonly IFlowExecutor _executor;
    private readonly IClock _clock;

    public ScheduleDispatcher(RelayDbContext db, IFlowExecutor executor, IClock clock)
    {
        _db = db;
        _executor = executor;
        _clock = clock;
    }

    /// <summary>Runs all due schedules. Returns the number of runs triggered.</summary>
    public async Task<int> RunDueSchedulesAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var due = await _db.Schedules
            .Include(s => s.Flow)
            .Where(s => s.IsEnabled && s.NextRunAtUtc != null && s.NextRunAtUtc <= now)
            .OrderBy(s => s.NextRunAtUtc)
            .ToListAsync(ct);

        var triggered = 0;
        foreach (var schedule in due)
        {
            // Advance first so a mis-firing flow can't wedge the schedule.
            schedule.LastRunAtUtc = now;
            schedule.NextRunAtUtc = ComputeNextRun(schedule.CronExpression, now);
            schedule.UpdatedAtUtc = now;

            if (schedule.Flow?.IsEnabled == true)
            {
                await _executor.RunFlowAsync(schedule.FlowId, RunTrigger.Schedule, null, ct);
                triggered++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return triggered;
    }

    /// <summary>Computes the next run for a cron expression after <paramref name="after"/>.</summary>
    public static DateTimeOffset? ComputeNextRun(string cronExpression, DateTimeOffset after) =>
        CronExpression.TryParse(cronExpression, out var cron) ? cron!.GetNextOccurrence(after) : null;
}
