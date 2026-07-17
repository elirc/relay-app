using System.Net;
using System.Net.Http.Json;
using System.Text;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Runs;
using Relay.Api.Contracts.Webhooks;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class WebhooksApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public WebhooksApiTests(RelayApiFactory factory)
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

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task CreateWebhook_ReturnsTokenAndUrl()
    {
        var response = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/webhooks", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var hook = await response.Content.ReadFromJsonAsync<WebhookDto>(TestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(hook!.Token));
        Assert.Contains($"/api/hooks/{hook.Token}", hook.Url);
    }

    [Fact]
    public async Task Trigger_SeededWebhook_RunsFlow_AsWebhookTrigger()
    {
        var response = await _client.PostAsync("/api/hooks/demo-signup-hook", JsonBody("""{"email":"hook@user.test"}"""));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var runs = await _client.GetFromJsonAsync<PagedResult<RunSummaryDto>>(
            $"/api/workspaces/{Ws}/runs", TestJson.Options);
        Assert.Contains(runs!.Items, r => r.Trigger == RunTrigger.Webhook);
    }

    [Fact]
    public async Task Trigger_UnknownToken_Returns404()
    {
        var response = await _client.PostAsync("/api/hooks/does-not-exist", JsonBody("{}"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trigger_DisabledFlow_Returns409()
    {
        // New flows are created disabled.
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            "Disabled flow", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]));
        var flow = await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);

        var hookResponse = await _client.PostAsync($"/api/workspaces/{Ws}/flows/{flow!.Id}/webhooks", null);
        var hook = await hookResponse.Content.ReadFromJsonAsync<WebhookDto>(TestJson.Options);

        var trigger = await _client.PostAsync($"/api/hooks/{hook!.Token}", JsonBody("{}"));
        Assert.Equal(HttpStatusCode.Conflict, trigger.StatusCode);
    }
}
