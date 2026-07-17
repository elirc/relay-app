using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connections;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class ConnectionsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static string Base(Guid ws) => $"/api/workspaces/{ws}/connections";

    public ConnectionsApiTests(RelayApiFactory factory)
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

    [Fact]
    public async Task List_ForDemoWorkspace_ReturnsSeededConnections()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectionDto>>(
            Base(DatabaseSeeder.DemoWorkspaceId), TestJson.Options);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, c => c.Name == "Acme #alerts" && c.ConnectorKey == "slack");
    }

    [Fact]
    public async Task List_UnknownWorkspace_Returns404()
    {
        var response = await _client.GetAsync(Base(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_InstallsConnection_AndNeverExposesCredentials()
    {
        var request = new CreateConnectionRequest(
            DatabaseSeeder.EmailConnectorId, "Ops email", """{"from":"ops@acme.test"}""", """{"apiKey":"secret"}""");

        var response = await _client.PostAsJsonAsync(Base(DatabaseSeeder.DemoWorkspaceId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Raw payload must not carry the stored secret.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", raw);
        Assert.DoesNotContain("credentialsJson", raw, StringComparison.OrdinalIgnoreCase);

        var dto = JsonSerializer.Deserialize<ConnectionDto>(raw, TestJson.Options);
        Assert.True(dto!.HasCredentials);
        Assert.Equal("email", dto.ConnectorKey);
    }

    [Fact]
    public async Task Create_UnknownConnector_Returns400()
    {
        var request = new CreateConnectionRequest(Guid.NewGuid(), "Bad", "{}", null);

        var response = await _client.PostAsJsonAsync(Base(DatabaseSeeder.DemoWorkspaceId), request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownWorkspace_Returns404()
    {
        var request = new CreateConnectionRequest(DatabaseSeeder.SlackConnectorId, "X", "{}", null);

        var response = await _client.PostAsJsonAsync(Base(Guid.NewGuid()), request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_PreservesCredentials_WhenOmitted()
    {
        var created = await _client.PostAsJsonAsync(Base(DatabaseSeeder.DemoWorkspaceId),
            new CreateConnectionRequest(DatabaseSeeder.SlackConnectorId, "Editable", "{}", """{"token":"abc"}"""));
        var dto = await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);

        // Update without credentials → should stay HasCredentials = true.
        var updated = await _client.PutAsJsonAsync($"{Base(DatabaseSeeder.DemoWorkspaceId)}/{dto!.Id}",
            new UpdateConnectionRequest("Renamed", "{}", null, ConnectionStatus.Disabled));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var result = await updated.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.Equal("Renamed", result!.Name);
        Assert.Equal(ConnectionStatus.Disabled, result.Status);
        Assert.True(result.HasCredentials);
    }

    [Fact]
    public async Task Delete_ConnectionUsedByFlow_Returns409()
    {
        // The demo inbound connection is the trigger for the demo flow.
        var response = await _client.DeleteAsync(
            $"{Base(DatabaseSeeder.DemoWorkspaceId)}/{DatabaseSeeder.DemoInboundConnectionId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnusedConnection_Returns204()
    {
        var created = await _client.PostAsJsonAsync(Base(DatabaseSeeder.DemoWorkspaceId),
            new CreateConnectionRequest(DatabaseSeeder.DelayConnectorId, "Temp", "{}", null));
        var dto = await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);

        var response = await _client.DeleteAsync($"{Base(DatabaseSeeder.DemoWorkspaceId)}/{dto!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
