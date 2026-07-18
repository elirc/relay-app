using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Metrics;
using Relay.Api.Contracts.Runs;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class MetricsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public MetricsApiTests(RelayApiFactory factory)
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

    private async Task<Guid> CreateFlow(string name, bool failing)
    {
        var config = failing ? """{"fail":true}""" : "{}";
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            name, null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Step", DatabaseSeeder.DemoSlackConnectionId, "send_message", config, 1, 0)]));
        var flow = (await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        return flow.Id;
    }

    private Task Run(Guid flowId) =>
        _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows/{flowId}/run", new TriggerRunRequest(null));

    private async Task SetStepFailing(Guid flowId, bool failing)
    {
        var config = failing ? """{"fail":true}""" : "{}";
        await _client.PutAsJsonAsync($"/api/workspaces/{Ws}/flows/{flowId}", new UpdateFlowRequest(
            "Mixed", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Step", DatabaseSeeder.DemoSlackConnectionId, "send_message", config, 1, 0)]));
    }

    [Fact]
    public async Task FlowMetrics_MixedRuns_ComputesRatesAndSeries()
    {
        var flowId = await CreateFlow("Mixed", failing: true);
        await Run(flowId);                 // fails
        await SetStepFailing(flowId, false);
        await Run(flowId);                 // succeeds

        var metrics = await _client.GetFromJsonAsync<FlowMetricsDto>(
            $"/api/workspaces/{Ws}/flows/{flowId}/metrics?days=7", TestJson.Options);

        Assert.Equal(2, metrics!.Summary.TotalRuns);
        Assert.Equal(1, metrics.Summary.Succeeded);
        Assert.Equal(1, metrics.Summary.Failed);
        Assert.Equal(0.5, metrics.Summary.SuccessRate);
        Assert.Equal(7, metrics.RunsOverTime.Count);
        Assert.Equal(2, metrics.RunsOverTime[^1].Total); // today's bucket
    }

    [Fact]
    public async Task WorkspaceMetrics_IncludesPerFlowRow_AndSeriesLength()
    {
        var flowId = await CreateFlow("Observed", failing: false);
        await Run(flowId);

        var metrics = await _client.GetFromJsonAsync<WorkspaceMetricsDto>(
            $"/api/workspaces/{Ws}/metrics?days=7", TestJson.Options);

        Assert.Equal(7, metrics!.Days);
        Assert.Equal(7, metrics.RunsOverTime.Count);
        Assert.Contains(metrics.PerFlow, f => f.FlowId == flowId && f.Summary.Succeeded >= 1);
        Assert.True(metrics.Overall.TotalRuns >= 1);
    }

    [Fact]
    public async Task WorkspaceMetrics_Days_AreClamped()
    {
        var metrics = await _client.GetFromJsonAsync<WorkspaceMetricsDto>(
            $"/api/workspaces/{Ws}/metrics?days=1000", TestJson.Options);
        Assert.Equal(90, metrics!.Days);
        Assert.Equal(90, metrics.RunsOverTime.Count);
    }

    [Fact]
    public async Task WorkspaceMetrics_UnknownWorkspace_Returns404()
    {
        var response = await _client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/metrics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FlowMetrics_UnknownFlow_Returns404()
    {
        var response = await _client.GetAsync($"/api/workspaces/{Ws}/flows/{Guid.NewGuid()}/metrics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
