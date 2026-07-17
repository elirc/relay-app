using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Runs;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class RunsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public RunsApiTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RunDetailDto> RunDemoFlow()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/run",
            new TriggerRunRequest("""{"email":"new@user.test"}"""));
        return (await response.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task RunFlow_Manual_CreatesSucceededRunWithStepLogs()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/run",
            new TriggerRunRequest(null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options);
        Assert.Equal(RunStatus.Succeeded, run!.Status);
        Assert.Equal(RunTrigger.Manual, run.Trigger);
        Assert.True(run.StepLogs.Count >= 2); // trigger + at least one step
    }

    [Fact]
    public async Task RunFlow_UnknownFlow_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/{Guid.NewGuid()}/run", new TriggerRunRequest(null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListRuns_ReturnsRecentRuns_NewestFirst()
    {
        await RunDemoFlow();

        var page = await _client.GetFromJsonAsync<PagedResult<RunSummaryDto>>(
            $"/api/workspaces/{Ws}/runs", TestJson.Options);

        Assert.NotNull(page);
        Assert.NotEmpty(page!.Items);
        Assert.All(page.Items, r => Assert.Equal(DatabaseSeeder.DemoFlowId, r.FlowId));
    }

    [Fact]
    public async Task GetRun_ReturnsDetailWithLogs()
    {
        var created = await RunDemoFlow();

        var detail = await _client.GetFromJsonAsync<RunDetailDto>(
            $"/api/workspaces/{Ws}/runs/{created!.Id}", TestJson.Options);

        Assert.NotNull(detail);
        Assert.Contains(detail!.StepLogs, l => l.StepOrder == 0);
    }

    [Fact]
    public async Task Retry_CreatesANewRun()
    {
        var original = await RunDemoFlow();

        var response = await _client.PostAsync($"/api/workspaces/{Ws}/runs/{original!.Id}/retry", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var retryRun = await response.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options);
        Assert.NotEqual(original.Id, retryRun!.Id);
        Assert.Equal(original.FlowId, retryRun.FlowId);
    }

    [Fact]
    public async Task GetRun_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/workspaces/{Ws}/runs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
