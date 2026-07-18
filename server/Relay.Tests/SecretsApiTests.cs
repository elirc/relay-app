using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Connections;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class SecretsApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string Base => $"/api/workspaces/{Ws}/connections";
    private const string Secret = """{"apiKey":"sk-live-do-not-leak"}""";

    public SecretsApiTests(RelayApiFactory factory)
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

    private async Task<ConnectionDto> CreateWithSecret(string name)
    {
        var response = await _client.PostAsJsonAsync(Base, new CreateConnectionRequest(
            DatabaseSeeder.SlackConnectorId, name, """{"channel":"#ops"}""", Secret));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options))!;
    }

    private async Task<string?> StoredSecret(Guid id)
    {
        string? envelope = null;
        await _factory.WithDbAsync(async db =>
        {
            envelope = await db.Connections.Where(c => c.Id == id).Select(c => c.EncryptedSecret).SingleAsync();
        });
        return envelope;
    }

    [Fact]
    public async Task Create_EncryptsSecret_AndNeverEchoesIt()
    {
        var response = await _client.PostAsJsonAsync(Base, new CreateConnectionRequest(
            DatabaseSeeder.SlackConnectorId, "Secretive", """{"channel":"#ops"}""", Secret));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-live-do-not-leak", raw);
        Assert.DoesNotContain("apiKey", raw, StringComparison.OrdinalIgnoreCase);

        var dto = await response.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.True(dto!.HasCredentials);
    }

    [Fact]
    public async Task Stored_Secret_IsEncrypted_NotPlaintext()
    {
        var dto = await CreateWithSecret("At-rest");
        var envelope = await StoredSecret(dto.Id);

        Assert.NotNull(envelope);
        Assert.DoesNotContain("sk-live-do-not-leak", envelope!);
    }

    [Fact]
    public async Task RotateSecret_ChangesCiphertext_KeepsHasCredentials()
    {
        var dto = await CreateWithSecret("Rotatable");
        var before = await StoredSecret(dto.Id);

        var response = await _client.PostAsync($"{Base}/{dto.Id}/rotate-secret", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = await response.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.True(rotated!.HasCredentials);

        var after = await StoredSecret(dto.Id);
        Assert.NotEqual(before, after);            // re-encrypted under a new data key
        Assert.DoesNotContain("sk-live-do-not-leak", after!);
    }

    [Fact]
    public async Task RotateSecret_NoStoredSecret_Returns400()
    {
        var created = await _client.PostAsJsonAsync(Base, new CreateConnectionRequest(
            DatabaseSeeder.SlackConnectorId, "No secret", """{"channel":"#ops"}""", null));
        var dto = await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);
        Assert.False(dto!.HasCredentials);

        var response = await _client.PostAsync($"{Base}/{dto.Id}/rotate-secret", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithEmptyBraces_ClearsSecret()
    {
        var dto = await CreateWithSecret("Clearable");

        var updated = await _client.PutAsJsonAsync($"{Base}/{dto.Id}",
            new UpdateConnectionRequest("Clearable", """{"channel":"#ops"}""", "{}", ConnectionStatus.Active));
        var result = await updated.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);

        Assert.False(result!.HasCredentials);
        Assert.Null(await StoredSecret(dto.Id));
    }

    [Fact]
    public async Task Update_WithNullSecret_PreservesIt()
    {
        var dto = await CreateWithSecret("Preserve");
        var before = await StoredSecret(dto.Id);

        var updated = await _client.PutAsJsonAsync($"{Base}/{dto.Id}",
            new UpdateConnectionRequest("Preserve", """{"channel":"#ops"}""", null, ConnectionStatus.Disabled));
        var result = await updated.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options);

        Assert.True(result!.HasCredentials);
        Assert.Equal(before, await StoredSecret(dto.Id)); // untouched
    }
}
