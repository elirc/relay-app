using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class FlowsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string Base => $"/api/workspaces/{Ws}/flows";

    public FlowsApiTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static CreateFlowRequest ValidFlow(string name) => new(
        name,
        "desc",
        DatabaseSeeder.DemoInboundConnectionId,
        [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]);

    [Fact]
    public async Task List_ReturnsSeededFlow()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<FlowSummaryDto>>(Base, TestJson.Options);

        Assert.NotNull(page);
        var demo = Assert.Single(page!.Items, f => f.Id == DatabaseSeeder.DemoFlowId);
        Assert.Equal(1, demo.StepCount);
        Assert.True(demo.IsEnabled);
    }

    [Fact]
    public async Task Get_ReturnsDetailWithOrderedSteps()
    {
        var flow = await _client.GetFromJsonAsync<FlowDetailDto>($"{Base}/{DatabaseSeeder.DemoFlowId}", TestJson.Options);

        Assert.NotNull(flow);
        Assert.Single(flow!.Steps);
        Assert.Equal("Post to Slack", flow.Steps[0].Name);
    }

    [Fact]
    public async Task Create_ValidFlow_Returns201_DisabledByDefault()
    {
        var response = await _client.PostAsJsonAsync(Base, ValidFlow("New flow"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var flow = await response.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);
        Assert.False(flow!.IsEnabled);
        Assert.Equal(0, flow.Steps[0].Order);
    }

    [Fact]
    public async Task Create_NoSteps_Returns400()
    {
        var request = new CreateFlowRequest("Empty", null, DatabaseSeeder.DemoInboundConnectionId, []);

        var response = await _client.PostAsJsonAsync(Base, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_TriggerConnectionOutsideWorkspace_Returns400()
    {
        var request = new CreateFlowRequest(
            "Bad trigger", null, Guid.NewGuid(),
            [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]);

        var response = await _client.PostAsJsonAsync(Base, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_StepConnectionOutsideWorkspace_Returns400()
    {
        var request = new CreateFlowRequest(
            "Bad step", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Post", Guid.NewGuid(), "send_message", "{}")]);

        var response = await _client.PostAsJsonAsync(Base, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReplacesSteps()
    {
        var created = await _client.PostAsJsonAsync(Base, ValidFlow("Editable"));
        var flow = await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);

        var update = new UpdateFlowRequest(
            "Editable v2", "changed", DatabaseSeeder.DemoInboundConnectionId,
            [
                new FlowStepInput("Step A", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}"),
                new FlowStepInput("Step B", DatabaseSeeder.DemoInboundConnectionId, "http_request", "{}"),
            ]);

        var response = await _client.PutAsJsonAsync($"{Base}/{flow!.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);
        Assert.Equal("Editable v2", result!.Name);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal([0, 1], result.Steps.Select(s => s.Order).ToArray());
        Assert.Equal("Step B", result.Steps[1].Name);
    }

    [Fact]
    public async Task EnableThenDisable_TogglesState()
    {
        var created = await _client.PostAsJsonAsync(Base, ValidFlow("Toggle"));
        var flow = await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);

        var enabled = await _client.PostAsync($"{Base}/{flow!.Id}/enable", null);
        var enabledDto = await enabled.Content.ReadFromJsonAsync<FlowSummaryDto>(TestJson.Options);
        Assert.True(enabledDto!.IsEnabled);

        var disabled = await _client.PostAsync($"{Base}/{flow.Id}/disable", null);
        var disabledDto = await disabled.Content.ReadFromJsonAsync<FlowSummaryDto>(TestJson.Options);
        Assert.False(disabledDto!.IsEnabled);
    }

    [Fact]
    public async Task Delete_RemovesFlow()
    {
        var created = await _client.PostAsJsonAsync(Base, ValidFlow("Deletable"));
        var flow = await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);

        var deleted = await _client.DeleteAsync($"{Base}/{flow!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var fetch = await _client.GetAsync($"{Base}/{flow.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownFlow_Returns404()
    {
        var response = await _client.DeleteAsync($"{Base}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
