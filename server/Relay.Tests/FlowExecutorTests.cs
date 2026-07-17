using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Domain.Execution;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class FlowExecutorTests
{
    [Fact]
    public async Task RunFlow_AllStepsSucceed_RecordsSucceededRunWithLogs()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);

        await using var ctx = database.CreateContext();
        var executor = new FlowExecutor(ctx, new FakeActionDispatcher());

        var run = await executor.RunFlowAsync(DatabaseSeeder.DemoFlowId, RunTrigger.Manual, """{"email":"a@b.c"}""");

        Assert.NotNull(run);
        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(0, run.RetryCount);
        // Trigger log + one action step.
        Assert.Equal(2, run.StepLogs.Count);
        Assert.Contains(run.StepLogs, l => l.StepOrder == 0 && l.Status == RunStatus.Succeeded);
    }

    [Fact]
    public async Task RunFlow_StepKeepsFailing_RetriesThenFailsAndSkipsRest()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);
        var flowId = await AddTwoStepFlow(seed);

        await using var ctx = database.CreateContext();
        // Fail whenever the first step (slack) runs; second step never reached.
        var dispatcher = new FakeActionDispatcher
        {
            Handler = req => req.ConnectorKey == "slack"
                ? StepExecutionResult.Fail("boom")
                : StepExecutionResult.Ok("ok"),
        };
        var executor = new FlowExecutor(ctx, dispatcher);

        var run = await executor.RunFlowAsync(flowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Failed, run!.Status);
        Assert.Equal(FlowExecutor.MaxAttempts - 1, run.RetryCount);
        Assert.Equal(FlowExecutor.MaxAttempts, dispatcher.Calls); // retried, second step skipped
        Assert.Contains(run.StepLogs, l => l.Status == RunStatus.Failed);
        Assert.Contains(run.StepLogs, l => l.Status == RunStatus.Skipped);
    }

    [Fact]
    public async Task RunFlow_TransientFailureThenSuccess_Recovers()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);

        await using var ctx = database.CreateContext();
        var attempt = 0;
        var dispatcher = new FakeActionDispatcher
        {
            Handler = _ => ++attempt < 2 ? StepExecutionResult.Fail("transient") : StepExecutionResult.Ok("ok"),
        };
        var executor = new FlowExecutor(ctx, dispatcher);

        var run = await executor.RunFlowAsync(DatabaseSeeder.DemoFlowId, RunTrigger.Manual, null);

        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(1, run.RetryCount);
    }

    [Fact]
    public async Task RunFlow_UnknownFlow_ReturnsNull()
    {
        using var database = new SqliteTestDatabase();
        await using var seed = database.CreateContext();
        await DatabaseSeeder.SeedAsync(seed);

        await using var ctx = database.CreateContext();
        var executor = new FlowExecutor(ctx, new FakeActionDispatcher());

        var run = await executor.RunFlowAsync(Guid.NewGuid(), RunTrigger.Manual, null);

        Assert.Null(run);
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
                new FlowStep { Id = Guid.NewGuid(), Order = 0, Name = "Slack", ConnectionId = DatabaseSeeder.DemoSlackConnectionId, Action = "send_message", ConfigJson = "{}" },
                new FlowStep { Id = Guid.NewGuid(), Order = 1, Name = "Http", ConnectionId = DatabaseSeeder.DemoInboundConnectionId, Action = "http_request", ConfigJson = "{}" },
            },
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync();
        return flow.Id;
    }
}
