using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Scheduling;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Scheduling;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Cron next-run edge cases (month boundaries, leap years, year rollover, pure-UTC
/// DST-agnosticism) and fake-clock catch-up semantics: a schedule that missed many
/// slots fires exactly once per tick and re-arms past "now".
/// </summary>
public sealed class SchedulingExpansionTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) =>
        new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    // ---- cron edges ----

    [Fact]
    public void DayOfMonth31_SkipsShortMonths()
    {
        Assert.True(CronExpression.TryParse("0 0 31 * *", out var cron));
        // From Jan 31 the next "day 31" is Mar 31 — February has no 31st.
        var next = cron!.GetNextOccurrence(Utc(2026, 1, 31, 0, 0));
        Assert.Equal(Utc(2026, 3, 31, 0, 0), next);
    }

    [Fact]
    public void Feb29_OnlyMatchesInLeapYears()
    {
        Assert.True(CronExpression.TryParse("0 0 29 2 *", out var cron));
        // 2027 is not a leap year; the next Feb 29 is in 2028.
        var next = cron!.GetNextOccurrence(Utc(2027, 1, 1, 0, 0));
        Assert.Equal(Utc(2028, 2, 29, 0, 0), next);
    }

    [Fact]
    public void EveryMinute_AdvancesByExactlyOneMinute()
    {
        Assert.True(CronExpression.TryParse("* * * * *", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 6, 1, 12, 0));
        Assert.Equal(Utc(2026, 6, 1, 12, 1), next);
    }

    [Fact]
    public void YearRollover_FromDecemberEnd()
    {
        Assert.True(CronExpression.TryParse("* * * * *", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 12, 31, 23, 59));
        Assert.Equal(Utc(2027, 1, 1, 0, 0), next);
    }

    [Fact]
    public void SpecificDailyTime_IsComputedInPureUtc_AcrossADstBoundary()
    {
        // 2026-03-08 is the US spring-forward date. Cron is pure UTC, so 02:00 daily
        // is unaffected — the result is 02:00 UTC with a zero offset, no wall-clock skew.
        Assert.True(CronExpression.TryParse("0 2 * * *", out var cron));
        var next = cron!.GetNextOccurrence(Utc(2026, 3, 8, 1, 0));
        Assert.Equal(Utc(2026, 3, 8, 2, 0), next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);
        Assert.Equal(2, next.Value.Hour);
    }

    // ---- dispatcher catch-up ----

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> AddSchedule(RelayDbContext db, DateTimeOffset? nextRun, Guid flowId, string cron = "*/5 * * * *")
    {
        var id = Guid.NewGuid();
        db.Schedules.Add(new Schedule
        {
            Id = id,
            WorkspaceId = DatabaseSeeder.DemoWorkspaceId,
            FlowId = flowId,
            CronExpression = cron,
            IsEnabled = true,
            NextRunAtUtc = nextRun,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static ScheduleDispatcher Dispatcher(RelayDbContext ctx, DateTimeOffset now) =>
        new(ctx, new FlowExecutor(ctx, new FakeActionDispatcher()), new FakeClock(now));

    [Fact]
    public async Task ManyMissedSlots_FireOncePerTick_AndReArmPastNow()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        // Overdue by 30 minutes on a 5-minute cadence (6 slots missed).
        var id = await AddSchedule(seed, Now.AddMinutes(-30), DatabaseSeeder.DemoFlowId);

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(1, count); // a single catch-up run, not one per missed slot

        await using var read = database.CreateContext();
        Assert.Equal(1, await read.Runs.CountAsync());
        var schedule = await read.Schedules.FindAsync(id);
        Assert.True(schedule!.NextRunAtUtc > Now); // re-armed to a future slot
    }

    [Fact]
    public async Task MultipleDueSchedules_EachFireOnce_InOneTick()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        // Two enabled schedules on the same enabled flow, both overdue.
        await AddSchedule(seed, Now.AddMinutes(-1), DatabaseSeeder.DemoFlowId);
        await AddSchedule(seed, Now.AddMinutes(-2), DatabaseSeeder.DemoFlowId);

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(2, count);

        await using var read = database.CreateContext();
        Assert.Equal(2, await read.Runs.CountAsync());
    }

    [Fact]
    public async Task SecondTick_WithNothingNewlyDue_FiresNothing()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        await AddSchedule(seed, Now.AddMinutes(-1), DatabaseSeeder.DemoFlowId);

        await using var ctx = database.CreateContext();
        Assert.Equal(1, await Dispatcher(ctx, Now).RunDueSchedulesAsync());
        // Same instant, schedule already advanced → no double fire.
        Assert.Equal(0, await Dispatcher(ctx, Now).RunDueSchedulesAsync());
    }
}
