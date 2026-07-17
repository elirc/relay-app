using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connectors;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class ConnectorsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;

    public ConnectorsApiTests(RelayApiFactory factory)
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
    public async Task List_ReturnsSeededCatalog_Paged()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectorDto>>("/api/connectors", TestJson.Options);

        Assert.NotNull(page);
        Assert.True(page!.TotalCount >= 5);
        Assert.Contains(page.Items, c => c.Key == "slack" && c.AuthKind == AuthKind.OAuth2);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404_ProblemDetails()
    {
        var response = await _client.GetAsync($"/api/connectors/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_ThenGet_RoundTrips()
    {
        var request = new CreateConnectorRequest("webhook-test", "Webhook Test", "desc", AuthKind.ApiKey, "{}");

        var created = await _client.PostAsJsonAsync("/api/connectors", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var dto = await created.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options);
        Assert.NotNull(dto);
        Assert.Equal("webhook-test", dto!.Key);

        var fetched = await _client.GetFromJsonAsync<ConnectorDto>($"/api/connectors/{dto.Id}", TestJson.Options);
        Assert.Equal("Webhook Test", fetched!.Name);
    }

    [Fact]
    public async Task Create_MissingRequiredFields_Returns400()
    {
        // Missing key and authKind → validation failure (nullable fields make this a 400, not a bad bind).
        var response = await _client.PostAsJsonAsync("/api/connectors",
            new { name = "No key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateKey_Returns409()
    {
        var response = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest("slack", "Dupe", "d", AuthKind.None, "{}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesName()
    {
        var created = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest("updatable", "Before", "d", AuthKind.None, "{}"));
        var dto = await created.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options);

        var updated = await _client.PutAsJsonAsync($"/api/connectors/{dto!.Id}",
            new UpdateConnectorRequest("After", "d2", AuthKind.ApiKey, "{}"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var result = await updated.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options);
        Assert.Equal("After", result!.Name);
        Assert.Equal(AuthKind.ApiKey, result.AuthKind);
    }

    [Fact]
    public async Task Delete_ConnectorInUse_Returns409()
    {
        // The seeded HTTP connector is installed as a demo connection.
        var response = await _client.DeleteAsync($"/api/connectors/{DatabaseSeeder.HttpConnectorId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnusedConnector_Returns204()
    {
        var created = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest("deletable", "Deletable", "d", AuthKind.None, "{}"));
        var dto = await created.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options);

        var response = await _client.DeleteAsync($"/api/connectors/{dto!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
