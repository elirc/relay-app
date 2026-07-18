using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Webhooks;
using Relay.Domain.Enums;
using Relay.Domain.Security;
using Relay.Domain.Time;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Timestamp-window boundary and rotation behaviour for HMAC-signed webhooks. The
/// freshness check depends on the clock, so a fake-clock host variant pins "now"
/// to make the exactly-at-limit boundary deterministic under load.
/// </summary>
public sealed class WebhookSecurityBoundaryTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private const string Body = """{"email":"boundary@user.test"}""";

    public WebhookSecurityBoundaryTests(RelayApiFactory factory)
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
            "Boundary flow", null, DatabaseSeeder.DemoInboundConnectionId,
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

    /// <summary>A client whose host pins the clock to <see cref="FixedNow"/> for deterministic freshness checks.</summary>
    private HttpClient FakeClockClient()
    {
        var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FakeClock(FixedNow));
            }));
        return host.CreateClient();
    }

    private static HttpRequestMessage SignedRequest(string token, string body, string timestamp, string secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/hooks/{token}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Relay-Timestamp", timestamp);
        request.Headers.Add("X-Relay-Signature", WebhookSignature.Compute(secret, timestamp, body));
        return request;
    }

    [Fact]
    public async Task Timestamp_ExactlyAtWindowEdge_IsAccepted()
    {
        var setup = await SetupSignedWebhook();
        var ts = FixedNow.Subtract(Window).ToUnixTimeSeconds().ToString(); // drift == window
        using var client = FakeClockClient();

        var response = await client.SendAsync(SignedRequest(setup.Token, Body, ts, setup.Secret));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Timestamp_OneSecondPastWindow_IsRejected()
    {
        var setup = await SetupSignedWebhook();
        var ts = FixedNow.Subtract(Window).AddSeconds(-1).ToUnixTimeSeconds().ToString(); // drift > window
        using var client = FakeClockClient();

        var response = await client.SendAsync(SignedRequest(setup.Token, Body, ts, setup.Secret));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var deliveries = await GetDeliveries(setup.FlowId, setup.WebhookId);
        Assert.Contains(deliveries.Items, d => d.Outcome == WebhookDeliveryOutcome.TimestampExpired);
    }

    [Fact]
    public async Task Timestamp_InFutureWithinWindow_IsAccepted()
    {
        // Clock skew can put a sender slightly ahead; the check uses absolute drift.
        var setup = await SetupSignedWebhook();
        var ts = FixedNow.Add(Window).ToUnixTimeSeconds().ToString();
        using var client = FakeClockClient();

        var response = await client.SendAsync(SignedRequest(setup.Token, Body, ts, setup.Secret));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task IdenticalSignedRequest_WithSameIdempotencyKey_ReusesTheSameRun()
    {
        var setup = await SetupSignedWebhook();
        using var client = FakeClockClient();
        var ts = FixedNow.ToUnixTimeSeconds().ToString();

        async Task<HttpResponseMessage> Send()
        {
            var req = SignedRequest(setup.Token, Body, ts, setup.Secret);
            req.Headers.Add("Idempotency-Key", "signed-dupe-1");
            return await client.SendAsync(req);
        }

        var first = await Send();
        var second = await Send();
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        var firstRun = (await first.Content.ReadFromJsonAsync<TriggerAck>(TestJson.Options))!;
        var secondRun = (await second.Content.ReadFromJsonAsync<TriggerAck>(TestJson.Options))!;
        Assert.Equal(firstRun.RunId, secondRun.RunId); // deduplicated, not a new run
        Assert.True(secondRun.Deduplicated);
        Assert.False(firstRun.Deduplicated);
    }

    [Fact]
    public async Task AfterSecretRotation_OldSignatureFails_NewSignatureWorks()
    {
        var setup = await SetupSignedWebhook();

        // Rotate the signing secret (POST again returns a brand-new secret).
        var rotated = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/{setup.FlowId}/webhooks/{setup.WebhookId}/signing-secret", null);
        var newSecret = (await rotated.Content.ReadFromJsonAsync<SigningSecretResponse>(TestJson.Options))!.SigningSecret;
        Assert.NotEqual(setup.Secret, newSecret);

        using var client = FakeClockClient();
        var ts = FixedNow.ToUnixTimeSeconds().ToString();

        // The old secret's signature no longer verifies.
        var stale = await client.SendAsync(SignedRequest(setup.Token, Body, ts, setup.Secret));
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        // The new secret's signature is accepted.
        var fresh = await client.SendAsync(SignedRequest(setup.Token, Body, ts, newSecret));
        Assert.Equal(HttpStatusCode.Accepted, fresh.StatusCode);
    }

    private async Task<PagedResult<WebhookDeliveryDto>> GetDeliveries(Guid flowId, Guid webhookId) =>
        (await _client.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>(
            $"/api/workspaces/{Ws}/flows/{flowId}/webhooks/{webhookId}/deliveries", TestJson.Options))!;

    private sealed record TriggerAck(Guid RunId, string Status, bool Deduplicated);
}
