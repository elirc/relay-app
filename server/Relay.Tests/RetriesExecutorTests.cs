using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class RetriesExecutorTests
{
    private static async Task<Guid> AddSingleStepFlow(
        RelayDbContext db, int maxAttempts, int backoffSeconds)
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            WorkspaceId = DatabaseSeeder.DemoWorkspaceId,
            Name = "Retry flow",
            TriggerConnectionId = DatabaseSeeder.DemoInboundConnectionId,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Steps =
            {
                new FlowStep
                {
                    Id = Guid.NewGuid(), Order = 0, Name = "Slack",
                    ConnectionId = DatabaseSeeder.DemoSlackConnectionId, Action = "send_message",
                    ConfigJson = "{}", MaxAttempts = maxAttempts, BackoffSeconds = backoffSeconds,
                },
            },
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return flow.Id;
    }

    private static async Task<Guid> AddTwoStepFlow(RelayDbContext db)
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            WorkspaceId = DatabaseSeeder.DemoWorkspaceId,
            Name = "Two step",
            TriggerConnectionId = DatabaseSeeder.DemoInboundConnectionId,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Steps =
            {
                new FlowStep { Id = Guid.NewGuid(), Order = 0, Name = "First", ConnectionId = DatabaseSeeder.DemoSlackConnectionId, Action = "send_message", ConfigJson = "{}" },
                new FlowStep { Id = Guid.NewGuid(), Order = 1, Name = "Second", ConnectionId = DatabaseSeeder.DemoInboundConnectionId, Action = "http_request", ConfigJson = "{}" },
            },
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return flow.Id;
    }

    [Fact]
    public async Task PerStepMaxAttempts_LimitsRetries()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddSingleStepFlow(seed, maxAttempts: 1, backoffSeconds: 0);

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher { Handler = _ => StepExecutionResult.Fail("boom") };
        var run = await new FlowExecutor(ctx, dispatcher).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        Assert.Equal(0, run.RetryCount);       // maxAttempts = 1 → no retry
        Assert.Equal(1, dispatcher.Calls);
    }

    [Fact]
    public async Task Backoff_WaitsBetweenAttempts()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddSingleStepFlow(seed, maxAttempts: 3, backoffSeconds: 5);

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher { Handler = _ => StepExecutionResult.Fail("boom") };
        var delayer = new FakeDelayer();
        var run = await new FlowExecutor(ctx, dispatcher, delayer).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        Assert.Equal(3, dispatcher.Calls);
        // Backoff applied between the 3 attempts → 2 waits of 5s each.
        Assert.Equal(2, delayer.Delays.Count);
        Assert.All(delayer.Delays, d => Assert.Equal(TimeSpan.FromSeconds(5), d));
    }

    [Fact]
    public async Task Backoff_NotApplied_OnSuccess()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddSingleStepFlow(seed, maxAttempts: 3, backoffSeconds: 5);

        await using var ctx = database.CreateContext();
        var delayer = new FakeDelayer();
        var run = await new FlowExecutor(ctx, new FakeActionDispatcher(), delayer).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Empty(delayer.Delays);
    }

    [Fact]
    public async Task Replay_FromStep_SkipsEarlierSteps()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddTwoStepFlow(seed);

        await using var ctx = database.CreateContext();
        var run = await new FlowExecutor(ctx, new FakeActionDispatcher())
            .RunFlowAsync(flowId, RunTrigger.Manual, null, fromStepOrder: 1);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        // Trigger (0) succeeded, first action (StepOrder 1) skipped for replay, second ran.
        var first = run.StepLogs.Single(l => l.StepOrder == 1);
        Assert.Equal(RunStatus.Skipped, first.Status);
        Assert.Contains("replay", first.Message!, StringComparison.OrdinalIgnoreCase);
        var second = run.StepLogs.Single(l => l.StepOrder == 2);
        Assert.Equal(RunStatus.Succeeded, second.Status);
    }
}
