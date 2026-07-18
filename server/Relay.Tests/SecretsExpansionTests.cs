using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Relay.Api.Contracts.Connections;
using Relay.Api.Contracts.Connectors;
using Relay.Domain.Enums;
using Relay.Domain.Security;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Security;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Reinforces the write-only secret invariant across every endpoint that touches a
/// connection, the re-encryption guarantee of rotation, and that config is
/// validated against the connection's own connector-schema version.
/// </summary>
public sealed class SecretsExpansionTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string Base => $"/api/workspaces/{Ws}/connections";
    private const string SecretMarker = "sk-live-never-echo-me";
    private static readonly string Secret = $$"""{"apiKey":"{{SecretMarker}}"}""";

    public SecretsExpansionTests(RelayApiFactory factory)
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

    [Fact]
    public async Task SecretIsNeverEchoed_AcrossEveryConnectionEndpoint()
    {
        var dto = await CreateWithSecret("Sweep");

        // Sweep the raw bodies of every read/write endpoint that returns the connection.
        var bodies = new List<string>
        {
            await (await _client.GetAsync($"{Base}?page=1&pageSize=100")).Content.ReadAsStringAsync(),
            await (await _client.GetAsync($"{Base}/{dto.Id}")).Content.ReadAsStringAsync(),
            await (await _client.PutAsJsonAsync($"{Base}/{dto.Id}",
                new UpdateConnectionRequest("Sweep", """{"channel":"#ops"}""", null, ConnectionStatus.Active)))
                .Content.ReadAsStringAsync(),
            await (await _client.PostAsync($"{Base}/{dto.Id}/rotate-secret", null)).Content.ReadAsStringAsync(),
        };

        foreach (var body in bodies)
        {
            Assert.DoesNotContain(SecretMarker, body);
            Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("encryptedSecret", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Rotation_RewrapsUnderANewDataKey_KeepsPlaintext()
    {
        // At-rest proof: the envelope's wrapped data key changes on rotation, but
        // the decrypted plaintext is unchanged. Uses the protector directly since
        // the API never reveals a secret.
        var protector = new EnvelopeSecretProtector(new FakeKms());
        var original = protector.Protect(Secret);
        var rotated = protector.Rotate(original);

        var beforeKey = ExtractWrappedKey(original);
        var afterKey = ExtractWrappedKey(rotated);
        Assert.NotEqual(beforeKey, afterKey);                 // re-wrapped under a fresh data key
        Assert.Equal(Secret, protector.Reveal(rotated));      // data intact
        Assert.DoesNotContain(SecretMarker, rotated);
    }

    [Fact]
    public async Task RotatedConnection_StoresDifferentCiphertext_AndStaysUsable()
    {
        var dto = await CreateWithSecret("Rotatable");
        var before = await StoredSecret(dto.Id);

        var response = await _client.PostAsync($"{Base}/{dto.Id}/rotate-secret", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await StoredSecret(dto.Id);
        Assert.NotEqual(before, after);
        // The rotated envelope still reveals the original secret (server-side only),
        // using the app's own registered protector so the master key matches exactly.
        using var scope = _factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        Assert.Equal(Secret, protector.Reveal(after!));
    }

    [Fact]
    public async Task Update_ValidatesConfig_AgainstTheConnectionsOwnSchemaVersion()
    {
        // Create a connector (v1 requires "channel"), install a connection on v1,
        // then publish v2 with a *different* schema (requires "url"). The existing
        // connection is still pinned to v1, so an update must satisfy v1, not v2.
        var connector = await NewConnector("secrets-schema", ChannelSchema);
        var created = await _client.PostAsJsonAsync(Base,
            new CreateConnectionRequest(connector.Id, "Pinned v1", """{"channel":"#a"}""", null));
        var conn = (await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options))!;
        Assert.Equal(1, conn.ConnectorVersion);

        await _client.PostAsJsonAsync($"/api/connectors/{connector.Id}/versions",
            new CreateConnectorVersionRequest(UrlSchema));

        // Valid under v1 (channel) — accepted even though v2 now requires url.
        var okUpdate = await _client.PutAsJsonAsync($"{Base}/{conn.Id}",
            new UpdateConnectionRequest("Pinned v1", """{"channel":"#b"}""", null, ConnectionStatus.Active));
        Assert.Equal(HttpStatusCode.OK, okUpdate.StatusCode);

        // Invalid under v1 (missing channel), even though it would satisfy v2.
        var badUpdate = await _client.PutAsJsonAsync($"{Base}/{conn.Id}",
            new UpdateConnectionRequest("Pinned v1", """{"url":"https://x.test"}""", null, ConnectionStatus.Active));
        Assert.Equal(HttpStatusCode.BadRequest, badUpdate.StatusCode);
    }

    [Fact]
    public async Task Connection_OnDeprecatedVersion_RemainsReadableAndFlaggedDeprecated()
    {
        var connector = await NewConnector("secrets-dep", ChannelSchema);
        var created = await _client.PostAsJsonAsync(Base,
            new CreateConnectionRequest(connector.Id, "Legacy", """{"channel":"#a"}""", null));
        var conn = (await created.Content.ReadFromJsonAsync<ConnectionDto>(TestJson.Options))!;

        await _client.PostAsync($"/api/connectors/{connector.Id}/versions/1/deprecate", null);

        var reread = await _client.GetFromJsonAsync<ConnectionDto>($"{Base}/{conn.Id}", TestJson.Options);
        Assert.Equal(1, reread!.ConnectorVersion);
        Assert.True(reread.IsVersionDeprecated);
    }

    // ---- helpers ----

    private const string ChannelSchema =
        """{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}""";
    private const string UrlSchema =
        """{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""";

    private async Task<ConnectorDto> NewConnector(string key, string schema)
    {
        var response = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest(key, key, "d", AuthKind.None, schema));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConnectorDto>(TestJson.Options))!;
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

    private static string ExtractWrappedKey(string envelope)
    {
        using var doc = JsonDocument.Parse(envelope);
        // The envelope is serialized with default (PascalCase) options by the protector.
        return doc.RootElement.GetProperty("WrappedKey").GetString()!;
    }
}
