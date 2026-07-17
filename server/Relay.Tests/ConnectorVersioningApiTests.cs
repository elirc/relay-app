using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Connections;
using Relay.Api.Contracts.Connectors;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Connector versioning + config validation. The class fixture shares one DB, so
/// every test creates its own connector (unique key) to avoid mutating shared
/// seed state.
/// </summary>
public sealed class ConnectorVersioningApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string ConnBase => $"/api/workspaces/{Ws}/connections";

    private const string ChannelSchema =
        """{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}""";
    private const string SecondsSchema =
        """{"type":"object","properties":{"seconds":{"type":"integer"}},"required":["seconds"]}""";

    public ConnectorVersioningApiTests(RelayApiFactory factory)
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

    private async Task<ConnectorDto> NewConnector(string key, string schema)
    {
        var response = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest(key, key, "d", AuthKind.None, schema));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options))!;
    }

    private string VersionsBase(Guid connectorId) => $"/api/connectors/{connectorId}/versions";

    [Fact]
    public async Task ListVersions_ReturnsInitialV1()
    {
        var connector = await NewConnector("vt-list", ChannelSchema);

        var versions = await _client.GetFromJsonAsync<List<ConnectorVersionDto>>(
            VersionsBase(connector.Id), TestJson.Options);

        var v1 = Assert.Single(versions!);
        Assert.Equal(1, v1.Version);
        Assert.False(v1.IsDeprecated);
    }

    [Fact]
    public async Task ListVersions_UnknownConnector_Returns404()
    {
        var response = await _client.GetAsync(VersionsBase(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublishVersion_IncrementsAndUpdatesLatest()
    {
        var connector = await NewConnector("vt-publish", ChannelSchema);

        var created = await _client.PostAsJsonAsync(VersionsBase(connector.Id),
            new CreateConnectorVersionRequest(ChannelSchema));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ConnectorVersionDto>(TestJson.Options);
        Assert.Equal(2, dto!.Version);

        var reloaded = await _client.GetFromJsonAsync<ConnectorDto>(
            $"/api/connectors/{connector.Id}", TestJson.Options);
        Assert.Equal(2, reloaded!.LatestVersion);
    }

    [Fact]
    public async Task Deprecate_MarksVersion()
    {
        var connector = await NewConnector("vt-deprecate", ChannelSchema);

        var response = await _client.PostAsync($"{VersionsBase(connector.Id)}/1/deprecate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ConnectorVersionDto>(TestJson.Options);
        Assert.True(dto!.IsDeprecated);
    }

    [Fact]
    public async Task Deprecate_UnknownVersion_Returns404()
    {
        var connector = await NewConnector("vt-dep404", ChannelSchema);
        var response = await _client.PostAsync($"{VersionsBase(connector.Id)}/99/deprecate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateConnection_InvalidConfig_Returns400()
    {
        var connector = await NewConnector("vt-invalid", ChannelSchema);

        var response = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Bad", "{}", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateConnection_WrongType_Returns400()
    {
        var connector = await NewConnector("vt-type", SecondsSchema);

        var response = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Bad delay", """{"seconds":"soon"}""", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateConnection_ValidConfig_SetsVersion()
    {
        var connector = await NewConnector("vt-valid", ChannelSchema);

        var response = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Good", """{"channel":"#ops"}""", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.Equal(1, dto!.ConnectorVersion);
        Assert.False(dto.IsVersionDeprecated);
    }

    [Fact]
    public async Task CreateConnection_DefaultsToLatestLiveVersion_AfterDeprecation()
    {
        var connector = await NewConnector("vt-live", ChannelSchema);
        await _client.PostAsJsonAsync(VersionsBase(connector.Id), new CreateConnectorVersionRequest(ChannelSchema));
        await _client.PostAsync($"{VersionsBase(connector.Id)}/1/deprecate", null);

        var response = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Live", """{"channel":"#ops"}""", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.Equal(2, dto!.ConnectorVersion);
    }

    [Fact]
    public async Task CreateConnection_OnDeprecatedVersion_Returns400()
    {
        var connector = await NewConnector("vt-depinstall", ChannelSchema);
        await _client.PostAsJsonAsync(VersionsBase(connector.Id), new CreateConnectorVersionRequest(ChannelSchema));
        await _client.PostAsync($"{VersionsBase(connector.Id)}/1/deprecate", null);

        var response = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Dep", """{"channel":"#ops"}""", null, ConnectorVersion: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConnection_InvalidConfig_Returns400()
    {
        var connector = await NewConnector("vt-update", ChannelSchema);
        var created = await _client.PostAsJsonAsync(ConnBase,
            new CreateConnectionRequest(connector.Id, "Editable", """{"channel":"#a"}""", null));
        var dto = await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);

        var response = await _client.PutAsJsonAsync($"{ConnBase}/{dto!.Id}",
            new UpdateConnectionRequest("Editable", "{}", null, ConnectionStatus.Active));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
