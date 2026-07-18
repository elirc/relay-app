using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Scheduling;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class ScheduleDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> AddSchedule(
        RelayDbContext db, DateTimeOffset? nextRun, bool enabled = true, string cron = "*/5 * * * *")
    {
        var id = Guid.NewGuid();
        db.Schedules.Add(new Schedule
        {
            Id = id,
            WorkspaceId = DatabaseSeeder.DemoWorkspaceId,
            FlowId = DatabaseSeeder.DemoFlowId,
            CronExpression = cron,
            IsEnabled = enabled,
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
    public async Task DueSchedule_TriggersScheduledRun_AndAdvancesNextRun()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var id = await AddSchedule(seed, Now.AddMinutes(-1));

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(1, count);

        await using var read = database.CreateContext();
        var run = await read.Runs.OrderByDescending(r => r.StartedAtUtc).FirstAsync();
        Assert.Equal(RunTrigger.Schedule, run.Trigger);

        var schedule = await read.Schedules.FindAsync(id);
        Assert.Equal(Now, schedule!.LastRunAtUtc);
        Assert.NotNull(schedule.NextRunAtUtc);
        Assert.True(schedule.NextRunAtUtc > Now);
    }

    [Fact]
    public async Task NotYetDue_DoesNotTrigger()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        await AddSchedule(seed, Now.AddMinutes(5));

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(0, count);

        await using var read = database.CreateContext();
        Assert.Empty(read.Runs);
    }

    [Fact]
    public async Task DisabledSchedule_DoesNotTrigger_NorAdvance()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var id = await AddSchedule(seed, Now.AddMinutes(-1), enabled: false);

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(0, count);

        await using var read = database.CreateContext();
        var schedule = await read.Schedules.FindAsync(id);
        Assert.Equal(Now.AddMinutes(-1), schedule!.NextRunAtUtc);
        Assert.Null(schedule.LastRunAtUtc);
    }

    [Fact]
    public async Task DueSchedule_ForDisabledFlow_AdvancesButDoesNotRun()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flow = await seed.Flows.FindAsync(DatabaseSeeder.DemoFlowId);
        flow!.IsEnabled = false;
        await seed.SaveChangesAsync();
        var id = await AddSchedule(seed, Now.AddMinutes(-1));

        await using var ctx = database.CreateContext();
        var count = await Dispatcher(ctx, Now).RunDueSchedulesAsync();
        Assert.Equal(0, count);

        await using var read = database.CreateContext();
        Assert.Empty(read.Runs);
        var schedule = await read.Schedules.FindAsync(id);
        Assert.True(schedule!.NextRunAtUtc > Now); // still advanced past the missed slot
    }
}
