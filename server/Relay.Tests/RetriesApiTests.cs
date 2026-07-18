using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Runs;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class RetriesApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public RetriesApiTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        await _factory.AuthenticateAsync(_client);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>Creates an enabled flow whose only step fails, and returns its id.</summary>
    private async Task<Guid> CreateFailingFlow(string name)
    {
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            name, null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Boom", DatabaseSeeder.DemoSlackConnectionId, "send_message", """{"fail":true}""", 1, 0)]));
        var flow = (await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        await _client.PostAsync($"/api/workspaces/{Ws}/flows/{flow.Id}/enable", null);
        return flow.Id;
    }

    [Fact]
    public async Task Create_StepRetryPolicy_IsPersisted()
    {
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            "Policy flow", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Step", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}", 5, 10)]));
        var flow = await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);

        Assert.Equal(5, flow!.Steps[0].MaxAttempts);
        Assert.Equal(10, flow.Steps[0].BackoffSeconds);
    }

    [Fact]
    public async Task DeadLetter_ListsFailedRuns()
    {
        var flowId = await CreateFailingFlow("Dead-letter flow");
        await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows/{flowId}/run", new TriggerRunRequest(null));

        var page = await _client.GetFromJsonAsync<PagedResult<RunSummaryDto>>(
            $"/api/workspaces/{Ws}/dead-letter", TestJson.Options);

        Assert.NotEmpty(page!.Items);
        Assert.All(page.Items, r => Assert.Equal(RunStatus.Failed, r.Status));
        Assert.Contains(page.Items, r => r.FlowId == flowId);
    }

    [Fact]
    public async Task ListRuns_StatusFilter_ReturnsOnlyMatching()
    {
        var flowId = await CreateFailingFlow("Filter flow");
        await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows/{flowId}/run", new TriggerRunRequest(null));

        var page = await _client.GetFromJsonAsync<PagedResult<RunSummaryDto>>(
            $"/api/workspaces/{Ws}/runs?status=Failed", TestJson.Options);

        Assert.All(page!.Items, r => Assert.Equal(RunStatus.Failed, r.Status));
    }

    [Fact]
    public async Task Replay_FromStep_CreatesNewRun()
    {
        // A two-step flow; run it, then replay from step 1.
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            "Replayable", null, DatabaseSeeder.DemoInboundConnectionId,
            [
                new FlowStepInput("A", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}"),
                new FlowStepInput("B", DatabaseSeeder.DemoInboundConnectionId, "http_request", "{}"),
            ]));
        var flow = (await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        await _client.PostAsync($"/api/workspaces/{Ws}/flows/{flow.Id}/enable", null);
        var original = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows/{flow.Id}/run", new TriggerRunRequest(null));
        var originalRun = (await original.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options))!;

        var replay = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/runs/{originalRun.Id}/replay", new ReplayRunRequest(1));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var replayRun = (await replay.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options))!;
        Assert.NotEqual(originalRun.Id, replayRun.Id);
        Assert.Contains(replayRun.StepLogs, l => l.StepOrder == 1 && l.Status == RunStatus.Skipped);
        Assert.Contains(replayRun.StepLogs, l => l.StepOrder == 2 && l.Status == RunStatus.Succeeded);
    }

    [Fact]
    public async Task Replay_UnknownRun_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/runs/{Guid.NewGuid()}/replay", new ReplayRunRequest(0));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_DuplicateDeliveryWithSameKey_ReturnsSameRun()
    {
        var first = await PostHook("dupe-key-1");
        var second = await PostHook("dupe-key-1");

        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        Assert.Equal(HttpStatusCode.Accepted, second.Status);
        Assert.Equal(first.RunId, second.RunId);
        Assert.True(second.Deduplicated);
        Assert.False(first.Deduplicated);
    }

    [Fact]
    public async Task Webhook_DifferentKeys_CreateDifferentRuns()
    {
        var a = await PostHook("key-a");
        var b = await PostHook("key-b");
        Assert.NotEqual(a.RunId, b.RunId);
    }

    [Fact]
    public async Task Webhook_NoKey_AlwaysCreatesNewRun()
    {
        var a = await PostHook(null);
        var b = await PostHook(null);
        Assert.NotEqual(a.RunId, b.RunId);
    }

    private async Task<(HttpStatusCode Status, Guid RunId, bool Deduplicated)> PostHook(string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/hooks/demo-signup-hook")
        {
            Content = Json("""{"email":"x@y.z"}"""),
        };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);

        var response = await _client.SendAsync(request);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return (
            response.StatusCode,
            root.GetProperty("runId").GetGuid(),
            root.TryGetProperty("deduplicated", out var d) && d.GetBoolean());
    }
}
