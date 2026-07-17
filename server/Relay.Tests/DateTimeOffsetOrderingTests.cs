using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Guards the SQLite gotcha: DateTimeOffset stored via the UTC-ticks converter
/// must order and round-trip correctly (a naive mapping cannot ORDER BY it).
/// </summary>
public sealed class DateTimeOffsetOrderingTests
{
    [Fact]
    public async Task Runs_OrderByStartedAt_ReturnsChronologicalOrder()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();
        await DatabaseSeeder.SeedAsync(ctx);

        var baseTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        // Insert deliberately out of chronological order.
        ctx.Runs.AddRange(
            NewRun(baseTime.AddMinutes(20), "third"),
            NewRun(baseTime.AddMinutes(0), "first"),
            NewRun(baseTime.AddMinutes(10), "second"));
        await ctx.SaveChangesAsync();

        await using var readCtx = database.CreateContext();
        var ordered = await readCtx.Runs
            .OrderBy(r => r.StartedAtUtc)
            .Select(r => r.Error)
            .ToListAsync();

        Assert.Equal(new[] { "first", "second", "third" }, ordered);
    }

    [Fact]
    public async Task DateTimeOffset_RoundTripsThroughSqlite()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();
        await DatabaseSeeder.SeedAsync(ctx);

        var when = new DateTimeOffset(2026, 5, 4, 3, 2, 1, TimeSpan.Zero);
        var run = NewRun(when, "roundtrip");
        ctx.Runs.Add(run);
        await ctx.SaveChangesAsync();

        await using var readCtx = database.CreateContext();
        var reloaded = await readCtx.Runs.SingleAsync(r => r.Id == run.Id);

        Assert.Equal(when, reloaded.StartedAtUtc);
    }

    private static Run NewRun(DateTimeOffset startedAt, string tag) => new()
    {
        FlowId = DatabaseSeeder.DemoFlowId,
        Status = RunStatus.Succeeded,
        Trigger = RunTrigger.Manual,
        StartedAtUtc = startedAt,
        CompletedAtUtc = startedAt.AddSeconds(1),
        DurationMs = 1000,
        Error = tag,
    };
}
