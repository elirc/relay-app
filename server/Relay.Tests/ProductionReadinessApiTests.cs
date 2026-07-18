using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connectors;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Runs;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class ProductionReadinessApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string FlowBase => $"/api/workspaces/{Ws}/flows";

    public ProductionReadinessApiTests(RelayApiFactory factory)
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

    private static CreateFlowRequest ValidFlow(string name) => new(
        name, "desc", DatabaseSeeder.DemoInboundConnectionId,
        [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]);

    private UpdateFlowRequest Update(string name, Guid? expected) => new(
        name, "desc", DatabaseSeeder.DemoInboundConnectionId,
        [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")],
        expected);

    [Fact]
    public async Task Flow_HasConcurrencyToken()
    {
        var created = await _client.PostAsJsonAsync(FlowBase, ValidFlow("Token flow"));
        var flow = await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);
        Assert.NotEqual(Guid.Empty, flow!.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_WithCurrentToken_Succeeds_AndRotatesToken()
    {
        var created = await _client.PostAsJsonAsync(FlowBase, ValidFlow("Concurrent"));
        var flow = (await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;

        var response = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}", Update("Concurrent v2", flow.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);
        Assert.NotEqual(flow.ConcurrencyToken, updated!.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_WithStaleToken_Returns409()
    {
        var created = await _client.PostAsJsonAsync(FlowBase, ValidFlow("Racy"));
        var flow = (await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;

        // First update wins and rotates the token.
        await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}", Update("Racy v2", flow.ConcurrencyToken));

        // Second update with the now-stale token conflicts.
        var response = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}", Update("Racy v3", flow.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/connectors")]
    public async Task ListEndpoints_ArePaged_Global(string path)
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectorDto>>($"{path}?page=1&pageSize=1", TestJson.Options);
        Assert.Equal(1, page!.PageSize);
        Assert.True(page.TotalCount >= 1);
        Assert.True(page.Items.Count <= 1);
    }

    [Fact]
    public async Task ListEndpoints_ArePaged_WorkspaceScoped()
    {
        var connections = await _client.GetFromJsonAsync<PagedResult<Relay.Api.Contracts.Connections.ConnectionDto>>(
            $"/api/workspaces/{Ws}/connections?page=1&pageSize=1", TestJson.Options);
        Assert.Equal(1, connections!.PageSize);

        var flows = await _client.GetFromJsonAsync<PagedResult<FlowSummaryDto>>(
            $"{FlowBase}?page=1&pageSize=1", TestJson.Options);
        Assert.Equal(1, flows!.PageSize);

        var runs = await _client.GetFromJsonAsync<PagedResult<RunSummaryDto>>(
            $"/api/workspaces/{Ws}/runs?page=1&pageSize=1", TestJson.Options);
        Assert.Equal(1, runs!.PageSize);
    }
}
