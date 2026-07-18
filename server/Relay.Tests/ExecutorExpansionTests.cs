using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Deeper coverage of the flow executor's real failure modes: mid-flow failure,
/// retry-policy boundaries, backoff sequencing, and replay isolation (including a
/// replay of a replay). Complements <see cref="FlowExecutorTests"/> and
/// <see cref="RetriesExecutorTests"/>.
/// </summary>
public sealed class ExecutorExpansionTests
{
    // Connection keys per seeded demo connection, so a dispatcher can target a step.
    private const string SlackKey = "slack";
    private const string HttpKey = "http";

    private static FlowStep Step(int order, string name, Guid connectionId, int maxAttempts = 3, int backoff = 0) => new()
    {
        Id = Guid.NewGuid(), Order = order, Name = name, ConnectionId = connectionId,
        Action = "act", ConfigJson = "{}", MaxAttempts = maxAttempts, BackoffSeconds = backoff,
    };

    private static async Task<Guid> AddFlow(RelayDbContext db, params FlowStep[] steps)
    {
        var flow = new Flow
        {
            Id = Guid.NewGuid(),
            WorkspaceId = DatabaseSeeder.DemoWorkspaceId,
            Name = "Executor flow",
            TriggerConnectionId = DatabaseSeeder.DemoInboundConnectionId,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        foreach (var s in steps) flow.Steps.Add(s);
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return flow.Id;
    }

    [Fact]
    public async Task MiddleStepFails_LaterStepsSkipped_AndRunStateIsCorrect()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        // 3 action steps: slack, http (fails), slack. Second connection is HTTP.
        var flowId = await AddFlow(seed,
            Step(0, "First", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1),
            Step(1, "Middle", DatabaseSeeder.DemoInboundConnectionId, maxAttempts: 1),
            Step(2, "Last", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1));

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher
        {
            Handler = req => req.ConnectorKey == HttpKey ? StepExecutionResult.Fail("mid-flow boom") : StepExecutionResult.Ok("ok"),
        };
        var run = await new FlowExecutor(ctx, dispatcher).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        Assert.Equal("One or more steps failed.", run.Error);
        // The failing step ran; the last step never dispatched (trigger + 2 attempted).
        Assert.Equal(2, dispatcher.Calls);

        var byOrder = run.StepLogs.OrderBy(l => l.StepOrder).ToList();
        Assert.Equal(RunStatus.Succeeded, byOrder[0].Status); // trigger (order 0)
        Assert.Equal(RunStatus.Succeeded, byOrder[1].Status); // first action (order 1)
        Assert.Equal(RunStatus.Failed, byOrder[2].Status);    // middle action (order 2)
        Assert.Equal(RunStatus.Skipped, byOrder[3].Status);   // last action (order 3)
        Assert.Contains("earlier step failed", byOrder[3].Message);
    }

    [Fact]
    public async Task RetriesExactlyMaxAttempts_ThenGivesUp()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed, Step(0, "Flaky", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 4, backoff: 2));

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher { Handler = _ => StepExecutionResult.Fail("always") };
        var delayer = new FakeDelayer();
        var run = await new FlowExecutor(ctx, dispatcher, delayer).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        Assert.Equal(4, dispatcher.Calls);          // exactly maxAttempts, no more
        Assert.Equal(3, run.RetryCount);            // attempts - 1
        Assert.Equal(3, delayer.Delays.Count);      // a backoff between each of the 4 attempts
        Assert.All(delayer.Delays, d => Assert.Equal(TimeSpan.FromSeconds(2), d));
    }

    [Fact]
    public async Task SucceedsOnLastAllowedAttempt_NoBackoffAfterSuccess()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed, Step(0, "Recover", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 3, backoff: 1));

        await using var ctx = database.CreateContext();
        var attempt = 0;
        var dispatcher = new FakeActionDispatcher
        {
            Handler = _ => ++attempt < 3 ? StepExecutionResult.Fail("transient") : StepExecutionResult.Ok("ok"),
        };
        var delayer = new FakeDelayer();
        var run = await new FlowExecutor(ctx, dispatcher, delayer).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(3, dispatcher.Calls);
        Assert.Equal(2, run.RetryCount);
        Assert.Equal(2, delayer.Delays.Count);  // backoff only between the two failed attempts
    }

    [Fact]
    public async Task DispatcherThrows_IsClassifiedAsAFailure_NotAnUnhandledError()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed, Step(0, "Throwing", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1));

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher { Handler = _ => throw new InvalidOperationException("kaboom") };
        var run = await new FlowExecutor(ctx, dispatcher).RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        var stepLog = run.StepLogs.Single(l => l.StepOrder == 1);
        Assert.Equal(RunStatus.Failed, stepLog.Status);
        Assert.Contains("kaboom", stepLog.Message);
    }

    [Fact]
    public async Task Replay_DoesNotRedispatchEarlierSteps()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed,
            Step(0, "A", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1),
            Step(1, "B", DatabaseSeeder.DemoInboundConnectionId, maxAttempts: 1),
            Step(2, "C", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1));

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher();
        // Replay from step order 2: only step C (the third action) should dispatch.
        var run = await new FlowExecutor(ctx, dispatcher).RunFlowAsync(flowId, RunTrigger.Manual, null, fromStepOrder: 2);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(1, dispatcher.Calls); // A and B skipped, only C ran
        var logs = run.StepLogs.OrderBy(l => l.StepOrder).ToList();
        Assert.Equal(RunStatus.Skipped, logs[1].Status); // A
        Assert.Equal(RunStatus.Skipped, logs[2].Status); // B
        Assert.Equal(RunStatus.Succeeded, logs[3].Status); // C
    }

    [Fact]
    public async Task ReplayOfAReplay_IsConsistent()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed,
            Step(0, "A", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1),
            Step(1, "B", DatabaseSeeder.DemoInboundConnectionId, maxAttempts: 1));

        await using var ctx = database.CreateContext();
        var executor = new FlowExecutor(ctx, new FakeActionDispatcher());

        var first = await executor.RunFlowAsync(flowId, RunTrigger.Manual, null, fromStepOrder: 1);
        var second = await executor.RunFlowAsync(flowId, RunTrigger.Manual, null, fromStepOrder: 1);

        Assert.NotEqual(first!.Id, second!.Id);            // each replay is a distinct run
        Assert.Equal(RunStatus.Succeeded, second.Status);
        // Both replays skip A and run B identically.
        foreach (var run in new[] { first, second })
        {
            var logs = run.StepLogs.OrderBy(l => l.StepOrder).ToList();
            Assert.Equal(RunStatus.Skipped, logs[1].Status);
            Assert.Equal(RunStatus.Succeeded, logs[2].Status);
        }
    }

    [Fact]
    public async Task Replay_BeyondLastStep_RunsNothing_ButStillSucceeds()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddFlow(seed, Step(0, "Only", DatabaseSeeder.DemoSlackConnectionId, maxAttempts: 1));

        await using var ctx = database.CreateContext();
        var dispatcher = new FakeActionDispatcher();
        var run = await new FlowExecutor(ctx, dispatcher).RunFlowAsync(flowId, RunTrigger.Manual, null, fromStepOrder: 99);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(0, dispatcher.Calls); // every action step skipped
    }
}
