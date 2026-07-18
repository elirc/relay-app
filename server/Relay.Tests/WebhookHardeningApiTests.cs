using System.Net;
using System.Net.Http.Json;
using System.Text;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Webhooks;
using Relay.Domain.Enums;
using Relay.Domain.Security;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class WebhookHardeningApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private const string Body = """{"email":"hook@user.test"}""";

    public WebhookHardeningApiTests(RelayApiFactory factory)
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

    private async Task<(Guid FlowId, Guid WebhookId, string Token, string Secret)> SetupSignedWebhook()
    {
        var create = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows", new CreateFlowRequest(
            "Signed flow", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Post", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]));
        var flow = (await create.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        await _client.PostAsync($"/api/workspaces/{Ws}/flows/{flow.Id}/enable", null);

        var hookResponse = await _client.PostAsync($"/api/workspaces/{Ws}/flows/{flow.Id}/webhooks", null);
        var hook = (await hookResponse.Content.ReadFromJsonAsync<WebhookDto>(TestJson.Options))!;

        var secretResponse = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/{flow.Id}/webhooks/{hook.Id}/signing-secret", null);
        var secret = (await secretResponse.Content.ReadFromJsonAsync<SigningSecretResponse>(TestJson.Options))!;

        return (flow.Id, hook.Id, hook.Token, secret.SigningSecret);
    }

    private HttpRequestMessage SignedRequest(string token, string body, string? timestamp = null, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/hooks/{token}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (timestamp is not null) request.Headers.Add("X-Relay-Timestamp", timestamp);
        if (signature is not null) request.Headers.Add("X-Relay-Signature", signature);
        return request;
    }

    [Fact]
    public async Task GenerateSigningSecret_TurnsOnSignature_ButNeverEchoesSecretInList()
    {
        var setup = await SetupSignedWebhook();

        var list = await _client.GetFromJsonAsync<List<WebhookDto>>(
            $"/api/workspaces/{Ws}/flows/{setup.FlowId}/webhooks", TestJson.Options);
        var hook = Assert.Single(list!, w => w.Id == setup.WebhookId);
        Assert.True(hook.RequireSignature);
        Assert.True(hook.HasSigningSecret);

        var raw = await (await _client.GetAsync($"/api/workspaces/{Ws}/flows/{setup.FlowId}/webhooks"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain(setup.Secret, raw);
    }

    [Fact]
    public async Task ValidSignature_IsAccepted_AndLoggedDelivered()
    {
        var setup = await SetupSignedWebhook();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig = WebhookSignature.Compute(setup.Secret, ts, Body);

        var response = await _client.SendAsync(SignedRequest(setup.Token, Body, ts, sig));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var deliveries = await GetDeliveries(setup.FlowId, setup.WebhookId);
        Assert.Contains(deliveries.Items, d => d.Outcome == WebhookDeliveryOutcome.Delivered && d.Success);
    }

    [Fact]
    public async Task MissingSignature_Returns401_AndLogged()
    {
        var setup = await SetupSignedWebhook();

        var response = await _client.SendAsync(SignedRequest(setup.Token, Body));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var deliveries = await GetDeliveries(setup.FlowId, setup.WebhookId);
        Assert.Contains(deliveries.Items, d => d.Outcome == WebhookDeliveryOutcome.MissingSignature && !d.Success);
    }

    [Fact]
    public async Task InvalidSignature_Returns401_AndLogged()
    {
        var setup = await SetupSignedWebhook();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var response = await _client.SendAsync(SignedRequest(setup.Token, Body, ts, "deadbeef"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var deliveries = await GetDeliveries(setup.FlowId, setup.WebhookId);
        Assert.Contains(deliveries.Items, d => d.Outcome == WebhookDeliveryOutcome.InvalidSignature);
    }

    [Fact]
    public async Task ExpiredTimestamp_Returns401_AndLogged()
    {
        var setup = await SetupSignedWebhook();
        var oldTs = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds().ToString();
        var sig = WebhookSignature.Compute(setup.Secret, oldTs, Body);

        var response = await _client.SendAsync(SignedRequest(setup.Token, Body, oldTs, sig));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var deliveries = await GetDeliveries(setup.FlowId, setup.WebhookId);
        Assert.Contains(deliveries.Items, d => d.Outcome == WebhookDeliveryOutcome.TimestampExpired);
    }

    [Fact]
    public async Task DisablingSignature_AllowsUnsignedDelivery()
    {
        var setup = await SetupSignedWebhook();

        var disable = await _client.DeleteAsync(
            $"/api/workspaces/{Ws}/flows/{setup.FlowId}/webhooks/{setup.WebhookId}/signing-secret");
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var response = await _client.SendAsync(SignedRequest(setup.Token, Body));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<PagedResult<WebhookDeliveryDto>> GetDeliveries(Guid flowId, Guid webhookId) =>
        (await _client.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>(
            $"/api/workspaces/{Ws}/flows/{flowId}/webhooks/{webhookId}/deliveries", TestJson.Options))!;
}
